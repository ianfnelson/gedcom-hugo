using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using GedcomHugo.Model;

namespace GedcomHugo;

/// <summary>
/// Emits Hugo content: one page per public person under content/people/,
/// one per cited source under content/sources/. Section pages (_index.md) are
/// deliberately not generated, so the site owns their titles and intro text. Front matter is JSON (which
/// Hugo reads natively) so that no amount of apostrophes, colons or
/// "Mary-Ann 'Polly' O'Neill" can break the YAML.
///
/// URLs are keyed on the Family Historian record id (/people/i123/), so
/// correcting a name never breaks an inbound link or a search index entry.
///
/// Site-wide values go to data/site.json, for the theme to read as site.Data.site.
/// </summary>
public sealed class HugoWriter(Tree tree, string outDir, DateTime buildTime)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep é, ö, – readable in the files
    };

    private readonly Dictionary<string, List<Person>> _citedBy = new();

    public (int People, int Sources) Write()
    {
        var peopleDir = Path.Combine(outDir, "content", "people");
        var sourcesDir = Path.Combine(outDir, "content", "sources");

        Directory.CreateDirectory(peopleDir);
        Directory.CreateDirectory(sourcesDir);

        var people = 0;
        foreach (var p in tree.People.Values.Where(p => !p.IsPrivate))
        {
            WritePage(Path.Combine(peopleDir, $"{p.Id.ToLowerInvariant()}.md"), BuildPersonPage(p), Body(p.Notes));
            people++;
        }

        var sources = 0;
        foreach (var s in tree.Sources.Values)
        {
            if (!_citedBy.TryGetValue(s.Id, out var citers)) continue; // only sources that support something published
            var page = new SourcePage(
                Title: s.Title,
                SourceId: s.Id,
                Author: s.Author,
                Publication: s.Publication,
                CitedBy: citers.Distinct().OrderBy(c => c.SortName).Select(Ref).ToList());
            var body = string.Join("\n\n", new[] { s.Text }.Concat(s.Notes).Where(t => !string.IsNullOrWhiteSpace(t)));
            WritePage(Path.Combine(sourcesDir, $"{s.Id.ToLowerInvariant()}.md"), page, body);
            sources++;
        }

        WriteSiteData();

        return (people, sources);
    }

    // ---------------------------------------------------------------- site data

    /// <summary>
    /// "Last updated" looks at published records only, so that an edit to a living
    /// person can't move it, even indirectly. Call once the sources are known.
    /// </summary>
    private void WriteSiteData()
    {
        var published = tree.People.Values.Where(p => !p.IsPrivate).Select(p => p.LastChanged)
            .Concat(tree.Families.Values.Where(IsPublished).Select(f => f.LastChanged))
            .Concat(tree.Sources.Values.Where(s => _citedBy.ContainsKey(s.Id)).Select(s => s.LastChanged));

        var data = new SiteData(LastUpdated: published.Max() ?? tree.Exported ?? buildTime);

        var dataDir = Path.Combine(outDir, "data");
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(Path.Combine(dataDir, "site.json"), JsonSerializer.Serialize(data, Json), new UTF8Encoding(false));
    }

    /// <summary>
    /// A family has no page of its own, and its record changes when a member is added
    /// or reordered, so one with a withheld member could give away an edit to them.
    /// </summary>
    private bool IsPublished(Family f)
    {
        var members = f.SpouseIds.Concat(f.ChildIds).ToList();
        return members.Count > 0 && members.All(id => tree.Person(id) is { IsPrivate: false });
    }

    // ---------------------------------------------------------------- people

    private PersonPage BuildPersonPage(Person p)
    {
        var citations = new CitationLedger(tree);
        var events = p.Events
            .OrderBy(e => e.Date?.SortKey ?? int.MaxValue)
            .Select(e => ToEvent(e, citations))
            .ToList();
        foreach (var c in p.Citations) citations.Number(c);

        var families = p.SpouseInFamilies.Select(tree.Family).Where(f => f is not null).Select(f =>
        {
            var spouse = tree.Person(f!.OtherSpouse(p.Id));
            var marriage = f.Events.FirstOrDefault(e => e.Tag == "MARR");
            var showDetails = spouse is null || !spouse.IsPrivate;
            return new FamilyBlock(
                Spouse: spouse is null ? null : Ref(spouse),
                Marriage: showDetails && marriage is not null ? ToEvent(marriage, citations) : null,
                Children: f.ChildIds.Select(tree.Person).Where(c => c is not null)
                    .OrderBy(c => c!.EstimatedBirthYear ?? int.MaxValue).Select(c => Ref(c!)).ToList());
        }).ToList();

        var parents = tree.ParentsOf(p).ToList();
        var siblings = p.ChildOfFamilies.Select(tree.Family).Where(f => f is not null)
            .SelectMany(f => f!.ChildIds).Distinct().Where(id => id != p.Id)
            .Select(tree.Person).Where(s => s is not null)
            .OrderBy(s => s!.EstimatedBirthYear ?? int.MaxValue).Select(s => Ref(s!)).ToList();

        foreach (var c in citations.All) Register(c.SourceId, p);

        var lifespan = Lifespan(p);
        var updated = LastUpdated(p);
        var surnames = new[] { p.Surname }.Where(s => s.Length > 0).ToList();
        var places = p.Events.Select(e => e.Place).Concat(families.Select(f => f.Marriage?.Place))
            .Where(pl => !string.IsNullOrWhiteSpace(pl)).Select(pl => pl!).Distinct().ToList();

        return new PersonPage(
            Title: lifespan is null ? p.FullName : $"{p.FullName} ({lifespan})",
            Description: Describe(p, parents),
            PersonId: p.Id,
            Name: p.FullName,
            Given: p.Given,
            Surname: p.Surname,
            SortName: p.SortName,
            Sex: p.Sex,
            Lifespan: lifespan,
            BirthYear: p.Birth?.Date?.Year,
            Updated: updated,
            Lastmod: updated,
            AltNames: p.AlternateNames.Count > 0 ? p.AlternateNames : null,
            Birth: p.Birth is { } b ? ToEvent(b, citations) : null,
            Death: p.Death is { } d ? ToEvent(d, citations) : null,
            Events: events,
            Parents: parents.Select(Ref).ToList(),
            Siblings: siblings,
            Families: families,
            Pedigree: Pedigree(p, depth: 3),
            Citations: citations.All,
            Surnames: surnames,
            Places: places);
    }

    /// <summary>
    /// The latest CHAN on a person's record and their published families, capped at the
    /// build time. This becomes Hugo's lastmod and so the page's publish date, and Hugo
    /// silently drops pages published in the future. CHAN times are local, so in a time
    /// zone ahead of UTC, anyone edited shortly before a build would otherwise vanish.
    /// </summary>
    private DateTime? LastUpdated(Person p)
    {
        var latest = p.SpouseInFamilies.Concat(p.ChildOfFamilies).Select(tree.Family)
            .Where(f => f is not null && IsPublished(f)).Select(f => f!.LastChanged)
            .Append(p.LastChanged).Max();
        return latest > buildTime ? buildTime : latest;
    }

    private static string? Lifespan(Person p)
    {
        var b = p.Birth?.Date?.Short;
        var d = p.Death?.Date?.Short;
        if (b is null && d is null) return null;
        return $"{b ?? "?"}–{d ?? "?"}";
    }

    /// <summary>A one-sentence meta description; this is what Google shows under the title.</summary>
    private static string Describe(Person p, List<Person> parents)
    {
        var sb = new StringBuilder(p.FullName);
        if (p.Birth is { } b && (b.Date is not null || b.Place is not null))
        {
            sb.Append(b.Tag == "BIRT" ? ", born" : ", baptised");
            if (b.Date is not null) sb.Append(' ').Append(b.Date.Display);
            if (b.Place is not null) sb.Append(" in ").Append(b.Place);
        }
        if (p.Death is { } d && (d.Date is not null || d.Place is not null))
        {
            sb.Append(d.Tag == "DEAT" ? "; died" : "; buried");
            if (d.Date is not null) sb.Append(' ').Append(d.Date.Display);
            if (d.Place is not null) sb.Append(" in ").Append(d.Place);
        }
        var publicParents = parents.Where(x => !x.IsPrivate).Select(x => x.FullName).ToList();
        if (publicParents.Count > 0)
        {
            var rel = p.Sex switch { "M" => "Son", "F" => "Daughter", _ => "Child" };
            sb.Append(". ").Append(rel).Append(" of ").Append(string.Join(" and ", publicParents));
        }
        return sb.Append('.').ToString();
    }

    private PedigreeNode? Pedigree(Person p, int depth)
    {
        if (depth == 0) return null;
        var fam = tree.Family(p.ChildOfFamilies.FirstOrDefault());
        var father = tree.Person(fam?.HusbandId);
        var mother = tree.Person(fam?.WifeId);
        return new PedigreeNode(
            Ref(p),
            father is null ? null : (father.IsPrivate ? new PedigreeNode(Ref(father), null, null) : Pedigree(father, depth - 1)),
            mother is null ? null : (mother.IsPrivate ? new PedigreeNode(Ref(mother), null, null) : Pedigree(mother, depth - 1)));
    }

    private EventBlock ToEvent(LifeEvent e, CitationLedger ledger) => new(
        Label: e.Label,
        Date: e.Date?.Display,
        Iso: e.Date?.IsoDate,
        Place: e.Place,
        Value: e.Value,
        Note: e.Note,
        Cites: e.Citations.Count == 0 ? null : e.Citations.Select(ledger.Number).Distinct().ToList());

    private static PersonRef Ref(Person p) => p.IsPrivate
        ? new PersonRef(null, "Private", null, null, true)
        : new PersonRef(p.Id, p.FullName, Lifespan(p), $"/people/{p.Id.ToLowerInvariant()}/", null);

    private void Register(string sourceId, Person p)
    {
        if (!_citedBy.TryGetValue(sourceId, out var list)) _citedBy[sourceId] = list = new();
        list.Add(p);
    }

    // ---------------------------------------------------------------- output

    private static void WritePage(string path, object frontMatter, string body)
    {
        var sb = new StringBuilder();
        sb.Append(JsonSerializer.Serialize(frontMatter, frontMatter.GetType(), Json)).Append("\n\n");
        if (!string.IsNullOrWhiteSpace(body)) sb.Append(body.Trim()).Append('\n');
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static string Body(IEnumerable<string> notes) =>
        string.Join("\n\n", notes.Select(n => n.Replace("\r", "")));

    // ---------------------------------------------------------------- shapes

    private sealed record PersonRef(string? Id, string Name, string? Lifespan, string? Url, bool? Private);
    private sealed record EventBlock(string Label, string? Date, string? Iso, string? Place, string? Value, string? Note, List<int>? Cites);
    private sealed record FamilyBlock(PersonRef? Spouse, EventBlock? Marriage, List<PersonRef> Children);
    private sealed record PedigreeNode(PersonRef Person, PedigreeNode? Father, PedigreeNode? Mother);
    private sealed record CitationBlock(int N, string SourceId, string Source, string? Url, string? Page, string? Text, string? Quality);

    private sealed record PersonPage(
        string Title, string Description, string PersonId, string Name, string Given, string Surname, string SortName,
        string? Sex, string? Lifespan, int? BirthYear, DateTime? Updated, DateTime? Lastmod, List<string>? AltNames, EventBlock? Birth, EventBlock? Death,
        List<EventBlock> Events, List<PersonRef> Parents, List<PersonRef> Siblings, List<FamilyBlock> Families,
        PedigreeNode? Pedigree, List<CitationBlock> Citations, List<string> Surnames, List<string> Places);

    private sealed record SourcePage(string Title, string SourceId, string? Author, string? Publication, List<PersonRef> CitedBy);

    private sealed record SiteData(DateTime LastUpdated);

    /// <summary>Numbers citations per page, de-duplicating identical source+page pairs.</summary>
    private sealed class CitationLedger(Tree tree)
    {
        private readonly Dictionary<(string, string?), CitationBlock> _byKey = new();
        public List<CitationBlock> All { get; } = new();

        public int Number(Citation c)
        {
            var key = (c.SourceId, c.Page);
            if (_byKey.TryGetValue(key, out var existing)) return existing.N;

            var src = tree.Sources.GetValueOrDefault(c.SourceId);
            var block = new CitationBlock(
                N: All.Count + 1,
                SourceId: c.SourceId,
                Source: src?.Title ?? c.SourceId, // inline (non-pointer) SOUR: the value is the citation text
                Url: src is null ? null : $"/sources/{src.Id.ToLowerInvariant()}/",
                Page: c.Page,
                Text: c.Text,
                Quality: c.Quality switch { "3" => "primary evidence", "2" => "secondary evidence", "1" => "questionable", "0" => "unreliable", _ => null });
            _byKey[key] = block;
            All.Add(block);
            return block.N;
        }
    }
}
