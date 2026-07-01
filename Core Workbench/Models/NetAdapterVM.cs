using System.ComponentModel;
using Core_Workbench.Services;

namespace Core_Workbench.Models
{
    /// <summary>Bindable view-model for one network adapter card.</summary>
    public sealed class NetAdapterVM : INotifyPropertyChanged
    {
        public string Id { get; private set; } = "";

        private NetAdapterSnapshot _s = new();

        public void Update(NetAdapterSnapshot s)
        {
            _s = s;
            Id = s.Id;
            foreach (var p in _props) Raise(p);
        }

        public string Name => _s.Name;
        public string Description => _s.Description;
        public string Ipv4 => _s.Ipv4;
        public string DownText => NetworkService.FormatRate(_s.DownBytesPerSec);
        public string UpText => NetworkService.FormatRate(_s.UpBytesPerSec);
        public string TotalReceived => NetworkService.FormatBytes(_s.TotalReceived);
        public string TotalSent => NetworkService.FormatBytes(_s.TotalSent);

        public string LinkSpeed
        {
            get
            {
                if (_s.LinkSpeedBits <= 0) return "—";
                double mbps = _s.LinkSpeedBits / 1_000_000.0;
                return mbps >= 1000 ? $"{mbps / 1000:0.#} Gbps" : $"{mbps:0} Mbps";
            }
        }

        private static readonly string[] _props =
        {
            nameof(Name), nameof(Description), nameof(Ipv4), nameof(DownText), nameof(UpText),
            nameof(TotalReceived), nameof(TotalSent), nameof(LinkSpeed),
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
