// <copyright file="ILogger.cs" company="LibTools4DJs">
// Copyright (c) LibTools4DJs. All rights reserved.
// </copyright>

namespace LibTools4DJs.Logging
{
    /// <summary>
    /// Simplified logging interface for console output with optional progress updates.
    /// </summary>
    public interface ILogger
    {
        /// <summary>
        /// Logs an informational message. Optionally renders in a specific color.
        /// </summary>
        /// <param name="message">The text to write to the console.</param>
        /// <param name="consoleColor">Optional color for the message.</param>
        void Info(string message, ConsoleColor? consoleColor = null);

        /// <summary>
        /// Logs a warning message.
        /// </summary>
        /// <param name="message">The text to write to the console.</param>
        void Warn(string message);

        /// <summary>
        /// Logs an error message.
        /// </summary>
        /// <param name="message">The text to write to the console.</param>
        void Error(string message);

        /// <summary>
        /// Logs a debug message. Implementations may suppress console output unless debug mode is enabled.
        /// </summary>
        /// <param name="message">The text to write to the console.</param>
        void Debug(string message);

        /// <summary>
        /// Associates a progress bar with the logger so that render calls can follow log writes.
        /// </summary>
        /// <param name="progressBar">The progress bar to render after messages.</param>
        void WithProgressBar(ProgressBar progressBar);
    }
}
