// <copyright file="RekordboxXmlLibrary.cs" company="LibTools4DJs">
// Copyright (c) LibTools4DJs. All rights reserved.
// </copyright>

namespace LibTools4DJs.Rekordbox;

using System.Xml;

/// <summary>
/// Helper wrapper around a Rekordbox XML document, providing common operations.
/// </summary>
public sealed class RekordboxXmlLibrary
{
    private RekordboxXmlLibrary(string path, XmlDocument doc)
    {
        this.Path = path;
        this.Document = doc;
    }

    /// <summary>
    /// Gets the original XML path.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the underlying XML document.
    /// </summary>
    public XmlDocument Document { get; }

    /// <summary>
    /// Loads a Rekordbox XML from the given path.
    /// </summary>
    /// <param name="xmlPath">The path to the XML file.</param>
    /// <returns>A <see cref="RekordboxXmlLibrary"/> instance.</returns>
    public static RekordboxXmlLibrary Load(string xmlPath)
    {
        var doc = new XmlDocument();
        doc.Load(xmlPath);
        return new RekordboxXmlLibrary(xmlPath, doc);
    }

    /// <summary>
    /// Decodes a file URI (file:/// prefix and URL-encoded characters) into a local path.
    /// </summary>
    /// <param name="raw">The raw value from the XML Location attribute.</param>
    /// <returns>The decoded file path.</returns>
    public static string DecodeFileUri(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return raw;
        }

