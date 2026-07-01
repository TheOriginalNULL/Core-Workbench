using System.Management;
using Core_Workbench.Models;

namespace Core_Workbench.Services.DriveHealth
{
    /// <summary>
    /// A source of drive-health records. Implement this to add more collectors
    /// later (e.g. a raw NVMe IOCTL reader or smartctl bridge) without touching
    /// the page or service orchestration.
    /// </summary>
    public interface IDriveHealthSource
    {
        IEnumerable<DriveHealthData> Collect(IReadOnlyDictionary<int, DiskSnapshot> lhm);
    }

    /// <summary>
    /// Orchestrates drive-health collection. Reads the shared hardware monitor for
    /// lifetime counters, then asks each source to build records. Fully async and
    /// defensive: a failing drive or WMI call never throws to the caller.
    /// </summary>
    public sealed class DriveHealthService
    {
        private readonly IDriveHealthSource _source = new WmiDriveHealthSource();

        /// <summary>Collect a fresh set of drive-health records off the UI thread.</summary>
        public Task<List<DriveHealthData>> CollectAsync() => Task.Run(Collect);

        private List<DriveHealthData> Collect()
        {
            // Lifetime read/written + temperature fallback from LibreHardwareMonitor,
            // keyed by physical drive index.
            var lhm = new Dictionary<int, DiskSnapshot>();
            try
            {
                foreach (var d in HardwareMonitor.Instance.Read().Disks)
                    if (d.Index >= 0) lhm[d.Index] = d;
            }
            catch { /* monitor unavailable — continue with WMI only */ }

            var list = new List<DriveHealthData>();
            try
            {
                list.AddRange(_source.Collect(lhm));
            }
            catch { /* a source failing shouldn't blank the whole page */ }

            // Enrich with raw SMART/NVMe data for fields WMI doesn't expose.
            for (int i = 0; i < list.Count; i++)
            {
                try
                {
                    var extra = SmartReader.TryRead(list[i].Index, list[i].Interface);
                    if (extra != null) list[i] = Merge(list[i], extra);
                }
                catch { /* keep the WMI-only record for this drive */ }
            }

            return list.OrderBy(d => d.Index).ToList();
        }

        /// <summary>Keep the WMI string if meaningful, otherwise fall back to the SMART one.</summary>
        private static string Prefer(string wmi, string? smart, string placeholder)
        {
            if (!string.IsNullOrWhiteSpace(wmi) && wmi != "Unknown drive") return wmi;
            if (!string.IsNullOrWhiteSpace(smart)) return smart!;
            return string.IsNullOrWhiteSpace(wmi) ? placeholder : wmi;
        }

        /// <summary>Fill any gaps in the WMI record from the raw SMART read, then re-score status.</summary>
        private static DriveHealthData Merge(DriveHealthData d, SmartExtra e)
        {
            double? temp = d.TempC ?? e.TempC;
            double? remaining = d.RemainingLifePercent ?? e.RemainingLifePercent;
            long? realloc = d.ReallocatedSectors ?? e.ReallocatedSectors;
            long? mediaErrors = d.MediaErrors ?? e.MediaErrors;
            string? critical = d.CriticalWarning ?? e.CriticalWarning;
            double? health = d.HealthPercent ?? remaining;

            return new DriveHealthData
            {
                Index = d.Index,
                Model = Prefer(d.Model, e.Model, "Unknown drive"),
                // WMI often returns blank/zeroed NVMe serials — trust Identify when present.
                Serial = !string.IsNullOrEmpty(e.Serial) ? e.Serial! : d.Serial,
                Firmware = Prefer(d.Firmware, e.Firmware, ""),
                Interface = d.Interface,
                MediaType = d.MediaType,
                SizeBytes = d.SizeBytes,
                TempC = temp,
                HealthPercent = health,
                RemainingLifePercent = remaining,
                PowerOnHours = d.PowerOnHours ?? e.PowerOnHours,
                PowerCycles = d.PowerCycles ?? e.PowerCycles,
                HostReadsGb = d.HostReadsGb ?? e.HostReadsGb,
                HostWritesGb = d.HostWritesGb ?? e.HostWritesGb,
                ReallocatedSectors = realloc,
                MediaErrors = mediaErrors,
                CriticalWarning = critical,
                HealthStatusText = d.HealthStatusText,
                Status = ComputeStatus(d.HealthStatusText, temp, health, realloc, critical, mediaErrors),
            };
        }

