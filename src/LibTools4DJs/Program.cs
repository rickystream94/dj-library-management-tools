using System.CommandLine;
using LibTools4DJs.Rekordbox;
using LibTools4DJs.Handlers;
using LibTools4DJs.Logging;

namespace LibTools4DJs;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // Root command description
        var root = new RootCommand("Library Management Tools for DJs - CLI");

        // Global/logging options
        var debugOption = new Option<bool>(Constants.DebugOption, () => false, description: "Enable verbose debug logs during execution.");
        var saveLogsOption = new Option<bool>(Constants.SaveLogsOption, () => false, description: "Persist all logs to LibTools4DJs_Logs/<timestamp>.log in working directory.");

        // Shared option
        var xmlOption = GetRekordboxXmlOption();

        // delete-tracks command
        var whatIfOption = new Option<bool>(Constants.WhatIfOption, () => false, description: "Simulate command without applying changes (no file deletions or XML modifications)");
        var deleteCmd = new Command(Constants.DeleteTracksCommand, $"Bulk delete tracks marked for deletion, if they're added to the '{Constants.LibraryManagement} > {Constants.DeletePlaylistName}' playlist")
        {
            xmlOption,
            whatIfOption,
            debugOption,
            saveLogsOption
        };
        deleteCmd.SetHandler(async (FileInfo xml, bool whatIf, bool debug, bool saveLogs) =>
        {
            var logPath = saveLogs ? InitLogFile(Constants.DeleteTracksCommand) : null;
            var console = new ConsoleLogger(debug, logPath);
            console.PrintCommandInvocation(Constants.DeleteTracksCommand,
                [
                    (xmlOption.Name, xml.FullName),
                    (whatIfOption.Name, whatIf),
                    (debugOption.Name, debug),
                    (saveLogsOption.Name, saveLogs)
                ]);
            var library = RekordboxXmlLibrary.Load(xml.FullName);
            var handler = new DeleteTracksHandler(library, console);
            await handler.RunAsync(whatIf);
            console.Info("Delete command completed.");
        }, xmlOption, whatIfOption, debugOption, saveLogsOption);

        // sync-mik-tags-to-rekordbox command
        var syncCmd = new Command(Constants.SyncMikTagsToRekordboxCommand, "Sync Mixed In Key key tag for M4A tracks & energy level back into Rekordbox XML collection. This also sets a colour to the track based on the energy level.")
        {
            xmlOption,
            whatIfOption,
            debugOption,
            saveLogsOption
        };
        syncCmd.SetHandler(async (FileInfo xml, bool whatIf, bool debug, bool saveLogs) =>
        {
            var logPath = saveLogs ? InitLogFile(Constants.SyncMikTagsToRekordboxCommand) : null;
            var console = new ConsoleLogger(debug, logPath);
            console.PrintCommandInvocation(Constants.SyncMikTagsToRekordboxCommand,
                [
                    (xmlOption.Name, xml.FullName),
                    (whatIfOption.Name, whatIf),
                    (debugOption.Name, debug),
                    (saveLogsOption.Name, saveLogs)
                ]);
            var library = RekordboxXmlLibrary.Load(xml.FullName);
            var handler = new SyncMikTagsToRekordboxHandler(library, console);
            await handler.RunAsync(whatIf, debug);
            console.Info("Sync MIK tags command completed.");
        }, xmlOption, whatIfOption, debugOption, saveLogsOption);

        // sync-rekordbox-library-to-mik command
        var mikVersionOption = new Option<string>(
            name: Constants.MikVersionOption,
            getDefaultValue: () => Constants.MikDefaultVersion,
            description: $"Mixed In Key version (e.g., {Constants.MikDefaultVersion}).");

        // --mik-db becomes optional; if omitted, we auto-resolve from USERPROFILE + version
        var mikDbOption = new Option<FileInfo?>(
            name: Constants.MikDbOption,
            description: $"Path to Mixed In Key SQLite database ({Constants.MikDatabaseFileName}). Optional; if omitted, the path will be auto-resolved from USERPROFILE and {Constants.MikVersionOption}.",
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

        var resetMikLibraryOption = new Option<bool>(Constants.ResetMikLibraryOption, () => false, description: "Before syncing, wipe MIK collections and memberships (preserving system rows: Sequence IS NULL AND IsLibrary = 1 AND ParentFolderId IS NULL). Use with caution.");

        var syncRekordboxLibraryToMikCmd = new Command(Constants.SyncRekordboxLibraryToMikCommand, "Replicate Rekordbox playlist/folder hierarchy into Mixed In Key database (additive). Optionally reset MIK library before syncing.")
        {
            xmlOption,
            mikDbOption,
            mikVersionOption,
            resetMikLibraryOption,
            whatIfOption,
            debugOption,
            saveLogsOption
        };
        syncRekordboxLibraryToMikCmd.SetHandler(async (FileInfo xml, FileInfo? mikDb, string mikVersion, bool whatIf, bool resetMikLibrary, bool debug, bool saveLogs) =>
        {
            await HandleMikCommandAsync(Constants.SyncRekordboxLibraryToMikCommand, xml, mikDb, mikVersion, whatIf,
                (console, library, mikPath, dryRun) => new SyncRekordboxPlaylistsToMikHandler(library, console).RunAsync(mikPath, dryRun, resetMikLibrary, debug),
                extraParams: new[] { (resetMikLibraryOption.Name, (object?)resetMikLibrary), (debugOption.Name, (object?)debug), (saveLogsOption.Name, (object?)saveLogs) },
                debugEnabled: debug,
                saveLogs: saveLogs);
        }, xmlOption, mikDbOption, mikVersionOption, whatIfOption, resetMikLibraryOption, debugOption, saveLogsOption);

        // sync-mik-folder-to-rekordbox command
        var mikFolderNameOption = new Option<string>(
            name: Constants.MikFolderOption,
            description: $"Name of the Mixed In Key folder to mirror into Rekordbox XML under {Constants.SyncFromMikFolderName}",
            parseArgument: result =>
            {
                var token = result.Tokens.SingleOrDefault()?.Value;
                if (string.IsNullOrWhiteSpace(token))
                {
                    result.ErrorMessage = $"{Constants.MikFolderOption} is required";
                    return null!;
                }
                return token;
            }) { IsRequired = true };

        var syncMikFolderToRekordboxCmd = new Command(Constants.SyncMikFolderToRekordboxCommand, $"Mirror a Mixed In Key folder hierarchy back into Rekordbox XML under a dedicated folder; idempotent and supports {Constants.WhatIfOption}.")
        {
            xmlOption,
            mikDbOption,
            mikVersionOption,
            mikFolderNameOption,
            whatIfOption,
            debugOption,
            saveLogsOption
        };
        syncMikFolderToRekordboxCmd.SetHandler(async (FileInfo xml, FileInfo? mikDb, string mikVersion, string mikFolder, bool whatIf, bool debug, bool saveLogs) =>
        {
            await HandleMikCommandAsync(Constants.SyncMikFolderToRekordboxCommand, xml, mikDb, mikVersion, whatIf,
                (console, library, mikPath, dryRun) => new SyncMikFolderToRekordboxHandler(library, console).RunAsync(mikPath, mikFolder, dryRun, debug),
                extraParams: new[] { (mikFolderNameOption.Name, (object?)mikFolder), (debugOption.Name, (object?)debug), (saveLogsOption.Name, (object?)saveLogs) },
                debugEnabled: debug,
                saveLogs: saveLogs);
        }, xmlOption, mikDbOption, mikVersionOption, mikFolderNameOption, whatIfOption, debugOption, saveLogsOption);

        root.AddCommand(deleteCmd);
        root.AddCommand(syncCmd);
        root.AddCommand(syncRekordboxLibraryToMikCmd);
        root.AddCommand(syncMikFolderToRekordboxCmd);

        return await root.InvokeAsync(args);
    }

    private static string? ResolveMikDbPath(ConsoleLogger log, FileInfo? mikDb, string mikVersion)
    {
        if (mikDb is not null)
            return mikDb.FullName;

        var userProfile = Environment.GetEnvironmentVariable(Constants.UserProfileVariableName);
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            log.Error($"{Constants.UserProfileVariableName} environment variable is not set. Cannot auto-resolve MIK database path.");
            return null;
        }

        var resolved = Path.Combine(
            userProfile,
            Constants.AppDataFolderName,
            Constants.LocalAppDataFolderName,
            Constants.MikFolderName,
            Constants.MikFolderName,
            mikVersion,
            Constants.MikDatabaseFileName);
        if (!File.Exists(resolved))
        {
            log.Error($"Auto-resolved MIK database file not found: {resolved}. Provide {Constants.MikDbOption} explicitly or adjust {Constants.MikVersionOption}.");
            return null;
        }
        return resolved;
    }

    private static void PrintInvocation(ConsoleLogger log, string command, params (string Name, object? Value)[] args)
    {
        log.PrintCommandInvocation(command, args);
    }

    private static string InitLogFile(string commandName)
    {
        var ts = DateTime.Now.ToString(Constants.DefaultTimestampFormat);
        var dir = Path.Combine(Environment.CurrentDirectory, Constants.LogsFolderName);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{commandName}_{ts}.log");
        return path;
    }

    private static async Task HandleMikCommandAsync(
        string commandName,
        FileInfo xml,
        FileInfo? mikDb,
        string mikVersion,
        bool whatIf,
        Func<ConsoleLogger, RekordboxXmlLibrary, string, bool, Task> handlerInvoker,
        (string Name, object? Value)[]? extraParams = null,
        bool debugEnabled = false,
        bool saveLogs = false)
    {
        var logPath = saveLogs ? InitLogFile(commandName) : null;
        var console = new ConsoleLogger(debugEnabled, logPath);
        var resolvedMikDbPath = ResolveMikDbPath(console, mikDb, mikVersion);
        if (resolvedMikDbPath is null) return;

        var paramList = new List<(string Name, object? Value)>
        {
            (Constants.RekordboxXmlOption, xml.FullName),
            (Constants.MikDbOption, resolvedMikDbPath),
            (Constants.MikVersionOption, mikVersion),
            (Constants.WhatIfOption, whatIf)
        };
        if (extraParams is not null)
            paramList.AddRange(extraParams);

        PrintInvocation(console, commandName, paramList.ToArray());

        var library = RekordboxXmlLibrary.Load(xml.FullName);
        await handlerInvoker(console, library, resolvedMikDbPath, whatIf);
        console.Info($"{commandName} command completed.");
    }

    private static Option<FileInfo> GetRekordboxXmlOption()
    {
        return new Option<FileInfo>(name: "--xml", description: "Path to Rekordbox exported collection XML", parseArgument: result =>
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
        })
        { IsRequired = true };
    }
}
