// <copyright file="ConsoleLogger.cs" company="LibTools4DJs">
// Copyright (c) LibTools4DJs. All rights reserved.
// </copyright>

namespace LibTools4DJs.Logging;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Console logger with optional progress bar and file persistence.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class ConsoleLogger : ILogger
{
    private readonly bool debugEnabled;
    private readonly string? logFilePath;
    private ProgressBar? progress;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConsoleLogger"/> class.
    /// </summary>
    /// <param name="debugEnabled">Enable verbose debug output.</param>
    /// <param name="logFilePath">Optional path to append logs to.</param>
    public ConsoleLogger(bool debugEnabled = false, string? logFilePath = null)
    {
        this.debugEnabled = debugEnabled;
        this.logFilePath = logFilePath;
    }

    /// <inheritdoc />
    public void WithProgressBar(ProgressBar progressBar)
    {
        this.progress = progressBar;
    }

    /// <inheritdoc />
    public void Info(string message, ConsoleColor? consoleColor = null)
    {
        this.Log(message, consoleColor);
        this.progress?.Render();
    }

    /// <inheritdoc />
    public void Warn(string message)
    {
        this.Log(message, ConsoleColor.Yellow);
        this.progress?.Render();
    }

    /// <inheritdoc />
    public void Error(string message)
    {
        this.Log(message, ConsoleColor.Red);
        this.progress?.Render();
    }

    /// <inheritdoc />
    public void Debug(string message)
    {
        if (this.debugEnabled)
        {
            this.Log(message, ConsoleColor.DarkGray);
            this.progress?.Render();
        }
        else
        {
            // Still persist to file if enabled, but do not print to console
            this.PersistToFile(message);
        }
    }

    /// <summary>
    /// Pretty prints a command invocation with its parameter names and values in cyan.
    /// </summary>
    /// <param name="commandName">The command name.</param>
    /// <param name="parameters">Collection of (optionOrArgName, value) pairs. Value may be null.</param>
    public void PrintCommandInvocation(string commandName, IEnumerable<(string Name, object? Value)> parameters)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Cyan;
        const int padding = 2;
        var paramList = parameters.ToList();
        int nameColWidth = Math.Max("Parameter".Length, paramList.Count == 0 ? 8 : paramList.Max(p => p.Name.Length));

        // Build lines
        var header = $"Command: {commandName}";
        var lines = new List<string>
        {
            header,
        };
        if (paramList.Count > 0)
        {
            lines.Add(string.Empty);
            foreach (var (name, value) in paramList)
            {
                var valStr = value switch
                {
                    null => "(null)",
                    bool b => b ? "true" : "false",
                    FileInfo fi => fi.FullName,
                    DirectoryInfo di => di.FullName,
                    _ => value.ToString() ?? string.Empty,
                };
                valStr = valStr.Replace('\n', ' ').Replace('\r', ' ');
                lines.Add($"{name.PadRight(nameColWidth)} : {valStr}");
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
        this.progress?.Render();
        this.PersistToFile(string.Join(Environment.NewLine, lines));
    }

    private void Log(string message, ConsoleColor? consoleColor = null)
    {
        if (consoleColor.HasValue)
        {
            var prev = Console.ForegroundColor;
            Console.ForegroundColor = consoleColor.Value;
            Console.WriteLine(message);
            Console.ForegroundColor = prev;
            this.PersistToFile(message);
            return;
        }

        Console.WriteLine(message);
        this.PersistToFile(message);
    }

    private void PersistToFile(string message)
    {
        if (string.IsNullOrWhiteSpace(this.logFilePath))
        {
            return;
        }

        try
        {
            File.AppendAllText(this.logFilePath!, message + Environment.NewLine);
        }
        catch
        {
            // swallow file I/O errors to avoid breaking command execution
        }
    }
}
