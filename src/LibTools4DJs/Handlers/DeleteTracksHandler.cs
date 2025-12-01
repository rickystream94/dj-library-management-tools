using LibTools4DJs.Logging;
using LibTools4DJs.Rekordbox;

namespace LibTools4DJs.Handlers;

public sealed class DeleteTracksHandler
{
    private readonly ILogger _log;
    private readonly RekordboxXmlLibrary _library;

    public DeleteTracksHandler(RekordboxXmlLibrary library, ILogger log)
    {
        this._library = library ?? throw new ArgumentNullException(nameof(library));
        this._log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public Task RunAsync(bool whatIf)
    {
        var deletePlaylistTracks = this._library.GetTracksToDelete().ToList();
        if (deletePlaylistTracks.Count == 0)
        {
            this._log.Warn("No 'Delete' playlist or no tracks found. Aborting delete operation.");
            return Task.CompletedTask;
        }

        var trackIdsToDelete = deletePlaylistTracks
            .Select(t => t.GetAttribute(Constants.KeyAttributeName))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet();
        var tracksToDelete = this._library.GetCollectionTracks()
            .Where(t => trackIdsToDelete.Contains(t.GetAttribute(Constants.TrackIdAttributeName)))
            .ToList();

        this._log.Info($"Found {tracksToDelete.Count} tracks to be deleted.");
        if (tracksToDelete.Count == 0)
            return Task.CompletedTask;

        int deleted = 0, failed = 0, missing = 0;
        foreach (var track in tracksToDelete)
        {
            var location = track.GetAttribute(Constants.LocationAttributeName);
            var filePath = RekordboxXmlLibrary.DecodeFileUri(location);
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                this._log.Warn($"Track file not found: {filePath}");
                missing++;
                continue;
            }

            var name = Path.GetFileName(filePath);
            if (whatIf)
            {
                this._log.Info($"[WhatIf] Would delete: {name}");
                continue;
            }

            try
            {
                File.Delete(filePath);
                this._log.Info($"Deleted: {name}");
                deleted++;
            }
            catch (Exception ex)
            {
                this._log.Error($"Failed to delete {name}: {ex.Message}");
                failed++;
            }
        }

        this._log.Info($"Summary:\n" +
            $"Deleted: {deleted}\n" +
            $"Failed: {failed}\n" +
            $"Missing: {missing}\n" +
            $"Total targeted: {tracksToDelete.Count}");

        return Task.CompletedTask;
    }
}
