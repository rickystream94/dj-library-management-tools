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

        var existingCollections = await LoadExistingCollectionsAsync(connection);
        var songPathToId = await LoadSongPathMapAsync(connection);
        var totalTracks = CountTotalPlaylistTracks(playlistsRoot);
        var progress = new ProgressBar(totalTracks, whatIf ? "Simulating" : "Syncing");

        // Override logger to be progress-aware
        this._log = new ProgressAwareLogger(_log, progress);
        var stats = new SyncStats();
        using var tx = whatIf ? null : connection.BeginTransaction();
        TraverseNode(playlistsRoot, null, connection, library, existingCollections, songPathToId, whatIf, tx, stats, progress);

        if (!whatIf)
            tx!.Commit();

        this._log.Info($"Playlist sync complete. New collections: {stats.NewCollections}, Existing skipped: {stats.ExistingCollectionsSkipped}, Memberships inserted: {stats.MembershipsInserted}, Missing songs: {stats.TracksMissingInMik.Count}", ConsoleColor.Green);

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

    private static async Task<HashSet<string>> LoadExistingCollectionsAsync(SqliteConnection connection)
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

    private static async Task<Dictionary<string, Guid>> LoadSongPathMapAsync(SqliteConnection connection)
    {
        var dict = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, File FROM Song";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var id = reader.GetGuid(0);
            var file = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            var norm = NormalizePath(file);
            if (!string.IsNullOrWhiteSpace(norm) && !dict.ContainsKey(norm))
                dict[norm] = id;
        }
        return dict;
    }

    private Guid? GetOrCreateCollectionId(string name, Guid? parentId, bool isFolder, bool whatIf, SqliteConnection connection, SqliteTransaction? tx, HashSet<string> existingCollections, SyncStats stats)
    {
        Guid? collectionId = null;
        if (!existingCollections.Contains(name))
        {
            collectionId = Guid.NewGuid();
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
        }
        else
        {
            stats.ExistingCollectionsSkipped++;
        }

        if (collectionId == null)
        {
            using var findCmd = connection.CreateCommand();
            findCmd.CommandText = "SELECT Id FROM Collection WHERE Name = $name LIMIT 1";
            findCmd.Parameters.AddWithValue("$name", name);
            var idObj = findCmd.ExecuteScalar();
            if (idObj is string idStr && Guid.TryParse(idStr, out var parsed)) collectionId = parsed;
            else if (idObj is Guid guidVal) collectionId = guidVal;
        }

        return collectionId;
    }

    private void TraverseNode(XmlNode node, Guid? parentId, SqliteConnection connection, RekordboxXmlLibrary library, HashSet<string> existingCollections, Dictionary<string, Guid> songPathToId, bool whatIf, SqliteTransaction? tx, SyncStats stats, ProgressBar progress)
    {
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child is not XmlElement el) continue;
            var type = el.GetAttribute("Type");
            var name = el.GetAttribute("Name");
            if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(name)) continue;
            bool isFolder = type == "0";
            bool isPlaylist = type == "1";

            if (parentId == null && name.Equals(Constants.RootPlaylistName, StringComparison.OrdinalIgnoreCase))
            {
                TraverseNode(el, null, connection, library, existingCollections, songPathToId, whatIf, tx, stats, progress);
                continue;
            }

            if (isFolder && name.Equals(Constants.LibraryManagement, StringComparison.OrdinalIgnoreCase))
            {
                this._log.Info($"Skipping '{Constants.LibraryManagement}' folder and its contents.");
                continue;
            }

            if (isPlaylist && name.Equals(Constants.CUEAnalysisPlaylistName, StringComparison.OrdinalIgnoreCase))
            {
                this._log.Info($"Skipping '{Constants.CUEAnalysisPlaylistName}'.");
                continue;
            }

            var collectionId = GetOrCreateCollectionId(name, parentId, isFolder, whatIf, connection, tx, existingCollections, stats);
            if (isPlaylist && collectionId != null)
            {
                // Update progress bar context with current playlist name
                progress.UpdateCurrentPlaylist(name);
                var existingMembershipSongIds = new HashSet<Guid>();
                int maxExistingSequence = -1;
                using (var existingCmd = connection.CreateCommand())
                {
                    existingCmd.CommandText = "SELECT SongId, Sequence FROM SongCollectionMembership WHERE CollectionId = $cid";
                    existingCmd.Parameters.AddWithValue("$cid", collectionId.ToString());
                    using var r = existingCmd.ExecuteReader();
                    while (r.Read())
                    {
                        var sid = r.GetGuid(0);
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
                    if (string.IsNullOrWhiteSpace(trackId)) continue;
                    var trackEl = library.Document.SelectSingleNode($"/DJ_PLAYLISTS/COLLECTION/TRACK[@TrackID='{trackId}']") as XmlElement;
                    if (trackEl == null) continue;
                    var location = trackEl.GetAttribute("Location");
                    var absPath = RekordboxXmlLibrary.DecodeFileUri(location);
                    var normAbs = NormalizePath(absPath);
                    if (string.IsNullOrWhiteSpace(normAbs) || !songPathToId.TryGetValue(normAbs, out var songId))
                    {
                        stats.TracksMissingInMik.Add(normAbs);
                        this._log.Warn($"Song not found in MIK DB for playlist '{name}': {absPath} (normalized='{normAbs}')");
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
                        insertMem.Parameters.AddWithValue("$id", Guid.NewGuid());
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
                    this._log.Info($"Playlist '{name}': skipped {existingMembershipsSkipped} already existing memberships (idempotent). Max existing sequence was {maxExistingSequence}.");
            }

            if (isFolder)
            {
                TraverseNode(el, collectionId, connection, library, existingCollections, songPathToId, whatIf, tx, stats, progress);
            }
        }
    }

    private static int CountTotalPlaylistTracks(XmlNode root)
    {
        int total = 0;
        void Walk(XmlNode node)
        {
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child is not XmlElement el) continue;
                var type = el.GetAttribute("Type");
                var name = el.GetAttribute("Name");
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

    private sealed class ProgressBar
    {
        private readonly DateTime _start = DateTime.UtcNow;
        private readonly string _label;
        private readonly bool _supportsCursor;
        private readonly int _total;
        private int _processed;
        private int _lastPercent;
        private string? _currentPlaylist;
        private bool _completed;

        public ProgressBar(int total, string label)
        {
            _total = total <= 0 ? 1 : total;
            _label = label;
            _supportsCursor = !Console.IsOutputRedirected;
            if (_supportsCursor)
            {
                // Insert an initial blank line for progress bar occupancy
                Console.WriteLine();
            }
            Render();
        }

        public void Increment()
        {
            _processed++;
            var percent = (int)((double)_processed / _total * 100);
            if (percent != _lastPercent && (percent % 1 == 0))
            {
                Render();
                _lastPercent = percent;
            }
        }

        public void UpdateCurrentPlaylist(string playlistName)
        {
            _currentPlaylist = playlistName;
            Render();
        }

        public void Render()
        {
            var percent = (double)_processed / _total;
            int barWidth = 40;
            int filled = (int)(percent * barWidth);
            var bar = new string('#', filled) + new string('-', barWidth - filled);
            var elapsed = DateTime.UtcNow - _start;
            var elapsedStr = elapsed.ToString("mm\\:ss");
            var playlistSegment = _currentPlaylist == null ? string.Empty : $" | Playlist: {_currentPlaylist}";
            var line = $"{_label}{playlistSegment} [{bar}] {_processed}/{_total} {(percent*100):F1}% Elapsed {elapsedStr}";

            if (_supportsCursor)
            {
                int curLeft = Console.CursorLeft;
                int curTop = Console.CursorTop;
                int top = Console.WindowTop; // always use current window top as anchor
                try
                {
                    Console.SetCursorPosition(0, top);
                    // Clear current top line then write progress
                    var clear = new string(' ', Console.BufferWidth - 1);
                    Console.Write(clear);
                    Console.SetCursorPosition(0, top);
                    if (line.Length >= Console.BufferWidth)
                        Console.Write(line.Substring(0, Console.BufferWidth - 1));
                    else
                        Console.Write(line);
                    if (curTop == top)
                        curTop = top + 1; // avoid overwriting the bar on next log write
                    Console.SetCursorPosition(curLeft, curTop);
                }
                catch
                {
                    Console.WriteLine(line); // degrade gracefully
                }
            }
            else
            {
                Console.WriteLine(line);
            }

            if (_processed >= _total && !_completed)
            {
                _completed = true;
            }
        }
    }

    private sealed class SyncStats
    {
        public int NewCollections;
        public int ExistingCollectionsSkipped;
        public int MembershipsInserted;
        public HashSet<string> TracksMissingInMik = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ProgressAwareLogger : ILogger
    {
        private readonly ILogger _inner;
        private readonly ProgressBar _progress;

        public ProgressAwareLogger(ILogger inner, ProgressBar progress)
        {
            _inner = inner;
            _progress = progress;
        }

        public void Info(string message, ConsoleColor? consoleColor = null)
        {
            _inner.Info(message, consoleColor);
            _progress.Render();
        }

        public void Warn(string message)
        {
            _inner.Warn(message);
            _progress.Render();
        }

        public void Error(string message)
        {
            _inner.Error(message);
            _progress.Render();
        }
    }
}