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



        private int _baseB;
        public int BaseB { get => _baseB; private set { if (_baseB != value) { _baseB = value; OnPropertyChanged(); } } }
        private int _baseA;
        public int BaseA { get => _baseA; private set { if (_baseA != value) { _baseA = value; OnPropertyChanged(); } } }
        private int _baseS;
        public int BaseS { get => _baseS; private set { if (_baseS != value) { _baseS = value; OnPropertyChanged(); } } }
        private int _baseE;
        public int BaseE { get => _baseE; private set { if (_baseE != value) { _baseE = value; OnPropertyChanged(); } } }
        private int _baseOthers;
        public int BaseOthers { get => _baseOthers; private set { if (_baseOthers != value) { _baseOthers = value; OnPropertyChanged(); } } }

        private DbProfile _currentProfile = DbProfile.Modern;
        public DbProfile CurrentProfile
        {
            get => _currentProfile;
            private set { if (_currentProfile != value) { _currentProfile = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentProfileLabel)); } }
        }

        public string CurrentProfileLabel => CurrentProfile == DbProfile.Legacy ? "DB: Legacy" : "DB: Modern";

        private readonly IJumpsReader _reader;
        private readonly Dictionary<string, string> _objectTypeByName = new(StringComparer.OrdinalIgnoreCase);
        public ObservableCollection<JumpListItem> Items { get; } = new();
        public ObservableCollection<JumpListItem> FilteredItems { get; } = new();
        public ObservableCollection<StatBarItem> ObjectStats { get; } = new();
        public ObservableCollection<StatBarItem> TopYearStats { get; } = new();
        public ObservableCollection<StatBarItem> JumpTypeStats { get; } = new();
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
            _reader.SetDbProfile(CurrentProfile);
        }

        public async Task LoadAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                Items.Clear();
                FilteredItems.Clear();

                //await EnsureDefaultJumpTypesAsync();
                var rows = await _reader.GetJumpsAsync();
                foreach (var it in rows.OrderByDescending(x => x.NumeroSalto))
                    Items.Add(it);

                var objects = await _reader.GetObjectsCatalogAsync();
                _objectTypeByName.Clear();
                foreach (var o in objects.Where(o => !string.IsNullOrWhiteSpace(o.Name)))
                    _objectTypeByName[o.Name.Trim()] = o.ObjectType?.Trim() ?? string.Empty;

                var objectNames = await _reader.GetObjectNamesAsync();
                UniqueObjects = objectNames.Count;

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

        public async Task<IReadOnlyList<string>> GetJumpTypeNamesAsync()
            => await _reader.GetJumpTypeNamesAsync();

        public async Task<IReadOnlyList<string>> GetRigNamesAsync()
            => await _reader.GetRigNamesAsync();

        public Task<IReadOnlyList<ObjectCatalogItem>> GetObjectsCatalogAsync()
            => _reader.GetObjectsCatalogAsync();

        public Task<IReadOnlyList<CatalogItem>> GetRigsCatalogAsync()
            => _reader.GetRigsCatalogAsync();

        public Task<IReadOnlyList<CatalogItem>> GetJumpTypesCatalogAsync()
            => _reader.GetJumpTypesCatalogAsync();

        public Task<(double? Latitude, double? Longitude)> GetObjectCoordinatesAsync(string? objectName)
            => _reader.GetObjectCoordinatesAsync(objectName);


        public async Task ToggleDbProfileAsync()
        {
            CurrentProfile = CurrentProfile == DbProfile.Legacy ? DbProfile.Modern : DbProfile.Legacy;
            _reader.SetDbProfile(CurrentProfile);
            await LoadAsync();
        }

        public Task<bool> ExportLightweightJsonAsync(string filePath)
            => _reader.ExportLightweightJsonAsync(filePath);

        public Task<bool> ImportLightweightJsonAsync(string filePath)
            => _reader.ImportLightweightJsonAsync(filePath);

        public Task<bool> ExportFullDbAsync(string filePath)
            => _reader.ExportFullDbAsync(filePath);

        public Task<bool> ImportFullDbAsync(string filePath)
            => _reader.ImportFullDbAsync(filePath);

        public string GetCurrentDbPath()
            => _reader.GetCurrentDbPath();

        public bool HasJumpNumberConflict(int number, int? currentId = null)
            => Items.Any(x => x.NumeroSalto == number && (!currentId.HasValue || x.Id != currentId.Value));

        public Task<bool> SupportsJumpNumberShiftAsync()
            => _reader.SupportsJumpNumberShiftAsync();

        public async Task<bool> ShiftJumpNumbersUpFromAsync(int fromNumber, int? excludeId = null)
            => await _reader.ShiftJumpNumbersUpFromAsync(fromNumber, excludeId);

        public async Task<bool> SaveJumpAsync(JumpListItem newJump)
        {
            var saved = newJump.IsEdit
                ? await _reader.UpdateJumpAsync(newJump)
                : await _reader.AddJumpAsync(newJump);
            if (saved)
                await LoadAsync();

            return saved;
        }

        public async Task<bool> AddObjectAsync(string name, string? objectType, string? description, string? position, string? heightMeters, byte[]? photoBytes)
            => await _reader.AddObjectAsync(name, objectType, description, position, heightMeters, photoBytes);

        public async Task<bool> AddRigAsync(string name, string? description)
            => await _reader.AddRigAsync(name, description);

        public async Task<bool> AddJumpTypeAsync(string name, string? notes)
            => await _reader.AddJumpTypeAsync(name, notes);

        public Task<bool> UpdateObjectAsync(int id, string name, string? objectType, string? description, string? position, string? heightMeters, byte[]? photoBytes)
            => _reader.UpdateObjectAsync(id, name, objectType, description, position, heightMeters, photoBytes);

        public Task<bool> UpdateRigAsync(int id, string name, string? description)
            => _reader.UpdateRigAsync(id, name, description);

        public Task<bool> UpdateJumpTypeAsync(int id, string name, string? notes)
            => _reader.UpdateJumpTypeAsync(id, name, notes);

        public Task<int> NormalizeJumpNumbersAsync()
            => _reader.NormalizeJumpNumbersAsync();

        public int GetMaxAllowedJumpNumber(int? currentId = null)
        {
            var maxExisting = Items
                .Where(x => !currentId.HasValue || x.Id != currentId.Value)
                .Select(x => x.NumeroSalto)
                .DefaultIfEmpty(0)
                .Max();
            return maxExisting + 1;
        }

        public Task<(bool CanDelete, string? Reason)> CanDeleteObjectAsync(int id)
            => _reader.CanDeleteObjectAsync(id);

        public Task<(bool CanDelete, string? Reason)> CanDeleteRigAsync(int id)
            => _reader.CanDeleteRigAsync(id);

        public Task<(bool CanDelete, string? Reason)> CanDeleteJumpTypeAsync(int id)
            => _reader.CanDeleteJumpTypeAsync(id);

        public async Task<bool> DeleteObjectAsync(int id)
        {
            var ok = await _reader.DeleteObjectAsync(id);
            if (ok)
                await LoadAsync();
            return ok;
        }

        public async Task<bool> DeleteRigAsync(int id)
        {
            var ok = await _reader.DeleteRigAsync(id);
            if (ok)
                await LoadAsync();
            return ok;
        }

        public async Task<bool> DeleteJumpTypeAsync(int id)
        {
            var ok = await _reader.DeleteJumpTypeAsync(id);
            if (ok)
                await LoadAsync();
            return ok;
        }

        public void AddJump(JumpListItem newJump)
        {
            var insertIndex = Items.TakeWhile(x => x.NumeroSalto > newJump.NumeroSalto).Count();
            Items.Insert(insertIndex, newJump);

            ApplyFilter(Query);
            RecalculateStats();
        }



        public async Task<bool> DeleteJumpAsync(JumpListItem jump)
        {
            var deleted = await _reader.DeleteJumpAsync(jump);
            if (deleted)
                await LoadAsync();

            return deleted;
        }


        public void SetExpandedState(JumpListItem target, bool isExpanded)
        {
            if (!isExpanded)
            {
                target.IsExpanded = false;
                return;
            }

            foreach (var item in FilteredItems)
                item.IsExpanded = ReferenceEquals(item, target);
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
                    || (!string.IsNullOrEmpty(it.Note) && it.Note.Contains(q, StringComparison.OrdinalIgnoreCase))
                    || (it.DelaySeconds.HasValue && it.DelaySeconds.Value.ToString(CultureInfo.InvariantCulture).Contains(q, StringComparison.OrdinalIgnoreCase))
                    || (it.HeadingDegrees.HasValue && it.HeadingDegrees.Value.ToString(CultureInfo.InvariantCulture).Contains(q, StringComparison.OrdinalIgnoreCase)));
            }

            FilteredItems.Clear();
            foreach (var it in filtered.OrderByDescending(x => x.NumeroSalto))
            {
                it.IsExpanded = false;
                FilteredItems.Add(it);
            }

            Count = FilteredItems.Count;
        }

        private void RecalculateStats()
        {
            TotalJumps = Items.Count;
            NextJumpNumber = Items.Count == 0 ? 1 : Items.Max(x => x.NumeroSalto) + 1;

            BaseB = 0; BaseA = 0; BaseS = 0; BaseE = 0; BaseOthers = 0;
            foreach (var jump in Items.Where(i => !string.IsNullOrWhiteSpace(i.Oggetto)))
            {
                _objectTypeByName.TryGetValue(jump.Oggetto!.Trim(), out var t);
                var type = (t ?? string.Empty).Trim();
                if (type.StartsWith("B", StringComparison.OrdinalIgnoreCase) || type.Equals("Building", StringComparison.OrdinalIgnoreCase)) BaseB++;
                else if (type.StartsWith("A", StringComparison.OrdinalIgnoreCase) || type.Equals("Antenna", StringComparison.OrdinalIgnoreCase) || type.Equals("Antennas", StringComparison.OrdinalIgnoreCase)) BaseA++;
                else if (type.StartsWith("S", StringComparison.OrdinalIgnoreCase) || type.Equals("Span", StringComparison.OrdinalIgnoreCase)) BaseS++;
                else if (type.StartsWith("E", StringComparison.OrdinalIgnoreCase) || type.Equals("Earth", StringComparison.OrdinalIgnoreCase)) BaseE++;
                else BaseOthers++;
            }

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


            JumpTypeStats.Clear();
            var typeGroups = Items
                .Where(i => !string.IsNullOrWhiteSpace(i.TipoSalto))
                .GroupBy(i => i.TipoSalto!)
                .Select(g => new { Label = g.Key, Value = g.Count() })
                .OrderByDescending(g => g.Value)
                .Take(3)
                .ToList();
            var maxType = Math.Max(1, typeGroups.Select(x => x.Value).DefaultIfEmpty(1).Max());
            foreach (var t in typeGroups)
            {
                JumpTypeStats.Add(new StatBarItem
                {
                    Label = t.Label,
                    Value = t.Value,
                    Ratio = (double)t.Value / maxType
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

            if (DateTime.TryParseExact(value, new[] { "dd/MM/yyyy HH:mm", "dd/MM/yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
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
