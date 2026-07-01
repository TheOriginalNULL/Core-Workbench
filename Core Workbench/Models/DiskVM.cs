using System.ComponentModel;

namespace Core_Workbench.Models
{
    /// <summary>Bindable view-model for one disk panel on the dashboard.</summary>
    public sealed class DiskVM : INotifyPropertyChanged
    {
        private string _name = "Disk";
        public string Name
        {
            get => _name;
            set { _name = value; Raise(nameof(Name)); }
        }

        private double _usedPercent;
        public double UsedPercent
        {
            get => _usedPercent;
            set { _usedPercent = value; Raise(nameof(UsedPercent)); }
        }

        private string _detail = "-- / -- GB";
        public string Detail
        {
            get => _detail;
            set { _detail = value; Raise(nameof(Detail)); }
        }

        private string _tempText = "temp n/a";
        public string TempText
        {
            get => _tempText;
            set { _tempText = value; Raise(nameof(TempText)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
