using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Core_Workbench.Models;
using Core_Workbench.Services;
using Core_Workbench.Services.DriveHealth;

namespace Core_Workbench.Views
{
    /// <summary>
    /// Performance + Drive Health in one page. The top half polls live hardware
    /// usage every second; the lower half collects S.M.A.R.T./NVMe drive health
    /// asynchronously, refreshing every 30 seconds with optional alerts.
    /// </summary>
    public partial class PerformancePage : UserControl
    {
        // ---- live usage (shared monitor, 1s) ----
        private readonly HardwareMonitorService _monitor = HardwareMonitor.Instance;
        private readonly DispatcherTimer _usageTimer;
        private readonly ObservableCollection<DiskVM> _disks = new();
        private readonly NetworkService _net = new();
        private readonly ObservableCollection<NetAdapterVM> _adapters = new();
        private bool _usageBusy;
        private bool _sawAnyTemp;

        // ---- drive health (WMI + SMART, 30s) ----
        private readonly DriveHealthService _healthService = new();
        private readonly ObservableCollection<DriveHealthVM> _drives = new();
        private readonly DispatcherTimer _healthTimer;
        private readonly DhSettings _settings = DhSettings.Load();
        private List<DriveHealthData> _lastData = new();
        private bool _healthBusy;

        public PerformancePage()
        {
            InitializeComponent();

            DiskList.ItemsSource = _disks;
            AdapterList.ItemsSource = _adapters;
            DriveList.ItemsSource = _drives;
            NotifyCheck.IsChecked = _settings.Notify;
            ThresholdText.Text = $"{_settings.HealthThreshold}%";

            _usageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _usageTimer.Tick += async (_, _) => await UsageTick();

            _healthTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _healthTimer.Tick += async (_, _) => await RefreshHealth();

            Loaded += async (_, _) =>
            {
                _usageTimer.Start();
                _healthTimer.Start();
                _ = UsageTick();
                await RefreshHealth();
            };
            Unloaded += (_, _) =>
            {
                _usageTimer.Stop();
                _healthTimer.Stop();
            };
        }

        // ============ live usage ============

        private async Task UsageTick()
        {
            if (_usageBusy) return;
            _usageBusy = true;
            try
            {
                HardwareSnapshot snap = await Task.Run(() => _monitor.Read());
                ApplyUsage(snap);
            }
            catch { /* transient sensor hiccup */ }
            finally { _usageBusy = false; }
        }

