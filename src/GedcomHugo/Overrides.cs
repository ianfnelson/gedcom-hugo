using GedcomHugo.Model;

namespace GedcomHugo;

/// <summary>
/// Manual privacy decisions that beat the automatic rules. Plain text, one per line:
///
///   publish  I123   # great-great-grandfather; no surviving dates but certainly dead
///   withhold I456   # asked not to be included
///
/// Record ids are as Family Historian shows them, with or without the @ signs.
/// </summary>
public sealed class Overrides
{
    private readonly Dictionary<string, (bool Publish, int Line)> _entries = new(StringComparer.OrdinalIgnoreCase);

    public static Overrides Load(string? path, Action<string> warn)
    {
        var o = new Overrides();
        if (path is null) return o;
        if (!File.Exists(path)) throw new FileNotFoundException($"Overrides file not found: {path}");

        var lineNo = 0;
        foreach (var raw in File.ReadLines(path))
        {
            lineNo++;
            var line = raw.Split('#', 2)[0].Trim();
            if (line.Length == 0) continue;

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || parts[0].ToLowerInvariant() is not ("publish" or "withhold"))
            {
                warn($"{Path.GetFileName(path)}:{lineNo}: expected 'publish <id>' or 'withhold <id>', skipped");
                continue;
            }

            var id = parts[1].Trim('@');
            var publish = parts[0].Equals("publish", StringComparison.OrdinalIgnoreCase);
            if (o._entries.TryGetValue(id, out var existing) && existing.Publish != publish)
                warn($"{Path.GetFileName(path)}:{lineNo}: {id} is both published and withheld; withhold wins");
            if (!publish || !o._entries.ContainsKey(id)) o._entries[id] = (publish, lineNo);
        }
        return o;
    }

    /// <summary>Applies overrides after the automatic filter; returns how many changed the outcome.</summary>
    public (int Published, int Withheld) Apply(Tree tree, PrivacyReport report, Action<string> warn)
    {
        int published = 0, withheld = 0;
        foreach (var (id, (publish, _)) in _entries)
        {
            if (tree.Person(id) is not { } p)
            {
                warn($"override for {id}: no such individual in the GEDCOM");
                continue;
            }

            if (publish && p.IsPrivate)
            {
                p.IsPrivate = false;
                report.Withheld.RemoveAll(w => w.Person == p);
                published++;
            }
            else if (!publish && !p.IsPrivate)
            {
                p.IsPrivate = true;
                report.Withheld.Add((p, "withheld by override"));
                withheld++;
            }
        }
        return (published, withheld);
    }
}
