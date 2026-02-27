using BaseLogApp.Core.ViewModels;

namespace BaseLogApp.Views;

public partial class DiagnosticPage : ContentPage
{
    private readonly JumpsViewModel _vm;
    private string _fullLogText = string.Empty;

    public DiagnosticPage(JumpsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        CategoryPicker.ItemsSource = new List<string>
        {
            "Tutte",
            "DATA_CONSISTENCY",
            "NUMBER_SHIFT",
            "IMPORT_EXPORT",
            "REFERENCE_INTEGRITY",
            "RUNTIME_ERROR"
        };
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
        var logPath = GetLogPath();
        var confirm = await DisplayAlert("Diagnostic", "Vuoi cancellare il contenuto del log?", "Sì", "No");
        if (!confirm)
            return;

        try
        {
            if (File.Exists(logPath))
                File.WriteAllText(logPath, string.Empty);
            await LoadLogAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Diagnostic", $"Errore pulizia log: {ex.Message}", "OK");
        }
    }

    private async Task LoadLogAsync()
    {
        var logPath = GetLogPath();
        LogPathLabel.Text = $"File log: {logPath}";

        try
        {
            if (!File.Exists(logPath))
            {
                _fullLogText = string.Empty;
                LogEditor.Text = "Nessun log disponibile per il DB corrente.";
                return;
            }

            _fullLogText = await File.ReadAllTextAsync(logPath);
            ApplyCategoryFilter();
        }
        catch (Exception ex)
        {
            _fullLogText = string.Empty;
            LogEditor.Text = $"Errore lettura log: {ex.Message}";
        }
    }

    private string GetLogPath()
        => _vm.GetCurrentDbPath() + ".log";

    private void OnCategoryChanged(object? sender, EventArgs e)
        => ApplyCategoryFilter();

    private void ApplyCategoryFilter()
    {
        if (string.IsNullOrWhiteSpace(_fullLogText))
        {
            LogEditor.Text = "Log vuoto.";
            return;
        }

        var selected = CategoryPicker.SelectedItem?.ToString() ?? "Tutte";
        if (selected == "Tutte")
        {
            LogEditor.Text = _fullLogText;
            return;
        }

        var lines = _fullLogText.Split(Environment.NewLine);
        var blocks = new List<string>();
        var current = new List<string>();

        foreach (var line in lines)
        {
            if (line.StartsWith("[") && current.Count > 0)
            {
                blocks.Add(string.Join(Environment.NewLine, current));
                current.Clear();
            }

            if (!string.IsNullOrWhiteSpace(line) || current.Count > 0)
                current.Add(line);
        }

        if (current.Count > 0)
            blocks.Add(string.Join(Environment.NewLine, current));

        var filtered = blocks.Where(b => b.Contains($"[CAT:{selected}]", StringComparison.OrdinalIgnoreCase)).ToList();
        LogEditor.Text = filtered.Count == 0
            ? $"Nessun evento per categoria '{selected}'."
            : string.Join(Environment.NewLine + Environment.NewLine, filtered);
    }
}
