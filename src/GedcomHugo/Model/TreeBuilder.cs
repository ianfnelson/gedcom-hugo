using System.Text.RegularExpressions;
using GedcomHugo.Gedcom;

namespace GedcomHugo.Model;

/// <summary>Turns the raw GEDCOM node forest into a typed <see cref="Tree"/>.</summary>
public static partial class TreeBuilder
{
    private static readonly Dictionary<string, string> EventLabels = new()
    {
        ["BIRT"] = "Birth", ["CHR"] = "Christening", ["BAPM"] = "Baptism",
        ["DEAT"] = "Death", ["BURI"] = "Burial", ["CREM"] = "Cremation",
        ["MARR"] = "Marriage", ["DIV"] = "Divorce", ["ENGA"] = "Engagement", ["MARB"] = "Banns",
        ["MARL"] = "Marriage licence", ["MARS"] = "Marriage settlement",
        ["OCCU"] = "Occupation", ["RESI"] = "Residence", ["CENS"] = "Census",
        ["EMIG"] = "Emigration", ["IMMI"] = "Immigration", ["NATU"] = "Naturalisation",
        ["EDUC"] = "Education", ["GRAD"] = "Graduation", ["RETI"] = "Retirement",
        ["PROB"] = "Probate", ["WILL"] = "Will", ["CONF"] = "Confirmation",
        ["RELI"] = "Religion", ["TITL"] = "Title", ["NATI"] = "Nationality",
        ["DSCR"] = "Description", ["SSN"] = null!, // never published
        ["EVEN"] = "Event", ["FACT"] = "Fact",
    };

    [GeneratedRegex(@"^(?<given>[^/]*)/(?<surname>[^/]*)/?(?<suffix>.*)$")]
    private static partial Regex NamePattern();

    public static Tree Build(IReadOnlyList<GedcomNode> roots, Action<string> warn)
    {
        var tree = new Tree();

        foreach (var note in roots.Where(r => r.Tag == "NOTE" && r.XRef is not null))
            tree.SharedNotes[Strip(note.XRef!)] = note.Value;

        foreach (var r in roots)
        {
            switch (r.Tag)
            {
                case "INDI" when r.XRef is not null: tree.People[Strip(r.XRef)] = BuildPerson(r, tree); break;
                case "FAM" when r.XRef is not null: tree.Families[Strip(r.XRef)] = BuildFamily(r, tree); break;
                case "SOUR" when r.XRef is not null: tree.Sources[Strip(r.XRef)] = BuildSource(r, tree); break;
            }
        }

        // Repair one-sided links, which hand-edited and merged trees accumulate.
        foreach (var f in tree.Families.Values)
        {
            foreach (var sid in f.SpouseIds)
                if (tree.Person(sid) is { } sp && !sp.SpouseInFamilies.Contains(f.Id)) sp.SpouseInFamilies.Add(f.Id);
            foreach (var cid in f.ChildIds)
                if (tree.Person(cid) is { } ch && !ch.ChildOfFamilies.Contains(f.Id)) ch.ChildOfFamilies.Add(f.Id);
        }

        var dangling = tree.Families.Values.SelectMany(f => f.SpouseIds.Concat(f.ChildIds)).Count(id => tree.Person(id) is null);
        if (dangling > 0) warn($"{dangling} family member pointer(s) refer to missing individuals");

        return tree;
    }

    private static Person BuildPerson(GedcomNode n, Tree tree)
    {
        var p = new Person { Id = Strip(n.XRef!) };

        var names = n.All("NAME").ToList();
        if (names.Count > 0)
        {
            (p.Given, p.Surname, p.Suffix) = SplitName(names[0]);
            foreach (var alt in names.Skip(1))
            {
                var (g, s, x) = SplitName(alt);
                p.AlternateNames.Add(string.Join(' ', new[] { g, s, x }.Where(t => !string.IsNullOrWhiteSpace(t))));
            }
        }

        p.Sex = n.ChildValue("SEX")?.ToUpperInvariant();

        var resn = n.ChildValue("RESN")?.ToLowerInvariant();
        p.RestrictedInFile = resn is "privacy" or "confidential";

        foreach (var c in n.Children)
        {
            switch (c.Tag)
            {
                case "FAMC": p.ChildOfFamilies.Add(Strip(c.Value)); break;
                case "FAMS": p.SpouseInFamilies.Add(Strip(c.Value)); break;
                case "NOTE": AddNote(p.Notes, c, tree); break;
                case "SOUR": p.Citations.Add(BuildCitation(c)); break;
                default:
                    if (BuildEvent(c, tree) is { } e) p.Events.Add(e);
                    break;
            }
        }

        return p;
    }

