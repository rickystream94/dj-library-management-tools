using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using LibTools4DJs.MixedInKey;
using LibTools4DJs.MixedInKey.Models;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibTools4DJs.Tests
{
    [TestClass]
    public class MikDaoTests
    {
        private static string? _tempDbPath;
        private const string RootFolderName = "ROOT";
        private const string ChildFolderA = "Folder A";
        private const string ChildFolderB = "Folder B";
        private const string PlaylistA1 = "Playlist A1";
        private const string PlaylistB1 = "Playlist B1";
        private static string? _rootFolderId;
        private static string? _folderAId;
        private static string? _folderBId;
        private static string? _playlistA1Id;
        private static string? _playlistB1Id;

        [ClassInitialize]
        public static async Task ClassInitializeAsync(TestContext _)
        {
            // Prepare a temporary database file for all tests in this class
            _tempDbPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".db");
            using var mem = await CreateInMemoryDbAsync();
            await SeedRealisticDataAsync(mem);
            using var dest = new SqliteConnection($"Data Source={_tempDbPath}");
            await dest.OpenAsync();
            mem.BackupDatabase(dest);
        }

        [ClassCleanup]
        public static async Task ClassCleanupAsync()
        {
            if (!string.IsNullOrEmpty(_tempDbPath) && File.Exists(_tempDbPath))
            {
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        File.Delete(_tempDbPath);
                        break;
                    }
                    catch (IOException)
                    {
                        await Task.Delay(100);
                    }
                }
            }
        }

        [TestMethod]
        public async Task CreatePlaylist_And_AddSongs_Works()
        {
            Assert.IsNotNull(_tempDbPath);

            await using var dao = new MikDao(_tempDbPath!);
            var coll = new MikCollection(null, "Test Playlist", isFolder: false);
            var collId = Guid.NewGuid().ToString();
            await dao.CreateNewCollectionAsync(coll, collId);

            // Seed an extra song (non-conflicting with pre-seeded data)
            await SeedSongsInDatabaseAsync();

            var memberships = new List<MikSongCollectionMembership>
                {
                    new("SX1", collId, 1),
                };
            await dao.AddSongsToPlaylistAsync(memberships);

            var songs = await dao.GetSongsInPlaylistAsync(collId);
            Assert.IsTrue(songs.Contains("SX1"));
        }

        [TestMethod]
        public async Task ExistingCollections_Returns_Map_Of_Collections()
        {
            Assert.IsNotNull(_tempDbPath);
            await using var dao = new MikDao(_tempDbPath!);
            var existing = await dao.ExistingCollections;
            // Expect seeded root and child folders/playlists
            Assert.IsTrue(existing.Count >= 4);
        }

        [TestMethod]
        public async Task SongIdsByPath_Maps_Normalized_Paths()
        {
            Assert.IsNotNull(_tempDbPath);
            await using var dao = new MikDao(_tempDbPath!);
            var map = await dao.SongIdsByPath;
            var norm = LibTools4DJs.Utils.PathUtils.NormalizePath("C:/Music/Seed/track_1.m4a");
            Assert.IsTrue(map.ContainsKey(norm));
        }

        [TestMethod]
        public async Task GetMaxSongSequenceInPlaylistAsync_Returns_Max_Or_MinusOne()
        {
            Assert.IsNotNull(_tempDbPath);
            await using var dao = new MikDao(_tempDbPath!);
            // For a seeded playlist we expect sequence >= 1
            var maxSeq = await dao.GetMaxSongSequenceInPlaylistAsync(_playlistA1Id!);
            Assert.IsTrue(maxSeq >= 1);

            // Create an empty playlist and check -1
            var emptyId = Guid.NewGuid().ToString();
            await dao.CreateNewCollectionAsync(new MikCollection(_folderBId, "Empty PL", false), emptyId);
            var maxEmpty = await dao.GetMaxSongSequenceInPlaylistAsync(emptyId);
            Assert.AreEqual(-1, maxEmpty);
        }

        [TestMethod]
        public async Task GetSongsInPlaylistAsync_Returns_SongId_Set()
        {
            Assert.IsNotNull(_tempDbPath);
            await using var dao = new MikDao(_tempDbPath!);
            var set = await dao.GetSongsInPlaylistAsync(_playlistB1Id!);
            Assert.IsTrue(set.Count >= 1);
        }

        [TestMethod]
        public async Task AddSongsToPlaylistAsync_Inserts_Rows()
        {
            Assert.IsNotNull(_tempDbPath);
            await using var dao = new MikDao(_tempDbPath!);
            var memberships = new List<MikSongCollectionMembership>
            {
                new("S1", _playlistA1Id!, 99),
            };
            await dao.AddSongsToPlaylistAsync(memberships);
            var set = await dao.GetSongsInPlaylistAsync(_playlistA1Id!);
            Assert.IsTrue(set.Contains("S1"));
        }

        [TestMethod]
        public async Task BeginTransactionAsync_Starts_Tx()
        {
            Assert.IsNotNull(_tempDbPath);
            await using var dao = new MikDao(_tempDbPath!);
            var tx = await dao.BeginTransactionAsync();
            Assert.IsNotNull(tx);
            await tx.RollbackAsync();
        }

        [TestMethod]
        public async Task GetRootFolderIdByNameAsync_Returns_Id()
        {
            Assert.IsNotNull(_tempDbPath);
            await using var dao = new MikDao(_tempDbPath!);
            var id = await dao.GetRootFolderIdByNameAsync(RootFolderName);
            Assert.AreEqual(_rootFolderId, id);
        }

        [TestMethod]
        public async Task GetChildFoldersAsync_Returns_Folders()
        {
            Assert.IsNotNull(_tempDbPath);
            await using var dao = new MikDao(_tempDbPath!);
            var children = await dao.GetChildFoldersAsync(_rootFolderId!);
            var names = new HashSet<string>(children.ConvertAll(x => x.Name));
            Assert.IsTrue(names.Contains(ChildFolderA));
            Assert.IsTrue(names.Contains(ChildFolderB));
        }

        [TestMethod]
        public async Task GetChildPlaylistsAsync_Returns_Playlists()
        {
            Assert.IsNotNull(_tempDbPath);
            await using var dao = new MikDao(_tempDbPath!);
            var children = await dao.GetChildPlaylistsAsync(_folderAId!);
            var names = new HashSet<string>(children.ConvertAll(x => x.Name));
            Assert.IsTrue(names.Contains(PlaylistA1));
        }

        [TestMethod]
        public async Task GetPlaylistSongFilesAsync_Returns_Ordered_Files()
        {
            Assert.IsNotNull(_tempDbPath);
            await using var dao = new MikDao(_tempDbPath!);
            var files = await dao.GetPlaylistSongFilesAsync(_playlistA1Id!);
            Assert.IsTrue(files.Count >= 1);
            StringAssert.Contains(files[0], "C:/Music/Seed/");
        }

        [TestMethod]
        public async Task ResetLibraryAsync_Clears_NonSystem_Collections_And_Memberships()
        {
            // Arrange: use a fresh temp DB for isolation
            var tempDb = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".db");
            await CreateTempDbWithSeedAsync(tempDb);

            await using var dao = new MikDao(tempDb);

            // Sanity pre-conditions: there should be memberships and non-system collections
            using (var conn = new SqliteConnection($"Data Source={tempDb}"))
            {
                await conn.OpenAsync();
                using var preMemCmd = conn.CreateCommand();
                preMemCmd.CommandText = "SELECT COUNT(*) FROM SongCollectionMembership";
                var preMemCount = Convert.ToInt32(await preMemCmd.ExecuteScalarAsync());
                Assert.IsTrue(preMemCount > 0);

                using var preCollCmd = conn.CreateCommand();
                preCollCmd.CommandText = "SELECT COUNT(*) FROM Collection WHERE NOT (Sequence IS NULL AND IsLibrary = 1 AND ParentFolderId IS NULL)";
                var preNonSystemCount = Convert.ToInt32(await preCollCmd.ExecuteScalarAsync());
                Assert.IsTrue(preNonSystemCount > 0);
            }

            // Act
            await dao.ResetLibraryAsync();

            // Assert: memberships cleared, only system collections remain
            using (var conn = new SqliteConnection($"Data Source={tempDb}"))
            {
                await conn.OpenAsync();
                using var memCmd = conn.CreateCommand();
                memCmd.CommandText = "SELECT COUNT(*) FROM SongCollectionMembership";
                var memCount = Convert.ToInt32(await memCmd.ExecuteScalarAsync());
                Assert.AreEqual(0, memCount);

                using var collCmd = conn.CreateCommand();
                collCmd.CommandText = "SELECT COUNT(*) FROM Collection WHERE NOT (Sequence IS NULL AND IsLibrary = 1 AND ParentFolderId IS NULL)";
                var nonSystemCount = Convert.ToInt32(await collCmd.ExecuteScalarAsync());
                Assert.AreEqual(0, nonSystemCount);
            }

            // Cleanup
            try { File.Delete(tempDb); } catch { }
        }

        private static async Task<SqliteConnection> CreateInMemoryDbAsync()
        {
            var conn = new SqliteConnection("Data Source=:memory:");
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE Collection (
  Id TEXT PRIMARY KEY,
  ExternalId TEXT,
  Name TEXT,
  Emoji TEXT,
  Sequence INTEGER,
  LibraryTypeId INTEGER,
  IsLibrary INTEGER,
  IsFolder INTEGER,
  ParentFolderId TEXT
);
CREATE TABLE Song (
  Id TEXT PRIMARY KEY,
  File TEXT
);
CREATE TABLE SongCollectionMembership (
  Id TEXT PRIMARY KEY,
  SongId TEXT,
  CollectionId TEXT,
  Sequence INTEGER
);
";
            await cmd.ExecuteNonQueryAsync();
            return conn;
        }

        private static async Task SeedRealisticDataAsync(SqliteConnection conn)
        {
            // Create folders and playlists
            _rootFolderId = Guid.NewGuid().ToString();
            _folderAId = Guid.NewGuid().ToString();
            _folderBId = Guid.NewGuid().ToString();
            _playlistA1Id = Guid.NewGuid().ToString();
            _playlistB1Id = Guid.NewGuid().ToString();

            await InsertCollectionAsync(conn, _rootFolderId!, RootFolderName, isFolder: true, isLibrary: true, parentId: null, sequence: null);
            await InsertCollectionAsync(conn, _folderAId!, ChildFolderA, isFolder: true, isLibrary: false, parentId: _rootFolderId!, sequence: 0);
            await InsertCollectionAsync(conn, _folderBId!, ChildFolderB, isFolder: true, isLibrary: false, parentId: _rootFolderId!, sequence: 0);
            await InsertCollectionAsync(conn, _playlistA1Id!, PlaylistA1, isFolder: false, isLibrary: false, parentId: _folderAId!, sequence: 0);
            await InsertCollectionAsync(conn, _playlistB1Id!, PlaylistB1, isFolder: false, isLibrary: false, parentId: _folderBId!, sequence: 0);

            // Create songs
            int songCount = 25;
            for (int i = 1; i <= songCount; i++)
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO Song (Id, File) VALUES ($id, $file)";
                cmd.Parameters.AddWithValue("$id", $"S{i}");
                cmd.Parameters.AddWithValue("$file", $"C:/Music/Seed/track_{i}.m4a");
                await cmd.ExecuteNonQueryAsync();
            }

            // Create memberships for playlists
            int seq = 1;
            for (int i = 1; i <= 10; i++)
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO SongCollectionMembership (Id, SongId, CollectionId, Sequence) VALUES ($id, $songId, $cid, $seq)";
                cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
                cmd.Parameters.AddWithValue("$songId", $"S{i}");
                cmd.Parameters.AddWithValue("$cid", _playlistA1Id);
                cmd.Parameters.AddWithValue("$seq", seq++);
                await cmd.ExecuteNonQueryAsync();
            }

            seq = 1;
            for (int i = 11; i <= 20; i++)
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO SongCollectionMembership (Id, SongId, CollectionId, Sequence) VALUES ($id, $songId, $cid, $seq)";
                cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
                cmd.Parameters.AddWithValue("$songId", $"S{i}");
                cmd.Parameters.AddWithValue("$cid", _playlistB1Id);
                cmd.Parameters.AddWithValue("$seq", seq++);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        private static async Task InsertCollectionAsync(SqliteConnection conn, string id, string name, bool isFolder, bool isLibrary, string? parentId, int? sequence)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO Collection (Id, ExternalId, Name, Emoji, Sequence, LibraryTypeId, IsLibrary, IsFolder, ParentFolderId)
                                VALUES ($id, NULL, $name, NULL, $seq, 1, $isLib, $isFolder, $parent)";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$name", name);
            if (sequence is null)
            {
                cmd.Parameters.AddWithValue("$seq", DBNull.Value);
            }
            else
            {
                cmd.Parameters.AddWithValue("$seq", sequence);
            }
            cmd.Parameters.AddWithValue("$isLib", isLibrary ? 1 : 0);
            cmd.Parameters.AddWithValue("$isFolder", isFolder ? 1 : 0);
            if (parentId is null)
            {
                cmd.Parameters.AddWithValue("$parent", DBNull.Value);
            }
            else
            {
                cmd.Parameters.AddWithValue("$parent", parentId);
            }
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task SeedSongsInDatabaseAsync()
        {
            using var conn = new SqliteConnection($"Data Source={_tempDbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Song (Id, File) VALUES ($id, $file)";
            cmd.Parameters.AddWithValue("$id", "SX1");
            cmd.Parameters.AddWithValue("$file", "C:/Music/track_extra_1.m4a");
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task CreateTempDbWithSeedAsync(string path)
        {
            // Build an in-memory DB, schema + seed: one system collection, two non-system collections, songs, memberships
            using var mem = await CreateInMemoryDbAsync();

            // System collection (ROOT library)
            var systemId = Guid.NewGuid().ToString();
            await InsertCollectionAsync(mem, systemId, "ROOT", isFolder: true, isLibrary: true, parentId: null, sequence: null);

            // Non-system folder and playlist
            var folderId = Guid.NewGuid().ToString();
            var playlistId = Guid.NewGuid().ToString();
            await InsertCollectionAsync(mem, folderId, "User Folder", isFolder: true, isLibrary: false, parentId: systemId, sequence: 0);
            await InsertCollectionAsync(mem, playlistId, "User Playlist", isFolder: false, isLibrary: false, parentId: folderId, sequence: 0);

            // Seed songs and memberships
            for (int i = 1; i <= 3; i++)
            {
                await using var songCmd = mem.CreateCommand();
                songCmd.CommandText = "INSERT INTO Song (Id, File) VALUES ($id, $file)";
                songCmd.Parameters.AddWithValue("$id", $"U{i}");
                songCmd.Parameters.AddWithValue("$file", $"C:/Music/User/track_{i}.m4a");
                await songCmd.ExecuteNonQueryAsync();
            }

            for (int seq = 1; seq <= 3; seq++)
            {
                await using var memCmd = mem.CreateCommand();
                memCmd.CommandText = "INSERT INTO SongCollectionMembership (Id, SongId, CollectionId, Sequence) VALUES ($id, $songId, $cid, $seq)";
                memCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
                memCmd.Parameters.AddWithValue("$songId", $"U{seq}");
                memCmd.Parameters.AddWithValue("$cid", playlistId);
                memCmd.Parameters.AddWithValue("$seq", seq);
                await memCmd.ExecuteNonQueryAsync();
            }

            using var dest = new SqliteConnection($"Data Source={path}");
            await dest.OpenAsync();
            mem.BackupDatabase(dest);
        }
    }
}