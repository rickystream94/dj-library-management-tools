using System.Xml;

namespace DJTools.Rekordbox;

public sealed class RekordboxXmlLibrary
{
    private RekordboxXmlLibrary(string path, XmlDocument doc)
    {
        Path = path;
        Document = doc;
    }

    public string Path { get; }
    public XmlDocument Document { get; }

    public static RekordboxXmlLibrary Load(string xmlPath)
    {
        var doc = new XmlDocument();
        doc.Load(xmlPath);
        return new RekordboxXmlLibrary(xmlPath, doc);
    }

    public XmlNode? FindLibraryManagementNode()
    {
        // DJ_PLAYLISTS/PLAYLISTS/NODE/NODE with Name="LIBRARY MANAGEMENT"
        return Document.SelectSingleNode("/DJ_PLAYLISTS/PLAYLISTS/NODE/NODE[@Name='LIBRARY MANAGEMENT']");
    }

    public IEnumerable<XmlElement> GetCollectionTracks()
    {
        var list = Document.SelectNodes("/DJ_PLAYLISTS/COLLECTION/TRACK");
        if (list == null) yield break;
        foreach (XmlElement el in list) yield return el;
    }

    public IEnumerable<XmlElement> GetPlaylistTrackElements(string playlistName)
    {
        var lm = FindLibraryManagementNode();
        if (lm == null) yield break;
        var pl = lm.SelectSingleNode($"NODE[@Name='{playlistName}']");
        if (pl == null) yield break;
        var tracks = pl.SelectNodes("TRACK");
        if (tracks == null) yield break;
        foreach (XmlElement t in tracks) yield return t;
    }

    public XmlElement EnsurePlaylist(string playlistName)
    {
        var lm = FindLibraryManagementNode() ?? throw new InvalidOperationException("'LIBRARY MANAGEMENT' playlist folder not found in XML.");
        var existing = lm.SelectSingleNode($"NODE[@Name='{playlistName}']") as XmlElement;
        if (existing != null)
        {
            existing.RemoveAll();
            existing.SetAttribute("Name", playlistName);
            existing.SetAttribute("Type", "1");
            existing.SetAttribute("KeyType", "0");
            existing.SetAttribute("Entries", "0");
            return existing;
        }
        var newNode = Document.CreateElement("NODE");
        newNode.SetAttribute("Name", playlistName);
        newNode.SetAttribute("Type", "1");
        newNode.SetAttribute("KeyType", "0");
        newNode.SetAttribute("Entries", "0");
        lm.AppendChild(newNode);
        return newNode;
    }

    public void AddTrackToPlaylist(XmlElement playlistNode, string trackId)
    {
        var trackNode = Document.CreateElement("TRACK");
        trackNode.SetAttribute("Key", trackId);
        playlistNode.AppendChild(trackNode);
    }

    public void UpdatePlaylistCount(XmlElement playlistNode, int count)
    {
        playlistNode.SetAttribute("Entries", count.ToString());
    }

    public void SaveAs(string outputPath)
    {
        Document.Save(outputPath);
    }
}
