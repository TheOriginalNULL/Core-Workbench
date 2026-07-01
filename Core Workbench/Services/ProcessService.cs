using System.Diagnostics;

namespace Core_Workbench.Services
{
    public sealed class ProcessSnapshot
    {
        public int Pid { get; init; }
        public string Name { get; init; } = "";
        public double CpuPercent { get; init; }
        public long MemoryBytes { get; init; }
    }

    /// <summary>
    /// Enumerates running processes and computes per-process CPU% from the change
    /// in total processor time between samples (normalised by core count). Memory
    /// is the working set. Some protected processes deny access — they're skipped.
    /// </summary>
    public sealed class ProcessService
    {
        private readonly Dictionary<int, (TimeSpan cpu, DateTime t)> _prev = new();
        private readonly int _cores = Environment.ProcessorCount;

        public List<ProcessSnapshot> Read()
        {
            DateTime now = DateTime.UtcNow;
            var result = new List<ProcessSnapshot>();
            var seen = new HashSet<int>();

            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    TimeSpan cpu = p.TotalProcessorTime;
                    long mem = p.WorkingSet64;

                    double pct = 0;
                    if (_prev.TryGetValue(p.Id, out var prev))
                    {
                        double dt = (now - prev.t).TotalSeconds;
                        if (dt > 0)
                            pct = (cpu - prev.cpu).TotalSeconds / (dt * _cores) * 100.0;
                    }
                    _prev[p.Id] = (cpu, now);
                    seen.Add(p.Id);

                    result.Add(new ProcessSnapshot
                    {
                        Pid = p.Id,
                        Name = p.ProcessName,
                        CpuPercent = Math.Clamp(pct, 0, 100),
                        MemoryBytes = mem,
                    });
                }
                catch { /* access denied / exited */ }
                finally { p.Dispose(); }
            }

            // Drop bookkeeping for processes that have exited.
            foreach (int dead in _prev.Keys.Where(k => !seen.Contains(k)).ToList())
                _prev.Remove(dead);

            return result
                .OrderByDescending(r => r.CpuPercent)
                .ThenByDescending(r => r.MemoryBytes)
                .ToList();
        }

        public bool Kill(int pid)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                p.Kill();
                return true;
            }
            catch { return false; }
        }

        public static string FormatBytes(long bytes)
        {
            string[] u = { "B", "KB", "MB", "GB" };
            double s = bytes; int i = 0;
            while (s >= 1024 && i < u.Length - 1) { s /= 1024; i++; }
            return $"{s:0.#} {u[i]}";
        }
    }
}