        // ---- shared status logic (fixed, sensible thresholds) ----

        internal static DriveStatus ComputeStatus(string healthStatus, double? tempC,
            double? healthPct, long? reallocated, string? criticalWarning, long? mediaErrors)
        {
            // Base band from health %: 0–10 Failing, 10–30 Bad, 30–60 Fair, 60–90 Good, 90+ Great.
            DriveStatus band = healthPct switch
            {
                null => StatusFromText(healthStatus),
                < 10 => DriveStatus.Failing,
                < 30 => DriveStatus.Bad,
                < 60 => DriveStatus.Fair,
                < 90 => DriveStatus.Good,
                _ => DriveStatus.Great,
            };

            if (band == DriveStatus.Unknown) return band;

            // Conditions that pull the rating down regardless of the % band.
            if (!string.IsNullOrEmpty(criticalWarning) || tempC is >= 70)
                return DriveStatus.Failing;
            if (tempC is >= 60) band = Worse(band, DriveStatus.Fair);
            if (reallocated is > 0 || mediaErrors is > 0) band = Worse(band, DriveStatus.Fair);

            return band;
        }

        private static DriveStatus StatusFromText(string healthStatus) => healthStatus switch
        {
            "Unhealthy" => DriveStatus.Failing,
            "Warning" => DriveStatus.Bad,
            "Healthy" => DriveStatus.Good,
            _ => DriveStatus.Unknown,
        };

        /// <summary>The worse (lower-ranked) of two statuses.</summary>
        private static DriveStatus Worse(DriveStatus a, DriveStatus b)
            => (int)a <= (int)b ? a : b;
    }

    /// <summary>
    /// Primary source: Windows Storage Management WMI.
    /// • MSFT_PhysicalDisk — model, serial, firmware, size, media/bus type, health.
    /// • MSFT_StorageReliabilityCounter — temperature, power-on hours, wear, cycles,
    ///   uncorrected errors. Both live in root\Microsoft\Windows\Storage and return
    ///   real values without any kernel IOCTL.
    /// </summary>
    public sealed class WmiDriveHealthSource : IDriveHealthSource
    {
        public IEnumerable<DriveHealthData> Collect(IReadOnlyDictionary<int, DiskSnapshot> lhm)
        {
            var results = new List<DriveHealthData>();

            ManagementObjectSearcher searcher;
            try
            {
                var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
                scope.Connect();
                searcher = new ManagementObjectSearcher(scope,
                    new ObjectQuery("SELECT * FROM MSFT_PhysicalDisk"));
            }
            catch
            {
                return results;   // storage namespace unavailable
            }

            using (searcher)
            foreach (ManagementBaseObject obj in searcher.Get())
            {
                var disk = (ManagementObject)obj;
                try { results.Add(BuildDrive(disk, lhm)); }
                catch { /* skip a drive that misbehaves rather than crash */ }
                finally { disk.Dispose(); }
            }

            return results;
        }

