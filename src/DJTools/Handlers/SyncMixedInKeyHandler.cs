using DJTools.Rekordbox;
using TagLib;
using System.Text.Json;

namespace DJTools.Handlers;

public sealed class SyncMixedInKeyHandler
{
    private readonly ILogger _log;
    public SyncMixedInKeyHandler(ILogger log) => _log = log;

    public Task RunAsync(RekordboxXmlLibrary library, string? mappingPath)
    {
        var mapping = LoadMapping(mappingPath);
        var tracks = library.GetCollectionTracks().ToList();
        var lmNode = library.FindLibraryManagementNode() ?? throw new InvalidOperationException("'LIBRARY MANAGEMENT' playlist folder not found.");

        var keyPlaylist = library.EnsurePlaylist("MIK Key Analysis");
        var energyPlaylist = library.EnsurePlaylist("MIK Energy Level Analysis");

        int fixedKey = 0, fixedColor = 0, missingEnergy = 0, missingKey = 0, skippedNonM4A = 0, missingFile = 0;

        foreach (var track in tracks)
        {
            var trackId = track.GetAttribute("TrackID");
            var location = track.GetAttribute("Location");
            var path = DecodeFileUri(location);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                _log.Warn($"Track not found: {path}");
                missingFile++;
                continue;
            }

            var name = Path.GetFileName(path);
            File? media = null;
            try { media = File.Create(path); }
            catch (Exception ex)
            {
                _log.Error($"Failed to read tag for '{name}': {ex.Message}");
                continue;
            }

            var comment = media.Tag.Comment ?? string.Empty;
            var initialKey = comment.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

            // ENERGY
            var energyMatch = System.Text.RegularExpressions.Regex.Match(comment, "Energy (\\d{1,2})");
            if (energyMatch.Success)
            {
                var energyLevel = energyMatch.Groups[1].Value;
                if (mapping.TryGetValue(energyLevel, out var expectedColor))
                {
                    var currentColor = track.GetAttribute("Colour");
                    if (!string.Equals(currentColor, expectedColor, StringComparison.OrdinalIgnoreCase))
                    {
                        track.SetAttribute("Colour", expectedColor);
                        library.AddTrackToPlaylist(energyPlaylist, trackId);
                        fixedColor++;
                        _log.Info($"Updated color for energy {energyLevel} on '{name}' -> {expectedColor}");
                    }
                }
            }
            else
            {
                missingEnergy++;
                _log.Warn($"No energy level in comment for '{name}'");
            }

            // KEY (M4A only)
            var kind = track.GetAttribute("Kind");
            if (!string.Equals(kind, "M4A File", StringComparison.OrdinalIgnoreCase))
            {
                skippedNonM4A++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(initialKey) || !System.Text.RegularExpressions.Regex.IsMatch(initialKey, "^\\d{1,2}[A-G]$"))
            {
                missingKey++;
                _log.Warn($"Invalid or missing key token for '{name}'");
                continue;
            }

            var currentTonality = track.GetAttribute("Tonality");
            if (!string.Equals(currentTonality, initialKey, StringComparison.OrdinalIgnoreCase))
            {
                track.SetAttribute("Tonality", initialKey);
                library.AddTrackToPlaylist(keyPlaylist, trackId);
                fixedKey++;
                _log.Info($"Fixed tonality for '{name}' -> {initialKey}");
            }
        }

        library.UpdatePlaylistCount(keyPlaylist, fixedKey);
        library.UpdatePlaylistCount(energyPlaylist, fixedColor);

        var output = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(library.Path)!, $"rekordbox_collection_{DateTime.Now:yyyy-MM-dd_HH-mm}.xml");
        library.SaveAs(output);

        _log.Info($"\nDone. Tracks processed: {tracks.Count}. Fixed key: {fixedKey}. Fixed color: {fixedColor}. Missing key: {missingKey}. Missing energy: {missingEnergy}. Non-M4A skipped for key: {skippedNonM4A}. Missing files: {missingFile}. New XML: {output}");
        return Task.CompletedTask;
    }

    private static Dictionary<string,string> LoadMapping(string? mappingPath)
    {
        var path = mappingPath ?? System.IO.Path.Combine(AppContext.BaseDirectory, "EnergyLevelToColorCode.json");
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>();
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string,string>>(json) ?? new();
        }
        catch
        {
            return new Dictionary<string,string>();
        }
    }

    private static string DecodeFileUri(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        var cleaned = raw.Replace("file://localhost/", string.Empty);
        return Uri.UnescapeDataString(cleaned);
    }
}
