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
    public class MikDaoInMemoryTests
    {
        private static string? _tempDbPath;

        [ClassInitialize]
        public static async Task ClassInitializeAsync(TestContext _)
        {
            // Prepare a temporary database file for all tests in this class
            _tempDbPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".db");
            using var mem = await CreateInMemoryDbAsync();
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

            // Seed a song
            await SeedSongsInDatabaseAsync();

            var memberships = new List<MikSongCollectionMembership>
                {
                    new("S1", collId, 1),
                };
            await dao.AddSongsToPlaylistAsync(memberships);

            var songs = await dao.GetSongsInPlaylistAsync(collId);
            Assert.IsTrue(songs.Contains("S1"));
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

        private static async Task SeedSongsInDatabaseAsync()
        {
            using (var conn = new SqliteConnection($"Data Source={_tempDbPath}"))
            {
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO Song (Id, File) VALUES ($id, $file)";
                cmd.Parameters.AddWithValue("$id", "S1");
                cmd.Parameters.AddWithValue("$file", "C:/Music/track1.m4a");
                await cmd.ExecuteNonQueryAsync();
            }
        }
    }
}