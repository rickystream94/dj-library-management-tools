// <copyright file="SyncRekordboxPlaylistsToMikHandler.cs" company="LibTools4DJs">
// Copyright (c) LibTools4DJs. All rights reserved.
// </copyright>

namespace LibTools4DJs.Handlers;

using System.Xml;
using LibTools4DJs.Logging;
using LibTools4DJs.MixedInKey;
using LibTools4DJs.MixedInKey.Models;
using LibTools4DJs.Rekordbox;
using LibTools4DJs.Utils;
using Microsoft.Data.Sqlite;

/// <summary>
/// Mirrors the Rekordbox playlist and folder hierarchy into the Mixed In Key database,
/// creating missing collections and adding song memberships without duplicates.
/// </summary>
public sealed class SyncRekordboxPlaylistsToMikHandler
{
    private readonly RekordboxXmlLibrary library;
    private readonly ILogger log;
    private ProgressBar? progress;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncRekordboxPlaylistsToMikHandler"/> class.
    /// </summary>
    /// <param name="library">Rekordbox XML library abstraction.</param>
    /// <param name="log">Logger for console output.</param>
    public SyncRekordboxPlaylistsToMikHandler(RekordboxXmlLibrary library, ILogger log)
    {
        this.library = library ?? throw new ArgumentNullException(nameof(library));
        this.log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// Executes a one-way mirror from Rekordbox playlists/folders into the Mixed In Key database.
    /// </summary>
    /// <param name="mikDbPath">Path to the Mixed In Key SQLite database file.</param>
    /// <param name="whatIf">When true, performs a dry-run without DB mutations.</param>
    /// <param name="resetMikLibrary">When true, interactively reset MIK collections and memberships before syncing.</param>
    /// <param name="debugEnabled">When true, prints debug messages and disables progress bar.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RunAsync(string mikDbPath, bool whatIf, bool resetMikLibrary = false, bool debugEnabled = false)
    {
        if (!File.Exists(mikDbPath))
        {
            throw new FileNotFoundException("MIK database not found", mikDbPath);
        }

        // Backup existing DB when applying changes
        if (!whatIf)
        {
            try
            {
                var dbDir = Path.GetDirectoryName(mikDbPath)!;
                var backupDir = Path.Combine(dbDir, Constants.BackupFolderName);
                Directory.CreateDirectory(backupDir);

                var timestamp = DateTime.Now.ToString(Constants.DefaultTimestampFormat);
                var backupFile = Path.Combine(backupDir, $"{Path.GetFileNameWithoutExtension(mikDbPath)}_{timestamp}.db");
                File.Copy(mikDbPath, backupFile, overwrite: false);
                this.log.Info($"Backup created: {backupFile}");
            }
            catch (Exception ex)
            {
                // Fail-safe: surface clear error; avoid proceeding without backup
                throw new IOException($"Failed to create backup of MIK database '{mikDbPath}'. Aborting to avoid data loss. Details: {ex.Message}", ex);
            }
        }

        var playlistsRoot = this.library.GetPlaylistsRoot() ?? throw new InvalidOperationException("Root playlist node not found in Rekordbox XML.");

        if (whatIf)
        {
            this.log.Debug("[WhatIf] Simulating playlist sync. No database changes will be committed.");
        }

        await using MikDao mikDao = new(mikDbPath);

        // Optional reset: wipe MIK library structure (collections and memberships), preserving system rows
        if (resetMikLibrary)
        {
            await this.ResetMikLibraryAsync(mikDao, whatIf);
        }

        var totalTracks = CountTotalPlaylistTracks(playlistsRoot);
        if (!debugEnabled)
        {
            this.progress = new ProgressBar(totalTracks, whatIf ? "Simulating" : "Syncing");
            this.log.WithProgressBar(this.progress);
        }

        var stats = new SyncStats();
        using var tx = whatIf ? null : await mikDao.BeginTransactionAsync();
        await this.TraverseNodeAsync(playlistsRoot, null, mikDao, stats, tx, whatIf);

        if (!whatIf)
        {
            tx!.Commit();
        }

        this.log.Info(
            $"Playlist sync complete.\n" +
            $"New MIK playlists: {stats.NewCollections}\n" +
            $"Existing MIK playlists/folders skipped: {stats.ExistingCollectionsSkipped}\n" +
            $"Memberships inserted: {stats.MembershipsInserted}\n" +
            $"Songs not found in MIK: {stats.TracksMissingInMik.Count}",
            ConsoleColor.Green);

        if (whatIf)
        {
            this.log.Info("[WhatIf] No changes persisted.");
        }
    }

    private static int CountTotalPlaylistTracks(XmlNode root)
    {
        int total = 0;
        void Walk(XmlNode node)
        {
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child is not XmlElement el)
                {
                    continue;
                }

                var type = el.GetAttribute(Constants.TypeAttributeName);
                var name = el.GetAttribute(Constants.NameAttributeName);

                if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                bool isFolder = type == "0";
                bool isPlaylist = type == "1";

                if (isFolder && name.Equals(Constants.LibraryManagement, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (isPlaylist && name.Equals(Constants.CUEAnalysisPlaylistName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (isPlaylist)
                {
                    var tracks = el.SelectNodes("TRACK");
                    if (tracks != null)
                    {
                        total += tracks.Count;
                    }
                }

                if (isFolder)
                {
                    Walk(el);
                }
            }
        }

        Walk(root);
        return total;
    }

    private async Task<(MikCollection Collection, string Id)> GetOrCreateCollectionAsync(
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
                this.log.Debug($"[WhatIf] Would create {(collection.IsFolder ? "folder" : "playlist")} '{collection.Name}' (Id={collectionId}, Parent={collection.ParentId})");
            }
            else
            {
                await mikDao.CreateNewCollectionAsync(collection, collectionId, tx);
                existingCollections.Add(collection, collectionId);
                stats.NewCollections++;
                this.log.Debug($"Created {(isFolder ? "folder" : "playlist")} '{name}' (Id={collectionId}, Parent={parentId})");
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
        SyncStats stats,
        SqliteTransaction? tx = null,
        bool whatIf = false)
    {
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child is not XmlElement el)
            {
                continue;
            }

            var type = el.GetAttribute(Constants.TypeAttributeName);
            var name = el.GetAttribute(Constants.NameAttributeName);

            if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            bool isFolder = type == "0";
            bool isPlaylist = type == "1";

            if (parentId == null && name.Equals(Constants.RootPlaylistName, StringComparison.OrdinalIgnoreCase))
            {
                await this.TraverseNodeAsync(el, null, mikDao, stats, tx,  whatIf);
                continue;
            }

            if (isFolder && name.Equals(Constants.LibraryManagement, StringComparison.OrdinalIgnoreCase))
            {
                this.log.Warn($"Skipping '{Constants.LibraryManagement}' folder and its contents.");
                continue;
            }

            if (isPlaylist &&
                (name.Equals(Constants.CUEAnalysisPlaylistName, StringComparison.OrdinalIgnoreCase) ||
                name.Equals(Constants.MikCuePointsPlaylistName, StringComparison.OrdinalIgnoreCase)))
            {
                this.log.Warn($"Skipping '{name}'.");
                continue;
            }

            (MikCollection collection, string collectionId) = await this.GetOrCreateCollectionAsync(name, parentId, isFolder, mikDao, stats, tx, whatIf);
            if (isPlaylist)
            {
                await this.ProcessPlaylistAsync(el, (collection, collectionId), mikDao, stats, tx, whatIf);
            }

            if (isFolder)
            {
                await this.TraverseNodeAsync(el, collectionId, mikDao, stats, tx, whatIf);
            }
        }
    }

    private async Task ProcessPlaylistAsync(
        XmlElement playlistNode,
        (MikCollection Collection, string Id) collectionInfo,
        MikDao mikDao,
        SyncStats stats,
        SqliteTransaction? tx = null,
        bool whatIf = false)
    {
        // Update progress bar context with current playlist name
        this.progress?.CurrentItemName = collectionInfo.Collection.Name;

        int maxExistingSequence = await mikDao.GetMaxSongSequenceInPlaylistAsync(collectionInfo.Id);
        var songsInPlaylist = await mikDao.GetSongsInPlaylistAsync(collectionInfo.Id);
        var songIdsByPath = await mikDao.SongIdsByPath;

        int sequence = maxExistingSequence + 1;
        int existingMembershipsSkipped = 0;
        var membershipsToCreate = new List<MikSongCollectionMembership>();
        foreach (XmlElement trackNode in playlistNode.SelectNodes("TRACK")!)
        {
            var trackId = trackNode.GetAttribute(Constants.KeyAttributeName);
            if (string.IsNullOrWhiteSpace(trackId))
            {
                continue;
            }

            if (this.library.GetTrackElementById(trackId) is not XmlElement trackEl)
            {
                continue;
            }

            var location = trackEl.GetAttribute(Constants.LocationAttributeName);
            var absPath = RekordboxXmlLibrary.DecodeFileUri(location);
            var normAbs = PathUtils.NormalizePath(absPath);
            if (string.IsNullOrWhiteSpace(normAbs) || !songIdsByPath.TryGetValue(normAbs, out var songId))
            {
                stats.TracksMissingInMik.Add(normAbs);
                this.log.Warn($"Song not found in MIK for playlist '{collectionInfo.Collection.Name}': {absPath} (normalized='{normAbs}')");
                continue;
            }

            if (songsInPlaylist.Contains(songId))
            {
                existingMembershipsSkipped++;
                this.progress?.Increment();
                continue;
            }

            if (whatIf)
            {
                this.log.Debug($"[WhatIf] Would add track SongId={songId} Path='{absPath}' (normalized) to playlist '{collectionInfo.Collection.Name}' seq={sequence}");
            }
            else
            {
                membershipsToCreate.Add(new MikSongCollectionMembership(songId, collectionInfo.Id, sequence));
                songsInPlaylist.Add(songId);
                this.log.Debug($"Queued track SongId={songId} Path='{absPath}' (normalized) for playlist '{collectionInfo.Collection.Name}' seq={sequence}");
            }

            sequence++;
            this.progress?.Increment();
        }

        if (!whatIf && membershipsToCreate.Count > 0)
        {
            await mikDao.AddSongsToPlaylistAsync(membershipsToCreate, tx);
            stats.MembershipsInserted += membershipsToCreate.Count;
            this.log.Debug($"Inserted {membershipsToCreate.Count} memberships into playlist '{collectionInfo.Collection.Name}'.");
        }

        if (existingMembershipsSkipped > 0)
        {
            this.log.Debug($"Playlist '{collectionInfo.Collection.Name}': skipped {existingMembershipsSkipped} already existing memberships.");
        }
    }

    private async Task ResetMikLibraryAsync(MikDao mikDao, bool whatIf)
    {
        if (whatIf)
        {
            this.log.Warn("[WhatIf] Would reset MIK library structure: delete all SongCollectionMembership rows and non-system Collection rows.");
        }
        else
        {
            // Interactive confirmation for destructive operation
            this.log.Warn("You are about to RESET the Mixed In Key library structure. This will:\n" +
                " - Delete ALL rows in SongCollectionMembership\n" +
                " - Delete ALL rows in Collection except system defaults (Sequence IS NULL AND IsLibrary = 1 AND ParentFolderId IS NULL)\n\n" +
                "This cannot be undone.");
            Console.Write("Type 'RESET' to confirm and proceed: ");
            var input = Console.ReadLine();
            if (!string.Equals(input, "RESET", StringComparison.OrdinalIgnoreCase))
            {
                this.log.Info("Reset aborted by user. No changes applied.");
                return;
            }

            try
            {
                await mikDao.ResetLibraryAsync();
                this.log.Info("MIK library structure reset completed: memberships cleared and non-system collections removed.", ConsoleColor.Yellow);
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to reset MIK library structure. No changes were applied. Details: {ex.Message}", ex);
            }
        }
    }

    private sealed class SyncStats
    {
        public int NewCollections { get; set; }

        public int ExistingCollectionsSkipped { get; set; }

        public int MembershipsInserted { get; set; }

        public HashSet<string> TracksMissingInMik { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }
}