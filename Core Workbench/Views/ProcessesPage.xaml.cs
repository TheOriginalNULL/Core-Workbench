using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Core_Workbench.Models;
using Core_Workbench.Services;

namespace Core_Workbench.Views
{
    /// <summary>
    /// Mini task-manager: live CPU/memory per process with search, sort, colour-coded
    /// values, an "open file location" action, and a kill button. Enumeration runs off
    /// the UI thread; the list refreshes every 2 seconds.
    /// </summary>
    public partial class ProcessesPage : UserControl
    {
        private enum SortMode { Cpu, Mem, Name }
        private const int TopCount = 50;

        private readonly ProcessService _service = new();
        private readonly ObservableCollection<ProcessVM> _procs = new();
        private readonly Dictionary<int, ProcessVM> _cache = new();
        private readonly DispatcherTimer _timer;
        private List<ProcessSnapshot> _last = new();
        private SortMode _sort = SortMode.Cpu;
        private bool _busy;

        public ProcessesPage()
        {
            InitializeComponent();
            ProcessList.ItemsSource = _procs;
            UpdateSortButtons();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _timer.Tick += async (_, _) => await Tick();

            Loaded += async (_, _) => { _timer.Start(); await Tick(); };
            Unloaded += (_, _) => _timer.Stop();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await Tick();

        private async Task Tick()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                _last = await Task.Run(() => _service.Read());
                RebuildView();
            }
            finally { _busy = false; }
        }

        // ---- search / sort ----

        private void Search_TextChanged(object sender, TextChangedEventArgs e) => RebuildView();

        private void Sort_Click(object sender, RoutedEventArgs e)
        {
            _sort = ((sender as Button)?.Tag as string) switch
            {
                "Mem" => SortMode.Mem,
                "Name" => SortMode.Name,
                _ => SortMode.Cpu,
            };
            UpdateSortButtons();
            RebuildView();
        }

        private void RebuildView()
        {
            string q = SearchBox.Text?.Trim() ?? "";

            IEnumerable<ProcessSnapshot> items = _last;
            if (!string.IsNullOrEmpty(q))
                items = items.Where(p => p.Name.Contains(q, StringComparison.OrdinalIgnoreCase));

            items = _sort switch
            {
                SortMode.Mem => items.OrderByDescending(p => p.MemoryBytes),
                SortMode.Name => items.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase),
                _ => items.OrderByDescending(p => p.CpuPercent).ThenByDescending(p => p.MemoryBytes),
            };

            // For CPU/Memory show the top N; for Name show everything (filtered).
            var shown = (_sort == SortMode.Name ? items : items.Take(TopCount)).ToList();

            _procs.Clear();
            foreach (var s in shown)
            {
                if (!_cache.TryGetValue(s.Pid, out var vm))
                {
                    vm = new ProcessVM();
                    _cache[s.Pid] = vm;
                }
                vm.Update(s);
                _procs.Add(vm);
            }

            if (_cache.Count > 600)
            {
                var keep = _last.Select(s => s.Pid).ToHashSet();
                foreach (int pid in _cache.Keys.Where(k => !keep.Contains(k)).ToList())
                    _cache.Remove(pid);
            }

            long totalMem = shown.Sum(s => s.MemoryBytes);
            SubText.Text = $"{shown.Count} of {_last.Count} processes · {ProcessService.FormatBytes(totalMem)} shown";
            EmptyText.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateSortButtons()
        {
            Highlight(SortCpu, _sort == SortMode.Cpu);
            Highlight(SortMem, _sort == SortMode.Mem);
            Highlight(SortName, _sort == SortMode.Name);
        }

        private void Highlight(Button b, bool active)
            => b.Background = (Brush)FindResource(active ? "AccentBrush" : "SurfaceAltBrush");

        // ---- row actions ----

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not int pid) return;
            try
            {
                using var p = Process.GetProcessById(pid);
                string? path = p.MainModule?.FileName;
                if (string.IsNullOrEmpty(path)) throw new InvalidOperationException();
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
            catch
            {
                MessageBox.Show("Couldn't open the file location (the process may be protected).",
                    "Open location", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void Kill_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not int pid) return;

            string name = _procs.FirstOrDefault(p => p.Pid == pid)?.Name ?? $"PID {pid}";
            if (MessageBox.Show($"End process \"{name}\" (PID {pid})?", "Confirm",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            if (!_service.Kill(pid))
                MessageBox.Show("Couldn't end that process (it may be protected or already gone).",
                    "Failed", MessageBoxButton.OK, MessageBoxImage.Information);

            await Tick();
        }
    }
}