        private static DriveHealthData BuildDrive(ManagementObject disk,
            IReadOnlyDictionary<int, DiskSnapshot> lhm)
        {
            int index = ParseInt(GetStr(disk, "DeviceId"), -1);
            string model = GetStr(disk, "FriendlyName");
            string serial = GetStr(disk, "SerialNumber");
            string firmware = GetStr(disk, "FirmwareVersion");
            long size = GetLong(disk, "Size") ?? 0;
            int media = GetInt(disk, "MediaType", 0);
            int bus = GetInt(disk, "BusType", 0);
            int health = GetInt(disk, "HealthStatus", 5);

            // Reliability counters (associated instance; absent on some drivers).
            double? temp = null, wear = null;
            long? powerOnHours = null, cycles = null, readErr = null, writeErr = null;
            try
            {
                foreach (ManagementBaseObject rcObj in disk.GetRelated("MSFT_StorageReliabilityCounter"))
                {
                    using var rc = (ManagementObject)rcObj;
                    temp = GetDouble(rc, "Temperature");
                    wear = GetDouble(rc, "Wear");
                    powerOnHours = GetLong(rc, "PowerOnHours");
                    cycles = GetLong(rc, "StartStopCycleCount");
                    readErr = GetLong(rc, "ReadErrorsUncorrected");
                    writeErr = GetLong(rc, "WriteErrorsUncorrected");
                    break;
                }
            }
            catch { /* reliability counters not supported by this driver */ }

            // Enrich with LibreHardwareMonitor lifetime stats / temperature fallback.
            lhm.TryGetValue(index, out var s);
            if (temp is null or 0) temp = s?.TempC;
            double? remainingLife = (wear.HasValue ? (double?)(100.0 - wear.Value) : null)
                                    ?? s?.RemainingLifePercent;

            string mediaType = media switch { 4 => "SSD", 3 => "HDD", 5 => "SCM", _ => bus == 17 ? "SSD" : "—" };
            string iface = bus switch
            {
                17 => "NVMe",
                11 => "SATA",
                7 => "USB",
                10 => "SAS",
                3 => "ATA",
                8 => "RAID",
                _ => "—",
            };
            string healthStatus = health switch { 0 => "Healthy", 1 => "Warning", 2 => "Unhealthy", _ => "Unknown" };

            // Health %: prefer SSD remaining-life; otherwise derive from WMI health state.
            double? healthPct = remainingLife
                                ?? health switch { 0 => (double?)100, 1 => 60, 2 => 20, _ => null };

            long? mediaErrors = (readErr.HasValue || writeErr.HasValue)
                ? (readErr ?? 0) + (writeErr ?? 0)
                : null;
            string? criticalWarning = health switch
            {
                1 => "Drive reports Warning",
                2 => "Drive reports Unhealthy",
                _ => null,
            };

            var status = DriveHealthService.ComputeStatus(
                healthStatus, temp, healthPct, null, criticalWarning, mediaErrors);

            return new DriveHealthData
            {
                Index = index,
                Model = string.IsNullOrWhiteSpace(model) ? "Unknown drive" : model,
                Serial = serial,
                Firmware = firmware,
                Interface = iface,
                MediaType = mediaType,
                SizeBytes = size,
                TempC = temp is > 0 ? temp : null,
                HealthPercent = healthPct,
                RemainingLifePercent = remainingLife,
                PowerOnHours = powerOnHours,
                PowerCycles = cycles,
                HostReadsGb = s?.DataReadGb,
                HostWritesGb = s?.DataWrittenGb,
                ReallocatedSectors = null,   // not exposed by these WMI classes
                MediaErrors = mediaErrors,
                CriticalWarning = criticalWarning,
                HealthStatusText = healthStatus,
                Status = status,
            };
        }

        // ---- defensive WMI property readers ----

        private static string GetStr(ManagementBaseObject o, string name)
        {
            try { return o[name]?.ToString()?.Trim() ?? ""; } catch { return ""; }
        }

        private static int GetInt(ManagementBaseObject o, string name, int fallback)
        {
            try { var v = o[name]; return v is null ? fallback : Convert.ToInt32(v); }
            catch { return fallback; }
        }

        private static long? GetLong(ManagementBaseObject o, string name)
        {
            try { var v = o[name]; return v is null ? null : Convert.ToInt64(v); }
            catch { return null; }
        }

        private static double? GetDouble(ManagementBaseObject o, string name)
        {
            try { var v = o[name]; return v is null ? null : Convert.ToDouble(v); }
            catch { return null; }
        }

        private static int ParseInt(string s, int fallback)
            => int.TryParse(s, out int v) ? v : fallback;
    }
}
