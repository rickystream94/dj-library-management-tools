// <copyright file="ProgressBar.cs" company="LibTools4DJs">
// Copyright (c) LibTools4DJs. All rights reserved.
// </copyright>

namespace LibTools4DJs.Logging
{
    using System.Diagnostics.CodeAnalysis;

    /// <summary>
    /// Renders a lightweight console progress bar with label, counts, and elapsed time.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public sealed class ProgressBar
    {
        private readonly DateTime start = DateTime.UtcNow;
        private readonly string label;
        private readonly bool supportsCursor;
        private readonly int total;
        private int processed;
        private int lastPercent;
        private string? currentItemName;
        private bool completed;

        /// <summary>
        /// Initializes a new instance of the <see cref="ProgressBar"/> class.
        /// </summary>
        /// <param name="total">Total number of work items. Values less than or equal to 0 are coerced to 1.</param>
        /// <param name="label">A short label displayed before the progress bar.</param>
        public ProgressBar(int total, string label)
        {
            this.total = total <= 0 ? 1 : total;
            this.label = label;
            this.supportsCursor = !Console.IsOutputRedirected;
            if (this.supportsCursor)
            {
                // Insert an initial blank line for progress bar occupancy
                Console.WriteLine();
            }

            this.Render();
        }

        /// <summary>
        /// Gets or sets the name of the current item being processed, rendered inline with the bar.
        /// </summary>
        public string CurrentItemName
        {
            get => this.currentItemName!;
            set
            {
                this.currentItemName = value;
                this.Render();
            }
        }

        /// <summary>
        /// Increments the processed count by one and re-renders on percent change.
        /// </summary>
        public void Increment()
        {
            this.processed++;
            var percent = (int)((double)this.processed / this.total * 100);
            if (percent != this.lastPercent)
            {
                this.Render();
                this.lastPercent = percent;
            }
        }

        /// <summary>
        /// Renders the progress bar in-place when possible; otherwise writes a new line.
        /// </summary>
        public void Render()
        {
            var percent = (double)this.processed / this.total;
            int barWidth = 40;
            int filled = (int)(percent * barWidth);
            var bar = new string('#', filled) + new string('-', barWidth - filled);
            var elapsed = DateTime.UtcNow - this.start;
            var elapsedStr = elapsed.ToString("mm\\:ss");
            var currentItemSegment = this.currentItemName == null ? string.Empty : $" | Processing item: {this.currentItemName}";
            var line = $"{this.label}{currentItemSegment} [{bar}] {this.processed}/{this.total} {percent * 100:F1}% Elapsed {elapsedStr}";

            if (this.supportsCursor)
            {
                int curLeft = Console.CursorLeft;
                int curTop = Console.CursorTop;
                int top = Console.WindowTop; // always use current window top as anchor
                try
                {
                    Console.SetCursorPosition(0, top);

                    // Clear current top line then write progress
                    var clear = new string(' ', Console.BufferWidth - 1);
                    Console.Write(clear);
                    Console.SetCursorPosition(0, top);
                    if (line.Length >= Console.BufferWidth)
                    {
                        Console.Write(line.Substring(0, Console.BufferWidth - 1));
                    }
                    else
                    {
                        Console.Write(line);
                    }

                    if (curTop == top)
                    {
                        curTop = top + 1; // avoid overwriting the bar on next log write
                    }

                    Console.SetCursorPosition(curLeft, curTop);
                }
                catch
                {
                    Console.WriteLine(line); // degrade gracefully
                }
            }
            else
            {
                Console.WriteLine(line);
            }

            if (this.processed >= this.total && !this.completed)
            {
                this.completed = true;
            }
        }
    }
}