        private void ApplyUsage(HardwareSnapshot s)
        {
            CpuName.Text = s.CpuName;
            CpuGauge.Value = s.CpuLoad;
            CpuTemp.Text = s.CpuTemp.HasValue ? $"{s.CpuTemp:0} °C" : "temp n/a";

            GpuName.Text = s.GpuName;
            GpuGauge.Value = s.GpuLoad;
            GpuTemp.Text = s.GpuTemp.HasValue ? $"{s.GpuTemp:0} °C" : "temp n/a";

            RamGauge.Value = s.RamLoad;
            RamGauge.ValueText = $"{s.RamLoad:0}%";
            RamDetail.Text = s.RamTotalGb > 0
                ? $"{s.RamUsedGb:0.0} / {s.RamTotalGb:0.0} GB"
                : "-- / -- GB";

            // History graphs.
            SparkCpuLoad.Push(s.CpuLoad); CpuLoadVal.Text = $"{s.CpuLoad:0}%";
            SparkGpuLoad.Push(s.GpuLoad); GpuLoadVal.Text = $"{s.GpuLoad:0}%";
            if (s.CpuTemp.HasValue) { SparkCpuTemp.Push(s.CpuTemp.Value); CpuTempVal.Text = $"{s.CpuTemp:0}°C"; }
            else CpuTempVal.Text = "n/a";
            if (s.GpuTemp.HasValue) { SparkGpuTemp.Push(s.GpuTemp.Value); GpuTempVal.Text = $"{s.GpuTemp:0}°C"; }
            else GpuTempVal.Text = "n/a";

            UpdateUsageDisks(s.Disks);
            UpdateNetwork();

            if (s.CpuTemp.HasValue || s.GpuTemp.HasValue || s.Disks.Any(d => d.TempC.HasValue))
                _sawAnyTemp = true;
            AdminHint.Visibility = _sawAnyTemp ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UpdateNetwork()
        {
            List<NetAdapterSnapshot> snaps = _net.Read();
            NoNetText.Visibility = snaps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            if (_adapters.Count != snaps.Count)
            {
                _adapters.Clear();
                foreach (var _ in snaps) _adapters.Add(new NetAdapterVM());
            }
            for (int i = 0; i < snaps.Count; i++)
                _adapters[i].Update(snaps[i]);
        }

        private void UpdateUsageDisks(IReadOnlyList<DiskSnapshot> snaps)
        {
            NoDisksText.Visibility = snaps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            if (_disks.Count != snaps.Count)
            {
                _disks.Clear();
                foreach (var _ in snaps) _disks.Add(new DiskVM());
            }
            for (int i = 0; i < snaps.Count; i++)
            {
                DiskSnapshot d = snaps[i];
                DiskVM vm = _disks[i];
                vm.Name = d.Name;
                vm.UsedPercent = d.UsedPercent;
                vm.Detail = d.TotalGb > 0 ? $"{d.UsedGb:0.0} / {d.TotalGb:0.0} GB" : "size n/a";
                vm.TempText = d.TempC.HasValue ? $"{d.TempC:0} °C" : "temp n/a";
            }
        }

        // ============ drive health ============

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshHealth();

        private async Task RefreshHealth()
        {
            if (_healthBusy) return;
            _healthBusy = true;
            RefreshButton.IsEnabled = false;
            try
            {
                List<DriveHealthData> data = await _healthService.CollectAsync();
                _lastData = data;
                ApplyDrives(data);
                EvaluateAlerts(data);
                UpdatedText.Text = $"Updated {DateTime.Now:HH:mm:ss}";
            }
            catch { /* keep last good data */ }
            finally
            {
                _healthBusy = false;
                RefreshButton.IsEnabled = true;
            }
        }

        private void ApplyDrives(List<DriveHealthData> data)
        {
            if (_drives.Count != data.Count)
            {
                _drives.Clear();
                foreach (var d in data)
                {
                    var vm = new DriveHealthVM();
                    vm.Update(d);
                    _drives.Add(vm);
                }
            }
            else
            {
                for (int i = 0; i < data.Count; i++)
                    _drives[i].Update(data[i]);
            }
        }

        private void EvaluateAlerts(List<DriveHealthData> data)
        {
            if (!_settings.Notify)
            {
                AlertBanner.Visibility = Visibility.Collapsed;
                return;
            }

            var alerts = new List<string>();
            foreach (var d in data)
            {
                if (d.TempC is >= 70)
                    alerts.Add($"{d.Model}: temperature {d.TempC:0} °C exceeds the safe limit.");
                if (d.HealthPercent is double h && h < _settings.HealthThreshold)
                    alerts.Add($"{d.Model}: health {h:0}% is below the {_settings.HealthThreshold}% threshold.");
                if (!string.IsNullOrEmpty(d.CriticalWarning))
                    alerts.Add($"{d.Model}: {d.CriticalWarning}.");
            }

            if (alerts.Count == 0)
            {
                AlertBanner.Visibility = Visibility.Collapsed;
            }
            else
            {
                AlertText.Text = string.Join("\n", alerts);
                AlertBanner.Visibility = Visibility.Visible;
            }
        }

        private void Notify_Click(object sender, RoutedEventArgs e)
        {
            _settings.Notify = NotifyCheck.IsChecked == true;
            _settings.Save();
            EvaluateAlerts(_lastData);
        }

        private void ThresholdUp_Click(object sender, RoutedEventArgs e) => AdjustThreshold(+5);
        private void ThresholdDown_Click(object sender, RoutedEventArgs e) => AdjustThreshold(-5);

        private void AdjustThreshold(int delta)
        {
            _settings.HealthThreshold = Math.Clamp(_settings.HealthThreshold + delta, 0, 95);
            ThresholdText.Text = $"{_settings.HealthThreshold}%";
            _settings.Save();
            EvaluateAlerts(_lastData);
        }

        // ---- persisted notification settings ----

        private sealed class DhSettings
        {
            public bool Notify { get; set; } = true;
            public int HealthThreshold { get; set; } = 50;

            private static string FilePath => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Core Workbench", "drivehealth.json");

            public static DhSettings Load()
            {
                try
                {
                    if (File.Exists(FilePath))
                        return JsonSerializer.Deserialize<DhSettings>(File.ReadAllText(FilePath)) ?? new();
                }
                catch { }
                return new DhSettings();
            }

            public void Save()
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                    File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
                }
                catch { }
            }
        }
    }
}
