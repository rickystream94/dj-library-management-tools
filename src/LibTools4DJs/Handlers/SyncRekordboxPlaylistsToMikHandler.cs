using LibTools4DJs.Rekordbox;
using Microsoft.Data.Sqlite;
using System.Xml;

namespace LibTools4DJs.Handlers;

public sealed class SyncRekordboxPlaylistsToMikHandler
{
    private readonly ILogger _log;

    public SyncRekordboxPlaylistsToMikHandler(ILogger log) => _log = log;

    public async Task RunAsync(RekordboxXmlLibrary library, string mikDbPath, bool whatIf)
    {
        if (!File.Exists(mikDbPath))
            throw new FileNotFoundException("MIK database not found", mikDbPath);

        var playlistsRoot = library.Document.SelectSingleNode("/DJ_PLAYLISTS/PLAYLISTS/NODE[@Type='0' and @Name='ROOT']");
        if (playlistsRoot == null)
            throw new InvalidOperationException("Root playlist node not found in Rekordbox XML.");

        if (whatIf)
            _log.Info("[WhatIf] Simulating playlist sync. No database changes will be committed.");

        using var connection = new SqliteConnection($"Data Source={mikDbPath}");
        await connection.OpenAsync();

        // Load existing folder/playlist names to avoid duplicates
        var existingCollections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT Name FROM Collection";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(0);
                existingCollections.Add(name);
            }
        }

        // Build a lookup from absolute path (File column) to SongId for membership linking
        var songPathToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, File FROM Song";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var id = reader.GetString(0);
                var file = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                if (!string.IsNullOrWhiteSpace(file) && !songPathToId.ContainsKey(file))
                {
                    songPathToId[file] = id;
                }
            }
        }

        var newCollections = 0; var skippedExisting = 0; var membershipsInserted = 0; var tracksMissingInMik = 0;

        using var tx = whatIf ? null : connection.BeginTransaction();

        // Recursive traversal
        void Traverse(XmlNode node, string? parentId)
        {
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child is not XmlElement el) continue;
                var type = el.GetAttribute("Type");
                var name = el.GetAttribute("Name");
                if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(name)) continue;

                bool isFolder = type == "0";
                bool isPlaylist = type == "1";

                // Skip the Rekordbox synthetic ROOT folder itself (already at top) except its children.
                if (parentId == null && name.Equals("ROOT", StringComparison.OrdinalIgnoreCase))
                {
                    Traverse(el, null);
                    continue;
                }

                // Insert collection row if new
                string? collectionId = null;
                if (!existingCollections.Contains(name))
                {
                    collectionId = Guid.NewGuid().ToString();
                    if (whatIf)
                    {
                        _log.Info($"[WhatIf] Would create {(isFolder ? "folder" : "playlist")} '{name}' (Id={collectionId}) parent={parentId}");
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
                        existingCollections.Add(name); // prevent re-adding by name
                        newCollections++;
                    }
                }
                else
                {
                    skippedExisting++;
                }

                // Determine collectionId for membership inserts (new or lookup by name)
                if (collectionId == null)
                {
                    // Fetch id by name
                    using var findCmd = connection.CreateCommand();
                    findCmd.CommandText = "SELECT Id FROM Collection WHERE Name = $name LIMIT 1";
                    findCmd.Parameters.AddWithValue("$name", name);
                    collectionId = (string?)findCmd.ExecuteScalar();
                }

                if (isPlaylist && collectionId != null)
                {
                    // Add tracks in order; Sequence starts at 0
                    int sequence = 0;
                    foreach (XmlElement trackNode in el.SelectNodes("TRACK")!)
                    {
                        var trackId = trackNode.GetAttribute("Key");
                        if (string.IsNullOrWhiteSpace(trackId)) continue;
                        // Find corresponding track element in collection to read file path
                        var trackEl = library.Document.SelectSingleNode($"/DJ_PLAYLISTS/COLLECTION/TRACK[@TrackID='{trackId}']") as XmlElement;
                        if (trackEl == null) continue;
                        var location = trackEl.GetAttribute("Location");
                        var absPath = RekordboxXmlLibrary.DecodeFileUri(location);
                        if (string.IsNullOrWhiteSpace(absPath) || !songPathToId.TryGetValue(absPath, out var songId))
                        {
                            tracksMissingInMik++;
                            _log.Warn($"Song not found in MIK DB for playlist '{name}': {absPath}");
                            continue;
                        }
                        if (whatIf)
                        {
                            _log.Info($"[WhatIf] Would add track SongId={songId} Path='{absPath}' to playlist '{name}' seq={sequence}");
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
                            membershipsInserted++;
                        }
                        sequence++;
                    }
                }

                // Recurse into folders
                if (isFolder)
                {
                    Traverse(el, collectionId);
                }
            }
        }

        Traverse(playlistsRoot, null);

        if (!whatIf)
        {
            tx!.Commit();
        }

        _log.Info($"Playlist sync complete. New collections: {newCollections}, Existing skipped: {skippedExisting}, Memberships inserted: {membershipsInserted}, Missing songs: {tracksMissingInMik}");
        if (whatIf)
        {
            _log.Info("[WhatIf] No changes persisted.");
        }
    }
}