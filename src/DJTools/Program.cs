using System.CommandLine;
using DJTools.Rekordbox;
using DJTools.Handlers;

namespace DJTools;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // Root command description
        var root = new RootCommand("DJTools - Rekordbox & Mixed In Key library management CLI");

        // Shared option
        var xmlOption = new Option<FileInfo>(name: "--xml", description: "Path to Rekordbox exported collection XML", parseArgument: result =>
        {
            var token = result.Tokens.SingleOrDefault()?.Value;
            if (string.IsNullOrWhiteSpace(token))
            {
                result.ErrorMessage = "--xml is required";
                return null!;
            }
            var fi = new FileInfo(token);
            if (!fi.Exists)
            {
                result.ErrorMessage = $"XML file not found: {fi.FullName}";
            }
            return fi;
        }) { IsRequired = true };

        // delete-tracks command
        var whatIfOption = new Option<bool>("--what-if", () => false, "Simulate deletions without removing any files");
        var deleteCmd = new Command("delete-tracks", "Delete tracks whose IDs live in 'LIBRARY MANAGEMENT > Delete' playlist")
        {
            xmlOption,
            whatIfOption
        };
        deleteCmd.SetHandler(async (FileInfo xml, bool whatIf) =>
        {
            var console = new ConsoleLogger();
            var library = RekordboxXmlLibrary.Load(xml.FullName);
            var handler = new DeleteTracksHandler(console);
            await handler.RunAsync(library, whatIf);
        }, xmlOption, whatIfOption);

        // sync-mik command
        var mappingOption = new Option<FileInfo?>("--mapping", "Optional path to EnergyLevelToColorCode.json mapping file (defaults to same folder as executable)");
        var syncCmd = new Command("sync-mik", "Sync Mixed In Key key (M4A only) & energy (color) info, producing a new XML & playlists")
        {
            xmlOption,
            mappingOption
        };
        syncCmd.SetHandler(async (FileInfo xml, FileInfo? mapping) =>
        {
            var console = new ConsoleLogger();
            var library = RekordboxXmlLibrary.Load(xml.FullName);
            var handler = new SyncMixedInKeyHandler(console);
            await handler.RunAsync(library, mapping?.FullName);
        }, xmlOption, mappingOption);

        root.AddCommand(deleteCmd);
        root.AddCommand(syncCmd);

        return await root.InvokeAsync(args);
    }
}
