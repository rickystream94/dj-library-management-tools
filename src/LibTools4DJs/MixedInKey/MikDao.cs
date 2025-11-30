using LibTools4DJs.MixedInKey.Models;
using LibTools4DJs.Utils;
using Microsoft.Data.Sqlite;

namespace LibTools4DJs.MixedInKey
{
    internal class MikDao : IAsyncDisposable
    {
        private readonly string mikDbPath;
        private readonly Lazy<Task<SqliteConnection>> connectionLazy;
        private readonly Lazy<Task<Dictionary<MikCollection, string>>> existingCollectionsLazy;
        private readonly Lazy<Task<Dictionary<string, string>>> songIdsByPathLazy;

        public MikDao(string mikDbPath)
        {
            this.mikDbPath = mikDbPath ?? throw new ArgumentNullException(nameof(mikDbPath));
            this.connectionLazy = new Lazy<Task<SqliteConnection>>(this.InitializeConnectionAsync);
            this.existingCollectionsLazy = new Lazy<Task<Dictionary<MikCollection, string>>>(this.GetExistingCollectionsAsync);
            this.songIdsByPathLazy = new Lazy<Task<Dictionary<string, string>>>(this.GetTrackFilePathToIdMapAsync);
        }

        public Task<Dictionary<MikCollection, string>> ExistingCollections => this.existingCollectionsLazy.Value;

        public Task<Dictionary<string, string>> SongIdsByPath => this.songIdsByPathLazy.Value;

        private Task<SqliteConnection> Connection => this.connectionLazy.Value;

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
                insertCmd.Parameters.AddWithValue("$parentId", DBNull.Value);
            else
                insertCmd.Parameters.AddWithValue("$parentId", newCollection.ParentId);
            await insertCmd.ExecuteNonQueryAsync();
        }

        public async Task<int> GetMaxSongSequenceInPlaylistAsync(string collectionId)
        {
            using var cmd = (await this.Connection).CreateCommand();
            cmd.CommandText = "SELECT COALESCE(MAX(Sequence), -1) FROM SongCollectionMembership WHERE CollectionId = $cid";
            cmd.Parameters.AddWithValue("$cid", collectionId);
            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }

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

        public async Task AddSongToPlaylistAsync(string songId, string collectionId, int sequence, SqliteTransaction? tx = null)
        {
            using var cmd = (await this.Connection).CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO SongCollectionMembership (Id, SongId, CollectionId, Sequence)
                                    VALUES ($id, $songId, $collectionId, $seq)";
            cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("$songId", songId);
            cmd.Parameters.AddWithValue("$collectionId", collectionId);
            cmd.Parameters.AddWithValue("$seq", sequence);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task AddSongsToPlaylistBatchAsync(IEnumerable<(string songId, string collectionId, int sequence)> items, SqliteTransaction? tx = null)
        {
            foreach (var (songId, collectionId, sequence) in items)
            {
                await AddSongToPlaylistAsync(songId, collectionId, sequence, tx);
            }
        }

        public async Task<SqliteTransaction> BeginTransactionAsync()
        {
            var tx = (await this.Connection).BeginTransaction();
            return tx;
        }

        public async ValueTask DisposeAsync()
        {
            if (this.connectionLazy.IsValueCreated)
            {
                var connection = await this.Connection;
                await connection.CloseAsync();
                await connection.DisposeAsync();
            }
        }

        private async Task<SqliteConnection> InitializeConnectionAsync()
        {
            var connection = new SqliteConnection($"Data Source={this.mikDbPath}");
            await connection.OpenAsync();
            return connection;
        }

        // New helper: get root folder id by name
        public async Task<string?> GetRootFolderIdByNameAsync(string name)
        {
            using var cmd = (await this.Connection).CreateCommand();
            cmd.CommandText = "SELECT Id FROM Collection WHERE Name = $name AND IsFolder = 1 AND ParentFolderId IS NULL";
            cmd.Parameters.AddWithValue("$name", name);
            var result = await cmd.ExecuteScalarAsync();
            return result as string;
        }

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

        private async Task<Dictionary<MikCollection, string>> GetExistingCollectionsAsync()
        {
            Dictionary<MikCollection, string> existingCollections = [];
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
                    dict[norm] = id;
            }

            return dict;
        }
    }
}
