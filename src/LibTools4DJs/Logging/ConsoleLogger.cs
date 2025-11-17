namespace LibTools4DJs.Logging;

public sealed class ConsoleLogger : ILogger
{
    private readonly ProgressBar? _progress;

    public ConsoleLogger(ProgressBar? progressBar = null) => this._progress = progressBar;

    public void Info(string message, ConsoleColor? consoleColor = null)
    {
        Log(message, consoleColor);
        this._progress?.Render();
    }

    public void Warn(string message)
    {
        Log(message, ConsoleColor.Yellow);
        this._progress?.Render();
    }

    public void Error(string message)
    {
        Log(message, ConsoleColor.Red);
        this._progress?.Render();
    }

    // Pretty print a command invocation with its parameter names and values in cyan.
    // parameters: collection of (optionOrArgName, value) pairs. Value may be null.
    public void PrintCommandInvocation(string commandName, IEnumerable<(string Name, object? Value)> parameters)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Cyan;
        const int padding = 2;
        var paramList = parameters.ToList();
        int nameColWidth = Math.Max("Parameter".Length, paramList.Count == 0 ? 8 : paramList.Max(p => p.Name.Length));
        
        // Build lines
        var header = $"Command: {commandName}";
        var lines = new List<string> { header };
        if (paramList.Count > 0)
        {
            lines.Add("");
            foreach (var (Name, Value) in paramList)
            {
                var valStr = Value switch
                {
                    null => "(null)",
                    bool b => b ? "true" : "false",
                    FileInfo fi => fi.FullName,
                    DirectoryInfo di => di.FullName,
                    _ => Value.ToString() ?? string.Empty
                };
                valStr = valStr.Replace('\n', ' ').Replace('\r', ' ');
                lines.Add($"{Name.PadRight(nameColWidth)} : {valStr}");
            }
        }

        var maxWidth = Math.Min(Console.BufferWidth - 1, lines.Max(l => l.Length) + (padding * 2));
        string Bar(char left, char fill, char right) => left + new string(fill, maxWidth - 2) + right;
        Console.WriteLine(Bar('┌', '─', '┐'));
        foreach (var l in lines)
        {
            var content = l.Length > maxWidth - (padding * 2) ? l.Substring(0, maxWidth - (padding * 2) - 1) + '…' : l;
            Console.WriteLine("│" + new string(' ', padding) + content.PadRight(maxWidth - (padding * 2)) + "│");
        }
        
        Console.WriteLine(Bar('└', '─', '┘'));
        Console.ForegroundColor = prev;
        this._progress?.Render();
    }

    private static void Log(string message, ConsoleColor? consoleColor = null)
    {
        if (consoleColor.HasValue)
        {
            var prev = Console.ForegroundColor;
            Console.ForegroundColor = consoleColor.Value;
            Console.WriteLine(message);
            Console.ForegroundColor = prev;
            return;
        }

        Console.WriteLine(message);
    }
}
