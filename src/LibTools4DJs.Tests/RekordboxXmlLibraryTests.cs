using System;
using System.IO;
using System.Linq;
using LibTools4DJs.Rekordbox;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibTools4DJs.Tests
{
    [TestClass]
    public class RekordboxXmlLibraryTests
    {
        private const string ResourcesFolderName = "Resources";
        private const string SampleXmlFileName = "rekordbox.sample.xml";
        private const string NoLibraryManagementXmlFileName = "rekordbox.no_library_management.xml";
        private const string NoDeletePlaylistXmlFileName = "rekordbox.no_delete_playlist.xml";

        private RekordboxXmlLibrary? _lib;

        [TestInitialize]
        public void TestInitialize()
        {
            var path = Path.Combine(AppContext.BaseDirectory, ResourcesFolderName, SampleXmlFileName);
            this._lib = RekordboxXmlLibrary.Load(path);
        }

        [TestMethod]
        public void Load_And_GetRoots_Work()
        {
            Assert.IsNotNull(this._lib);
            Assert.IsNotNull(this._lib.Document);

            var root = this._lib.GetPlaylistsRoot();
            Assert.IsNotNull(root);

            var lm = this._lib.GetLibraryManagementFolder();
            Assert.IsNotNull(lm);
        }

        [TestMethod]
        public void GetTracksToDelete_Enumerates_Tracks()
        {
            Assert.IsNotNull(this._lib);
            var tracks = this._lib.GetTracksToDelete().ToList();
            Assert.AreEqual(3, tracks.Count);
            Assert.AreEqual("1", tracks[0].GetAttribute("Key"));
        }

        [TestMethod]
        public void GetCollectionTracks_Returns_All_Tracks()
        {
            Assert.IsNotNull(this._lib);
            var tracks = this._lib.GetCollectionTracks().ToList();
            Assert.AreEqual(20, tracks.Count);
            Assert.AreEqual("Skyline", tracks.First().GetAttribute("Name"));
        }

        [TestMethod]
        public void GetTrackElementById_Finds_Existing_Track()
        {
            Assert.IsNotNull(this._lib);
            var t = this._lib.GetTrackElementById("1") as System.Xml.XmlElement;
            Assert.IsNotNull(t);
            Assert.AreEqual("Skyline", t.GetAttribute("Name"));
        }

        [TestMethod]
        public void DecodeFileUri_Removes_Prefix_And_Unescapes()
        {
            var raw = "file://localhost/C:/Music/Tests/Sunset%20Drive.m4a";
            var decoded = RekordboxXmlLibrary.DecodeFileUri(raw);
            Assert.AreEqual("C:/Music/Tests/Sunset Drive.m4a", decoded);
        }

        [TestMethod]
        public void DecodeFileUri_Handles_Null_Empty_And_NonFile()
        {
            Assert.AreEqual(null, RekordboxXmlLibrary.DecodeFileUri(null!));
            Assert.AreEqual(string.Empty, RekordboxXmlLibrary.DecodeFileUri(string.Empty));

            // If prefix isn't present, the string is just unescaped
            var raw = "C:/Music/Tests/Weird%20Name%20%231.m4a";
            var decoded = RekordboxXmlLibrary.DecodeFileUri(raw);
            Assert.AreEqual("C:/Music/Tests/Weird Name #1.m4a", decoded);
        }

        [TestMethod]
        public void InitializeLibraryManagementChildPlaylist_Creates_And_Resets()
        {
            Assert.IsNotNull(this._lib);
            var pl = this._lib.InitializeLibraryManagementChildPlaylist("Analysis");
            Assert.AreEqual("Analysis", pl.GetAttribute("Name"));
            Assert.AreEqual("1", pl.GetAttribute("Type"));
            Assert.AreEqual("0", pl.GetAttribute("Entries"));

            // Add a fake child and ensure reset clears it
            var child = this._lib.Document.CreateElement("TRACK");
            child.SetAttribute("Key", "5");
            pl.AppendChild(child);
            Assert.AreEqual(1, pl.ChildNodes.Count);

            var reset = this._lib.InitializeLibraryManagementChildPlaylist("Analysis");
            Assert.AreEqual(0, reset.ChildNodes.Count);
        }

        [TestMethod]
        public void UpdatePlaylistTracksCount_Sets_Entries_Attribute()
        {
            Assert.IsNotNull(this._lib);
            var pl = this._lib.InitializeLibraryManagementChildPlaylist("CountTest");
            RekordboxXmlLibrary.UpdatePlaylistTracksCount(pl, 3);
            Assert.AreEqual("3", pl.GetAttribute("Entries"));
        }

        [TestMethod]
        public void GetOrCreateFolder_Creates_Nested_Folders()
        {
            Assert.IsNotNull(this._lib);
            var folder = this._lib.GetOrCreateFolder("Tests", "SubFolder");
            Assert.AreEqual("SubFolder", folder.GetAttribute("Name"));
            Assert.AreEqual("0", folder.GetAttribute("Type"));
            // Parent should contain the child
            var parent = folder.ParentNode as System.Xml.XmlElement;
            Assert.IsNotNull(parent);
            Assert.AreEqual("Tests", parent.GetAttribute("Name"));
        }

        [TestMethod]
        public void GetOrCreatePlaylist_Adds_Playlist_Under_Folder()
        {
            Assert.IsNotNull(this._lib);
            var folder = this._lib.GetOrCreateFolder("Unit", "Playlists");
            var pl = this._lib.GetOrCreatePlaylist(folder, "Sample PL");
            Assert.AreEqual("Sample PL", pl.GetAttribute("Name"));
            Assert.AreEqual("1", pl.GetAttribute("Type"));
        }

        [TestMethod]
        public void AddTrackToPlaylist_Appends_Track_Reference()
        {
            Assert.IsNotNull(this._lib);
            var folder = this._lib.GetOrCreateFolder("Unit", "Playlists");
            var pl = this._lib.GetOrCreatePlaylist(folder, "Add Track Test");
            this._lib.AddTrackToPlaylist(pl, "4");
            var added = pl.SelectSingleNode("TRACK[@Key='4']");
            Assert.IsNotNull(added);
        }

        // Exception paths
        [TestMethod]
        public void GetOrCreateFolder_Throws_On_Empty_Path()
        {
            Assert.IsNotNull(this._lib);
            Assert.ThrowsException<ArgumentException>(() => this._lib!.GetOrCreateFolder());
        }

        [TestMethod]
        public void GetOrCreatePlaylist_Throws_On_Null_Parent()
        {
            Assert.IsNotNull(this._lib);
            Assert.ThrowsException<ArgumentNullException>(() => this._lib!.GetOrCreatePlaylist(null!, "X"));
        }

        [TestMethod]
        public void InitializeLibraryManagementChildPlaylist_Throws_When_LibraryManagement_Missing()
        {
            var path = Path.Combine(AppContext.BaseDirectory, ResourcesFolderName, NoLibraryManagementXmlFileName);
            var lib = RekordboxXmlLibrary.Load(path);
            Assert.ThrowsException<InvalidOperationException>(() => lib.InitializeLibraryManagementChildPlaylist("Analysis"));
        }

        [TestMethod]
        public void GetOrCreateFolder_Throws_When_LibraryManagement_Missing()
        {
            var path = Path.Combine(AppContext.BaseDirectory, ResourcesFolderName, NoLibraryManagementXmlFileName);
            var lib = RekordboxXmlLibrary.Load(path);
            Assert.ThrowsException<InvalidOperationException>(() => lib.GetOrCreateFolder("FolderA"));
        }

        [TestMethod]
        public void GetTracksToDelete_Returns_Empty_When_Delete_Missing()
        {
            var path = Path.Combine(AppContext.BaseDirectory, ResourcesFolderName, NoDeletePlaylistXmlFileName);
            var lib = RekordboxXmlLibrary.Load(path);
            var tracks = lib.GetTracksToDelete().ToList();
            Assert.AreEqual(0, tracks.Count);
        }

        [TestMethod]
        public void CreateBackupCopy_Creates_File_In_Backups_Folder()
        {
            // Arrange: create a temp xml file in a temp directory
            var tempDir = Path.Combine(Path.GetTempPath(), "LibTools4DJs_UT_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var xmlPath = Path.Combine(tempDir, "temp.rekordbox.xml");
            var contentPath = Path.Combine(AppContext.BaseDirectory, ResourcesFolderName, SampleXmlFileName);
            File.Copy(contentPath, xmlPath);

            try
            {
                var lib = RekordboxXmlLibrary.Load(xmlPath);
                var backupPath = lib.CreateBackupCopy();

                // Assert: backup exists under LibTools4DJs_Backups next to original
                Assert.IsTrue(File.Exists(backupPath));
                StringAssert.Contains(backupPath, "LibTools4DJs_Backups");
                StringAssert.EndsWith(backupPath, ".bak.xml");

                // Clean up backup and temp
                File.Delete(backupPath);
            }
            finally
            {
                // Best-effort cleanup
                var backupDir = Path.Combine(tempDir, "LibTools4DJs_Backups");
                if (Directory.Exists(backupDir))
                {
                    // Remove any leftover files, then dir
                    foreach (var f in Directory.GetFiles(backupDir))
                    {
                        try { File.Delete(f); } catch { }
                    }
                    try { Directory.Delete(backupDir); } catch { }
                }

                if (File.Exists(xmlPath))
                {
                    try { File.Delete(xmlPath); } catch { }
                }
                try { Directory.Delete(tempDir); } catch { }
            }
        }
    }
}