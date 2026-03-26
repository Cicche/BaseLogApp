using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BaseLogApp.Core.Diagnostics;

public static class LogCategories
{
    public const string DataConsistency = "DATA_CONSISTENCY";
    public const string NumberShift = "NUMBER_SHIFT";
    public const string ImportExport = "IMPORT_EXPORT";
    public const string ReferenceIntegrity = "REFERENCE_INTEGRITY";
    public const string RuntimeError = "RUNTIME_ERROR";

    public static readonly IReadOnlyList<string> Defaults =
    [
        DataConsistency,
        NumberShift,
        ImportExport,
        ReferenceIntegrity,
        RuntimeError
    ];
}

public sealed class AppLogEntry
{
    public DateTimeOffset Timestamp { get; init; }
    public string Level { get; init; } = "INFO";
    public string Category { get; init; } = LogCategories.RuntimeError;
    public string Source { get; init; } = string.Empty;
    public string Operation { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? Details { get; init; }
    public string? ExceptionText { get; init; }
}

public static class AppLog
{
    private static readonly object SyncRoot = new();
    private const string Separator = "----------------------------------------------------------------";
    private const string DefaultLogFileName = "BASELogbook.sqlite.log";
    private static readonly Regex HeaderRegex = new(
        @"^\[(?<ts>.+?)\]\s+\[LVL:(?<lvl>[A-Z]+)\]\s+\[CAT:(?<cat>[A-Z_]+)\]\s+\[SRC:(?<src>[^\]]*)\]\s+\[OP:(?<op>[^\]]*)\]$",
        RegexOptions.Compiled);

    public static string DefaultLogPath
        => Path.Combine(FileSystem.AppDataDirectory, DefaultLogFileName);

    public static void Info(string logPath, string category, string source, string operation, string message, string? details = null)
        => Write(logPath, "INFO", category, source, operation, message, details, null);

    public static void Warn(string logPath, string category, string source, string operation, string message, string? details = null)
        => Write(logPath, "WARN", category, source, operation, message, details, null);

    public static void Error(string logPath, string category, string source, string operation, string message, string? details = null, Exception? ex = null)
        => Write(logPath, "ERROR", category, source, operation, message, details, ex);

    public static IReadOnlyList<string> GetCandidateLogPaths(string? preferredLogPath = null)
    {
        var result = new List<string>(3);
        AddCandidatePath(result, preferredLogPath);

        if (!string.IsNullOrWhiteSpace(preferredLogPath))
        {
            try
            {
                var fileName = Path.GetFileName(preferredLogPath.Trim());
                if (!string.IsNullOrWhiteSpace(fileName))
                    AddCandidatePath(result, Path.Combine(FileSystem.AppDataDirectory, fileName));
            }
            catch
            {
                // Ignore malformed paths from caller.
            }
        }

        AddCandidatePath(result, DefaultLogPath);
        return result;
    }

    public static Task<IReadOnlyList<AppLogEntry>> ReadEntriesAsync(string? logPath)
        => ReadEntriesAsync(GetCandidateLogPaths(logPath));

    public static async Task<IReadOnlyList<AppLogEntry>> ReadEntriesAsync(IEnumerable<string> logPaths)
    {
        if (logPaths is null)
            return Array.Empty<AppLogEntry>();

        var allEntries = new List<AppLogEntry>();
        foreach (var logPath in logPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(logPath) || !File.Exists(logPath))
                continue;

            try
            {
                var text = await File.ReadAllTextAsync(logPath);
                allEntries.AddRange(ParseEntries(text));
            }
            catch
            {
                // Ignore single-file read failures and keep loading the rest.
            }
        }

        if (allEntries.Count == 0)
            return Array.Empty<AppLogEntry>();

        var deduped = new List<AppLogEntry>(allEntries.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in allEntries.OrderByDescending(x => x.Timestamp))
        {
            if (seen.Add(BuildDedupKey(entry)))
                deduped.Add(entry);
        }

        return deduped;
    }

