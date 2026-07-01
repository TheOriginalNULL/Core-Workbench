using System.ComponentModel;
using System.Windows.Media;
using Core_Workbench.Services;

namespace Core_Workbench.Models
{
    /// <summary>Bindable view-model for one process row.</summary>
    public sealed class ProcessVM : INotifyPropertyChanged
    {
        private static readonly Brush Text = Frozen(0xEC, 0xE8, 0xF6);
        private static readonly Brush Amber = Frozen(0xFF, 0xB4, 0x54);
        private static readonly Brush Red = Frozen(0xFF, 0x6B, 0x6B);

        public int Pid { get; private set; }

        private ProcessSnapshot _s = new();

        public void Update(ProcessSnapshot s)
        {
            _s = s;
            Pid = s.Pid;
            Raise(nameof(Name));
            Raise(nameof(PidText));
            Raise(nameof(CpuText));
            Raise(nameof(MemText));
            Raise(nameof(CpuBrush));
            Raise(nameof(MemBrush));
        }

        public string Name => _s.Name;
        public string PidText => _s.Pid.ToString();
        public string CpuText => $"{_s.CpuPercent:0.0}%";
        public string MemText => ProcessService.FormatBytes(_s.MemoryBytes);

        public Brush CpuBrush => _s.CpuPercent >= 50 ? Red : _s.CpuPercent >= 20 ? Amber : Text;
        public Brush MemBrush =>
            _s.MemoryBytes >= 2L * 1024 * 1024 * 1024 ? Red
            : _s.MemoryBytes >= 1L * 1024 * 1024 * 1024 ? Amber
            : Text;

        private static Brush Frozen(byte r, byte g, byte b)
        {
            var br = new SolidColorBrush(Color.FromRgb(r, g, b));
            br.Freeze();
            return br;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
