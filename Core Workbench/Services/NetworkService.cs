using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Core_Workbench.Services
{
    /// <summary>One network adapter's live stats.</summary>
    public sealed class NetAdapterSnapshot
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        public string Ipv4 { get; init; } = "—";
        public long LinkSpeedBits { get; init; }
        public double DownBytesPerSec { get; init; }
        public double UpBytesPerSec { get; init; }
        public long TotalReceived { get; init; }
        public long TotalSent { get; init; }
    }

    /// <summary>
    /// Reads per-adapter throughput by sampling cumulative byte counters and
    /// dividing the delta by elapsed time. Call <see cref="Read"/> on a timer.
    /// </summary>
    public sealed class NetworkService
    {
        private readonly Dictionary<string, (long rx, long tx, DateTime t)> _prev = new();

        public List<NetAdapterSnapshot> Read()
        {
            var list = new List<NetAdapterSnapshot>();

            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;

                IPv4InterfaceStatistics stats;
                try { stats = ni.GetIPv4Statistics(); }
                catch { continue; }

                long rx = stats.BytesReceived, tx = stats.BytesSent;
                DateTime now = DateTime.UtcNow;
                double down = 0, up = 0;

                if (_prev.TryGetValue(ni.Id, out var p))
                {
                    double dt = (now - p.t).TotalSeconds;
                    if (dt > 0)
                    {
                        down = Math.Max(0, rx - p.rx) / dt;
                        up = Math.Max(0, tx - p.tx) / dt;
                    }
                }
                _prev[ni.Id] = (rx, tx, now);

                string ip = "—";
                try
                {
                    var addr = ni.GetIPProperties().UnicastAddresses
                        .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                    if (addr != null) ip = addr.Address.ToString();
                }
                catch { /* adapter has no IPv4 */ }

                list.Add(new NetAdapterSnapshot
                {
                    Id = ni.Id,
                    Name = ni.Name,
                    Description = ni.Description,
                    Ipv4 = ip,
                    LinkSpeedBits = ni.Speed,
                    DownBytesPerSec = down,
                    UpBytesPerSec = up,
                    TotalReceived = rx,
                    TotalSent = tx,
                });
            }

            return list;
        }

        /// <summary>Human-readable transfer rate, e.g. "1.2 MB/s".</summary>
        public static string FormatRate(double bytesPerSec)
        {
            string[] u = { "B/s", "KB/s", "MB/s", "GB/s" };
            double s = bytesPerSec; int i = 0;
            while (s >= 1024 && i < u.Length - 1) { s /= 1024; i++; }
            return $"{s:0.0} {u[i]}";
        }

        public static string FormatBytes(long bytes)
        {
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            double s = bytes; int i = 0;
            while (s >= 1024 && i < u.Length - 1) { s /= 1024; i++; }
            return $"{s:0.#} {u[i]}";
        }
    }
}
