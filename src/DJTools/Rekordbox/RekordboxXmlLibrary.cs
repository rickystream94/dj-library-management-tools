using System.Xml;

namespace DJTools.Rekordbox;

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

    public IEnumerable<XmlElement> GetCollectionTracks()
    {
        var tracks = this.Document.SelectNodes("/DJ_PLAYLISTS/COLLECTION/TRACK");
        if (tracks == null)
            yield break;
        foreach (XmlElement el in tracks)
            yield return el;
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

    public XmlElement InitializePlaylist(string playlistName)
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

    private static void SetPlaylistAttributes(XmlElement playlistNode, string playlistName)
    {
        playlistNode.SetAttribute(Constants.NameAttributeName, playlistName);
        playlistNode.SetAttribute(Constants.TypeAttributeName, "1");
        playlistNode.SetAttribute(Constants.KeyTypeAttributeName, "0");
        playlistNode.SetAttribute(Constants.EntriesAttributeName, "0");
    }
}
