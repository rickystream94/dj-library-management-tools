// <copyright file="DeleteTracksHandler.cs" company="LibTools4DJs">
// Copyright (c) LibTools4DJs. All rights reserved.
// </copyright>

namespace LibTools4DJs.Handlers;

using LibTools4DJs.Logging;
using LibTools4DJs.Rekordbox;

/// <summary>
/// Deletes files for tracks listed in the Rekordbox 'LIBRARY MANAGEMENT/Delete' playlist.
/// </summary>
public sealed class DeleteTracksHandler
{
    private readonly ILogger log;
    private readonly RekordboxXmlLibrary library;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeleteTracksHandler"/> class.
    /// </summary>
    /// <param name="library">Rekordbox XML library abstraction.</param>
    /// <param name="log">Logger for user feedback.</param>
    public DeleteTracksHandler(RekordboxXmlLibrary library, ILogger log)
    {
        this.library = library ?? throw new ArgumentNullException(nameof(library));
        this.log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// Executes the deletion routine, honoring the What-If flag for dry runs.
    /// </summary>
    /// <param name="whatIf">When true, no files are deleted; actions are logged only.</param>
    /// <returns>A completed task once processing finishes.</returns>
    public Task RunAsync(bool whatIf)
    {
        var deletePlaylistTracks = this.library.GetTracksToDelete().ToList();
        if (deletePlaylistTracks.Count == 0)
        {
            this.log.Warn("No 'Delete' playlist or no tracks found. Aborting delete operation.");
            return Task.CompletedTask;
        }

        var trackIdsToDelete = deletePlaylistTracks
            .Select(t => t.GetAttribute(Constants.KeyAttributeName))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet();
        var tracksToDelete = this.library.GetCollectionTracks()
            .Where(t => trackIdsToDelete.Contains(t.GetAttribute(Constants.TrackIdAttributeName)))
            .ToList();

        this.log.Info($"Found {tracksToDelete.Count} tracks to be deleted.");
        if (tracksToDelete.Count == 0)
        {
            return Task.CompletedTask;
        }

        int deleted = 0, failed = 0, missing = 0;
        foreach (var track in tracksToDelete)
        {
            var location = track.GetAttribute(Constants.LocationAttributeName);
            var filePath = RekordboxXmlLibrary.DecodeFileUri(location);
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                this.log.Warn($"Track file not found: {filePath}");
                missing++;
                continue;
            }

            var name = Path.GetFileName(filePath);
            if (whatIf)
            {
                this.log.Info($"[WhatIf] Would delete: {name}");
                continue;
            }

            try
            {
                File.Delete(filePath);
                this.log.Info($"Deleted: {name}");
                deleted++;
            }
            catch (Exception ex)
            {
                this.log.Error($"Failed to delete {name}: {ex.Message}");
                failed++;
            }
        }

        this.log.Info($"Summary:\n" +
            $"Deleted: {deleted}\n" +
            $"Failed: {failed}\n" +
            $"Missing: {missing}\n" +
            $"Total targeted: {tracksToDelete.Count}");

        return Task.CompletedTask;
    }
}
