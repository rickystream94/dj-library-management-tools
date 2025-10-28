namespace LibTools4DJs.Handlers;

public interface ILogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message);
}

public sealed class ConsoleLogger : ILogger
{
    public void Info(string message) => Console.WriteLine(message);

    public void Warn(string message)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(message);
        Console.ForegroundColor = prev;
    }

    public void Error(string message)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ForegroundColor = prev;
    }
}
