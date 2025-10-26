using DJTools.Rekordbox;
using System.Xml;

namespace DJTools.Handlers;

public sealed class DeleteTracksHandler
{
    private readonly ILogger _log;
    public DeleteTracksHandler(ILogger log) => _log = log;

    public Task RunAsync(RekordboxXmlLibrary library, bool whatIf)
    {
        var deletePlaylistTracks = library.GetPlaylistTrackElements("Delete").ToList();
        if (!deletePlaylistTracks.Any())
        {
            _log.Warn("No 'Delete' playlist or no tracks found. Aborting delete operation.");
            return Task.CompletedTask;
        }

        var trackIds = deletePlaylistTracks.Select(t => t.GetAttribute("Key")).Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet();
        var collectionTracks = library.GetCollectionTracks().Where(t => trackIds.Contains(t.GetAttribute("TrackID"))).ToList();

        _log.Info($"Found {collectionTracks.Count} tracks in collection matching Delete playlist IDs.");
        if (collectionTracks.Count == 0) return Task.CompletedTask;

        int deleted = 0, failed = 0, missing = 0;
        foreach (var track in collectionTracks)
        {
            var location = track.GetAttribute("Location");
            var filePath = DecodeFileUri(location);
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                _log.Warn($"Track file not found: {filePath}");
                missing++;
                continue;
            }

            var name = Path.GetFileName(filePath);
            if (whatIf)
            {
                _log.Info($"[WhatIf] Would delete: {name}");
                continue;
            }

            try
            {
                File.Delete(filePath);
                _log.Info($"Deleted: {name}");
                deleted++;
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to delete {name}: {ex.Message}");
                failed++;
            }
        }

        _log.Info($"Summary -> Deleted: {deleted}, Failed: {failed}, Missing: {missing}, Total targeted: {collectionTracks.Count}");
        return Task.CompletedTask;
    }

    private static string DecodeFileUri(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        var cleaned = raw.Replace("file://localhost/", string.Empty);
        return Uri.UnescapeDataString(cleaned);
    }
}
