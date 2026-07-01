using System.ComponentModel;
using System.IO;
using Microsoft.Win32;

namespace Core_Workbench.Services
{
    public enum StartupSource { HkcuRun, HklmRun, HklmRun32, UserFolder, CommonFolder }

    /// <summary>A program configured to launch at sign-in. Bindable for the toggle.</summary>
    public sealed class StartupEntry : INotifyPropertyChanged
    {
        public string Name { get; init; } = "";
        public string Command { get; init; } = "";
        public string LocationLabel { get; init; } = "";
        public StartupSource Source { get; init; }

        private bool _enabled = true;
        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; Raise(nameof(Enabled)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    /// <summary>
    /// Reads and edits Windows startup entries from the Run registry keys and the
    /// Startup folders. Enable/disable uses the same "StartupApproved" registry
    /// state Task Manager uses, so changes are consistent with the OS UI.
    /// Requires admin for the machine-wide (HKLM) entries.
    /// </summary>
    public sealed class StartupService
    {
        private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Run32Path = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
        private const string ApprovedRun = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        private const string ApprovedFolder = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

        public List<StartupEntry> GetEntries()
        {
            var list = new List<StartupEntry>();
            ReadRun(Registry.CurrentUser, RunPath, StartupSource.HkcuRun, "Current user · Run", list);
            ReadRun(Registry.LocalMachine, RunPath, StartupSource.HklmRun, "All users · Run", list);
            ReadRun(Registry.LocalMachine, Run32Path, StartupSource.HklmRun32, "All users · Run (32-bit)", list);
            ReadFolder(Environment.SpecialFolder.Startup, StartupSource.UserFolder, "Current user · Startup folder", list);
            ReadFolder(Environment.SpecialFolder.CommonStartup, StartupSource.CommonFolder, "All users · Startup folder", list);
            return list.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private void ReadRun(RegistryKey root, string path, StartupSource source, string label, List<StartupEntry> list)
        {
            try
            {
                using var key = root.OpenSubKey(path);
                if (key == null) return;
                foreach (string name in key.GetValueNames())
                {
                    if (string.IsNullOrEmpty(name)) continue;
                    list.Add(new StartupEntry
                    {
                        Name = name,
                        Command = key.GetValue(name)?.ToString() ?? "",
                        Source = source,
                        LocationLabel = label,
                        Enabled = IsApprovedEnabled(root, ApprovedRun, name),
                    });
                }
            }
            catch { /* key missing or denied */ }
        }

        private void ReadFolder(Environment.SpecialFolder folder, StartupSource source, string label, List<StartupEntry> list)
        {
            try
            {
                string dir = Environment.GetFolderPath(folder);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
                RegistryKey root = source == StartupSource.UserFolder ? Registry.CurrentUser : Registry.LocalMachine;

                foreach (string file in Directory.GetFiles(dir))
                {
                    string fileName = Path.GetFileName(file);
                    if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add(new StartupEntry
                    {
                        Name = Path.GetFileNameWithoutExtension(file),
                        Command = file,
                        Source = source,
                        LocationLabel = label,
                        Enabled = IsApprovedEnabled(root, ApprovedFolder, fileName),
                    });
                }
            }
            catch { }
        }

        private static bool IsApprovedEnabled(RegistryKey root, string approvedPath, string valueName)
        {
            try
            {
                using var k = root.OpenSubKey(approvedPath);
                if (k?.GetValue(valueName) is byte[] data && data.Length > 0)
                    return (data[0] & 0x01) == 0;   // odd first byte = disabled
            }
            catch { }
            return true;   // no record = enabled by default
        }

        /// <summary>Enable or disable an entry via the StartupApproved state.</summary>
        public bool SetEnabled(StartupEntry entry, bool enabled)
        {
            try
            {
                (RegistryKey root, string approvedPath) = ApprovedTarget(entry.Source);
                string valueName = entry.Source is StartupSource.UserFolder or StartupSource.CommonFolder
                    ? Path.GetFileName(entry.Command)
                    : entry.Name;

                using var key = root.CreateSubKey(approvedPath, writable: true);
                if (key == null) return false;

                byte[] data = new byte[12];
                data[0] = (byte)(enabled ? 0x02 : 0x03);
                key.SetValue(valueName, data, RegistryValueKind.Binary);
                entry.Enabled = enabled;
                return true;
            }
            catch { return false; }
        }

        /// <summary>Remove a startup entry entirely (registry value or shortcut file).</summary>
        public bool Remove(StartupEntry entry)
        {
            try
            {
                switch (entry.Source)
                {
                    case StartupSource.HkcuRun:
                        DeleteRunValue(Registry.CurrentUser, RunPath, entry.Name); break;
                    case StartupSource.HklmRun:
                        DeleteRunValue(Registry.LocalMachine, RunPath, entry.Name); break;
                    case StartupSource.HklmRun32:
                        DeleteRunValue(Registry.LocalMachine, Run32Path, entry.Name); break;
                    case StartupSource.UserFolder:
                    case StartupSource.CommonFolder:
                        if (File.Exists(entry.Command)) File.Delete(entry.Command); break;
                }
                return true;
            }
            catch { return false; }
        }

        private static void DeleteRunValue(RegistryKey root, string path, string name)
        {
            using var key = root.OpenSubKey(path, writable: true);
            if (key?.GetValue(name) != null) key.DeleteValue(name, throwOnMissingValue: false);
        }

        private static (RegistryKey root, string path) ApprovedTarget(StartupSource source) => source switch
        {
            StartupSource.HkcuRun => (Registry.CurrentUser, ApprovedRun),
            StartupSource.HklmRun => (Registry.LocalMachine, ApprovedRun),
            StartupSource.HklmRun32 => (Registry.LocalMachine, ApprovedRun),
            StartupSource.UserFolder => (Registry.CurrentUser, ApprovedFolder),
            StartupSource.CommonFolder => (Registry.LocalMachine, ApprovedFolder),
            _ => (Registry.CurrentUser, ApprovedRun),
        };
    }
}
