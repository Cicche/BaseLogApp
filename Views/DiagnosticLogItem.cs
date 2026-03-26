using BaseLogApp.Core.Diagnostics;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace BaseLogApp.Views;

public sealed class DiagnosticLogItem : INotifyPropertyChanged
{
    public string TimestampText { get; init; } = string.Empty;
    public string Level { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string OperationText { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? Details { get; init; }
    public string? ExceptionText { get; init; }
    public bool HasDetails => !string.IsNullOrWhiteSpace(Details);
    public bool HasException => !string.IsNullOrWhiteSpace(ExceptionText);
    public bool ShowDetails => HasDetails && IsExpanded;
    public bool ShowException => HasException && IsExpanded;
    public string ExpandHint => IsExpanded ? "Tocca per nascondere dettagli" : "Tocca per mostrare dettagli";

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
                return;

            _isExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowDetails));
            OnPropertyChanged(nameof(ShowException));
            OnPropertyChanged(nameof(ExpandHint));
        }
    }

    public string LevelBackground { get; init; } = "#2E7D32";
    public string CategoryBackground { get; init; } = "#7A859E";
    public string BorderColor { get; init; } = "#2E7D32";

    public event PropertyChangedEventHandler? PropertyChanged;

    public static DiagnosticLogItem From(AppLogEntry entry)
    {
        var level = string.IsNullOrWhiteSpace(entry.Level) ? "INFO" : entry.Level.ToUpperInvariant();
        var category = string.IsNullOrWhiteSpace(entry.Category) ? LogCategories.RuntimeError : entry.Category.ToUpperInvariant();
        var timestamp = entry.Timestamp == DateTimeOffset.MinValue
            ? "Data non disponibile"
            : entry.Timestamp.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);

        var levelColor = GetLevelColor(level);
        return new DiagnosticLogItem
        {
            TimestampText = timestamp,
            Level = level,
            Category = category,
            OperationText = BuildOperation(entry.Source, entry.Operation),
            Message = string.IsNullOrWhiteSpace(entry.Message) ? "Messaggio non disponibile" : entry.Message,
            Details = entry.Details,
            ExceptionText = entry.ExceptionText,
            LevelBackground = levelColor,
            CategoryBackground = GetCategoryColor(category),
            BorderColor = levelColor
        };
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static string BuildOperation(string source, string operation)
    {
        var safeSource = string.IsNullOrWhiteSpace(source) ? "UnknownSource" : source;
        var safeOperation = string.IsNullOrWhiteSpace(operation) ? "UnknownOperation" : operation;
        return $"{safeSource}.{safeOperation}";
    }

    private static string GetLevelColor(string level)
        => level switch
        {
            "ERROR" => "#B3261E",
            "WARN" => "#9A6A00",
            _ => "#2E7D32"
        };

    private static string GetCategoryColor(string category)
        => category switch
        {
            LogCategories.DataConsistency => "#4E5AB5",
            LogCategories.NumberShift => "#D17B00",
            LogCategories.ImportExport => "#00796B",
            LogCategories.ReferenceIntegrity => "#6A1B9A",
            LogCategories.RuntimeError => "#8D1B1B",
            _ => "#7A859E"
        };
}
