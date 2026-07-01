using System.Linq;
using System.Management;
using LibreHardwareMonitor.Hardware;

namespace Core_Workbench.Services
{
    /// <summary>One physical disk's vitals.</summary>
    public sealed class DiskSnapshot
    {
        public int Index { get; init; } = -1;        // \\.\PhysicalDriveN
        public string Name { get; init; } = "Disk";
        public float UsedPercent { get; init; }
        public float UsedGb { get; init; }
        public float TotalGb { get; init; }
        public float? TempC { get; init; }

        // Extra lifetime stats used by the Drive Health page.
        public float? DataReadGb { get; init; }
        public float? DataWrittenGb { get; init; }
        public float? RemainingLifePercent { get; init; }
    }

    /// <summary>One CPU core's live load and clock.</summary>
    public sealed class CoreLoad
    {
        public int Index { get; init; }
        public float Load { get; init; }
        public float? ClockMhz { get; init; }
    }

    /// <summary>One snapshot of the machine's vital signs.</summary>
    public sealed class HardwareSnapshot
    {
        public float CpuLoad { get; init; }          // %
        public float? CpuTemp { get; init; }         // °C, null if unavailable
        public string CpuName { get; init; } = "CPU";
        public IReadOnlyList<CoreLoad> CpuCores { get; init; } = Array.Empty<CoreLoad>();

        public float GpuLoad { get; init; }          // %
        public float? GpuTemp { get; init; }         // °C
        public string GpuName { get; init; } = "GPU";

        // Extended sensors (Afterburner-style; any may be null if unreported).
        public float? CpuClockMhz { get; init; }
        public float? CpuPowerW { get; init; }
        public float? GpuCoreClockMhz { get; init; }
        public float? GpuMemClockMhz { get; init; }
        public float? GpuVramUsedMb { get; init; }
        public float? GpuPowerW { get; init; }
        public float? GpuFanPercent { get; init; }

        public float RamLoad { get; init; }          // %
        public float RamUsedGb { get; init; }
        public float RamTotalGb { get; init; }

        public IReadOnlyList<DiskSnapshot> Disks { get; init; } = Array.Empty<DiskSnapshot>();
    }

    /// <summary>
    /// Wraps LibreHardwareMonitor. Call <see cref="Read"/> on a timer to get a
    /// fresh snapshot. Temperatures require the app to run as administrator.
    /// Dispose when done to release the underlying driver.
    /// </summary>
    public sealed class HardwareMonitorService : IDisposable
    {
        private readonly Computer _computer;
        private readonly object _gate = new();
        private bool _disposed;

        // Physical-disk capacities (bytes) keyed by drive index (\\.\PhysicalDriveN).
        // LibreHardwareMonitor reports temperature and used-% per disk but not total
        // size, so we pull capacity once from WMI and join on the index.
        private readonly Dictionary<int, long> _diskBytes = new();

