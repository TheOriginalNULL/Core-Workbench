using System.ComponentModel;
using System.Windows.Media;

namespace Core_Workbench.Models
{
    /// <summary>
    /// One cleanable location (a temp folder or the recycle bin). Implements
    /// INotifyPropertyChanged so the UI updates live as it's scanned/cleaned.
    /// </summary>
    public sealed class CleanTarget : INotifyPropertyChanged
    {
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";

        /// <summary>Folder to clean. Null when <see cref="IsRecycleBin"/> is true.</summary>
        public string? Path { get; init; }

        public bool IsRecycleBin { get; init; }

        /// <summary>Vector icon shown next to the row.</summary>
        public Geometry? Icon { get; init; }

        private bool _selected = true;
        public bool Selected
        {
            get => _selected;
            set { _selected = value; Raise(nameof(Selected)); }
        }

        private long _sizeBytes;
        public long SizeBytes
        {
            get => _sizeBytes;
            set { _sizeBytes = value; Raise(nameof(SizeBytes)); Raise(nameof(SizeText)); }
        }

        private int _fileCount;
        public int FileCount
        {
            get => _fileCount;
            set { _fileCount = value; Raise(nameof(FileCount)); Raise(nameof(DetailText)); }
        }

        private string _status = "Not scanned";
        public string Status
        {
            get => _status;
            set { _status = value; Raise(nameof(Status)); }
        }

        public string SizeText => FormatBytes(SizeBytes);

        public string DetailText =>
            FileCount > 0 ? $"{FileCount:N0} item{(FileCount == 1 ? "" : "s")}" : "";

        public static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
            return $"{size:0.#} {units[unit]}";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