    private static void Write(string? logPath, string level, string category, string source, string operation, string message, string? details, Exception? ex)
    {
        try
        {
            var now = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
            var safeCategory = NormalizeCategory(category);
            var safeSource = string.IsNullOrWhiteSpace(source) ? "UnknownSource" : source.Trim();
            var safeOperation = string.IsNullOrWhiteSpace(operation) ? "UnknownOperation" : operation.Trim();
            var safeMessage = string.IsNullOrWhiteSpace(message) ? "No message" : message.Trim();

            var sb = new StringBuilder(512);
            sb.Append('[').Append(now).Append("] ")
              .Append("[LVL:").Append(level.ToUpperInvariant()).Append("] ")
              .Append("[CAT:").Append(safeCategory).Append("] ")
              .Append("[SRC:").Append(safeSource).Append("] ")
              .Append("[OP:").Append(safeOperation).Append(']').AppendLine();

            sb.Append("Message: ").AppendLine(safeMessage);

            if (!string.IsNullOrWhiteSpace(details))
                sb.Append("Details: ").AppendLine(details.Trim());

            if (ex is not null)
            {
                sb.Append("Exception: ")
                  .Append(ex.GetType().Name)
                  .Append(": ")
                  .AppendLine(ex.Message);

                if (!string.IsNullOrWhiteSpace(ex.StackTrace))
                    sb.Append("Stack: ").AppendLine(ex.StackTrace);
            }

            sb.AppendLine(Separator);
            var payload = sb.ToString();

            foreach (var candidatePath in GetCandidateLogPaths(logPath))
            {
                try
                {
                    var folder = Path.GetDirectoryName(candidatePath);
                    if (!string.IsNullOrWhiteSpace(folder))
                        Directory.CreateDirectory(folder);

                    lock (SyncRoot)
                    {
                        File.AppendAllText(candidatePath, payload);
                    }
                }
                catch
                {
                    // Continue with remaining targets.
                }
            }
        }
        catch
        {
            // Logging failures must never break app flow.
        }
    }

    private static IReadOnlyList<AppLogEntry> ParseEntries(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<AppLogEntry>();

        var blocks = text
            .Split(Separator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        var result = new List<AppLogEntry>(blocks.Length);
        foreach (var block in blocks)
        {
            var parsed = ParseSingleBlock(block);
            if (parsed is not null)
                result.Add(parsed);
        }

        return result
            .OrderByDescending(x => x.Timestamp)
            .ToList();
    }

    private static AppLogEntry? ParseSingleBlock(string block)
    {
        if (string.IsNullOrWhiteSpace(block))
            return null;

        var lines = block
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToList();

        if (lines.Count == 0)
            return null;

        var first = lines[0];
        var match = HeaderRegex.Match(first);
        if (!match.Success)
        {
            return new AppLogEntry
            {
                Timestamp = DateTimeOffset.MinValue,
                Level = "INFO",
                Category = LogCategories.RuntimeError,
                Source = "LegacyLog",
                Operation = "Unknown",
                Message = string.Join(Environment.NewLine, lines)
            };
        }

        var timestamp = ParseTimestamp(match.Groups["ts"].Value);
        var level = match.Groups["lvl"].Value;
        var category = NormalizeCategory(match.Groups["cat"].Value);
        var source = match.Groups["src"].Value;
        var operation = match.Groups["op"].Value;

        var message = string.Empty;
        var details = string.Empty;
        var exceptionText = string.Empty;

        foreach (var line in lines.Skip(1))
        {
            if (line.StartsWith("Message:", StringComparison.OrdinalIgnoreCase))
            {
                message = line["Message:".Length..].Trim();
                continue;
            }

            if (line.StartsWith("Details:", StringComparison.OrdinalIgnoreCase))
            {
                details = line["Details:".Length..].Trim();
                continue;
            }

            if (line.StartsWith("Exception:", StringComparison.OrdinalIgnoreCase))
            {
                exceptionText = line["Exception:".Length..].Trim();
                continue;
            }

            if (line.StartsWith("Stack:", StringComparison.OrdinalIgnoreCase))
            {
                var stackPart = line["Stack:".Length..].Trim();
                exceptionText = string.IsNullOrWhiteSpace(exceptionText)
                    ? stackPart
                    : $"{exceptionText}{Environment.NewLine}{stackPart}";
                continue;
            }

            if (string.IsNullOrWhiteSpace(message))
                message = line;
            else
                details = string.IsNullOrWhiteSpace(details) ? line : $"{details}{Environment.NewLine}{line}";
        }

        if (string.IsNullOrWhiteSpace(message))
            message = "No message";

        return new AppLogEntry
        {
            Timestamp = timestamp,
            Level = level,
            Category = category,
            Source = source,
            Operation = operation,
            Message = message,
            Details = string.IsNullOrWhiteSpace(details) ? null : details,
            ExceptionText = string.IsNullOrWhiteSpace(exceptionText) ? null : exceptionText
        };
    }

    private static DateTimeOffset ParseTimestamp(string text)
    {
        if (DateTimeOffset.TryParseExact(text, "yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture, DateTimeStyles.None, out var ts))
            return ts;

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out ts))
            return ts;

        return DateTimeOffset.MinValue;
    }

    private static string NormalizeCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return LogCategories.RuntimeError;

        return category.Trim().ToUpperInvariant();
    }

    private static void AddCandidatePath(List<string> result, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            var fullPath = Path.GetFullPath(path.Trim());
            if (!result.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                result.Add(fullPath);
        }
        catch
        {
            // Ignore invalid path values.
        }
    }

    private static string BuildDedupKey(AppLogEntry entry)
        => string.Join(
            "|",
            entry.Timestamp.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            entry.Level,
            entry.Category,
            entry.Source,
            entry.Operation,
            entry.Message,
            entry.Details ?? string.Empty,
            entry.ExceptionText ?? string.Empty);
}
