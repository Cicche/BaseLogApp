using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
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

        private int _nextJumpNumber = 1;
        public int NextJumpNumber
        {
            get => _nextJumpNumber;
            private set
            {
                if (_nextJumpNumber == value) return;
                _nextJumpNumber = value;
                OnPropertyChanged();
            }
        }

        private int _totalJumps;
        public int TotalJumps
        {
            get => _totalJumps;
            private set { if (_totalJumps != value) { _totalJumps = value; OnPropertyChanged(); } }
        }

        private int _uniqueObjects;
        public int UniqueObjects
        {
            get => _uniqueObjects;
            private set { if (_uniqueObjects != value) { _uniqueObjects = value; OnPropertyChanged(); } }
        }

        private string _lastJumpDate = "-";
        public string LastJumpDate
        {
            get => _lastJumpDate;
            private set { if (_lastJumpDate != value) { _lastJumpDate = value; OnPropertyChanged(); } }
        }

        private int _last30Days;
        public int Last30Days
        {
            get => _last30Days;
            private set { if (_last30Days != value) { _last30Days = value; OnPropertyChanged(); } }
        }

        private int _last365Days;
        public int Last365Days
        {
            get => _last365Days;
            private set { if (_last365Days != value) { _last365Days = value; OnPropertyChanged(); } }
        }

        private readonly IJumpsReader _reader;
        public ObservableCollection<JumpListItem> Items { get; } = new();
        public ObservableCollection<JumpListItem> FilteredItems { get; } = new();
        public ObservableCollection<StatBarItem> ObjectStats { get; } = new();
        public ObservableCollection<StatBarItem> TopYearStats { get; } = new();
        public ObservableCollection<StatBarItem> MonthlyStats { get; } = new();

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
                foreach (var it in rows.OrderByDescending(x => x.NumeroSalto))
                    Items.Add(it);

                ApplyFilter(Query);
                RecalculateStats();
            }
            finally
            {
                IsBusy = false;
            }
        }


        public async Task<IReadOnlyList<string>> GetObjectNamesAsync()
            => await _reader.GetObjectNamesAsync();

        public void AddJump(JumpListItem newJump)
        {
            var insertIndex = Items.TakeWhile(x => x.NumeroSalto > newJump.NumeroSalto).Count();
            Items.Insert(insertIndex, newJump);

            ApplyFilter(Query);
            RecalculateStats();
        }

        public void ApplyFilter(string? text)
        {
            var q = (text ?? string.Empty).Trim();

            IEnumerable<JumpListItem> filtered = Items;
            if (q.Length > 0)
            {
                filtered = Items.Where(it =>
                       it.NumeroSalto.ToString().Contains(q, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrEmpty(it.Data) && it.Data.Contains(q, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrEmpty(it.Oggetto) && it.Oggetto.Contains(q, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrEmpty(it.TipoSalto) && it.TipoSalto.Contains(q, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrEmpty(it.Note) && it.Note.Contains(q, StringComparison.OrdinalIgnoreCase)));
            }

            FilteredItems.Clear();
            foreach (var it in filtered.OrderByDescending(x => x.NumeroSalto))
                FilteredItems.Add(it);

            Count = FilteredItems.Count;
        }

        private void RecalculateStats()
        {
            TotalJumps = Items.Count;
            NextJumpNumber = Items.Count == 0 ? 1 : Items.Max(x => x.NumeroSalto) + 1;

            UniqueObjects = Items.Select(x => x.Oggetto)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            var dated = Items
                .Select(i => new { Item = i, Date = ParseDate(i.Data) })
                .Where(x => x.Date.HasValue)
                .Select(x => new { x.Item, Date = x.Date!.Value })
                .ToList();

            var latest = dated.OrderByDescending(x => x.Date).FirstOrDefault();
            LastJumpDate = latest?.Date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? "-";

            var now = DateTime.Now.Date;
            Last30Days = dated.Count(x => x.Date >= now.AddDays(-30));
            Last365Days = dated.Count(x => x.Date >= now.AddDays(-365));

            ObjectStats.Clear();
            var objectGroups = Items
                .Where(i => !string.IsNullOrWhiteSpace(i.Oggetto))
                .GroupBy(i => i.Oggetto!)
                .Select(g => new { Label = g.Key, Value = g.Count() })
                .OrderByDescending(g => g.Value)
                .Take(6)
                .ToList();
            var maxObject = Math.Max(1, objectGroups.FirstOrDefault()?.Value ?? 1);
            foreach (var g in objectGroups)
            {
                ObjectStats.Add(new StatBarItem
                {
                    Label = g.Label,
                    Value = g.Value,
                    Ratio = (double)g.Value / maxObject
                });
            }


            TopYearStats.Clear();
            var byYear = dated
                .GroupBy(x => x.Date.Year)
                .Select(g => new { Label = g.Key.ToString(), Value = g.Count() })
                .OrderByDescending(g => g.Value)
                .ThenByDescending(g => g.Label)
                .Take(3)
                .ToList();
            var maxYear = Math.Max(1, byYear.Select(x => x.Value).DefaultIfEmpty(1).Max());
            foreach (var y in byYear)
            {
                TopYearStats.Add(new StatBarItem
                {
                    Label = y.Label,
                    Value = y.Value,
                    Ratio = (double)y.Value / maxYear
                });
            }

            MonthlyStats.Clear();
            var monthly = dated
                .GroupBy(x => new DateTime(x.Date.Year, x.Date.Month, 1))
                .OrderByDescending(g => g.Key)
                .Take(6)
                .OrderBy(g => g.Key)
                .Select(g => new { Label = g.Key.ToString("MMM yy", CultureInfo.InvariantCulture), Value = g.Count() })
                .ToList();
            var maxMonth = Math.Max(1, monthly.Select(x => x.Value).DefaultIfEmpty(1).Max());
            foreach (var m in monthly)
            {
                MonthlyStats.Add(new StatBarItem
                {
                    Label = m.Label,
                    Value = m.Value,
                    Ratio = (double)m.Value / maxMonth
                });
            }
        }

        private static DateTime? ParseDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (DateTime.TryParseExact(value, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return parsed;

            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                return parsed;

            return null;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
