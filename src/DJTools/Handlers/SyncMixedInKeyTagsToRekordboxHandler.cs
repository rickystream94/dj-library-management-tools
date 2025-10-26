using DJTools.Rekordbox;
using System.Text.Json;
using System.Text.RegularExpressions;
using TagLibFile = TagLib.File;
using File = System.IO.File;

namespace DJTools.Handlers;

public sealed class SyncMixedInKeyTagsToRekordboxHandler
{
    private readonly ILogger _log;
    private readonly Regex EnergyLevelRegex = new(@"Energy (\d{1,2})", RegexOptions.Compiled);
    private readonly Regex InitialKeyRegex = new(@"^\\d{1,2}[A-G]$", RegexOptions.Compiled);

    public SyncMixedInKeyTagsToRekordboxHandler(ILogger log) => _log = log;

    public Task RunAsync(RekordboxXmlLibrary library)
    {
        var energyLevelToColourCode = LoadEnergyLevelToColourMapping();
        var allTracks = library.GetCollectionTracks().ToList();
        _ = library.GetLibraryManagementFolder() ?? throw new InvalidOperationException($"'{Constants.LibraryManagement}' playlist folder not found.");

        // Create custom playlists the fixed tracks will be added to, so it's easier to re-import them in Rekordbox
        var keyAnalysisPlaylist = library.InitializePlaylist(Constants.MIKKeyAnalysis);
        var energyAnalysisPlaylist = library.InitializePlaylist(Constants.MIKEnergyAnalysis);

        int fixedKey = 0, fixedColor = 0, missingEnergy = 0, missingKey = 0, skippedNonM4A = 0, missingFile = 0;

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
                        track.SetAttribute(Constants.ColourAttributeName, expectedColor);
                        library.AddTrackToPlaylist(energyAnalysisPlaylist, trackId);
                        fixedColor++;
                        _log.Info($"Updated color for energy {energyLevel} on '{trackFileName}' -> {expectedColor}");
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
                track.SetAttribute(Constants.TonalityAttributeName, initialKey);
                library.AddTrackToPlaylist(keyAnalysisPlaylist, trackId);
                fixedKey++;
                _log.Info($"Fixed tonality for '{trackFileName}' -> {initialKey}");
            }
        }

        RekordboxXmlLibrary.UpdatePlaylistTracksCount(keyAnalysisPlaylist, fixedKey);
        RekordboxXmlLibrary.UpdatePlaylistTracksCount(energyAnalysisPlaylist, fixedColor);

        string outputFilePath = Path.Combine(Path.GetDirectoryName(library.Path)!, $"rekordbox_collection_{DateTime.Now:yyyy-MM-dd_HH-mm}.xml");
        library.SaveAs(outputFilePath);

        _log.Info($"\nDone!\nTracks processed: {allTracks.Count}\nFixed key: {fixedKey}\nFixed color: {fixedColor}\nMissing key: {missingKey}\nMissing energy: {missingEnergy}\nNon-M4A skipped for key: {skippedNonM4A}\nMissing files: {missingFile}. New XML: {outputFilePath}");
        return Task.CompletedTask;
    }

    private static Dictionary<int,string> LoadEnergyLevelToColourMapping()
    {
        var path = Path.Combine(AppContext.BaseDirectory, Constants.EnergyLevelToColourCodeMappingFileName);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"{Constants.EnergyLevelToColourCodeMappingFileName} file not found");
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<int, string>>(json) ?? new();
        }
        catch
        {
            throw new InvalidOperationException($"Failed to load or parse {Constants.EnergyLevelToColourCodeMappingFileName}");
        }
    }
}
