namespace LibTools4DJs.Logging
{
    public sealed class ProgressBar
    {
        private readonly DateTime _start = DateTime.UtcNow;
        private readonly string _label;
        private readonly bool _supportsCursor;
        private readonly int _total;
        private int _processed;
        private int _lastPercent;
        private string? _currentItemName;
        private bool _completed;

        public ProgressBar(int total, string label)
        {
            _total = total <= 0 ? 1 : total;
            _label = label;
            _supportsCursor = !Console.IsOutputRedirected;
            if (_supportsCursor)
            {
                // Insert an initial blank line for progress bar occupancy
                Console.WriteLine();
            }
            this.Render();
        }

        public string CurrentItemName
        {
            get => this._currentItemName!;
            set
            {
                this._currentItemName = value;
                this.Render();
            }
        }

        public void Increment()
        {
            _processed++;
            var percent = (int)((double)_processed / _total * 100);
            if (percent != _lastPercent && (percent % 1 == 0))
            {
                this.Render();
                _lastPercent = percent;
            }
        }

        public void Render()
        {
            var percent = (double)_processed / _total;
            int barWidth = 40;
            int filled = (int)(percent * barWidth);
            var bar = new string('#', filled) + new string('-', barWidth - filled);
            var elapsed = DateTime.UtcNow - _start;
            var elapsedStr = elapsed.ToString("mm\\:ss");
            var currentItemSegment = _currentItemName == null ? string.Empty : $" | Processing item: {_currentItemName}";
            var line = $"{_label}{currentItemSegment} [{bar}] {_processed}/{_total} {(percent * 100):F1}% Elapsed {elapsedStr}";

            if (_supportsCursor)
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
                        Console.Write(line.Substring(0, Console.BufferWidth - 1));
                    else
                        Console.Write(line);
                    if (curTop == top)
                        curTop = top + 1; // avoid overwriting the bar on next log write
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

            if (_processed >= _total && !_completed)
            {
                _completed = true;
            }
        }
    }
}
