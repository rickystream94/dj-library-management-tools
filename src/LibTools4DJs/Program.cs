using System.CommandLine;
using LibTools4DJs.Rekordbox;
using LibTools4DJs.Handlers;
using LibTools4DJs.Logging;

namespace LibTools4DJs;

internal static class Program
{
    private const string DeleteTracksCommand = "delete-tracks";
    private const string SyncMikToRekordboxCommand = "sync-mik-tags-to-rekordbox";
    private const string SyncRekordboxPlaylistsToMikCommand = "sync-rekordbox-playlists-to-mik";

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
        var whatIfOption = new Option<bool>("--what-if", () => false, description: "Simulate command without applying changes (no file deletions or XML modifications)");
        var deleteCmd = new Command(DeleteTracksCommand, $"Bulk delete tracks marked for deletion, if they're added to the '{Constants.LibraryManagement} > {Constants.DeletePlaylistName}' playlist")
        {
            xmlOption,
            whatIfOption
        };
        deleteCmd.SetHandler(async (FileInfo xml, bool whatIf) =>
        {
            var console = new ConsoleLogger();
            console.PrintCommandInvocation(DeleteTracksCommand,
                [
                    (xmlOption.Name, xml.FullName),
                    (whatIfOption.Name, whatIf)
                ]);
            var library = RekordboxXmlLibrary.Load(xml.FullName);
            var handler = new DeleteTracksHandler(console);
            await handler.RunAsync(library, whatIf);
        }, xmlOption, whatIfOption);

        // sync-mik-tags-to-rekordbox command
        var syncCmd = new Command(SyncMikToRekordboxCommand, "Sync Mixed In Key key tag for M4A tracks & energy level back into Rekordbox XML collection. This also sets a colour to the track based on the energy level.")
        {
            xmlOption,
            whatIfOption
        };
        syncCmd.SetHandler(async (FileInfo xml, bool whatIf) =>
        {
            var console = new ConsoleLogger();
            console.PrintCommandInvocation(SyncMikToRekordboxCommand,
                [
                    (xmlOption.Name, xml.FullName),
                    (whatIfOption.Name, whatIf)
                ]);
            var library = RekordboxXmlLibrary.Load(xml.FullName);
            var handler = new SyncMixedInKeyTagsToRekordboxHandler(console);
            await handler.RunAsync(library, whatIf);
        }, xmlOption, whatIfOption);

        // sync-rekordbox-playlists-to-mik command
        var mikDbOption = new Option<FileInfo>(name: "--mik-db", description: "Path to Mixed In Key SQLite database (MIKStore.db)", parseArgument: result =>
        {
            var token = result.Tokens.SingleOrDefault()?.Value;
            if (string.IsNullOrWhiteSpace(token))
            {
                result.ErrorMessage = "--mik-db is required";
                return null!;
            }
            var fi = new FileInfo(token);
            if (!fi.Exists)
            {
                result.ErrorMessage = $"MIK database file not found: {fi.FullName}";
            }
            return fi;
        }) { IsRequired = true };

        var syncPlaylistsCmd = new Command(SyncRekordboxPlaylistsToMikCommand, "Replicate Rekordbox playlist/folder hierarchy into Mixed In Key database (no deletion of existing).")
        {
            xmlOption,
            mikDbOption,
            whatIfOption
        };
        syncPlaylistsCmd.SetHandler(async (FileInfo xml, FileInfo mikDb, bool whatIf) =>
        {
            var console = new ConsoleLogger();
            console.PrintCommandInvocation(SyncRekordboxPlaylistsToMikCommand,
                [
                    (xmlOption.Name, xml.FullName),
                    (mikDbOption.Name, mikDb.FullName),
                    (whatIfOption.Name, whatIf)
                ]);
            var library = RekordboxXmlLibrary.Load(xml.FullName);
            var handler = new SyncRekordboxPlaylistsToMikHandler(console);
            await handler.RunAsync(library, mikDb.FullName, whatIf);
        }, xmlOption, mikDbOption, whatIfOption);

        root.AddCommand(deleteCmd);
        root.AddCommand(syncCmd);
        root.AddCommand(syncPlaylistsCmd);

        return await root.InvokeAsync(args);
    }
}
