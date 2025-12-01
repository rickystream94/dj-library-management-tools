using LibTools4DJs.Logging;
using LibTools4DJs.MixedInKey;
using LibTools4DJs.Rekordbox;
using LibTools4DJs.Utils;

namespace LibTools4DJs.Handlers;

public sealed class SyncMikFolderToRekordboxHandler
{
    private readonly ILogger _log;
    private readonly RekordboxXmlLibrary _library;
    private ProgressBar? _progress;

    public SyncMikFolderToRekordboxHandler(RekordboxXmlLibrary library, ILogger log)
    {
        this._library = library ?? throw new ArgumentNullException(nameof(library));
        this._log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task RunAsync(string mikDbPath, string targetMikFolderName, bool whatIf, bool debugEnabled = false)
    {
        if (!File.Exists(mikDbPath))
            throw new FileNotFoundException("MIK database not found", mikDbPath);

        // Backup original XML before making changes
        if (!whatIf)
        {
            var backupFile = this._library.CreateBackupCopy();
            this._log.Debug($"Creating backup of Rekordbox XML: {backupFile}");
        }

        // Build map of Rekordbox collection by normalized absolute path -> TrackID
        var rbPathToTrackId = this.GetRekordboxPathToTrackIdMap();

        await using MikDao dao = new(mikDbPath);
        string? targetMikFolderId = await dao.GetRootFolderIdByNameAsync(targetMikFolderName);

        if (string.IsNullOrWhiteSpace(targetMikFolderId))
        {
            this._log.Error($"MIK folder '{targetMikFolderName}' not found at root.");
            return;
        }

        var treeRoot = new TreeNode(targetMikFolderName);
        var folderNodes = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase) { [targetMikFolderName] = treeRoot };

        // Two-pass: first gather playlists and count total tracks for progress bar when not debug
        var playlistDescriptors = await GetMikPlaylistDescriptorsAsync(targetMikFolderId, targetMikFolderName, dao, folderNodes);

        if (!debugEnabled)
        {
            int totalTracks = playlistDescriptors.Sum(p => p.Files.Count);
            this._progress = new ProgressBar(totalTracks, whatIf ? "Simulating" : "Syncing MIK Folder");
            this._log.WithProgressBar(_progress);
        }

        // Second pass: create folders/playlists and add tracks
        foreach (var playlistDescriptor in playlistDescriptors)
        {
            await this.SyncMikPlaylistToRekordboxAsync(playlistDescriptor, rbPathToTrackId, folderNodes, whatIf);
        }

        // Save output (in place)
        if (!whatIf)
        {
            this._library.SaveAs(this._library.Path);
            this._log.Info($"Saved updated Rekordbox XML in place: {this._library.Path}", ConsoleColor.Green);
        }
        else
        {
            this._log.Info("[WhatIf] No changes saved.");
        }

        // Print ASCII tree summary
        this.PrintSummary(treeRoot);
    }

    private static async Task<List<MikPlaylistDescriptor>> GetMikPlaylistDescriptorsAsync(string targetMikFolderId, string targetMikFolderName, MikDao dao, Dictionary<string, TreeNode> folderNodes)
    {
        var playlistDescriptors = new List<MikPlaylistDescriptor>();
        var queue = new Queue<(string folderId, string folderPath)>();
        queue.Enqueue((targetMikFolderId!, targetMikFolderName));
        while (queue.Count > 0)
        {
            var (currentFolderId, currentPath) = queue.Dequeue();
            var currentNode = folderNodes[currentPath];

            foreach (var (Id, Name) in await dao.GetChildFoldersAsync(currentFolderId))
            {
                var childPath = string.Join("/", currentPath, Name);
                queue.Enqueue((Id, childPath));
                var childNode = new TreeNode(Name);
                currentNode.Children.Add(childNode);
                folderNodes[childPath] = childNode;
            }

            foreach (var (Id, Name) in await dao.GetChildPlaylistsAsync(currentFolderId))
            {
                var files = await dao.GetPlaylistSongFilesAsync(Id);
                playlistDescriptors.Add(new MikPlaylistDescriptor(currentPath, Id, Name, files));
            }
        }

        return playlistDescriptors;
    }

    private Dictionary<string, string> GetRekordboxPathToTrackIdMap()
    {
        var rbPathToTrackId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var trackEl in this._library.GetCollectionTracks())
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
        var targetFolderEl = this._library.GetOrCreateFolder(allSegments.ToArray());
        var rbPlaylist = this._library.GetOrCreatePlaylist(targetFolderEl, playlistDescriptor.Name);
        int added = 0;

        // Update progress bar context with current playlist name
        this._progress?.CurrentItemName = playlistDescriptor.Name;
        foreach (var file in playlistDescriptor.Files)
        {
            var norm = PathUtils.NormalizePath(file);
            if (string.IsNullOrWhiteSpace(norm))
            {
                this._progress?.Increment();
                continue;
            }

            if (!rbPathToTrackId.TryGetValue(norm, out var trackId))
            {
                this._log.Warn($"Rekordbox track not found for MIK path: {file}");
                this._progress?.Increment();
                continue;
            }

            if (whatIf)
            {
                this._log.Debug($"[WhatIf] Would add trackId={trackId} for '{file}' to playlist '{playlistDescriptor.Name}'.");
            }
            else
            {
                var trackAlreadyExists = rbPlaylist.SelectSingleNode($"TRACK[@{Constants.KeyAttributeName}='{trackId}']") != null;
                if (!trackAlreadyExists)
                {
                    this._library.AddTrackToPlaylist(rbPlaylist, trackId);
                    added++;
                }
            }
            this._progress?.Increment();
        }

        // Update entries
        var count = rbPlaylist.SelectNodes("TRACK")?.Count ?? 0;
        RekordboxXmlLibrary.UpdatePlaylistTracksCount(rbPlaylist, count);
        folderNodes[playlistDescriptor.FolderPath].Playlists.Add((playlistDescriptor.Name, added));
        this._log.Debug($"Playlist '{playlistDescriptor.Name}': added {added}.");
    }

    private void PrintSummary(TreeNode root)
    {
        this._log.Info("\nSummary of created folders/playlists:");
        void Walk(TreeNode node, string indent, bool isLast)
        {
            var branch = isLast ? "└──" : "├──";
            this._log.Debug($"{indent}{branch} {node.Name}");
            var childIndent = indent + (isLast ? "    " : "│   ");
            for (int i = 0; i < node.Playlists.Count; i++)
            {
                var (pl, added) = node.Playlists[i];
                var pBranch = (i == node.Playlists.Count - 1 && node.Children.Count == 0) ? "└──" : "├──";
                this._log.Debug($"{childIndent}{pBranch} [PL] {pl} (added {added})");
            }
            for (int i = 0; i < node.Children.Count; i++)
            {
                Walk(node.Children[i], childIndent, i == node.Children.Count - 1);
            }
        }
        Walk(root, "", true);
    }

    private sealed class TreeNode
    {
        public TreeNode(string name) => Name = name;

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