// <copyright file="MikDao.cs" company="LibTools4DJs">
// Copyright (c) LibTools4DJs. All rights reserved.
// </copyright>

namespace LibTools4DJs.MixedInKey
{
    using LibTools4DJs.MixedInKey.Models;
    using LibTools4DJs.Utils;
    using Microsoft.Data.Sqlite;

    /// <summary>
    /// Data access object (DAO) for Mixed In Key's SQLite database, exposing helpers for collections and memberships.
    /// </summary>
    internal class MikDao : IAsyncDisposable
    {
        private readonly string mikDbPath;
        private readonly Lazy<Task<SqliteConnection>> connectionLazy;
        private readonly Lazy<Task<Dictionary<string, string>>> songIdsByPathLazy;
        private Lazy<Task<Dictionary<MikCollection, string>>> existingCollectionsLazy;

        /// <summary>
        /// Initializes a new instance of the <see cref="MikDao"/> class.
        /// </summary>
        /// <param name="mikDbPath">Absolute path to the MIK SQLite database file.</param>
        public MikDao(string mikDbPath)
        {
            this.mikDbPath = mikDbPath ?? throw new ArgumentNullException(nameof(mikDbPath));
            this.connectionLazy = new Lazy<Task<SqliteConnection>>(this.InitializeConnectionAsync);
            this.existingCollectionsLazy = new Lazy<Task<Dictionary<MikCollection, string>>>(this.GetExistingCollectionsAsync);
            this.songIdsByPathLazy = new Lazy<Task<Dictionary<string, string>>>(this.GetTrackFilePathToIdMapAsync);
        }

        /// <summary>
        /// Gets a cached map of existing collections keyed by <see cref="MikCollection"/> (ParentId, Name, IsFolder).
        /// </summary>
        public Task<Dictionary<MikCollection, string>> ExistingCollections => this.existingCollectionsLazy.Value;

        /// <summary>
        /// Gets a cached map of normalized absolute file paths to Song IDs.
        /// </summary>
        public Task<Dictionary<string, string>> SongIdsByPath => this.songIdsByPathLazy.Value;

        private Task<SqliteConnection> Connection => this.connectionLazy.Value;

        /// <summary>
        /// Creates a new collection (folder or playlist).
        /// </summary>
        /// <param name="newCollection">The collection model (name, parent, folder flag).</param>
        /// <param name="id">The ID to assign.</param>
        /// <param name="tx">Optional transaction to join.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task CreateNewCollectionAsync(MikCollection newCollection, string id, SqliteTransaction? tx = null)
        {
            using var insertCmd = (await this.Connection).CreateCommand();
            insertCmd.Transaction = tx;
            insertCmd.CommandText = @"INSERT INTO Collection (Id, ExternalId, Name, Emoji, Sequence, LibraryTypeId, IsLibrary, IsFolder, ParentFolderId)
                                        VALUES ($id, NULL, $name, NULL, 0, 1, 0, $isFolder, $parentId)";
            insertCmd.Parameters.AddWithValue("$id", id);
            insertCmd.Parameters.AddWithValue("$name", newCollection.Name);
            insertCmd.Parameters.AddWithValue("$isFolder", newCollection.IsFolder ? 1 : 0);
            if (newCollection.ParentId == null)
            {
                insertCmd.Parameters.AddWithValue("$parentId", DBNull.Value);
            }
            else
            {
                insertCmd.Parameters.AddWithValue("$parentId", newCollection.ParentId);
            }

            await insertCmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Gets the current max sequence number in a playlist, or -1 when empty.
        /// </summary>
        /// <param name="collectionId">Playlist collection ID.</param>
        /// <returns>The max sequence or -1.</returns>
        public async Task<int> GetMaxSongSequenceInPlaylistAsync(string collectionId)
        {
            using var cmd = (await this.Connection).CreateCommand();
            cmd.CommandText = "SELECT COALESCE(MAX(Sequence), -1) FROM SongCollectionMembership WHERE CollectionId = $cid";
            cmd.Parameters.AddWithValue("$cid", collectionId);
            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }

        /// <summary>
        /// Retrieves the set of Song IDs currently present in a playlist.
        /// </summary>
        /// <param name="collectionId">Playlist collection ID.</param>
        /// <returns>Set of Song IDs.</returns>
        public async Task<HashSet<string>> GetSongsInPlaylistAsync(string collectionId)
        {
            var set = new HashSet<string>();
            using var cmd = (await this.Connection).CreateCommand();
            cmd.CommandText = "SELECT SongId FROM SongCollectionMembership WHERE CollectionId = $cid";
            cmd.Parameters.AddWithValue("$cid", collectionId);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var songId = reader.GetString(0);
                set.Add(songId);
            }

            return set;
        }

        /// <summary>
        /// Inserts memberships for songs into a playlist.
        /// </summary>
        /// <param name="memberships">Membership rows to insert.</param>
        /// <param name="tx">Optional transaction to join.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task AddSongsToPlaylistAsync(IEnumerable<MikSongCollectionMembership> memberships, SqliteTransaction? tx = null)
        {
            foreach (var membership in memberships)
            {
                using var cmd = (await this.Connection).CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"INSERT INTO SongCollectionMembership (Id, SongId, CollectionId, Sequence)
                                    VALUES ($id, $songId, $collectionId, $seq)";
                cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
                cmd.Parameters.AddWithValue("$songId", membership.SongId);
                cmd.Parameters.AddWithValue("$collectionId", membership.CollectionId);
                cmd.Parameters.AddWithValue("$seq", membership.Sequence);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        /// <summary>
        /// Resets the MIK library structure by deleting all memberships and non-system collections.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task ResetLibraryAsync()
        {
            var conn = await this.Connection;
            using var tx = conn.BeginTransaction();
            try
            {
                // Delete memberships first
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "DELETE FROM SongCollectionMembership";
                    await cmd.ExecuteNonQueryAsync();
                }

                // Delete non-system collections
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "DELETE FROM Collection WHERE NOT (Sequence IS NULL AND IsLibrary = 1 AND ParentFolderId IS NULL)";
                    await cmd.ExecuteNonQueryAsync();
                }

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }

            // Invalidate caches so subsequent calls rebuild from fresh state
            // Force re-load next time by creating new lazy
            this.existingCollectionsLazy = new Lazy<Task<Dictionary<MikCollection, string>>>(this.GetExistingCollectionsAsync);
        }

