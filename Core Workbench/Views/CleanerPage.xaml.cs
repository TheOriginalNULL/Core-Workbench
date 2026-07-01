using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Core_Workbench.Models;
using Core_Workbench.Services;

namespace Core_Workbench.Views
{
    /// <summary>
    /// Scans the configured temp locations, shows reclaimable space, and cleans
    /// the selected ones. All disk work runs off the UI thread.
    /// </summary>
    public partial class CleanerPage : UserControl
    {
        private readonly CleanerService _service = new();
        private readonly ObservableCollection<CleanTarget> _targets;

        public CleanerPage()
        {
            InitializeComponent();
            _targets = new ObservableCollection<CleanTarget>(_service.BuildTargets());
            TargetList.ItemsSource = _targets;

            // Keep the "reclaimable" total in sync as checkboxes are toggled.
            foreach (var t in _targets)
                t.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(CleanTarget.Selected)) UpdateTotal();
                };

            // Auto-scan on first open so the user sees numbers immediately.
            Loaded += async (_, _) => await ScanAll();
        }

        private async void Scan_Click(object sender, RoutedEventArgs e) => await ScanAll();

        private void SelectAll_Click(object sender, RoutedEventArgs e) => SetAllSelected(true);

        private void SelectNone_Click(object sender, RoutedEventArgs e) => SetAllSelected(false);

        private void SetAllSelected(bool selected)
        {
            foreach (var t in _targets) t.Selected = selected;
            UpdateTotal();
        }

        private async Task ScanAll()
        {
            SetBusy(true, "Scanning…");
            foreach (var t in _targets) t.Status = "Scanning…";

            await Task.Run(() =>
            {
                foreach (var t in _targets)
                    _service.Scan(t);
            });

            UpdateTotal();
            SetBusy(false, "Scan complete.");
            CleanButton.IsEnabled = true;
        }

        private async void Clean_Click(object sender, RoutedEventArgs e)
        {
            var chosen = _targets.Where(t => t.Selected).ToList();
            if (chosen.Count == 0)
            {
                FooterStatus.Text = "Nothing selected.";
                return;
            }

            long willFree = chosen.Sum(t => t.SizeBytes);
            if (MessageBox.Show(
                    $"Clean {chosen.Count} location(s) and free about {CleanTarget.FormatBytes(willFree)}?\n\n" +
                    "Files currently in use are skipped automatically.",
                    "Confirm clean", MessageBoxButton.YesNo, MessageBoxImage.Question)
                != MessageBoxResult.Yes)
                return;

            SetBusy(true, "Cleaning…");

            long totalFreed = 0;
            int totalFiles = 0, totalSkipped = 0;

            await Task.Run(() =>
            {
                foreach (var t in chosen)
                {
                    t.Status = "Cleaning…";
                    var r = _service.Clean(t);
                    totalFreed += r.BytesFreed;
                    totalFiles += r.FilesDeleted;
                    totalSkipped += r.Skipped;
                }
            });

            UpdateTotal();
            SetBusy(false,
                $"Freed {CleanTarget.FormatBytes(totalFreed)} · {totalFiles:N0} files removed" +
                (totalSkipped > 0 ? $" · {totalSkipped:N0} in use" : ""));
        }

        private void UpdateTotal()
        {
            long total = _targets.Where(t => t.Selected).Sum(t => t.SizeBytes);
            TotalText.Text = CleanTarget.FormatBytes(total);
        }

        private void SetBusy(bool busy, string status)
        {
            ScanButton.IsEnabled = !busy;
            CleanButton.IsEnabled = !busy;
            FooterStatus.Text = status;
        }
    }
}
