using System.Management;

namespace Core_Workbench.Services
{
    public sealed record InfoItem(string Label, string Value);

    public sealed class InfoSection
    {
        public string Title { get; init; } = "";
        public List<InfoItem> Items { get; init; } = new();
    }

    /// <summary>Collects PC specifications via WMI for the System Info page.</summary>
    public sealed class SystemInfoService
    {
        public List<InfoSection> Get()
        {
            return new List<InfoSection>
            {
                System(),
                Processor(),
                Memory(),
                Graphics(),
                Motherboard(),
                Storage(),
            };
        }

        private static InfoSection System()
        {
            var s = new InfoSection { Title = "System" };
            var cs = First("Win32_ComputerSystem");
            var os = First("Win32_OperatingSystem");

            string maker = Str(cs, "Manufacturer");
            string model = Str(cs, "Model");
            if (!string.IsNullOrWhiteSpace(maker) || !string.IsNullOrWhiteSpace(model))
                s.Items.Add(new("Device", $"{maker} {model}".Trim()));

            s.Items.Add(new("Computer name", Environment.MachineName));
            s.Items.Add(new("User", Environment.UserName));
            s.Items.Add(new("Operating system",
                $"{Str(os, "Caption")} ({Str(os, "OSArchitecture")})".Replace("Microsoft ", "")));
            s.Items.Add(new("Build", Str(os, "Version") + " · " + Str(os, "BuildNumber")));

            var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
            s.Items.Add(new("Uptime", $"{(int)up.TotalDays}d {up.Hours}h {up.Minutes}m"));
            return s;
        }

        private static InfoSection Processor()
        {
            var s = new InfoSection { Title = "Processor" };
            var cpu = First("Win32_Processor");
            s.Items.Add(new("Model", Str(cpu, "Name")));
            s.Items.Add(new("Cores / threads",
                $"{Num(cpu, "NumberOfCores")} cores · {Num(cpu, "NumberOfLogicalProcessors")} threads"));
            long mhz = Num(cpu, "MaxClockSpeed");
            if (mhz > 0) s.Items.Add(new("Base clock", $"{mhz / 1000.0:0.00} GHz"));
            return s;
        }

        private static InfoSection Memory()
        {
            var s = new InfoSection { Title = "Memory" };
            var cs = First("Win32_ComputerSystem");
            long total = Num(cs, "TotalPhysicalMemory");
            if (total > 0) s.Items.Add(new("Total", $"{total / 1024.0 / 1024 / 1024:0.0} GB"));

            int i = 0;
            foreach (var m in All("Win32_PhysicalMemory"))
            {
                long cap = Num(m, "Capacity");
                long speed = Num(m, "Speed");
                string maker = Str(m, "Manufacturer");
                string slot = Str(m, "DeviceLocator");
                string val = $"{cap / 1024.0 / 1024 / 1024:0} GB";
                if (speed > 0) val += $" · {speed} MHz";
                if (!string.IsNullOrWhiteSpace(maker)) val += $" · {maker}";
                s.Items.Add(new(string.IsNullOrWhiteSpace(slot) ? $"Stick {i + 1}" : slot, val.Trim()));
                i++;
            }
            return s;
        }

        private static InfoSection Graphics()
        {
            var s = new InfoSection { Title = "Graphics" };
            foreach (var g in All("Win32_VideoController"))
            {
                string name = Str(g, "Name");
                if (string.IsNullOrWhiteSpace(name)) continue;
                s.Items.Add(new("GPU", name));
                string drv = Str(g, "DriverVersion");
                if (!string.IsNullOrWhiteSpace(drv)) s.Items.Add(new("Driver", drv));
                long hx = Num(g, "CurrentHorizontalResolution"), vy = Num(g, "CurrentVerticalResolution");
                long hz = Num(g, "CurrentRefreshRate");
                if (hx > 0 && vy > 0) s.Items.Add(new("Resolution", $"{hx} × {vy}" + (hz > 0 ? $" @ {hz} Hz" : "")));
            }
            if (s.Items.Count == 0) s.Items.Add(new("GPU", "—"));
            return s;
        }

        private static InfoSection Motherboard()
        {
            var s = new InfoSection { Title = "Motherboard & BIOS" };
            var bb = First("Win32_BaseBoard");
            s.Items.Add(new("Board", $"{Str(bb, "Manufacturer")} {Str(bb, "Product")}".Trim()));
            var bios = First("Win32_BIOS");
            s.Items.Add(new("BIOS", $"{Str(bios, "Manufacturer")} {Str(bios, "SMBIOSBIOSVersion")}".Trim()));
            return s;
        }

        private static InfoSection Storage()
        {
            var s = new InfoSection { Title = "Storage" };
            foreach (var d in All("Win32_DiskDrive"))
            {
                string model = Str(d, "Model");
                if (string.IsNullOrWhiteSpace(model)) continue;
                long size = Num(d, "Size");
                s.Items.Add(new(model, size > 0 ? $"{size / 1_000_000_000.0:0} GB" : "—"));
            }
            if (s.Items.Count == 0) s.Items.Add(new("Disk", "—"));
            return s;
        }

        // ---- WMI helpers ----

        private static ManagementObject? First(string cls)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM " + cls);
                foreach (ManagementObject o in searcher.Get()) return o;
            }
            catch { }
            return null;
        }

        private static IEnumerable<ManagementObject> All(string cls)
        {
            var list = new List<ManagementObject>();
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM " + cls);
                foreach (ManagementObject o in searcher.Get()) list.Add(o);
            }
            catch { }
            return list;
        }

        private static string Str(ManagementObject? o, string prop)
        {
            try { return o?[prop]?.ToString()?.Trim() ?? ""; } catch { return ""; }
        }

        private static long Num(ManagementObject? o, string prop)
        {
            try { var v = o?[prop]; return v is null ? 0 : Convert.ToInt64(v); } catch { return 0; }
        }
    }
}
