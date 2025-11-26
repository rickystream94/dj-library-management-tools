using LibTools4DJs.Logging;
using LibTools4DJs.Rekordbox;
using Microsoft.Data.Sqlite;
using System.Xml;

namespace LibTools4DJs.Handlers;

public sealed class SyncRekordboxPlaylistsToMikHandler
{
    private ILogger _log;
    
    public SyncRekordboxPlaylistsToMikHandler(ILogger log) => this._log = log;

    public async Task RunAsync(RekordboxXmlLibrary library, string mikDbPath, bool whatIf)
    {
        if (!File.Exists(mikDbPath))
            throw new FileNotFoundException("MIK database not found", mikDbPath);

        var playlistsRoot = library.Document.SelectSingleNode($"/DJ_PLAYLISTS/PLAYLISTS/NODE[@Type='0' and @Name='{Constants.RootPlaylistName}']")
                ?? throw new InvalidOperationException("Root playlist node not found in Rekordbox XML.");
        if (whatIf)
            this._log.Info("[WhatIf] Simulating playlist sync. No database changes will be committed.");

        using var connection = new SqliteConnection($"Data Source={mikDbPath}");
        await connection.OpenAsync();

        var existingCollections = await GetExistingMIKPlaylistsAsync(connection);
        var songPathToId = await GetSongFilePathToIdMapAsync(connection);
        var totalTracks = CountTotalPlaylistTracks(playlistsRoot);
        var progress = new ProgressBar(totalTracks, whatIf ? "Simulating" : "Syncing");

        // Override logger to be progress-aware
        this._log = new ConsoleLogger(progress);
        var stats = new SyncStats();
        using var tx = whatIf ? null : connection.BeginTransaction();
        this.TraverseNode(playlistsRoot, null, connection, library, existingCollections, songPathToId, whatIf, tx, stats, progress);

        if (!whatIf)
            tx!.Commit();

        this._log.Info($"Playlist sync complete.\nNew MIK playlists: {stats.NewCollections}\nExisting MIK playlists/folders skipped: {stats.ExistingCollectionsSkipped}\nMemberships inserted: {stats.MembershipsInserted}\nSongs not found in MIK: {stats.TracksMissingInMik.Count}", ConsoleColor.Green);

        if (whatIf) this._log.Info("[WhatIf] No changes persisted.");
    }

    private static string NormalizePath(string p)
    {
        if (string.IsNullOrWhiteSpace(p)) return string.Empty;
        // Mixed In Key stores Windows paths with backslashes; Rekordbox decode may yield forward slashes.
        p = p.Trim();
        p = p.Replace('/', '\\');
        try
        {
            // Path.GetFullPath will also canonicalize casing where possible
            p = Path.GetFullPath(p);
        }
        catch
        {
            // If invalid path characters, keep as-is after slash normalization.
        }
        return p;
    }

