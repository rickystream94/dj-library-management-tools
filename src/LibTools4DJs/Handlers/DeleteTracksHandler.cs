using LibTools4DJs.Rekordbox;

namespace LibTools4DJs.Handlers;

public sealed class DeleteTracksHandler
{
    private readonly ILogger _log;
    public DeleteTracksHandler(ILogger log) => _log = log;

    public Task RunAsync(RekordboxXmlLibrary library, bool whatIf)
    {
        var deletePlaylistTracks = library.GetTracksToDelete().ToList();
        if (deletePlaylistTracks.Count == 0)
        {
            _log.Warn("No 'Delete' playlist or no tracks found. Aborting delete operation.");
            return Task.CompletedTask;
        }

        var trackIdsToDelete = deletePlaylistTracks
            .Select(t => t.GetAttribute(Constants.KeyAttributeName))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet();
        var tracksToDelete = library.GetCollectionTracks()
            .Where(t => trackIdsToDelete.Contains(t.GetAttribute(Constants.TrackIdAttributeName)))
            .ToList();

        _log.Info($"Found {tracksToDelete.Count} tracks to be deleted.");
        if (tracksToDelete.Count == 0)
            return Task.CompletedTask;

        int deleted = 0, failed = 0, missing = 0;
        foreach (var track in tracksToDelete)
        {
            var location = track.GetAttribute(Constants.LocationAttributeName);
            var filePath = RekordboxXmlLibrary.DecodeFileUri(location);
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

        _log.Info($"Summary:\nDeleted: {deleted}\nFailed: {failed}\nMissing: {missing}\nTotal targeted: {tracksToDelete.Count}");
        return Task.CompletedTask;
    }
}
