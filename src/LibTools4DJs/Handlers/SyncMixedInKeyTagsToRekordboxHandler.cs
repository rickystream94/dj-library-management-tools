using LibTools4DJs.Rekordbox;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using TagLibFile = TagLib.File;
using File = System.IO.File;
using LibTools4DJs.Logging;

namespace LibTools4DJs.Handlers;

public sealed class SyncMikTagsToRekordboxHandler
{
    private readonly ILogger _log;
    private readonly Regex EnergyLevelRegex = new(@"Energy (\d{1,2})", RegexOptions.Compiled);
    private readonly Regex InitialKeyRegex = new(@"^\d{1,2}[A-G]$", RegexOptions.Compiled);

    public SyncMikTagsToRekordboxHandler(ILogger log) => _log = log;

    public Task RunAsync(RekordboxXmlLibrary library, bool whatIf)
    {
        // Create backup before any modifications when not in what-if
        if (!whatIf)
        {
            var backupFile = library.CreateBackupCopy();
            _log.Info($"Creating backup of Rekordbox XML: {backupFile}");
        }
        var energyLevelToColourCode = GetEnergyLevelToColourMapping();
        var allTracks = library.GetCollectionTracks().ToList();
        _ = library.GetLibraryManagementFolder() ?? throw new InvalidOperationException($"'{Constants.LibraryManagement}' playlist folder not found.");

        XmlElement? keyAnalysisPlaylist = null;
        XmlElement? energyAnalysisPlaylist = null;
        if (!whatIf)
        {
            // Create custom playlists the fixed tracks will be added to, so it's easier to re-import them in Rekordbox
            keyAnalysisPlaylist = library.InitializeLibraryManagementChildPlaylist(Constants.MIKKeyAnalysis);
            energyAnalysisPlaylist = library.InitializeLibraryManagementChildPlaylist(Constants.MIKEnergyAnalysis);
        }

        if (whatIf)
        {
            _log.Info("[WhatIf] Simulating sync. No XML modifications or output file will be written.");
        }

        int fixedKey = 0, fixedColor = 0, missingEnergy = 0, missingKey = 0, skippedNonM4A = 0, missingFile = 0, failedToReadTags = 0;

        foreach (var track in allTracks)
        {
            var trackId = track.GetAttribute(Constants.TrackIdAttributeName);
            var location = track.GetAttribute(Constants.LocationAttributeName);
            var path = RekordboxXmlLibrary.DecodeFileUri(location);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                _log.Warn($"Track not found: {path}");
                missingFile++;
                continue;
            }

            var trackFileName = Path.GetFileName(path);
            TagLibFile? media = null;
            try
            {
                media = TagLibFile.Create(path);
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to read tag for '{trackFileName}': {ex.Message}");
                failedToReadTags++;
                continue;
            }

            // The assumption here is that we have setup MIK to write the key and energy level at the beginning of the 'Comment' tag, like "1A - Energy 6"
            var comment = media.Tag.Comment ?? string.Empty;
            var initialKey = comment.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

            // ENERGY
            // For energy level, we can't trust that it's always going to be last in the comments, as the MIK comment precedes any other pre-existing comment.
            // Extract the energy level using regex.
            var energyMatch = this.EnergyLevelRegex.Match(comment);
            if (energyMatch.Success)
            {
                // Map energy level from MIK with color codes used by Rekordbox
                var energyLevel = Convert.ToInt32(energyMatch.Groups[1].Value);
                if (energyLevelToColourCode.TryGetValue(energyLevel, out var expectedColor))
                {
                    var currentColor = track.GetAttribute(Constants.ColourAttributeName);
                    if (!string.Equals(currentColor, expectedColor, StringComparison.OrdinalIgnoreCase))
                    {
                        if (whatIf)
                        {
                            _log.Info($"[WhatIf] Updating track color for '{trackFileName}': {currentColor} -> {expectedColor} (energy {energyLevel})");
                        }
                        else
                        {
                            track.SetAttribute(Constants.ColourAttributeName, expectedColor);
                            library.AddTrackToPlaylist(energyAnalysisPlaylist!, trackId);
                            _log.Info($"Updated track color for '{trackFileName}': {currentColor} -> {expectedColor} (energy {energyLevel})");
                        }
                        fixedColor++;
                    }
                }
            }
            else
            {
                missingEnergy++;
                _log.Warn($"No energy level in comment for '{trackFileName}'");
            }

            // KEY (M4A only)
            var kind = track.GetAttribute(Constants.KindAttributeName);
            if (!string.Equals(kind, "M4A File", StringComparison.OrdinalIgnoreCase))
            {
                skippedNonM4A++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(initialKey) || !this.InitialKeyRegex.IsMatch(initialKey))
            {
                missingKey++;
                _log.Warn($"Invalid or missing key token for '{trackFileName}'");
                continue;
            }

            var currentTonality = track.GetAttribute(Constants.TonalityAttributeName);
            if (!string.Equals(currentTonality, initialKey, StringComparison.OrdinalIgnoreCase))
            {
                if (whatIf)
                {
                    _log.Info($"[WhatIf] Would fix tonality for '{trackFileName}': {currentTonality} -> {initialKey}");
                }
                else
                {
                    track.SetAttribute(Constants.TonalityAttributeName, initialKey);
                    library.AddTrackToPlaylist(keyAnalysisPlaylist!, trackId);
                    _log.Info($"Fixed tonality for '{trackFileName}': {currentTonality} -> {initialKey}");
                }
                fixedKey++;
            }
        }

        if (!whatIf)
        {
            RekordboxXmlLibrary.UpdatePlaylistTracksCount(keyAnalysisPlaylist!, fixedKey);
            RekordboxXmlLibrary.UpdatePlaylistTracksCount(energyAnalysisPlaylist!, fixedColor);

            library.SaveAs(library.Path);
            _log.Info($"\nDone!\nTracks processed: {allTracks.Count}\nFixed key: {fixedKey}\nFixed color: {fixedColor}\nMissing key: {missingKey}\nMissing energy: {missingEnergy}\nNon-M4A skipped for key: {skippedNonM4A}\nMissing files: {missingFile}\nFailed to read tags: {failedToReadTags}\nXML updated in place: {library.Path}");
        }
        else
        {
            _log.Info($"\n[WhatIf Complete]\nTracks analyzed: {allTracks.Count}\nWould fix key: {fixedKey}\nWould fix color: {fixedColor}\nMissing key: {missingKey}\nMissing energy: {missingEnergy}\nNon-M4A skipped for key: {skippedNonM4A}\nMissing files: {missingFile}\nFailed to read tags: {failedToReadTags}\nNo XML written.");
        }
        return Task.CompletedTask;
    }

    private static Dictionary<int,string> GetEnergyLevelToColourMapping()
    {
        string configurationPath = Path.Combine(AppContext.BaseDirectory, "Configuration", Constants.EnergyLevelToColourCodeMappingFileName);

        if (!File.Exists(configurationPath))
        {
            throw new InvalidOperationException($"{Constants.EnergyLevelToColourCodeMappingFileName} file not found. Expected at '{configurationPath}'.");
        }

        try
        {
            var json = File.ReadAllText(configurationPath);
            return JsonSerializer.Deserialize<Dictionary<int, string>>(json) ?? new();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load or parse {Constants.EnergyLevelToColourCodeMappingFileName}: {ex.Message}");
        }
    }
}
