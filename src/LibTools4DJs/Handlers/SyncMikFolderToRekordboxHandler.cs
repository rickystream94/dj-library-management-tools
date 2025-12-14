// <copyright file="SyncMikFolderToRekordboxHandler.cs" company="LibTools4DJs">
// Copyright (c) LibTools4DJs. All rights reserved.
// </copyright>

namespace LibTools4DJs.Handlers;

using LibTools4DJs.Logging;
using LibTools4DJs.MixedInKey;
using LibTools4DJs.Rekordbox;
using LibTools4DJs.Utils;

/// <summary>
/// Mirrors a Mixed In Key folder hierarchy into Rekordbox under LibTools4DJs_SyncFromMIK.
/// </summary>
public sealed class SyncMikFolderToRekordboxHandler
{
    private readonly ILogger log;
    private readonly RekordboxXmlLibrary library;
    private readonly IMikDaoFactory mikDaoFactory;
    private ProgressBar? progress;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncMikFolderToRekordboxHandler"/> class.
    /// </summary>
    /// <param name="library">Rekordbox XML library abstraction.</param>
    /// <param name="mikDaoFactory">Factory to create an <see cref="IMikDao"/> from a database path.</param>
    /// <param name="log">Logger for console output.</param>
    public SyncMikFolderToRekordboxHandler(RekordboxXmlLibrary library, IMikDaoFactory mikDaoFactory, ILogger log)
    {
        this.library = library ?? throw new ArgumentNullException(nameof(library));
        this.mikDaoFactory = mikDaoFactory ?? throw new ArgumentNullException(nameof(mikDaoFactory));
        this.log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// Executes the sync from a specified Mixed In Key root folder into Rekordbox, creating folders/playlists and adding tracks idempotently.
    /// </summary>
    /// <param name="mikDbPath">Path to the Mixed In Key SQLite database file.</param>
    /// <param name="targetMikFolderName">The name of the root MIK folder to mirror.</param>
    /// <param name="whatIf">When true, performs a dry-run without changes to the XML.</param>
    /// <param name="debugEnabled">When true, prints debug messages and disables progress bar.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RunAsync(string mikDbPath, string targetMikFolderName, bool whatIf, bool debugEnabled = false)
    {
        if (!File.Exists(mikDbPath))
        {
            throw new FileNotFoundException("MIK database not found", mikDbPath);
        }

        // Backup original XML before making changes
        if (!whatIf)
        {
            var backupFile = this.library.CreateBackupCopy();
            this.log.Debug($"Creating backup of Rekordbox XML: {backupFile}");
        }

        // Build map of Rekordbox collection by normalized absolute path -> TrackID
        var rbPathToTrackId = this.GetRekordboxPathToTrackIdMap();

        await using IMikDao dao = this.mikDaoFactory.CreateMikDao(mikDbPath);
        string? targetMikFolderId = await dao.GetRootFolderIdByNameAsync(targetMikFolderName);

        if (string.IsNullOrWhiteSpace(targetMikFolderId))
        {
            this.log.Error($"MIK folder '{targetMikFolderName}' not found at root.");
            return;
        }

        var treeRoot = new TreeNode(targetMikFolderName);
        var folderNodes = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase) { [targetMikFolderName] = treeRoot };

        // Two-pass: first gather playlists and count total tracks for progress bar when not debug
        var playlistDescriptors = await GetMikPlaylistDescriptorsAsync(targetMikFolderId, targetMikFolderName, dao, folderNodes);

        if (!debugEnabled)
        {
            int totalTracks = playlistDescriptors.Sum(p => p.Files.Count);
            this.progress = new ProgressBar(totalTracks, whatIf ? "Simulating" : "Syncing MIK Folder");
            this.log.WithProgressBar(this.progress);
        }

        // Second pass: create folders/playlists and add tracks
        foreach (var playlistDescriptor in playlistDescriptors)
        {
            await this.SyncMikPlaylistToRekordboxAsync(playlistDescriptor, rbPathToTrackId, folderNodes, whatIf);
        }

        // Save output (in place)
        if (!whatIf)
        {
            this.library.SaveAs(this.library.Path);
            this.log.Info($"Saved updated Rekordbox XML in place: {this.library.Path}", ConsoleColor.Green);
        }
        else
        {
            this.log.Info("[WhatIf] No changes saved.");
        }

        // Print ASCII tree summary
        this.PrintSummary(treeRoot);
    }

