using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Core_Workbench.Services;

namespace Core_Workbench.Views
{
    /// <summary>Lists Windows startup programs and lets you enable, disable, or remove them.</summary>
    public partial class StartupPage : UserControl
    {
        private readonly StartupService _service = new();
        private readonly ObservableCollection<StartupEntry> _entries = new();

        public StartupPage()
        {
            InitializeComponent();
            StartupList.ItemsSource = _entries;
            Loaded += (_, _) => Reload();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

        private void Reload()
        {
            _entries.Clear();
            foreach (var entry in _service.GetEntries()) _entries.Add(entry);

            int on = _entries.Count(e => e.Enabled);
            SubText.Text = $"{_entries.Count} entries · {on} enabled";
            EmptyText.Visibility = _entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Toggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb || cb.Tag is not StartupEntry entry) return;

            bool want = !entry.Enabled;     // checkbox is OneWay; compute target
            if (!_service.SetEnabled(entry, want))
            {
                MessageBox.Show("Couldn't change that entry (it may need different permissions).",
                    "Failed", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            cb.IsChecked = entry.Enabled;
            int on = _entries.Count(x => x.Enabled);
            SubText.Text = $"{_entries.Count} entries · {on} enabled";
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not StartupEntry entry) return;

            if (MessageBox.Show($"Remove \"{entry.Name}\" from startup?\n\nThis deletes the entry, not the program.",
                    "Confirm remove", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            if (_service.Remove(entry)) _entries.Remove(entry);
            else MessageBox.Show("Couldn't remove that entry.", "Failed",
                MessageBoxButton.OK, MessageBoxImage.Information);

            Reload();
        }
    }
}
