using LibTools4DJs.Logging;
using LibTools4DJs.MixedInKey;
using LibTools4DJs.MixedInKey.Models;
using LibTools4DJs.Rekordbox;
using LibTools4DJs.Utils;
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

        // Backup existing DB when applying changes
        if (!whatIf)
        {
            try
            {
                var dbDir = Path.GetDirectoryName(mikDbPath)!;
                var backupDir = Path.Combine(dbDir, Constants.BackupFolderName);
                Directory.CreateDirectory(backupDir);

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var backupFile = Path.Combine(backupDir, $"{Path.GetFileNameWithoutExtension(mikDbPath)}_{timestamp}.db");
                File.Copy(mikDbPath, backupFile, overwrite: false);
                this._log.Info($"Backup created: {backupFile}");
            }
            catch (Exception ex)
            {
                // Fail-safe: surface clear error; avoid proceeding without backup
                throw new IOException($"Failed to create backup of MIK database '{mikDbPath}'. Aborting to avoid data loss. Details: {ex.Message}", ex);
            }
        }

        var playlistsRoot = library.GetPlaylistsRoot() ?? throw new InvalidOperationException("Root playlist node not found in Rekordbox XML.");

        if (whatIf)
            this._log.Info("[WhatIf] Simulating playlist sync. No database changes will be committed.");

        await using MikDao mikDao = new(mikDbPath);

        var totalTracks = CountTotalPlaylistTracks(playlistsRoot);
        var progress = new ProgressBar(totalTracks, whatIf ? "Simulating" : "Syncing");

        // Override logger to be progress-aware
        this._log = new ConsoleLogger(progress);

        var stats = new SyncStats();
        using var tx = whatIf ? null : await mikDao.BeginTransactionAsync();
        await this.TraverseNodeAsync(playlistsRoot, null, mikDao, library, stats, progress, tx, whatIf);

        if (!whatIf)
            tx!.Commit();

        this._log.Info($"Playlist sync complete.\n" +
            $"New MIK playlists: {stats.NewCollections}\n" +
            $"Existing MIK playlists/folders skipped: {stats.ExistingCollectionsSkipped}\n" +
            $"Memberships inserted: {stats.MembershipsInserted}\n" +
            $"Songs not found in MIK: {stats.TracksMissingInMik.Count}", ConsoleColor.Green);

        if (whatIf)
            this._log.Info("[WhatIf] No changes persisted.");
    }

    private static int CountTotalPlaylistTracks(XmlNode root)
    {
        int total = 0;
        void Walk(XmlNode node)
        {
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child is not XmlElement el)
                    continue;

                var type = el.GetAttribute(Constants.TypeAttributeName);
                var name = el.GetAttribute(Constants.NameAttributeName);

                if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(name))
                    continue;
                bool isFolder = type == "0";
                bool isPlaylist = type == "1";

                if (isFolder && name.Equals(Constants.LibraryManagement, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (isPlaylist && name.Equals(Constants.CUEAnalysisPlaylistName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (isPlaylist)
                {
                    var tracks = el.SelectNodes("TRACK");
                    if (tracks != null)
                        total += tracks.Count;
                }

                if (isFolder)
                    Walk(el);
            }
        }

        Walk(root);
        return total;
    }

    private async Task<(MikCollection collection, string id)> GetOrCreateCollectionAsync(
        string name,
        string? parentId,
        bool isFolder,
        MikDao mikDao,
        SyncStats stats,
        SqliteTransaction? tx = null,
        bool whatIf = false)
    {
        var existingCollections = await mikDao.ExistingCollections;
        MikCollection collection = new(parentId, name, isFolder);
        if (!existingCollections.TryGetValue(collection, out string? collectionId))
        {
            // Playlist/folder does not exist; create it
            collectionId = Guid.NewGuid().ToString();
            if (whatIf)
            {
                this._log.Info($"[WhatIf] Would create {(collection.IsFolder ? "folder" : "playlist")} '{collection.Name}' (Id={collectionId}, Parent={collection.ParentId})");
            }
            else
            {
                await mikDao.CreateNewCollectionAsync(collection, collectionId, tx);
                existingCollections.Add(collection, collectionId);
                stats.NewCollections++;
                this._log.Info($"Created {(isFolder ? "folder" : "playlist")} '{name}' (Id={collectionId}, Parent={parentId})");
            }
        }
        else
        {
            stats.ExistingCollectionsSkipped++;
        }

        return (collection, collectionId);
    }

    private async Task TraverseNodeAsync(
        XmlNode node,
        string? parentId,
        MikDao mikDao,
        RekordboxXmlLibrary library,
        SyncStats stats,
        ProgressBar progress,
        SqliteTransaction? tx = null,
        bool whatIf = false)
    {
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child is not XmlElement el)
                continue;

            var type = el.GetAttribute(Constants.TypeAttributeName);
            var name = el.GetAttribute(Constants.NameAttributeName);

            if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(name))
                continue;

            bool isFolder = type == "0";
            bool isPlaylist = type == "1";

            if (parentId == null && name.Equals(Constants.RootPlaylistName, StringComparison.OrdinalIgnoreCase))
            {
                await this.TraverseNodeAsync(el, null, mikDao, library, stats, progress, tx,  whatIf);
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

            (MikCollection collection, string collectionId) = await this.GetOrCreateCollectionAsync(name, parentId, isFolder, mikDao, stats, tx, whatIf);
            if (isPlaylist)
            {
                await this.ProcessPlaylistAsync(el, (collection, collectionId), mikDao, library, stats, progress, tx, whatIf);
            }

            if (isFolder)
            {
                await this.TraverseNodeAsync(el, collectionId, mikDao, library, stats, progress, tx, whatIf);
            }
        }
    }

    private async Task ProcessPlaylistAsync(
        XmlElement playlistNode,
        (MikCollection collection, string id) collectionInfo,
        MikDao mikDao,
        RekordboxXmlLibrary library,
        SyncStats stats,
        ProgressBar progress,
        SqliteTransaction? tx = null,
        bool whatIf = false)
    {
        // Update progress bar context with current playlist name
        progress.CurrentItemName = collectionInfo.collection.Name;

        int maxExistingSequence = await mikDao.GetMaxSongSequenceInPlaylistAsync(collectionInfo.id);
        var songsInPlaylist = await mikDao.GetSongsInPlaylistAsync(collectionInfo.id);
        var songIdsByPath = await mikDao.SongIdsByPath;

    int sequence = maxExistingSequence + 1;
    int existingMembershipsSkipped = 0;
    var batchItems = new List<(string songId, string collectionId, int sequence)>();
        foreach (XmlElement trackNode in playlistNode.SelectNodes("TRACK")!)
        {
            var trackId = trackNode.GetAttribute("Key");
            if (string.IsNullOrWhiteSpace(trackId))
                continue;

            if (library.GetTrackElementById(trackId) is not XmlElement trackEl)
                continue;

            var location = trackEl.GetAttribute("Location");
            var absPath = RekordboxXmlLibrary.DecodeFileUri(location);
            var normAbs = PathUtils.NormalizePath(absPath);
            if (string.IsNullOrWhiteSpace(normAbs) || !songIdsByPath.TryGetValue(normAbs, out var songId))
            {
                stats.TracksMissingInMik.Add(normAbs);
                this._log.Warn($"Song not found in MIK for playlist '{collectionInfo.collection.Name}': {absPath} (normalized='{normAbs}')");
                continue;
            }

            if (songsInPlaylist.Contains(songId))
            {
                existingMembershipsSkipped++;
                progress.Increment();
                continue;
            }

            if (whatIf)
            {
                this._log.Info($"[WhatIf] Would add track SongId={songId} Path='{absPath}' (normalized) to playlist '{collectionInfo.collection.Name}' seq={sequence}");
            }
            else
            {
                batchItems.Add((songId, collectionInfo.id, sequence));
                songsInPlaylist.Add(songId);
                this._log.Info($"Queued track SongId={songId} Path='{absPath}' (normalized) for playlist '{collectionInfo.collection.Name}' seq={sequence}");
            }

            sequence++;
            progress.Increment();
        }

        if (!whatIf && batchItems.Count > 0)
        {
            await mikDao.AddSongsToPlaylistBatchAsync(batchItems, tx);
            stats.MembershipsInserted += batchItems.Count;
            this._log.Info($"Inserted {batchItems.Count} memberships into playlist '{collectionInfo.collection.Name}'.");
        }

        if (existingMembershipsSkipped > 0)
            this._log.Info($"Playlist '{collectionInfo.collection.Name}': skipped {existingMembershipsSkipped} already existing memberships.");
    }

    private sealed class SyncStats
    {
        public int NewCollections;
        public int ExistingCollectionsSkipped;
        public int MembershipsInserted;
        public HashSet<string> TracksMissingInMik = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }
}