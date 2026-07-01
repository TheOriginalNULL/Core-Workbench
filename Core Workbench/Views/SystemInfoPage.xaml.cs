using System.Windows;
using System.Windows.Controls;
using Core_Workbench.Services;

namespace Core_Workbench.Views
{
    /// <summary>Read-only PC specifications, grouped into cards.</summary>
    public partial class SystemInfoPage : UserControl
    {
        private readonly SystemInfoService _service = new();

        public SystemInfoPage()
        {
            InitializeComponent();
            Loaded += async (_, _) => await Load();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await Load();

        private async Task Load()
        {
            var sections = await Task.Run(() => _service.Get());
            Sections.ItemsSource = sections;
        }
    }
}