    private static async Task<List<MikPlaylistDescriptor>> GetMikPlaylistDescriptorsAsync(string targetMikFolderId, string targetMikFolderName, IMikDao dao, Dictionary<string, TreeNode> folderNodes)
    {
        var playlistDescriptors = new List<MikPlaylistDescriptor>();
        var queue = new Queue<(string FolderId, string FolderPath)>();
        queue.Enqueue((targetMikFolderId!, targetMikFolderName));
        while (queue.Count > 0)
        {
            var (currentFolderId, currentPath) = queue.Dequeue();
            var currentNode = folderNodes[currentPath];

            foreach (var (id, name) in await dao.GetChildFoldersAsync(currentFolderId))
            {
                var childPath = string.Join("/", currentPath, name);
                queue.Enqueue((id, childPath));
                var childNode = new TreeNode(name);
                currentNode.Children.Add(childNode);
                folderNodes[childPath] = childNode;
            }

            foreach (var (id, name) in await dao.GetChildPlaylistsAsync(currentFolderId))
            {
                var files = await dao.GetPlaylistSongFilesAsync(id);
                playlistDescriptors.Add(new MikPlaylistDescriptor(currentPath, id, name, files));
            }
        }

        return playlistDescriptors;
    }

    private Dictionary<string, string> GetRekordboxPathToTrackIdMap()
    {
        var rbPathToTrackId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var trackEl in this.library.GetCollectionTracks())
        {
            var id = trackEl.GetAttribute(Constants.TrackIdAttributeName);
            var location = trackEl.GetAttribute(Constants.LocationAttributeName);
            var abs = RekordboxXmlLibrary.DecodeFileUri(location);
            var norm = PathUtils.NormalizePath(abs);
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(norm) && !rbPathToTrackId.ContainsKey(norm))
            {
                rbPathToTrackId[norm] = id;
            }
        }

        return rbPathToTrackId;
    }

    private async Task SyncMikPlaylistToRekordboxAsync(
        MikPlaylistDescriptor playlistDescriptor,
        Dictionary<string, string> rbPathToTrackId,
        Dictionary<string, TreeNode> folderNodes,
        bool whatIf)
    {
        var allSegments = new List<string> { Constants.SyncFromMikFolderName };
        var pathSegments = playlistDescriptor.FolderPath.Split('/');
        allSegments.AddRange(pathSegments);
        var targetFolderEl = this.library.GetOrCreateFolder(allSegments.ToArray());
        var rbPlaylist = this.library.GetOrCreatePlaylist(targetFolderEl, playlistDescriptor.Name);
        int added = 0;

        // Update progress bar context with current playlist name
        this.progress?.CurrentItemName = playlistDescriptor.Name;
        foreach (var file in playlistDescriptor.Files)
        {
            var norm = PathUtils.NormalizePath(file);
            if (string.IsNullOrWhiteSpace(norm))
            {
                this.progress?.Increment();
                continue;
            }

            if (!rbPathToTrackId.TryGetValue(norm, out var trackId))
            {
                this.log.Warn($"Rekordbox track not found for MIK path: {file}");
                this.progress?.Increment();
                continue;
            }

            if (whatIf)
            {
                this.log.Debug($"[WhatIf] Would add trackId={trackId} for '{file}' to playlist '{playlistDescriptor.Name}'.");
            }
            else
            {
                var trackAlreadyExists = rbPlaylist.SelectSingleNode($"TRACK[@{Constants.KeyAttributeName}='{trackId}']") != null;
                if (!trackAlreadyExists)
                {
                    this.library.AddTrackToPlaylist(rbPlaylist, trackId);
                    added++;
                }
            }

            this.progress?.Increment();
        }

        // Update entries
        var count = rbPlaylist.SelectNodes("TRACK")?.Count ?? 0;
        RekordboxXmlLibrary.UpdatePlaylistTracksCount(rbPlaylist, count);
        folderNodes[playlistDescriptor.FolderPath].Playlists.Add((playlistDescriptor.Name, added));
        this.log.Debug($"Playlist '{playlistDescriptor.Name}': added {added}.");
    }

    private void PrintSummary(TreeNode root)
    {
        this.log.Info("\nSummary of created folders/playlists:");

        void Walk(TreeNode node, string indent, bool isLast)
        {
            var branch = isLast ? "└──" : "├──";
            this.log.Debug($"{indent}{branch} {node.Name}");

            var childIndent = indent + (isLast ? "    " : "│   ");

            for (int i = 0; i < node.Playlists.Count; i++)
            {
                var (pl, added) = node.Playlists[i];
                var pBranch = (i == node.Playlists.Count - 1 && node.Children.Count == 0) ? "└──" : "├──";
                this.log.Debug($"{childIndent}{pBranch} [PL] {pl} (added {added})");
            }

            for (int i = 0; i < node.Children.Count; i++)
            {
                Walk(node.Children[i], childIndent, i == node.Children.Count - 1);
            }
        }

        Walk(root, string.Empty, true);
    }

    private sealed class TreeNode
    {
        public TreeNode(string name) => this.Name = name;

        public string Name { get; }

        public List<TreeNode> Children { get; } = new();

        public List<(string Playlist, int Added)> Playlists { get; } = new();
    }

    private sealed class MikPlaylistDescriptor(string folderPath, string playlistId, string playlistName, List<string> files)
    {
        public string FolderPath { get; set; } = folderPath;

        public string Id { get; set; } = playlistId;

        public string Name { get; set; } = playlistName;

        public List<string> Files { get; set; } = files;
    }
}