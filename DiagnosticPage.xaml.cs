using BaseLogApp.Core.Diagnostics;
using BaseLogApp.Core.ViewModels;
using System.Collections.ObjectModel;

namespace BaseLogApp.Views;

public partial class DiagnosticPage : ContentPage
{
    private const string AllCategories = "Tutte";

    private readonly JumpsViewModel _vm;
    private readonly ObservableCollection<DiagnosticLogItem> _visibleEntries = new();
    private List<AppLogEntry> _allEntries = [];

    public DiagnosticPage(JumpsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        LogEntriesView.ItemsSource = _visibleEntries;
        CategoryPicker.ItemsSource = BuildDefaultCategories();
        CategoryPicker.SelectedIndex = 0;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadLogAsync();
    }

    private async void OnRefreshClicked(object sender, EventArgs e)
        => await LoadLogAsync();

    private async void OnClearClicked(object sender, EventArgs e)
    {
        var logPaths = GetLogPaths();
        var confirm = await DisplayAlert("Diagnostic", "Vuoi cancellare il contenuto del log?", "Si", "No");
        if (!confirm)
            return;

        try
        {
            foreach (var logPath in logPaths)
            {
                if (File.Exists(logPath))
                    File.WriteAllText(logPath, string.Empty);
            }

            _allEntries.Clear();
            _visibleEntries.Clear();
            RefreshCategoryPicker();
            LogPathLabel.Text = BuildLogPathLabel(logPaths);
        }
        catch (Exception ex)
        {
            var fallbackPath = logPaths.FirstOrDefault() ?? AppLog.DefaultLogPath;
            AppLog.Error(
                fallbackPath,
                LogCategories.RuntimeError,
                nameof(DiagnosticPage),
                nameof(OnClearClicked),
                "Clear log failed.",
                details: $"paths={string.Join(" | ", logPaths)}",
                ex: ex);

            await DisplayAlert("Diagnostic", $"Errore pulizia log: {ex.Message}", "OK");
        }
    }

    private async Task LoadLogAsync()
    {
        var logPaths = GetLogPaths();
        LogPathLabel.Text = BuildLogPathLabel(logPaths);

        try
        {
            _allEntries = (await AppLog.ReadEntriesAsync(logPaths)).ToList();
            RefreshCategoryPicker();
            ApplyCategoryFilter();
        }
        catch (Exception ex)
        {
            var fallbackPath = logPaths.FirstOrDefault() ?? AppLog.DefaultLogPath;
            AppLog.Error(
                fallbackPath,
                LogCategories.RuntimeError,
                nameof(DiagnosticPage),
                nameof(LoadLogAsync),
                "Read log failed.",
                details: $"paths={string.Join(" | ", logPaths)}",
                ex: ex);

            _allEntries.Clear();
            _visibleEntries.Clear();
            RefreshCategoryPicker();
            await DisplayAlert("Diagnostic", $"Errore lettura log: {ex.Message}", "OK");
        }
    }

    private IReadOnlyList<string> GetLogPaths()
        => AppLog.GetCandidateLogPaths(_vm.GetCurrentDbPath() + ".log");

    private static string BuildLogPathLabel(IReadOnlyList<string> logPaths)
    {
        if (logPaths.Count == 0)
            return "File log: non disponibile";

        var lines = logPaths
            .Select(x => $"{(File.Exists(x) ? "[OK]" : "[MISS]")} {x}");

        return "File log monitorati:" + Environment.NewLine + string.Join(Environment.NewLine, lines);
    }

    private void OnCategoryChanged(object? sender, EventArgs e)
        => ApplyCategoryFilter();

    private void OnLogItemTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not BindableObject bindable || bindable.BindingContext is not DiagnosticLogItem item)
            return;

        item.IsExpanded = !item.IsExpanded;
    }

    private List<string> BuildDefaultCategories()
        => [AllCategories, .. LogCategories.Defaults];

    private void RefreshCategoryPicker()
    {
        var selected = CategoryPicker.SelectedItem?.ToString() ?? AllCategories;

        var categories = BuildDefaultCategories();
        categories.AddRange(_allEntries
            .Select(x => x.Category)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x));

        var normalized = categories
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        CategoryPicker.ItemsSource = normalized;
        CategoryPicker.SelectedItem = normalized.FirstOrDefault(x => string.Equals(x, selected, StringComparison.OrdinalIgnoreCase)) ?? AllCategories;
    }

    private void ApplyCategoryFilter()
    {
        var selected = CategoryPicker.SelectedItem?.ToString() ?? AllCategories;

        IEnumerable<AppLogEntry> filtered = _allEntries;
        if (!string.Equals(selected, AllCategories, StringComparison.OrdinalIgnoreCase))
            filtered = filtered.Where(x => string.Equals(x.Category, selected, StringComparison.OrdinalIgnoreCase));

        _visibleEntries.Clear();
        foreach (var entry in filtered.OrderByDescending(x => x.Timestamp))
            _visibleEntries.Add(DiagnosticLogItem.From(entry));
    }

}
