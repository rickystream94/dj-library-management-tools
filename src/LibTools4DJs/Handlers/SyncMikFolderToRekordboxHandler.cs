using LibTools4DJs.Logging;
using LibTools4DJs.Rekordbox;
using LibTools4DJs.Utils;
using LibTools4DJs.MixedInKey;
using System.Xml;

namespace LibTools4DJs.Handlers;

public sealed class SyncMikFolderToRekordboxHandler
{
    private readonly ILogger _log;

    public SyncMikFolderToRekordboxHandler(ILogger log) => _log = log;

    public async Task RunAsync(RekordboxXmlLibrary library, string mikDbPath, string mikFolderName, bool whatIf)
    {
        if (!File.Exists(mikDbPath))
            throw new FileNotFoundException("MIK database not found", mikDbPath);

        // Backup original XML before making changes
        if (!whatIf)
        {
            var backupFile = library.CreateBackupCopy();
            _log.Info($"Creating backup of Rekordbox XML: {backupFile}");
        }

        // Build map of Rekordbox collection by normalized absolute path -> TrackID
        var rbPathToTrackId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var trackEl in library.GetCollectionTracks())
        {
            var id = trackEl.GetAttribute("TrackID");
            var location = trackEl.GetAttribute("Location");
            var abs = RekordboxXmlLibrary.DecodeFileUri(location);
            var norm = PathUtils.NormalizePath(abs);
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(norm) && !rbPathToTrackId.ContainsKey(norm))
            {
                rbPathToTrackId[norm] = id;
            }
        }

        // Use DAO for MIK DB operations
        await using MikDao dao = new(mikDbPath);
        string? rootFolderId = await dao.GetRootFolderIdByNameAsync(mikFolderName);

        if (string.IsNullOrWhiteSpace(rootFolderId))
        {
            _log.Error($"MIK folder '{mikFolderName}' not found at root.");
            return;
        }

        // Traverse the MIK hierarchy: BFS from rootFolderId
        var queue = new Queue<(string folderId, string folderPath)>();
        queue.Enqueue((rootFolderId!, mikFolderName));

        // Track created structure and additions for ASCII summary
        var treeRoot = new TreeNode(mikFolderName);
        var folderNodes = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase) { [mikFolderName] = treeRoot };

        while (queue.Count > 0)
        {
            var (currentFolderId, currentPath) = queue.Dequeue();
            var pathSegments = currentPath.Split('/');
            var allSegments = new List<string> { Constants.SyncFromMikFolderName };
            allSegments.AddRange(pathSegments);
            var targetFolderEl = library.GetOrCreateFolder(allSegments.ToArray());
            var currentNode = folderNodes[currentPath];

            // Enqueue child folders
            foreach (var (Id, Name) in await dao.GetChildFoldersAsync(currentFolderId))
            {
                var childPath = string.Join("/", currentPath, Name);
                queue.Enqueue((Id, childPath));
                var childNode = new TreeNode(Name);
                currentNode.Children.Add(childNode);
                folderNodes[childPath] = childNode;
            }

            // Process playlists directly under this folder
            foreach (var pl in await dao.GetChildPlaylistsAsync(currentFolderId))
            {
                var added = await this.MirrorSinglePlaylistAsync(library, rbPathToTrackId, targetFolderEl, dao, pl.Id, pl.Name, whatIf);
                currentNode.Playlists.Add((pl.Name, added));
            }
        }

        // Save output (in place)
        if (!whatIf)
        {
            library.SaveAs(library.Path);
            _log.Info($"Saved updated Rekordbox XML in place: {library.Path}", ConsoleColor.Green);
        }
        else
        {
            _log.Info("[WhatIf] No changes saved.");
        }

        // Print ASCII tree summary
        this.PrintSummary(treeRoot);
    }

    private async Task<int> MirrorSinglePlaylistAsync(
        RekordboxXmlLibrary library,
        Dictionary<string, string> rbPathToTrackId,
        XmlElement targetFolderEl,
        MikDao dao,
        string mikPlaylistId,
        string mikPlaylistName,
        bool whatIf)
    {
        // Create or get target playlist under target folder
        var rbPlaylist = library.GetOrCreatePlaylist(targetFolderEl, mikPlaylistName);

        // Collect song files in order inside the MIK playlist
        var files = await dao.GetPlaylistSongFilesAsync(mikPlaylistId);

        int added = 0;
        foreach (var file in files)
        {
            var norm = PathUtils.NormalizePath(file);
            if (string.IsNullOrWhiteSpace(norm))
                continue;

            if (!rbPathToTrackId.TryGetValue(norm, out var trackId))
            {
                _log.Warn($"Rekordbox track not found for MIK path: {file}");
                continue;
            }

            if (whatIf)
                _log.Info($"[WhatIf] Would add trackId={trackId} for '{file}' to playlist '{mikPlaylistName}'.");
            else
            {
                // Idempotent: don't add duplicates
                var trackAlreadyExists = rbPlaylist.SelectSingleNode($"TRACK[@Key='{trackId}']") != null;
                if (!trackAlreadyExists)
                {
                    library.AddTrackToPlaylist(rbPlaylist, trackId);
                    added++;
                }
            }
        }

        // Update entries
        var count = rbPlaylist.SelectNodes("TRACK")?.Count ?? 0;
        RekordboxXmlLibrary.UpdatePlaylistTracksCount(rbPlaylist, count);
        _log.Info($"Playlist '{mikPlaylistName}': added {added}.");
        return added;
    }

    private sealed class TreeNode
    {
        public string Name { get; }
        public List<TreeNode> Children { get; } = new();
        public List<(string Playlist, int Added)> Playlists { get; } = new();
        public TreeNode(string name) { Name = name; }
    }

    private void PrintSummary(TreeNode root)
    {
        _log.Info("\nSummary of created folders/playlists:");
        void Walk(TreeNode node, string indent, bool isLast)
        {
            var branch = isLast ? "└──" : "├──";
            _log.Info($"{indent}{branch} {node.Name}");
            var childIndent = indent + (isLast ? "    " : "│   ");
            for (int i = 0; i < node.Playlists.Count; i++)
            {
                var (pl, added) = node.Playlists[i];
                var pBranch = (i == node.Playlists.Count - 1 && node.Children.Count == 0) ? "└──" : "├──";
                _log.Info($"{childIndent}{pBranch} [PL] {pl} (added {added})");
            }
            for (int i = 0; i < node.Children.Count; i++)
            {
                Walk(node.Children[i], childIndent, i == node.Children.Count - 1);
            }
        }
        Walk(root, "", true);
    }
}