        public HardwareMonitorService()
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsStorageEnabled = true,
            };
            _computer.Open();
            LoadDiskCapacities();
        }

        private void LoadDiskCapacities()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Index, Size FROM Win32_DiskDrive");
                foreach (ManagementBaseObject mo in searcher.Get())
                {
                    if (mo["Index"] is null || mo["Size"] is null) continue;
                    int idx = Convert.ToInt32(mo["Index"]);
                    long size = Convert.ToInt64(mo["Size"]);
                    _diskBytes[idx] = size;
                }
            }
            catch { /* WMI unavailable — disks will show % only, no GB */ }
        }

        public HardwareSnapshot Read()
        {
            lock (_gate)
            {
                if (_disposed) return new HardwareSnapshot();

                float cpuLoad = 0, gpuLoad = 0, ramLoad = 0, ramUsed = 0, ramAvail = 0;
                float? cpuTemp = null, gpuTemp = null;
                float? cpuClock = null, cpuPower = null;
                float? gpuCoreClock = null, gpuMemClock = null, gpuVram = null, gpuPower = null, gpuFan = null;
                string cpuName = "CPU", gpuName = "GPU";
                IReadOnlyList<CoreLoad> cpuCores = Array.Empty<CoreLoad>();
                var disks = new List<DiskSnapshot>();

                foreach (var hw in _computer.Hardware)
                {
                    hw.Update();
                    foreach (var sub in hw.SubHardware) sub.Update();

                    switch (hw.HardwareType)
                    {
                        case HardwareType.Storage:
                            disks.Add(ReadDisk(hw));
                            break;

                        case HardwareType.Cpu:
                            cpuName = hw.Name;
                            cpuLoad = LoadTotal(hw) ?? cpuLoad;
                            cpuTemp = CpuTemperature(hw) ?? cpuTemp;
                            cpuClock = MaxClock(hw, "Core") ?? cpuClock;
                            cpuPower = SensorValue(hw, SensorType.Power, "Package")
                                       ?? SensorValue(hw, SensorType.Power, "CPU") ?? cpuPower;
                            cpuCores = ReadCores(hw);
                            break;

                        case HardwareType.GpuNvidia:
                        case HardwareType.GpuAmd:
                        case HardwareType.GpuIntel:
                            // Prefer a discrete GPU that's actually reporting load.
                            var gl = LoadCore(hw);
                            if (gl.HasValue)
                            {
                                gpuName = hw.Name;
                                gpuLoad = gl.Value;
                                gpuTemp = SensorValue(hw, SensorType.Temperature, "GPU Core")
                                          ?? FirstSensor(hw, SensorType.Temperature);
                                gpuCoreClock = SensorValue(hw, SensorType.Clock, "GPU Core");
                                gpuMemClock = SensorValue(hw, SensorType.Clock, "GPU Memory");
                                gpuVram = SensorValue(hw, SensorType.SmallData, "Memory Used")
                                          ?? SensorValue(hw, SensorType.Data, "Memory Used");
                                gpuPower = SensorValue(hw, SensorType.Power, "GPU Power")
                                           ?? FirstSensor(hw, SensorType.Power);
                                gpuFan = SensorValue(hw, SensorType.Control, "Fan")
                                         ?? SensorValue(hw, SensorType.Control, "GPU Fan");
                            }
                            break;

                        case HardwareType.Memory:
                            ramLoad = SensorValue(hw, SensorType.Load, "Memory") ?? ramLoad;
                            ramUsed = SensorValue(hw, SensorType.Data, "Memory Used") ?? ramUsed;
                            ramAvail = SensorValue(hw, SensorType.Data, "Memory Available") ?? ramAvail;
                            break;
                    }
                }

                return new HardwareSnapshot
                {
                    CpuLoad = cpuLoad,
                    CpuTemp = cpuTemp,
                    CpuName = cpuName,
                    CpuCores = cpuCores,
                    GpuLoad = gpuLoad,
                    GpuTemp = gpuTemp,
                    GpuName = gpuName,
                    CpuClockMhz = cpuClock,
                    CpuPowerW = cpuPower,
                    GpuCoreClockMhz = gpuCoreClock,
                    GpuMemClockMhz = gpuMemClock,
                    GpuVramUsedMb = gpuVram,
                    GpuPowerW = gpuPower,
                    GpuFanPercent = gpuFan,
                    RamLoad = ramLoad,
                    RamUsedGb = ramUsed,
                    RamTotalGb = ramUsed + ramAvail,
                    Disks = disks,
                };
            }
        }

        private DiskSnapshot ReadDisk(IHardware hw)
        {
            float usedPct = SensorValue(hw, SensorType.Load, "Used Space")
                            ?? FirstSensor(hw, SensorType.Load) ?? 0;
            float? temp = SensorValue(hw, SensorType.Temperature, "Temperature")
                          ?? FirstSensor(hw, SensorType.Temperature);

            // Identifier looks like "/nvme/0", "/ssd/1", "/hdd/2" — the trailing
            // number is the physical drive index used to join capacity from WMI.
            float totalGb = 0;
            int idx = ParseDriveIndex(hw.Identifier?.ToString());
            if (idx >= 0 && _diskBytes.TryGetValue(idx, out long bytes) && bytes > 0)
                totalGb = (float)(bytes / 1024.0 / 1024.0 / 1024.0);

            // Lifetime counters (best-effort; absent on many drives).
            float? dataRead = SensorValue(hw, SensorType.Data, "Data Read");
            float? dataWritten = SensorValue(hw, SensorType.Data, "Data Written");
            float? remainingLife = SensorValueAnyType(hw, "Remaining Life");

            return new DiskSnapshot
            {
                Index = idx,
                Name = string.IsNullOrWhiteSpace(hw.Name) ? "Disk" : hw.Name,
                UsedPercent = usedPct,
                TotalGb = totalGb,
                UsedGb = totalGb * usedPct / 100f,
                TempC = temp,
                DataReadGb = dataRead,
                DataWrittenGb = dataWritten,
                RemainingLifePercent = remainingLife,
            };
        }

        private static float? SensorValueAnyType(IHardware hw, string nameContains)
            => hw.Sensors.FirstOrDefault(s =>
                   s.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase) &&
                   s.Value.HasValue)?.Value;

        private static int ParseDriveIndex(string? identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return -1;
            int slash = identifier.LastIndexOf('/');
            return slash >= 0 && int.TryParse(identifier[(slash + 1)..], out int i) ? i : -1;
        }

        // ---- sensor helpers ----

        private static float? LoadTotal(IHardware hw)
            => SensorValue(hw, SensorType.Load, "CPU Total") ?? FirstSensor(hw, SensorType.Load);

        private static float? LoadCore(IHardware hw)
            => SensorValue(hw, SensorType.Load, "GPU Core")
               ?? SensorValue(hw, SensorType.Load, "D3D 3D")
               ?? FirstSensor(hw, SensorType.Load);

        private static float? CpuTemperature(IHardware hw)
            => SensorValue(hw, SensorType.Temperature, "Core (Tctl/Tdie)")
               ?? SensorValue(hw, SensorType.Temperature, "CPU Package")
               ?? SensorValue(hw, SensorType.Temperature, "Core Average")
               ?? FirstSensor(hw, SensorType.Temperature);

        private static float? SensorValue(IHardware hw, SensorType type, string nameContains)
            => hw.Sensors.FirstOrDefault(s =>
                   s.SensorType == type &&
                   s.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase) &&
                   s.Value.HasValue)?.Value;

        private static float? FirstSensor(IHardware hw, SensorType type)
            => hw.Sensors.FirstOrDefault(s => s.SensorType == type && s.Value.HasValue)?.Value;

        /// <summary>Per-core load (and matching clock) from "CPU Core #N" sensors.</summary>
        private static IReadOnlyList<CoreLoad> ReadCores(IHardware hw)
        {
            var cores = new List<CoreLoad>();
            foreach (var s in hw.Sensors.Where(s => s.SensorType == SensorType.Load && s.Value.HasValue &&
                                                     s.Name.StartsWith("CPU Core #", StringComparison.OrdinalIgnoreCase)))
            {
                int hash = s.Name.IndexOf('#');
                if (hash < 0 || !int.TryParse(s.Name[(hash + 1)..], out int idx)) continue;

                float? clock = hw.Sensors.FirstOrDefault(c =>
                    c.SensorType == SensorType.Clock && c.Value.HasValue &&
                    c.Name.Equals($"CPU Core #{idx}", StringComparison.OrdinalIgnoreCase))?.Value;

                cores.Add(new CoreLoad { Index = idx, Load = s.Value!.Value, ClockMhz = clock });
            }
            return cores.OrderBy(c => c.Index).ToList();
        }

        /// <summary>Highest clock among sensors whose name contains <paramref name="nameContains"/> (e.g. CPU core clocks).</summary>
        private static float? MaxClock(IHardware hw, string nameContains)
        {
            var clocks = hw.Sensors
                .Where(s => s.SensorType == SensorType.Clock && s.Value.HasValue &&
                            s.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase))
                .Select(s => s.Value!.Value)
                .ToList();
            return clocks.Count > 0 ? clocks.Max() : null;
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                try { _computer.Close(); } catch { /* driver may already be gone */ }
            }
        }
    }
}
