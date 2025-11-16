namespace LibTools4DJs.Handlers;

public interface ILogger
{
    void Info(string message, ConsoleColor? consoleColor = null);
    void Warn(string message);
    void Error(string message);
}

public sealed class ConsoleLogger : ILogger
{
    public void Info(string message, ConsoleColor? consoleColor = null)
    {
        this.Log(message, consoleColor);
    }

    public void Warn(string message)
    {
        this.Log(message, ConsoleColor.Yellow);
    }

    public void Error(string message)
    {
        this.Log(message, ConsoleColor.Red);
    }

    private void Log(string message, ConsoleColor? consoleColor = null)
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
