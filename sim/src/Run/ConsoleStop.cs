// Interactive stop + live status line for the endless runners (run --endless, search).
// ConsoleStop: Enter or Ctrl+C raises Level (1 = finish the fights in progress, 2 = stop now); a third Ctrl+C kills the process.
// LiveLine: one status line rewritten in place with \r on a console (once a second); a plain line every 10 s when stderr is redirected.
using System;
using System.Diagnostics;
using System.Threading;

namespace SeSim.Run
{
    public sealed class ConsoleStop : IDisposable
    {
        int _level;
        readonly ConsoleCancelEventHandler _onCancel;

        public int Level { get { return Volatile.Read(ref _level); } }

        public ConsoleStop()
        {
            _onCancel = (o, e) =>
            {
                if (Level >= 2) return;
                e.Cancel = true; Interlocked.Increment(ref _level);
            };
            Console.CancelKeyPress += _onCancel;
            var reader = new Thread(() =>
            {
                try
                {
                    while (Level < 2)
                    {
                        if (Console.In.ReadLine() == null) return;    // no keyboard (stdin redirected or closed)
                        Interlocked.Increment(ref _level);
                    }
                }
                catch (Exception) { }
            }) { IsBackground = true, Name = "stdin" };
            reader.Start();
        }

        public void Dispose() { Console.CancelKeyPress -= _onCancel; }
    }

    public sealed class LiveLine
    {
        readonly bool _live = !Console.IsErrorRedirected;
        readonly Stopwatch _sw = Stopwatch.StartNew();
        int _lastLen;
        long _last = -100000;

        public double ElapsedS { get { return _sw.ElapsedMilliseconds / 1000.0; } }

        /// <summary>True when the status line should be refreshed now.</summary>
        public bool Due() { return _sw.ElapsedMilliseconds - _last >= (_live ? 1000 : 10000); }

        public void Show(string msg)
        {
            _last = _sw.ElapsedMilliseconds;
            if (_live)
            {
                string pad = msg.Length < _lastLen ? new string(' ', _lastLen - msg.Length) : "";
                Console.Error.Write("\r" + msg + pad); _lastLen = msg.Length;
            }
            else Console.Error.WriteLine(msg);
        }

        /// <summary>A message on its own line (above the status line on a console).</summary>
        public void Event(string msg)
        {
            if (_live && _lastLen > 0) { Console.Error.Write("\r" + new string(' ', _lastLen) + "\r"); _lastLen = 0; _last = -100000; }
            Console.Error.WriteLine(msg);
        }

        public void End() { if (_live && _lastLen > 0) Console.Error.WriteLine(); _lastLen = 0; }

        public static string Fmt(double s)
        {
            int t = (int)s;
            return (t / 3600) + "h" + ((t / 60) % 60).ToString("00") + "m" + (t % 60).ToString("00") + "s";
        }
    }
}
