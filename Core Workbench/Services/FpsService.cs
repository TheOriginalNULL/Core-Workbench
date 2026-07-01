using System.Diagnostics;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace Core_Workbench.Services
{
    public sealed class FpsSample
    {
        public int Pid { get; init; }
        public double Fps { get; init; }
        public double FrametimeMs { get; init; }
        public double Low1 { get; init; }     // 1% low FPS
        public double Low01 { get; init; }    // 0.1% low FPS
    }

    /// <summary>
    /// Measures real per-process frame rate by listening to the Windows DXGI
    /// present ETW events — no injection, no external process. Each "Present"
    /// is one frame; FPS is the presents-per-second for a given process.
    /// Requires administrator (for the ETW session). Covers DXGI apps (most DX9–12
    /// games); Vulkan/OpenGL-only titles won't emit DXGI presents.
    /// </summary>
    public sealed class FpsService : IDisposable
    {
        private readonly object _gate = new();
        private readonly Dictionary<int, List<double>> _frames = new();   // pid -> present timestamps (ms)
        private TraceEventSession? _session;
        private Thread? _thread;

        public bool Available { get; private set; }

        public void Start()
        {
            if (_session != null) return;
            try
            {
                _session = new TraceEventSession("CoreWorkbench_FPS_" + Environment.ProcessId)
                {
                    StopOnDispose = true,
                };
                _session.EnableProvider("Microsoft-Windows-DXGI");
                _session.Source.Dynamic.All += OnEvent;

                _thread = new Thread(() => { try { _session.Source.Process(); } catch { } })
                {
                    IsBackground = true,
                    Name = "FPS-ETW",
                };
                _thread.Start();
                Available = true;
            }
            catch
            {
                Available = false;
                Dispose();
            }
        }

        private void OnEvent(TraceEvent data)
        {
            // Count present events as frames. DXGI present-start fires once per frame.
            if (data.ProcessID <= 0) return;
            if (!data.EventName.Contains("Present", StringComparison.OrdinalIgnoreCase)) return;

            double t = data.TimeStampRelativeMSec;
            lock (_gate)
            {
                if (!_frames.TryGetValue(data.ProcessID, out var list))
                {
                    list = new List<double>(256);
                    _frames[data.ProcessID] = list;
                }
                list.Add(t);
                if (list.Count > 2048) list.RemoveRange(0, list.Count - 1024);
            }
        }

        /// <summary>FPS / frametime / lows for a process over the last ~2 seconds, or null if it isn't presenting.</summary>
        public FpsSample? Get(int pid)
        {
            lock (_gate)
            {
                if (!_frames.TryGetValue(pid, out var list) || list.Count < 2) return null;

                double now = list[^1];
                if (now - list[^1] > 0) { /* now is the latest */ }

                int fps = 0;
                for (int i = list.Count - 1; i >= 0 && now - list[i] <= 1000; i--) fps++;
                if (fps == 0) return null;   // stale (not presenting anymore)

                var times = new List<double>();
                for (int i = list.Count - 1; i >= 1 && now - list[i] <= 2000; i--)
                    times.Add(list[i] - list[i - 1]);

                double avg = times.Count > 0 ? times.Average() : 1000.0 / fps;
                double low1 = 0, low01 = 0;
                if (times.Count >= 10)
                {
                    times.Sort();
                    low1 = 1000.0 / Percentile(times, 99);
                    low01 = 1000.0 / Percentile(times, 99.9);
                }
                return new FpsSample { Pid = pid, Fps = fps, FrametimeMs = avg, Low1 = low1, Low01 = low01 };
            }
        }

        private static double Percentile(List<double> sortedAsc, double p)
        {
            if (sortedAsc.Count == 0) return 0;
            double rank = p / 100.0 * (sortedAsc.Count - 1);
            int lo = (int)Math.Floor(rank), hi = (int)Math.Ceiling(rank);
            return lo == hi ? sortedAsc[lo] : sortedAsc[lo] + (rank - lo) * (sortedAsc[hi] - sortedAsc[lo]);
        }

        public void Dispose()
        {
            try { _session?.Dispose(); } catch { }
            _session = null;
            Available = false;
        }
    }
}
