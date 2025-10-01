using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using BaseLogApp.Core.Data;
using BaseLogApp.Core.Models;

namespace BaseLogApp.Core.ViewModels
{
    public class JumpsViewModel : INotifyPropertyChanged
    {
        private readonly IJumpsReader _reader;
        public ObservableCollection<JumpListItem> Items { get; } = new();
        private bool _isBusy;
        public bool IsBusy { get => _isBusy; set { _isBusy = value; OnPropertyChanged(); } }

        public JumpsViewModel(IJumpsReader reader) => _reader = reader;

        public async Task LoadAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            Items.Clear();
            var rows = await _reader.GetJumpsAsync();
            foreach (var it in rows) Items.Add(it);
            IsBusy = false;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        void OnPropertyChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}