using System.Xml;

namespace LibTools4DJs.Rekordbox;

public sealed class RekordboxXmlLibrary
{
    private RekordboxXmlLibrary(string path, XmlDocument doc)
    {
        this.Path = path;
        this.Document = doc;
    }

    public string Path { get; }
    public XmlDocument Document { get; }

    public static RekordboxXmlLibrary Load(string xmlPath)
    {
        var doc = new XmlDocument();
        doc.Load(xmlPath);
        return new RekordboxXmlLibrary(xmlPath, doc);
    }

    public static string DecodeFileUri(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return raw;
        var cleaned = raw.Replace(Constants.LocalFileUriPrefix, string.Empty);
        return Uri.UnescapeDataString(cleaned);
    }

    public static void UpdatePlaylistTracksCount(XmlElement playlistNode, int count)
    {
        playlistNode.SetAttribute(Constants.EntriesAttributeName, count.ToString());
    }

    public XmlNode? GetLibraryManagementFolder()
    {
        // DJ_PLAYLISTS/PLAYLISTS/NODE/NODE with Name="LIBRARY MANAGEMENT"
        return this.Document.SelectSingleNode($"/DJ_PLAYLISTS/PLAYLISTS/NODE/NODE[@Name='{Constants.LibraryManagement}']");
    }

    public XmlNode? GetPlaylistsRoot()
    {
        return this.Document.SelectSingleNode($"/DJ_PLAYLISTS/PLAYLISTS/NODE[@Type='0' and @Name='{Constants.RootPlaylistName}']");
    }

    public IEnumerable<XmlElement> GetCollectionTracks()
    {
        var tracks = this.Document.SelectNodes("/DJ_PLAYLISTS/COLLECTION/TRACK");
        if (tracks == null)
            yield break;
        foreach (XmlElement el in tracks)
            yield return el;
    }

    public XmlNode? GetTrackElementById(string trackId)
    {
        return this.Document.SelectSingleNode($"/DJ_PLAYLISTS/COLLECTION/TRACK[@TrackID='{trackId}']");
    }

    public IEnumerable<XmlElement> GetTracksToDelete()
    {
        var libraryManagementFolder = GetLibraryManagementFolder();
        if (libraryManagementFolder == null)
            yield break;

        var pl = libraryManagementFolder.SelectSingleNode($"NODE[@Name='{Constants.DeletePlaylistName}']");
        if (pl == null)
            yield break;

        var tracks = pl.SelectNodes("TRACK");
        if (tracks == null)
            yield break;
        foreach (XmlElement t in tracks)
            yield return t;
    }

    public XmlElement InitializeLibraryManagementChildPlaylist(string playlistName)
    {
        var libraryManagementFolder = GetLibraryManagementFolder() ?? throw new InvalidOperationException($"'{Constants.LibraryManagement}' playlist folder not found in XML.");
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

    public XmlElement GetOrCreateFolder(params string[] pathSegments)
    {
        if (pathSegments == null || pathSegments.Length == 0)
            throw new ArgumentException("Folder path must have at least one segment.");

        var root = GetLibraryManagementFolder() as XmlElement ?? throw new InvalidOperationException($"'{Constants.LibraryManagement}' playlist folder not found in XML.");
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

    public XmlElement GetOrCreatePlaylist(XmlElement parentFolder, string playlistName)
    {
        if (parentFolder == null)
            throw new ArgumentNullException(nameof(parentFolder));
        var existing = parentFolder.SelectSingleNode($"NODE[@Type='1' and @Name='{playlistName}']") as XmlElement;
        if (existing != null)
            return existing;

        var newPlaylist = this.Document.CreateElement("NODE");
        SetPlaylistAttributes(newPlaylist, playlistName);
        parentFolder.AppendChild(newPlaylist);
        return newPlaylist;
    }

    public void AddTrackToPlaylist(XmlElement playlistNode, string trackId)
    {
        var trackNode = this.Document.CreateElement("TRACK");
        trackNode.SetAttribute(Constants.KeyAttributeName, trackId);
        playlistNode.AppendChild(trackNode);
    }

    public void SaveAs(string outputPath)
    {
        this.Document.Save(outputPath);
    }

    // Create a timestamped backup copy of the current XML next to the original, under LibTools4DJs_Backups.
    // Returns the full path to the created backup file.
    public string CreateBackupCopy()
    {
        var xmlDir = System.IO.Path.GetDirectoryName(this.Path)!;
        var backupDir = System.IO.Path.Combine(xmlDir, Constants.BackupFolderName);
            Directory.CreateDirectory(backupDir);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backupFile = System.IO.Path.Combine(backupDir, System.IO.Path.GetFileName(this.Path) + "." + timestamp + ".bak.xml");
        System.IO.File.Copy(this.Path, backupFile, overwrite: false);
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
