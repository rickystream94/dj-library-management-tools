using System.CommandLine;
using DJTools.Rekordbox;
using DJTools.Handlers;

namespace DJTools;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // Root command description
        var root = new RootCommand("Library Management Tools for DJs - CLI");

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
        var whatIfOption = new Option<bool>("--what-if", () => false, description: "Simulate deletions without removing any files");
        var deleteCmd = new Command("delete-tracks", $"Bulk delete tracks marked for deletion, if they're added to the '{Constants.LibraryManagement} > {Constants.DeletePlaylistName}' playlist")
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

        // sync-mik-to-rekordbox command
        var syncCmd = new Command("sync-mik-to-rekordbox", "Sync Mixed In Key key tag for M4A tracks & energy level back into Rekordbox XML collection. This also sets a colour to the track based on the energy level.")
        {
            xmlOption
        };
        syncCmd.SetHandler(async (FileInfo xml) =>
        {
            var console = new ConsoleLogger();
            var library = RekordboxXmlLibrary.Load(xml.FullName);
            var handler = new SyncMixedInKeyTagsToRekordboxHandler(console);
            await handler.RunAsync(library);
        }, xmlOption);

        root.AddCommand(deleteCmd);
        root.AddCommand(syncCmd);

        return await root.InvokeAsync(args);
    }
}