        /// <summary>
        /// Begins a new database transaction.
        /// </summary>
        /// <returns>The started transaction.</returns>
        public async Task<SqliteTransaction> BeginTransactionAsync()
        {
            var tx = (await this.Connection).BeginTransaction();
            return tx;
        }

        /// <summary>
        /// Disposes the DAO by closing the SQLite connection if initialized.
        /// </summary>
        /// <returns>A task representing the asynchronous dispose operation.</returns>
        public async ValueTask DisposeAsync()
        {
            if (this.connectionLazy.IsValueCreated)
            {
                var connection = await this.Connection;
                await connection.CloseAsync();
                await connection.DisposeAsync();
            }
        }

        // New helper: get root folder id by name

        /// <summary>
        /// Gets the root folder ID by name.
        /// </summary>
        /// <param name="name">Folder name.</param>
        /// <returns>The folder ID or null.</returns>
        public async Task<string?> GetRootFolderIdByNameAsync(string name)
        {
            using var cmd = (await this.Connection).CreateCommand();
            cmd.CommandText = "SELECT Id FROM Collection WHERE Name = $name AND IsFolder = 1 AND ParentFolderId IS NULL";
            cmd.Parameters.AddWithValue("$name", name);
            var result = await cmd.ExecuteScalarAsync();
            return result as string;
        }

        /// <summary>
        /// Lists child folders under the specified parent folder.
        /// </summary>
        /// <param name="parentId">Parent folder ID.</param>
        /// <returns>List of (Id, Name) tuples.</returns>
        public async Task<List<(string Id, string Name)>> GetChildFoldersAsync(string parentId)
        {
            var list = new List<(string, string)>();
            using var cmd = (await this.Connection).CreateCommand();
            cmd.CommandText = "SELECT Id, Name FROM Collection WHERE IsFolder = 1 AND ParentFolderId = $pid";
            cmd.Parameters.AddWithValue("$pid", parentId);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var id = reader.GetString(0);
                var name = reader.GetString(1);
                list.Add((id, name));
            }

            return list;
        }

        /// <summary>
        /// Lists child playlists under the specified parent folder.
        /// </summary>
        /// <param name="parentId">Parent folder ID.</param>
        /// <returns>List of (Id, Name) tuples.</returns>
        public async Task<List<(string Id, string Name)>> GetChildPlaylistsAsync(string parentId)
        {
            var list = new List<(string, string)>();
            using var cmd = (await this.Connection).CreateCommand();
            cmd.CommandText = "SELECT Id, Name FROM Collection WHERE IsFolder = 0 AND ParentFolderId = $pid";
            cmd.Parameters.AddWithValue("$pid", parentId);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var id = reader.GetString(0);
                var name = reader.GetString(1);
                list.Add((id, name));
            }

            return list;
        }

    // New helper: get ordered song file paths for a playlist

    /// <summary>
        /// Gets ordered song file paths for a playlist.
        /// </summary>
        /// <param name="collectionId">Playlist collection ID.</param>
        /// <returns>List of absolute file paths, order by sequence.</returns>
        public async Task<List<string>> GetPlaylistSongFilesAsync(string collectionId)
        {
            var files = new List<string>();
            using var cmd = (await this.Connection).CreateCommand();
            cmd.CommandText = "SELECT s.File FROM Song s JOIN SongCollectionMembership m ON s.Id = m.SongId WHERE m.CollectionId = $cid ORDER BY m.Sequence";
            cmd.Parameters.AddWithValue("$cid", collectionId);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var file = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                files.Add(file);
            }

            return files;
        }

        private async Task<SqliteConnection> InitializeConnectionAsync()
        {
            var connection = new SqliteConnection($"Data Source={this.mikDbPath}");
            await connection.OpenAsync();
            return connection;
        }

        private async Task<Dictionary<MikCollection, string>> GetExistingCollectionsAsync()
        {
            Dictionary<MikCollection, string> existingCollections = new();
            using var cmd = (await this.Connection).CreateCommand();
            cmd.CommandText = "SELECT Id, Name, ParentFolderId, IsFolder FROM Collection";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var id = reader.GetString(0);
                var name = reader.GetString(1);
                var parentId = reader.IsDBNull(2) ? null : reader.GetString(2);
                var isFolder = reader.GetInt32(3) != 0;
                existingCollections.Add(new MikCollection(parentId, name, isFolder), id);
            }

            return existingCollections;
        }

        private async Task<Dictionary<string, string>> GetTrackFilePathToIdMapAsync()
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using var cmd = (await this.Connection).CreateCommand();
            cmd.CommandText = "SELECT Id, File FROM Song";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var id = reader.GetString(0);
                var file = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                var norm = PathUtils.NormalizePath(file);
                if (!string.IsNullOrWhiteSpace(norm) && !dict.ContainsKey(norm))
                {
                    dict[norm] = id;
                }
            }

            return dict;
        }
    }
}