        var cleaned = raw.Replace(Constants.LocalFileUriPrefix, string.Empty);
        return Uri.UnescapeDataString(cleaned);
    }

    /// <summary>
    /// Updates the Entries attribute on a playlist node.
    /// </summary>
    /// <param name="playlistNode">The playlist node to update.</param>
    /// <param name="count">The number of tracks.</param>
    public static void UpdatePlaylistTracksCount(XmlElement playlistNode, int count)
    {
        playlistNode.SetAttribute(Constants.EntriesAttributeName, count.ToString());
    }

    /// <summary>
    /// Returns the LIBRARY MANAGEMENT folder node.
    /// </summary>
    /// <returns>The LIBRARY MANAGEMENT folder node, or null if not found.</returns>
    public XmlNode? GetLibraryManagementFolder()
    {
        // DJ_PLAYLISTS/PLAYLISTS/NODE/NODE with Name="LIBRARY MANAGEMENT"
        return this.Document.SelectSingleNode($"/DJ_PLAYLISTS/PLAYLISTS/NODE/NODE[@Name='{Constants.LibraryManagement}']");
    }

    /// <summary>
    /// Returns the root playlists node.
    /// </summary>
    /// <returns>The root playlists folder node, or null if not found.</returns>
    public XmlNode? GetPlaylistsRoot()
    {
        return this.Document.SelectSingleNode($"/DJ_PLAYLISTS/PLAYLISTS/NODE[@Type='0' and @Name='{Constants.RootPlaylistName}']");
    }

    /// <summary>
    /// Enumerates collection tracks from the XML.
    /// </summary>
    /// <returns>An enumeration of TRACK elements in the collection.</returns>
    public IEnumerable<XmlElement> GetCollectionTracks()
    {
        var tracks = this.Document.SelectNodes("/DJ_PLAYLISTS/COLLECTION/TRACK");
        if (tracks == null)
        {
            yield break;
        }

        foreach (XmlElement el in tracks)
        {
            yield return el;
        }
    }

    /// <summary>
    /// Retrieves a collection track element by TrackID.
    /// </summary>
    /// <param name="trackId">The Rekordbox TrackID.</param>
    /// <returns>The track element, or null if not found.</returns>
    public XmlNode? GetTrackElementById(string trackId)
    {
        return this.Document.SelectSingleNode($"/DJ_PLAYLISTS/COLLECTION/TRACK[@TrackID='{trackId}']");
    }

    /// <summary>
    /// Enumerates track references inside the LIBRARY MANAGEMENT/Delete playlist.
    /// </summary>
    /// <returns>Track elements listed in the Delete playlist.</returns>
    public IEnumerable<XmlElement> GetTracksToDelete()
    {
        var libraryManagementFolder = this.GetLibraryManagementFolder();
        if (libraryManagementFolder == null)
        {
            yield break;
        }

        var pl = libraryManagementFolder.SelectSingleNode($"NODE[@Name='{Constants.DeletePlaylistName}']");
        if (pl == null)
        {
            yield break;
        }

        var tracks2 = pl.SelectNodes("TRACK");
        if (tracks2 == null)
        {
            yield break;
        }

        foreach (XmlElement t in tracks2)
        {
            yield return t;
        }
    }

    /// <summary>
    /// Creates or resets a child playlist inside LIBRARY MANAGEMENT.
    /// </summary>
    /// <param name="playlistName">Playlist name.</param>
    /// <returns>The created or reset playlist element.</returns>
    public XmlElement InitializeLibraryManagementChildPlaylist(string playlistName)
    {
        var libraryManagementFolder = this.GetLibraryManagementFolder() ?? throw new InvalidOperationException($"'{Constants.LibraryManagement}' playlist folder not found in XML.");
        if (libraryManagementFolder.SelectSingleNode($"NODE[@Name='{playlistName}']") is XmlElement existingPlaylist)
        {
            existingPlaylist.RemoveAll();
            SetPlaylistAttributes(existingPlaylist, playlistName);
            return existingPlaylist;
        }

        var newPlaylist = this.Document.CreateElement("NODE");
        SetPlaylistAttributes(newPlaylist, playlistName);
        libraryManagementFolder.AppendChild(newPlaylist);
        return newPlaylist;
    }

    /// <summary>
    /// Gets or creates a nested folder structure under LIBRARY MANAGEMENT by path segments.
    /// </summary>
    /// <param name="pathSegments">One or more folder names.</param>
    /// <returns>The deepest folder element.</returns>
    public XmlElement GetOrCreateFolder(params string[] pathSegments)
    {
        if (pathSegments == null || pathSegments.Length == 0)
        {
            throw new ArgumentException("Folder path must have at least one segment.");
        }

        var root = this.GetLibraryManagementFolder() as XmlElement ?? throw new InvalidOperationException($"'{Constants.LibraryManagement}' playlist folder not found in XML.");
        XmlElement current = root;
        foreach (var segment in pathSegments)
        {
            var next = current.SelectSingleNode($"NODE[@Type='0' and @Name='{segment}']") as XmlElement;
            if (next == null)
            {
                next = this.Document.CreateElement("NODE");
                next.SetAttribute(Constants.NameAttributeName, segment);
                next.SetAttribute(Constants.TypeAttributeName, "0");

                // Folders don't have KeyType or Entries; keep minimal attributes
                current.AppendChild(next);
            }

            current = next;
        }

        return current;
    }

    /// <summary>
    /// Gets or creates a playlist under the given folder.
    /// </summary>
    /// <param name="parentFolder">The parent folder element.</param>
    /// <param name="playlistName">Playlist name.</param>
    /// <returns>The playlist element.</returns>
    public XmlElement GetOrCreatePlaylist(XmlElement parentFolder, string playlistName)
    {
        if (parentFolder == null)
        {
            throw new ArgumentNullException(nameof(parentFolder));
        }

        var existing = parentFolder.SelectSingleNode($"NODE[@Type='1' and @Name='{playlistName}']") as XmlElement;
        if (existing != null)
        {
            return existing;
        }

        var newPlaylist = this.Document.CreateElement("NODE");
        SetPlaylistAttributes(newPlaylist, playlistName);
        parentFolder.AppendChild(newPlaylist);
        return newPlaylist;
    }

    /// <summary>
    /// Adds a track reference to a playlist node.
    /// </summary>
    /// <param name="playlistNode">The playlist element.</param>
    /// <param name="trackId">The collection track ID to reference.</param>
    public void AddTrackToPlaylist(XmlElement playlistNode, string trackId)
    {
        var trackNode = this.Document.CreateElement("TRACK");
        trackNode.SetAttribute(Constants.KeyAttributeName, trackId);
        playlistNode.AppendChild(trackNode);
    }

    /// <summary>
    /// Saves the XML to the specified path.
    /// </summary>
    /// <param name="outputPath">Destination path.</param>
    public void SaveAs(string outputPath)
    {
        this.Document.Save(outputPath);
    }

    /// <summary>
    /// Create a timestamped backup copy of the current XML next to the original, under LibTools4DJs_Backups.
    /// </summary>
    /// <returns>The full path to the created backup file.</returns>
    public string CreateBackupCopy()
    {
        var xmlDir = System.IO.Path.GetDirectoryName(this.Path)!;
        var backupDir = System.IO.Path.Combine(xmlDir, Constants.BackupFolderName);
        Directory.CreateDirectory(backupDir);
        var timestamp = DateTime.Now.ToString(Constants.DefaultTimestampFormat);
        var backupFile = System.IO.Path.Combine(backupDir, System.IO.Path.GetFileName(this.Path) + "." + timestamp + ".bak.xml");
        File.Copy(this.Path, backupFile, overwrite: false);
        return backupFile;
    }

    private static void SetPlaylistAttributes(XmlElement playlistNode, string playlistName)
    {
        playlistNode.SetAttribute(Constants.NameAttributeName, playlistName);
        playlistNode.SetAttribute(Constants.TypeAttributeName, "1");
        playlistNode.SetAttribute(Constants.KeyTypeAttributeName, "0");
        playlistNode.SetAttribute(Constants.EntriesAttributeName, "0");
    }
}
