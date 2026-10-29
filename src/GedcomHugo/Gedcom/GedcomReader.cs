using System.Text;
using System.Text.RegularExpressions;

namespace GedcomHugo.Gedcom;

/// <summary>
/// Reads a GEDCOM 5.5.1 file into a forest of level-0 records.
/// Tolerant by design: Family Historian output is well-formed, but trees that
/// have passed through other programs often are not, and a genealogy site
/// should degrade gracefully rather than refuse to build.
/// </summary>
public static partial class GedcomReader
{
    // level [@xref@] tag [value]
    [GeneratedRegex(@"^\s*(?<lvl>\d+)\s+(?:(?<xref>@[^@]+@)\s+)?(?<tag>[A-Za-z0-9_]+)(?:\s(?<val>.*))?$")]
    private static partial Regex LinePattern();

    public static IReadOnlyList<GedcomNode> Read(string path, Action<string>? warn = null)
    {
        warn ??= _ => { };
        var lines = ReadLines(path, warn);
        var roots = new List<GedcomNode>();
        var stack = new Stack<GedcomNode>();
        var lineNo = 0;

        foreach (var raw in lines)
        {
            lineNo++;
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var m = LinePattern().Match(raw);
            if (!m.Success)
            {
                // Some exporters wrap long notes without CONC; treat as continuation.
                if (stack.Count > 0) stack.Peek().Value += " " + raw.Trim();
                else warn($"line {lineNo}: unparseable, skipped");
                continue;
            }

            var level = int.Parse(m.Groups["lvl"].Value);
            var tag = m.Groups["tag"].Value.ToUpperInvariant();
            var value = m.Groups["val"].Success ? m.Groups["val"].Value : "";

            while (stack.Count > 0 && stack.Peek().Level >= level) stack.Pop();

            if (tag is "CONC" or "CONT" && stack.Count > 0)
            {
                var parent = stack.Peek();
                parent.Value += tag == "CONT" ? "\n" + value : value;
                continue;
            }

            var node = new GedcomNode
            {
                Level = level,
                XRef = m.Groups["xref"].Success ? m.Groups["xref"].Value : null,
                Tag = tag,
                Value = value,
            };

            if (level == 0) roots.Add(node);
            else if (stack.Count > 0) stack.Peek().Children.Add(node);
            else warn($"line {lineNo}: orphan level-{level} line, skipped");

            stack.Push(node);
        }

        return roots;
    }

    private static IEnumerable<string> ReadLines(string path, Action<string> warn)
    {
        var bytes = File.ReadAllBytes(path);

        // Family Historian writes UTF-8 (with BOM) by default; honour BOMs,
        // then fall back on the header's CHAR declaration.
        Encoding enc;
        int skip = 0;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) { enc = Encoding.UTF8; skip = 3; }
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) { enc = Encoding.Unicode; skip = 2; }
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) { enc = Encoding.BigEndianUnicode; skip = 2; }
        else
        {
            var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 4096));
            var charMatch = Regex.Match(head, @"^1 CHAR (\S+)", RegexOptions.Multiline);
            var declared = charMatch.Success ? charMatch.Groups[1].Value.ToUpperInvariant() : "UTF-8";
            enc = declared switch
            {
                "UTF-8" or "UTF8" => new UTF8Encoding(false),
                "ANSI" => Encoding.Latin1, // close enough for Windows-1252 names; ANSI-only chars are rare in names
                "ANSEL" => Encoding.Latin1,
                _ => new UTF8Encoding(false),
            };
            if (declared == "ANSEL")
                warn("file declares ANSEL; diacritics may be wrong. Re-export from Family Historian as UTF-8.");
        }

        var text = enc.GetString(bytes, skip, bytes.Length - skip);
        return text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
    }
}
