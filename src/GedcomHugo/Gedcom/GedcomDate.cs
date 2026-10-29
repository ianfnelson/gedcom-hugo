using System.Globalization;
using System.Text.RegularExpressions;

namespace GedcomHugo.Gedcom;

/// <summary>
/// A GEDCOM date value: exact, approximate, ranged or a free-text phrase.
/// Keeps the raw value, a human rendering, and a year for sorting and
/// living-person estimation. Does not attempt calendar conversion; Julian
/// dates and dual dating (1751/52) are rendered as written, sorted by the
/// first year.
/// </summary>
public sealed partial record GedcomDate(string Raw, string Display, int? Year, int? SortKey, string? IsoDate, string? Short)
{
    private static readonly string[] MonthCodes =
        ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];

    private static readonly Dictionary<string, string> Qualifiers = new()
    {
        ["ABT"] = "about", ["CAL"] = "calculated", ["EST"] = "estimated",
        ["BEF"] = "before", ["AFT"] = "after",
        ["FROM"] = "from", ["TO"] = "to", ["BET"] = "between", ["AND"] = "and",
        ["INT"] = "",
    };

    [GeneratedRegex(@"^(?:(?<d>\d{1,2})\s+)?(?:(?<m>[A-Z]{3})\s+)?(?<y>\d{1,4})(?:/(?<dual>\d{1,2}))?(?<bc>\s*B\.?C\.?)?$")]
    private static partial Regex SimpleDate();

    [GeneratedRegex(@"@#D[A-Z ]+@\s*")]
    private static partial Regex CalendarEscape();

    public static GedcomDate? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = raw.Trim();

        // Pure phrase, e.g. "(in his youth)"
        if (text.StartsWith('(') && text.EndsWith(')'))
            return new GedcomDate(raw, text[1..^1], null, null, null, null);

        // Julian/French/Hebrew dates: render as written, but never claim an exact
        // Gregorian date in structured data.
        var nonGregorian = CalendarEscape().IsMatch(text.ToUpperInvariant()) && !text.ToUpperInvariant().Contains("GREGORIAN");
        var cleaned = CalendarEscape().Replace(text.ToUpperInvariant(), "");
        // INT 1850 (phrase): keep the date part only
        var parenIdx = cleaned.IndexOf('(');
        if (parenIdx > 0) cleaned = cleaned[..parenIdx].Trim();

        var tokens = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var parts = new List<string>();
        int? firstYear = null, firstSort = null;
        string? iso = null;
        var simpleCount = 0;

        // Walk the tokens, splitting at qualifiers; each run between qualifiers is a simple date.
        var buffer = new List<string>();
        void Flush()
        {
            if (buffer.Count == 0) return;
            var s = string.Join(' ', buffer);
            buffer.Clear();
            var m = SimpleDate().Match(s);
            if (!m.Success) { parts.Add(s.ToLowerInvariant()); return; }

            simpleCount++;
            var year = int.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture);
            var writtenYear = year;
            if (m.Groups["dual"].Success)
            {
                // Dual dating (Old Style/New Style, pre-1752 England): "1751/52" is 1752 by modern reckoning.
                var dual = m.Groups["dual"].Value;
                var pow = (int)Math.Pow(10, dual.Length);
                var ns = year / pow * pow + int.Parse(dual, CultureInfo.InvariantCulture);
                year = ns > year ? ns : ns + pow;
            }
            if (m.Groups["bc"].Success && m.Groups["bc"].Value.Trim().Length > 0) year = -year;
            int month = m.Groups["m"].Success ? Array.IndexOf(MonthCodes, m.Groups["m"].Value) + 1 : 0;
            int day = m.Groups["d"].Success ? int.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture) : 0;

            var display = new List<string>();
            if (day > 0) display.Add(day.ToString(CultureInfo.InvariantCulture));
            if (month > 0) display.Add(CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month));
            else if (m.Groups["m"].Success) display.Add(m.Groups["m"].Value); // non-Gregorian month codes
            var yearText = Math.Abs(writtenYear).ToString(CultureInfo.InvariantCulture);
            if (m.Groups["dual"].Success) yearText += "/" + m.Groups["dual"].Value;
            if (year < 0) yearText += " BC";
            display.Add(yearText);
            parts.Add(string.Join(' ', display));

            if (firstYear is null)
            {
                firstYear = year;
                firstSort = year * 10000 + month * 100 + day;
                if (year > 0 && month > 0 && day > 0 && day <= DateTime.DaysInMonth(Math.Max(year, 1), month))
                    iso = $"{year:D4}-{month:D2}-{day:D2}";
                else if (year > 0 && month > 0)
                    iso = $"{year:D4}-{month:D2}";
                else if (year > 0)
                    iso = $"{year:D4}";
            }
        }

        var qualified = false;
        string? firstQualifier = null;
        foreach (var t in tokens)
        {
            if (Qualifiers.TryGetValue(t, out var word))
            {
                Flush();
                if (simpleCount == 0) firstQualifier ??= t;
                if (word.Length > 0) parts.Add(word);
                qualified = true;
            }
            else buffer.Add(t);
        }
        Flush();

        if (simpleCount == 0)
            return new GedcomDate(raw, text, null, null, null, null);

        // schema.org dates should only claim what we know exactly
        if (qualified || nonGregorian || tokens.Any(t => t.Contains('/'))) iso = null;

        // Compact year for lifespans: "1798", "c.1822", "bef.1871", "aft.1901"
        var y = firstYear!.Value;
        var yt = y < 0 ? $"{-y} BC" : y.ToString(CultureInfo.InvariantCulture);
        var shortForm = firstQualifier switch
        {
            null or "INT" => yt,
            "BEF" or "TO" => "bef." + yt,
            "AFT" => "aft." + yt,
            "FROM" => yt,
            _ => "c." + yt, // ABT, CAL, EST, BET
        };

        return new GedcomDate(raw, string.Join(' ', parts).Trim(), firstYear, firstSort, iso, shortForm);
    }
}
