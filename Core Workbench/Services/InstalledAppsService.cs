using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Win32;

namespace Core_Workbench.Services
{
    /// <summary>One installed program/game from the Windows uninstall registry.</summary>
    public sealed class InstalledApp : INotifyPropertyChanged
    {
        public string Name { get; init; } = "";
        public string Publisher { get; init; } = "";
        public string Version { get; init; } = "";
        public string UninstallCommand { get; init; } = "";
        public string InstallLocation { get; init; } = "";
        public string DisplayIcon { get; init; } = "";

        private long _sizeBytes;
        public long SizeBytes
        {
            get => _sizeBytes;
            set { _sizeBytes = value; Raise(nameof(SizeText)); }
        }

        public string Detail =>
            string.Join("  ·  ", new[] { Publisher, Version }.Where(s => !string.IsNullOrWhiteSpace(s)));

        public string SizeText
        {
            get
            {
                if (SizeBytes <= 0) return "—";
                string[] u = { "B", "KB", "MB", "GB", "TB" };
                double s = SizeBytes; int i = 0;
                while (s >= 1024 && i < u.Length - 1) { s /= 1024; i++; }
                return $"{s:0.#} {u[i]}";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    /// <summary>
    /// Enumerates installed applications from the standard uninstall registry keys
    /// (per-machine 64/32-bit and per-user) and launches their uninstallers.
    /// Updates, patches, and OS components are filtered out.
    /// </summary>
    public sealed class InstalledAppsService
    {
        private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        private const string UninstallPath32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

        public List<InstalledApp> GetApps()
        {
            var byName = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);

            ReadKey(Registry.LocalMachine, UninstallPath, byName);
            ReadKey(Registry.LocalMachine, UninstallPath32, byName);
            ReadKey(Registry.CurrentUser, UninstallPath, byName);

            return byName.Values.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void ReadKey(RegistryKey root, string path, Dictionary<string, InstalledApp> byName)
        {
            try
            {
                using var key = root.OpenSubKey(path);
                if (key == null) return;

                foreach (string sub in key.GetSubKeyNames())
                {
                    try
                    {
                        using var app = key.OpenSubKey(sub);
                        if (app == null) continue;

                        string name = (app.GetValue("DisplayName") as string)?.Trim() ?? "";
                        if (string.IsNullOrEmpty(name)) continue;

                        // Skip OS components, updates, and child entries.
                        if (Convert.ToInt32(app.GetValue("SystemComponent") ?? 0) == 1) continue;
                        if (app.GetValue("ParentKeyName") != null || app.GetValue("ParentDisplayName") != null) continue;
                        string releaseType = app.GetValue("ReleaseType") as string ?? "";
                        if (releaseType is "Update" or "Hotfix" or "Security Update") continue;
                        if (name.StartsWith("Update for", StringComparison.OrdinalIgnoreCase) ||
                            name.StartsWith("Security Update", StringComparison.OrdinalIgnoreCase) ||
                            name.StartsWith("Hotfix", StringComparison.OrdinalIgnoreCase)) continue;

                        string uninstall = app.GetValue("QuietUninstallString") as string
                                           ?? app.GetValue("UninstallString") as string ?? "";
                        if (string.IsNullOrWhiteSpace(uninstall)) continue;   // nothing to run

                        long sizeBytes = 0;
                        if (app.GetValue("EstimatedSize") is int kb && kb > 0) sizeBytes = (long)kb * 1024;

                        if (byName.ContainsKey(name)) continue;   // first wins (de-dupe 64/32)

                        byName[name] = new InstalledApp
                        {
                            Name = name,
                            Publisher = (app.GetValue("Publisher") as string)?.Trim() ?? "",
                            Version = (app.GetValue("DisplayVersion") as string)?.Trim() ?? "",
                            SizeBytes = sizeBytes,
                            UninstallCommand = uninstall,
                            InstallLocation = (app.GetValue("InstallLocation") as string)?.Trim() ?? "",
                            DisplayIcon = (app.GetValue("DisplayIcon") as string)?.Trim() ?? "",
                        };
                    }
                    catch { /* skip a malformed entry */ }
                }
            }
            catch { /* key unavailable */ }
        }

        /// <summary>Run the app's uninstaller (shows its own UI). Returns false if it couldn't start.</summary>
        public bool Uninstall(InstalledApp app)
        {
            try
            {
                // cmd /c handles the quoting of MsiExec / "unins000.exe" style commands.
                Process.Start(new ProcessStartInfo("cmd.exe", "/c " + app.UninstallCommand)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Best-effort install folder to measure: InstallLocation, else the folder of
        /// the DisplayIcon exe, else the folder of the uninstaller exe. Null if none
        /// is safe to scan.
        /// </summary>
        public static string? ResolveInstallFolder(InstalledApp app)
        {
            string fromLocation = app.InstallLocation.Trim().TrimEnd('\\');
            if (CanMeasure(fromLocation)) return fromLocation;

            string? fromIcon = FolderFromPathValue(app.DisplayIcon);
            if (fromIcon != null) return fromIcon;

            return FolderFromPathValue(ExtractExe(app.UninstallCommand));
        }

        /// <summary>Strip an icon index / quotes, then return the containing folder if measurable.</summary>
        private static string? FolderFromPathValue(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string p = raw.Trim().Trim('"');

            // Drop a trailing ",0" / ",-12" icon index.
            int comma = p.LastIndexOf(',');
            if (comma > 1 && int.TryParse(p[(comma + 1)..], out _)) p = p[..comma].Trim();

            try
            {
                string? dir = File.Exists(p) ? Path.GetDirectoryName(p)
                            : Directory.Exists(p) ? p
                            : null;
                return dir != null && CanMeasure(dir) ? dir.TrimEnd('\\') : null;
            }
            catch { return null; }
        }

        private static string ExtractExe(string command)
        {
            string c = command.Trim();
            if (c.StartsWith('"'))
            {
                int end = c.IndexOf('"', 1);
                if (end > 1) return c[1..end];
            }
            int space = c.IndexOf(' ');
            return space > 0 ? c[..space] : c;
        }

        /// <summary>Whether a folder is safe/worth measuring (not a shared or system root).</summary>
        public static bool CanMeasure(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string p = path.Trim().TrimEnd('\\');
            if (p.Length < 4) return false;                 // e.g. "C:\"
            if (!Directory.Exists(p)) return false;

            string lower = p.ToLowerInvariant();
            if (lower.StartsWith(@"c:\windows")) return false;
            string[] shared =
            {
                @"c:\program files", @"c:\program files (x86)",
                @"c:\programdata", @"c:\users",
            };
            return !shared.Contains(lower);
        }

        /// <summary>
        /// Total size of a folder tree, swallowing per-directory errors and giving up
        /// after <paramref name="maxMs"/> so a massive folder doesn't stall the scan
        /// (returns the partial total in that case).
        /// </summary>
        public static long FolderSize(string path, CancellationToken ct = default, int maxMs = 5000)
        {
            long total = 0;
            var sw = Stopwatch.StartNew();
            var stack = new Stack<string>();
            stack.Push(path);
            while (stack.Count > 0)
            {
                if (ct.IsCancellationRequested || sw.ElapsedMilliseconds > maxMs) break;
                string dir = stack.Pop();
                try { foreach (var f in Directory.GetFiles(dir)) { try { total += new FileInfo(f).Length; } catch { } } }
                catch { }
                try { foreach (var d in Directory.GetDirectories(dir)) stack.Push(d); }
                catch { }
            }
            return total;
        }
    }
}
