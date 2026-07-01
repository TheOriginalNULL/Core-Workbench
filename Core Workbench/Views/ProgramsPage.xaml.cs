using System.Collections.ObjectModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Core_Workbench.Services;

namespace Core_Workbench.Views
{
    /// <summary>Lists installed apps/games with search, sort, size filter, and uninstall.</summary>
    public partial class ProgramsPage : UserControl
    {
        private enum SortMode { Name, SizeDesc, SizeAsc }

        private readonly InstalledAppsService _service = new();
        private readonly ObservableCollection<InstalledApp> _view = new();
        private List<InstalledApp> _all = new();
        private CancellationTokenSource? _cts;

        private SortMode _sort = SortMode.Name;
        private long _minSize;

        public ProgramsPage()
        {
            InitializeComponent();
            AppList.ItemsSource = _view;
            UpdateButtonStates();
            Loaded += async (_, _) => await Reload();
            Unloaded += (_, _) => _cts?.Cancel();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await Reload();

        private async Task Reload()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            SubText.Text = "Scanning…";
            _all = await Task.Run(() => _service.GetApps());
            ApplyFilter();

            _ = ComputeMissingSizesAsync(_all, _cts.Token);
        }

        private async Task ComputeMissingSizesAsync(List<InstalledApp> apps, CancellationToken ct)
        {
            foreach (var app in apps)
            {
                if (ct.IsCancellationRequested) return;
                if (app.SizeBytes > 0) continue;

                string? folder = InstalledAppsService.ResolveInstallFolder(app);
                if (folder == null) continue;

                long size = await Task.Run(() => InstalledAppsService.FolderSize(folder, ct), ct);
                if (ct.IsCancellationRequested) return;
                if (size > 0)
                {
                    app.SizeBytes = size;                 // updates the row's size text
                    if (_sort != SortMode.Name || _minSize > 0) ApplyFilter();   // keep order/filter correct
                }
            }
        }

        // ---- search / sort / filter ----

        private void Search_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

        private void Sort_Click(object sender, RoutedEventArgs e)
        {
            _sort = ((sender as Button)?.Tag as string) switch
            {
                "Largest" => SortMode.SizeDesc,
                "Smallest" => SortMode.SizeAsc,
                _ => SortMode.Name,
            };
            UpdateButtonStates();
            ApplyFilter();
        }

        private void Size_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is string tag && long.TryParse(tag, out long bytes))
                _minSize = bytes;
            UpdateButtonStates();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string q = SearchBox.Text?.Trim() ?? "";

            IEnumerable<InstalledApp> items = _all;
            if (!string.IsNullOrEmpty(q))
                items = items.Where(a => a.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                         || a.Publisher.Contains(q, StringComparison.OrdinalIgnoreCase));
            if (_minSize > 0)
                items = items.Where(a => a.SizeBytes >= _minSize);

            items = _sort switch
            {
                SortMode.SizeDesc => items.OrderByDescending(a => a.SizeBytes).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase),
                // Unknown (0) sizes sort last when ascending.
                SortMode.SizeAsc => items.OrderBy(a => a.SizeBytes == 0 ? long.MaxValue : a.SizeBytes).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase),
                _ => items.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase),
            };

            _view.Clear();
            foreach (var a in items) _view.Add(a);

            SubText.Text = $"{_all.Count} installed · {_view.Count} shown";
            EmptyText.Text = _all.Count == 0 ? "No applications found." : "No matches.";
            EmptyText.Visibility = _view.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateButtonStates()
        {
            Highlight(SortName, _sort == SortMode.Name);
            Highlight(SortLargest, _sort == SortMode.SizeDesc);
            Highlight(SortSmallest, _sort == SortMode.SizeAsc);

            Highlight(SizeAll, _minSize == 0);
            Highlight(Size100, _minSize == 104857600);
            Highlight(Size1G, _minSize == 1073741824);
            Highlight(Size5G, _minSize == 5368709120);
        }

        private void Highlight(Button b, bool active)
            => b.Background = (Brush)FindResource(active ? "AccentBrush" : "SurfaceAltBrush");

        // ---- uninstall ----

        private void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not InstalledApp app) return;

            if (MessageBox.Show($"Uninstall \"{app.Name}\"?\n\nThis launches the program's own uninstaller.",
                    "Confirm uninstall", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            if (!_service.Uninstall(app))
            {
                MessageBox.Show("Couldn't start the uninstaller for this app.", "Failed",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            MessageBox.Show("The uninstaller has been launched. Click Refresh once it finishes to update the list.",
                "Uninstalling", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
