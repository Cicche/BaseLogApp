using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using BaseLogApp.Core.Data;
using BaseLogApp.Core.Models;

namespace BaseLogApp.Core.ViewModels
{
    public class JumpsViewModel : INotifyPropertyChanged
    {
        public string Title
        {
            get => _title;
            private set { if (_title != value) { _title = value; OnPropertyChanged(); } }
        }
        private string _title = "Salti";

        private int _count;
        public int Count
        {
            get => _count;
            private set
            {
                if (_count == value) return;
                _count = value;
                OnPropertyChanged();
                Title = $"Salti: {Count}";
            }
        }

        private readonly IJumpsReader _reader;
        // Tutti i salti
        public ObservableCollection<JumpListItem> Items { get; } = new();

        // Sorgente per la CollectionView (filtrata)
        public ObservableCollection<JumpListItem> FilteredItems { get; } = new();

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set { if (_isBusy != value) { _isBusy = value; OnPropertyChanged(); } }
        }

        private string? _query;
        public string? Query
        {
            get => _query;
            set
            {
                if (_query == value) return;
                _query = value;
                OnPropertyChanged();
                ApplyFilter(_query);
            }
        }

        public JumpsViewModel(IJumpsReader reader)
        {
            _reader = reader;
        }

        public async Task LoadAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                Items.Clear();
                FilteredItems.Clear();

                var rows = await _reader.GetJumpsAsync();

                foreach (var it in rows)
                {
                    Items.Add(it);
                    FilteredItems.Add(it);
                }
                Count = FilteredItems.Count; // conteggio iniziale (tutti)
            }
            finally
            {
                IsBusy = false;
            }
        }

        // Filtro in memoria su numero, data (senza orario), oggetto e tipo
        public void ApplyFilter(string? text)
        {
            var q = (text ?? string.Empty).Trim();

            if (q.Length == 0)
            {
                // reset rapido
                FilteredItems.Clear();
                foreach (var it in Items)
                    FilteredItems.Add(it);
                return;
            }

            var qLower = q.ToLowerInvariant();

            // Filtra con StringComparison OrdinalIgnoreCase su stringhe
            IEnumerable<JumpListItem> filtered = Items.Where(it =>
                   it.NumeroSalto.ToString().Contains(qLower, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(it.Data) && it.Data.Contains(q, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrEmpty(it.Oggetto) && it.Oggetto.Contains(q, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrEmpty(it.TipoSalto) && it.TipoSalto.Contains(q, StringComparison.OrdinalIgnoreCase)));

            FilteredItems.Clear();
            foreach (var it in filtered)
                FilteredItems.Add(it);
            Count = FilteredItems.Count; // conteggio filtrato
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}