    private static async Task<HashSet<string>> GetExistingMIKPlaylistsAsync(SqliteConnection connection)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Name FROM Collection";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            set.Add(name);
        }
        return set;
    }

    private static async Task<Dictionary<string, string>> GetSongFilePathToIdMapAsync(SqliteConnection connection)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, File FROM Song";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var id = reader.GetString(0);
            var file = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            var norm = NormalizePath(file);
            if (!string.IsNullOrWhiteSpace(norm) && !dict.ContainsKey(norm))
                dict[norm] = id;
        }
        return dict;
    }

    private static int CountTotalPlaylistTracks(XmlNode root)
    {
        int total = 0;
        void Walk(XmlNode node)
        {
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child is not XmlElement el) continue;
                var type = el.GetAttribute(Constants.TypeAttributeName);
                var name = el.GetAttribute(Constants.NameAttributeName);
                if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(name)) continue;
                bool isFolder = type == "0";
                bool isPlaylist = type == "1";
                if (isFolder && name.Equals(Constants.LibraryManagement, StringComparison.OrdinalIgnoreCase)) continue;
                if (isPlaylist && name.Equals(Constants.CUEAnalysisPlaylistName, StringComparison.OrdinalIgnoreCase)) continue;
                if (isPlaylist)
                {
                    var tracks = el.SelectNodes("TRACK");
                    if (tracks != null) total += tracks.Count;
                }
                if (isFolder) Walk(el);
            }
        }
        Walk(root);
        return total;
    }

    private string GetOrCreateCollectionId(string name, string? parentId, bool isFolder, bool whatIf, SqliteConnection connection, SqliteTransaction? tx, HashSet<string> existingCollections, SyncStats stats)
    {
        string? collectionId;
        if (!existingCollections.Contains(name))
        {
            // Playlist/folder does not exist; create it
            collectionId = Guid.NewGuid().ToString();
            if (whatIf)
            {
                this._log.Info($"[WhatIf] Would create {(isFolder ? "folder" : "playlist")} '{name}' (Id={collectionId}) parent={parentId}");
            }
            else
            {
                using var insertCmd = connection.CreateCommand();
                insertCmd.Transaction = tx;
                insertCmd.CommandText = @"INSERT INTO Collection (Id, ExternalId, Name, Emoji, Sequence, LibraryTypeId, IsLibrary, IsFolder, ParentFolderId)
                                          VALUES ($id, NULL, $name, NULL, 0, 1, 0, $isFolder, $parentId)";
                insertCmd.Parameters.AddWithValue("$id", collectionId);
                insertCmd.Parameters.AddWithValue("$name", name);
                insertCmd.Parameters.AddWithValue("$isFolder", isFolder ? 1 : 0);
                if (parentId == null)
                    insertCmd.Parameters.AddWithValue("$parentId", DBNull.Value);
                else
                    insertCmd.Parameters.AddWithValue("$parentId", parentId);
                insertCmd.ExecuteNonQuery();
                existingCollections.Add(name);
                stats.NewCollections++;
                this._log.Info($"Created {(isFolder ? "folder" : "playlist")} '{name}' (Id={collectionId}) parent={parentId}");
            }

            return collectionId;
        }

        stats.ExistingCollectionsSkipped++;

        using var findCmd = connection.CreateCommand();
        findCmd.CommandText = "SELECT Id FROM Collection WHERE Name = $name LIMIT 1";
        findCmd.Parameters.AddWithValue("$name", name);
        collectionId = (string?)findCmd.ExecuteScalar();
        
        return collectionId ?? throw new InvalidOperationException($"Detected null collection ID for existing collection named '{name}'");
    }

    private void TraverseNode(
        XmlNode node,
        string? parentId,
        SqliteConnection connection,
        RekordboxXmlLibrary library,
        HashSet<string> existingCollections,
        Dictionary<string, string> songPathToId,
        bool whatIf,
        SqliteTransaction? tx,
        SyncStats stats,
        ProgressBar progress)
    {
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child is not XmlElement el)
                continue;
            var type = el.GetAttribute(Constants.TypeAttributeName);
            var name = el.GetAttribute(Constants.NameAttributeName);
            if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(name)) continue;
            bool isFolder = type == "0";
            bool isPlaylist = type == "1";

            if (parentId == null && name.Equals(Constants.RootPlaylistName, StringComparison.OrdinalIgnoreCase))
            {
                this.TraverseNode(el, null, connection, library, existingCollections, songPathToId, whatIf, tx, stats, progress);
                continue;
            }

            if (isFolder && name.Equals(Constants.LibraryManagement, StringComparison.OrdinalIgnoreCase))
            {
                this._log.Warn($"Skipping '{Constants.LibraryManagement}' folder and its contents.");
                continue;
            }

            if (isPlaylist &&
                (name.Equals(Constants.CUEAnalysisPlaylistName, StringComparison.OrdinalIgnoreCase) ||
                name.Equals(Constants.MikCuePointsPlaylistName, StringComparison.OrdinalIgnoreCase)))
            {
                this._log.Warn($"Skipping '{name}'.");
                continue;
            }

            string collectionId = this.GetOrCreateCollectionId(name, parentId, isFolder, whatIf, connection, tx, existingCollections, stats);
            if (isPlaylist)
            {
                this.ProcessPlaylist(el, collectionId, name, connection, library, songPathToId, whatIf, tx, stats, progress);
            }

            if (isFolder)
            {
                this.TraverseNode(el, collectionId, connection, library, existingCollections, songPathToId, whatIf, tx, stats, progress);
            }
        }
    }

    private void ProcessPlaylist(
        XmlElement el,
        string collectionId,
        string name,
        SqliteConnection connection,
        RekordboxXmlLibrary library,
        Dictionary<string, string> songPathToId,
        bool whatIf,
        SqliteTransaction? tx,
        SyncStats stats,
        ProgressBar progress)
    {
        // Update progress bar context with current playlist name
        progress.CurrentItemName = name;
        var existingMembershipSongIds = new HashSet<string>();
        int maxExistingSequence = -1;
        using (var existingCmd = connection.CreateCommand())
        {
            existingCmd.CommandText = "SELECT SongId, Sequence FROM SongCollectionMembership WHERE CollectionId = $cid";
            existingCmd.Parameters.AddWithValue("$cid", collectionId);
            using var r = existingCmd.ExecuteReader();
            while (r.Read())
            {
                var sid = r.GetString(0);
                existingMembershipSongIds.Add(sid);
                if (!r.IsDBNull(1))
                {
                    var seq = r.GetInt32(1);
                    if (seq > maxExistingSequence) maxExistingSequence = seq;
                }
            }
        }

        int sequence = maxExistingSequence + 1;
        int existingMembershipsSkipped = 0;
        foreach (XmlElement trackNode in el.SelectNodes("TRACK")!)
        {
            var trackId = trackNode.GetAttribute("Key");
            if (string.IsNullOrWhiteSpace(trackId))
                continue;

            var trackEl = library.Document.SelectSingleNode($"/DJ_PLAYLISTS/COLLECTION/TRACK[@TrackID='{trackId}']") as XmlElement;
            if (trackEl == null)
                continue;
            var location = trackEl.GetAttribute("Location");
            var absPath = RekordboxXmlLibrary.DecodeFileUri(location);
            var normAbs = NormalizePath(absPath);
            if (string.IsNullOrWhiteSpace(normAbs) || !songPathToId.TryGetValue(normAbs, out var songId))
            {
                stats.TracksMissingInMik.Add(normAbs);
                this._log.Warn($"Song not found in MIK for playlist '{name}': {absPath} (normalized='{normAbs}')");
                continue;
            }

            if (existingMembershipSongIds.Contains(songId))
            {
                existingMembershipsSkipped++;
                progress.Increment();
                continue;
            }

            if (whatIf)
            {
                this._log.Info($"[WhatIf] Would add track SongId={songId} Path='{absPath}' (normalized) to playlist '{name}' seq={sequence}");
            }
            else
            {
                using var insertMem = connection.CreateCommand();
                insertMem.Transaction = tx;
                insertMem.CommandText = @"INSERT INTO SongCollectionMembership (Id, SongId, CollectionId, Sequence)
                                                   VALUES ($id, $songId, $collectionId, $seq)";
                insertMem.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
                insertMem.Parameters.AddWithValue("$songId", songId);
                insertMem.Parameters.AddWithValue("$collectionId", collectionId);
                insertMem.Parameters.AddWithValue("$seq", sequence);
                insertMem.ExecuteNonQuery();
                stats.MembershipsInserted++;
                existingMembershipSongIds.Add(songId);
                this._log.Info($"Added track SongId={songId} Path='{absPath}' (normalized) to playlist '{name}' seq={sequence}");
            }

            sequence++;
            progress.Increment();
        }

        if (existingMembershipsSkipped > 0)
            this._log.Info($"Playlist '{name}': skipped {existingMembershipsSkipped} already existing memberships. Max existing sequence was {maxExistingSequence}.");
    }

    private sealed class SyncStats
    {
        public int NewCollections;
        public int ExistingCollectionsSkipped;
        public int MembershipsInserted;
        public HashSet<string> TracksMissingInMik = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }
}