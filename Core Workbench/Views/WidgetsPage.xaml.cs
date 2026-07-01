using System.Windows;
using System.Windows.Controls;
using Core_Workbench.Services;

namespace Core_Workbench.Views
{
    /// <summary>Gallery to add live desktop widgets and a list of the active ones.</summary>
    public partial class WidgetsPage : UserControl
    {
        public WidgetsPage()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                WidgetManager.Changed += RefreshActive;
                RefreshActive();
            };
            Unloaded += (_, _) => WidgetManager.Changed -= RefreshActive;
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string tag
                && Enum.TryParse<WidgetType>(tag, out var type))
            {
                WidgetManager.OpenSystem(type);
            }
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is ActiveWidget aw) aw.Window.Close();
        }

        private void RefreshActive()
        {
            // Bind to lightweight descriptors — never the WidgetWindow visuals
            // themselves, or the ItemsControl would try to host them as children.
            var list = WidgetManager.Active
                .Select(w => new ActiveWidget { TitleLabel = w.TitleLabel, Type = w.Type.ToString(), Window = w })
                .ToList();
            ActiveList.ItemsSource = list;
            EmptyActive.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private sealed class ActiveWidget
        {
            public string TitleLabel { get; init; } = "";
            public string Type { get; init; } = "";
            public WidgetWindow Window { get; init; } = null!;
        }
    }
}