    private static (string Given, string Surname, string? Suffix) SplitName(GedcomNode name)
    {
        var givn = name.ChildValue("GIVN");
        var surn = name.ChildValue("SURN");
        var m = NamePattern().Match(name.Value);
        var given = givn ?? (m.Success ? m.Groups["given"].Value.Trim() : name.Value.Trim());
        var surname = surn ?? (m.Success ? m.Groups["surname"].Value.Trim() : "");
        var suffix = name.ChildValue("NSFX") ?? (m.Success && m.Groups["suffix"].Value.Trim().Length > 0 ? m.Groups["suffix"].Value.Trim() : null);
        return (given, surname, suffix);
    }

    private static Family BuildFamily(GedcomNode n, Tree tree)
    {
        var f = new Family { Id = Strip(n.XRef!) };
        foreach (var c in n.Children)
        {
            switch (c.Tag)
            {
                case "HUSB": f.HusbandId = Strip(c.Value); break;
                case "WIFE": f.WifeId = Strip(c.Value); break;
                case "CHIL": f.ChildIds.Add(Strip(c.Value)); break;
                case "NOTE": AddNote(f.Notes, c, tree); break;
                default:
                    if (BuildEvent(c, tree) is { } e) f.Events.Add(e);
                    break;
            }
        }
        return f;
    }

    private static Source BuildSource(GedcomNode n, Tree tree)
    {
        var s = new Source
        {
            Id = Strip(n.XRef!),
            // FH7 templated sources still carry a TITL; review titles before publishing.
            Title = n.ChildValue("TITL") ?? n.ChildValue("ABBR") ?? Strip(n.XRef!),
            Author = n.ChildValue("AUTH"),
            Publication = n.ChildValue("PUBL"),
            Text = n.ChildValue("TEXT"),
        };
        foreach (var note in n.All("NOTE")) AddNote(s.Notes, note, tree);
        return s;
    }

    private static LifeEvent? BuildEvent(GedcomNode c, Tree tree)
    {
        string? label;
        if (c.Tag is "EVEN" or "FACT" || c.Tag.StartsWith('_'))
        {
            // Custom/FH-specific facts carry their own label in TYPE.
            label = c.ChildValue("TYPE");
            if (label is null) return null;
        }
        else if (!EventLabels.TryGetValue(c.Tag, out label) || label is null) return null;

        var notes = new List<string>();
        foreach (var n in c.All("NOTE")) AddNote(notes, n, tree);

        var addr = c.Child("ADDR")?.Value;
        var place = c.ChildValue("PLAC");
        if (place is null && !string.IsNullOrWhiteSpace(addr)) place = addr.Replace('\n', ',').Trim();

        return new LifeEvent(
            c.Tag,
            label,
            GedcomDate.Parse(c.ChildValue("DATE")),
            place,
            string.IsNullOrWhiteSpace(c.Value) || c.Value.Trim() == "Y" ? null : c.Value.Trim(),
            notes.Count > 0 ? string.Join("\n\n", notes) : null,
            c.All("SOUR").Select(BuildCitation).ToList());
    }

    private static Citation BuildCitation(GedcomNode c)
    {
        var data = c.Child("DATA");
        return new Citation(
            Strip(c.Value),
            c.ChildValue("PAGE"),
            data?.ChildValue("TEXT") ?? c.ChildValue("TEXT"),
            c.ChildValue("QUAY"));
    }

    private static void AddNote(List<string> into, GedcomNode note, Tree tree)
    {
        if (note.IsPointer)
        {
            if (tree.SharedNotes.TryGetValue(Strip(note.Value), out var shared)) into.Add(shared);
        }
        else if (!string.IsNullOrWhiteSpace(note.Value)) into.Add(note.Value.Trim());
    }

    public static string Strip(string xref) => xref.Trim().Trim('@');
}
