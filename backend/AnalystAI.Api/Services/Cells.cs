using System.Globalization;

namespace AnalystAI.Api.Services;

/// <summary>
/// How a single cell of an uploaded file is read. The profiler and the
/// dashboard analyser both go through here, so a value one of them calls a
/// number or a gap is read the same way by the other.
/// </summary>
public static class Cells
{
    /// <summary>Placeholders exported files use for "no value".</summary>
    private static readonly HashSet<string> MissingTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "", "na", "n/a", "null", "none", "nan", "nil", "-", "--", "?", "#n/a",
    };

    private const string CurrencySymbols = "$€£₹¥₨";

    public static bool IsMissing(string? value) => value is null || MissingTokens.Contains(value.Trim());

    /// <summary>
    /// Reads a number the way people write one in a spreadsheet: "1,250",
    /// "$1,250.00", "87%" and "Rs 400" are all numbers; "2026-01-05" is not.
    /// </summary>
    public static bool TryNumber(string raw, out double value) => TryNumber(raw, out value, out _, out _);

    public static bool TryNumber(string raw, out double value, out char? currency, out bool percent)
    {
        value = 0;
        currency = null;
        percent = false;

        var text = raw.Trim();
        if (text.Length == 0) return false;

        if (text.StartsWith("Rs", StringComparison.OrdinalIgnoreCase) && text.Length > 2 && !char.IsLetter(text[2]))
        {
            currency = '₨';
            text = text[2..].TrimStart('.', ' ');
        }

        if (text.Length > 0 && CurrencySymbols.Contains(text[0]))
        {
            currency = text[0];
            text = text[1..].TrimStart();
        }
        else if (text.Length > 1 && text[0] == '-' && CurrencySymbols.Contains(text[1]))
        {
            currency = text[1];
            text = "-" + text[2..].TrimStart();
        }

        if (text.EndsWith('%'))
        {
            percent = true;
            text = text[..^1].TrimEnd();
        }

        return text.Length > 0
               && (char.IsDigit(text[0]) || text[0] is '-' or '+' or '.')
               && double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value)
               && double.IsFinite(value);
    }

    public static bool TryDate(string raw, out DateOnly date)
    {
        date = default;
        var text = raw.Trim();
        if (text.Length < 6) return false;

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var stamp))
        {
            date = DateOnly.FromDateTime(stamp);
            return true;
        }

        return false;
    }
}
