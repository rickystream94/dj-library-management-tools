namespace LibTools4DJs.Logging
{
    public interface ILogger
    {
        void Info(string message, ConsoleColor? consoleColor = null);

        void Warn(string message);

        void Error(string message);

        void Debug(string message);

        void WithProgressBar(ProgressBar progressBar);
    }
}
