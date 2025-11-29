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
            cmd.CommandText = "SELECT IFNULL(MAX(Sequence), -1) FROM SongCollectionMembership WHERE CollectionId = $cid";
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
            using var insertMem = (await this.Connection).CreateCommand();
            insertMem.Transaction = tx;
            insertMem.CommandText = @"INSERT INTO SongCollectionMembership (Id, SongId, CollectionId, Sequence)
                                                   VALUES ($id, $songId, $collectionId, $seq)";
            insertMem.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            insertMem.Parameters.AddWithValue("$songId", songId);
            insertMem.Parameters.AddWithValue("$collectionId", collectionId);
            insertMem.Parameters.AddWithValue("$seq", sequence);
            await insertMem.ExecuteNonQueryAsync();
        }

        public async Task<SqliteTransaction> BeginTransactionAsync()
        {
            return (await this.Connection).BeginTransaction();
        }

        public async ValueTask DisposeAsync()
        {
            if (this.connectionLazy.IsValueCreated)
            {
                var connection = await this.Connection;
                await connection.CloseAsync();
            }
        }

        private async Task<SqliteConnection> InitializeConnectionAsync()
        {
            var connection = new SqliteConnection($"Data Source={this.mikDbPath}");
            await connection.OpenAsync();
            return connection;
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
