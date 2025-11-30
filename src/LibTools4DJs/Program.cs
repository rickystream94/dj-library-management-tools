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
    private const string SyncMikFolderToRekordboxCommand = "sync-mik-folder-to-rekordbox";

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
            var handler = new SyncMikTagsToRekordboxHandler(console);
            await handler.RunAsync(library, whatIf);
        }, xmlOption, whatIfOption);

        // sync-rekordbox-playlists-to-mik command
        var mikVersionOption = new Option<string>(
            name: "--mik-version",
            getDefaultValue: () => "11.0",
            description: "Mixed In Key version (e.g., 11.0). Defaults to 11.0.");

        // --mik-db becomes optional; if omitted, we auto-resolve from USERPROFILE + version
        var mikDbOption = new Option<FileInfo?>(
            name: "--mik-db",
            description: "Path to Mixed In Key SQLite database (MIKStore.db). Optional; if omitted, the path will be auto-resolved from USERPROFILE and --mik-version.",
            parseArgument: result =>
            {
                var token = result.Tokens.SingleOrDefault()?.Value;
                if (string.IsNullOrWhiteSpace(token))
                {
                    // Optional; return null to trigger auto-resolution later
                    return null;
                }
                var fi = new FileInfo(token);
                if (!fi.Exists)
                {
                    result.ErrorMessage = $"MIK database file not found: {fi.FullName}";
                }
                return fi;
            })
        { IsRequired = false };

        var syncPlaylistsCmd = new Command(SyncRekordboxPlaylistsToMikCommand, "Replicate Rekordbox playlist/folder hierarchy into Mixed In Key database (no deletion of existing).")
        {
            xmlOption,
            mikDbOption,
            mikVersionOption,
            whatIfOption
        };
        syncPlaylistsCmd.SetHandler(async (FileInfo xml, FileInfo? mikDb, string mikVersion, bool whatIf) =>
        {
            await HandleMikCommandAsync(SyncRekordboxPlaylistsToMikCommand, xml, mikDb, mikVersion, whatIf,
                (console, library, mikPath, dryRun) => new SyncRekordboxPlaylistsToMikHandler(console).RunAsync(library, mikPath, dryRun));
        }, xmlOption, mikDbOption, mikVersionOption, whatIfOption);

        // sync-mik-folder-to-rekordbox command
        var mikFolderNameOption = new Option<string>(
            name: "--mik-folder",
            description: $"Name of the Mixed In Key folder to mirror into Rekordbox XML under {Constants.SyncFromMikFolderName}",
            parseArgument: result =>
            {
                var token = result.Tokens.SingleOrDefault()?.Value;
                if (string.IsNullOrWhiteSpace(token))
                {
                    result.ErrorMessage = "--mik-folder is required";
                    return null!;
                }
                return token;
            }) { IsRequired = true };

        var syncMikFolderCmd = new Command(SyncMikFolderToRekordboxCommand, "Mirror a Mixed In Key folder hierarchy back into Rekordbox XML under a dedicated folder; idempotent and supports --what-if.")
        {
            xmlOption,
            mikDbOption,
            mikVersionOption,
            mikFolderNameOption,
            whatIfOption
        };
        syncMikFolderCmd.SetHandler(async (FileInfo xml, FileInfo? mikDb, string mikVersion, string mikFolder, bool whatIf) =>
        {
            await HandleMikCommandAsync(SyncMikFolderToRekordboxCommand, xml, mikDb, mikVersion, whatIf,
                (console, library, mikPath, dryRun) => new SyncMikFolderToRekordboxHandler(console).RunAsync(library, mikPath, mikFolder, dryRun),
                extraParams: new[] { (mikFolderNameOption.Name, (object?)mikFolder) });
        }, xmlOption, mikDbOption, mikVersionOption, mikFolderNameOption, whatIfOption);

        root.AddCommand(deleteCmd);
        root.AddCommand(syncCmd);
        root.AddCommand(syncPlaylistsCmd);
        root.AddCommand(syncMikFolderCmd);

        return await root.InvokeAsync(args);
    }

    private static string? ResolveMikDbPath(ConsoleLogger log, FileInfo? mikDb, string mikVersion)
    {
        if (mikDb is not null)
            return mikDb.FullName;

        var userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            log.Error("USERPROFILE environment variable is not set. Cannot auto-resolve MIK database path.");
            return null;
        }
        var resolved = Path.Combine(
            userProfile,
            "AppData",
            "Local",
            "Mixed In Key",
            "Mixed In Key",
            mikVersion,
            "MIKStore.db");
        if (!File.Exists(resolved))
        {
            log.Error($"Auto-resolved MIK database file not found: {resolved}. Provide --mik-db explicitly or adjust --mik-version.");
            return null;
        }
        return resolved;
    }

    private static void PrintInvocation(ConsoleLogger log, string command, params (string Name, object? Value)[] args)
    {
        log.PrintCommandInvocation(command, args);
    }

    private static async Task HandleMikCommandAsync(
        string commandName,
        FileInfo xml,
        FileInfo? mikDb,
        string mikVersion,
        bool whatIf,
        Func<ConsoleLogger, RekordboxXmlLibrary, string, bool, Task> handlerInvoker,
        (string Name, object? Value)[]? extraParams = null)
    {
        var console = new ConsoleLogger();
        var resolvedMikDbPath = ResolveMikDbPath(console, mikDb, mikVersion);
        if (resolvedMikDbPath is null) return;

        var paramList = new List<(string Name, object? Value)>
        {
            ("--xml", xml.FullName),
            ("--mik-db", resolvedMikDbPath),
            ("--mik-version", mikVersion),
            ("--what-if", whatIf)
        };
        if (extraParams is not null)
            paramList.AddRange(extraParams);

        PrintInvocation(console, commandName, paramList.ToArray());

        var library = RekordboxXmlLibrary.Load(xml.FullName);
        await handlerInvoker(console, library, resolvedMikDbPath, whatIf);
    }
}
