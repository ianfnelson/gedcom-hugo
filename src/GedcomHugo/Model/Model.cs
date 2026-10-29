using GedcomHugo.Gedcom;

namespace GedcomHugo.Model;

public sealed record Citation(string SourceId, string? Page, string? Text, string? Quality);

public sealed record LifeEvent(
    string Tag,
    string Label,
    GedcomDate? Date,
    string? Place,
    string? Value,      // e.g. occupation text for OCCU, cause for DEAT
    string? Note,
    IReadOnlyList<Citation> Citations);

public sealed class Person
{
    public required string Id { get; init; }                 // "I123" (xref sans @)
    public string Given { get; set; } = "";
    public string Surname { get; set; } = "";
    public string? Suffix { get; set; }
    public List<string> AlternateNames { get; } = new();
    public string? Sex { get; set; }                         // M / F / U
    public List<LifeEvent> Events { get; } = new();
    public List<string> Notes { get; } = new();
    public List<Citation> Citations { get; } = new();        // person-level SOUR
    public List<string> ChildOfFamilies { get; } = new();     // FAMC
    public List<string> SpouseInFamilies { get; } = new();    // FAMS
    public bool RestrictedInFile { get; set; }               // RESN privacy/confidential, or FH "living" flag

    public bool IsPrivate { get; set; }                      // decided by PrivacyFilter
    public int? EstimatedBirthYear { get; set; }

    public string FullName => string.Join(' ', new[] { Given, Surname, Suffix }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public string SortName => $"{(Surname.Length > 0 ? Surname : "~")}, {Given}";

    public LifeEvent? First(params string[] tags) => Events.FirstOrDefault(e => tags.Contains(e.Tag));
    public LifeEvent? Birth => First("BIRT") ?? First("CHR", "BAPM");
    public LifeEvent? Death => First("DEAT") ?? First("BURI", "CREM");
    public bool HasDeathEvidence => Events.Any(e => e.Tag is "DEAT" or "BURI" or "CREM" or "PROB" or "WILL");
}

public sealed class Family
{
    public required string Id { get; init; }
    public string? HusbandId { get; set; }
    public string? WifeId { get; set; }
    public List<string> ChildIds { get; } = new();
    public List<LifeEvent> Events { get; } = new();
    public List<string> Notes { get; } = new();

    public IEnumerable<string> SpouseIds => new[] { HusbandId, WifeId }.Where(x => x is not null)!;
    public string? OtherSpouse(string personId) => HusbandId == personId ? WifeId : WifeId == personId ? HusbandId : null;
}

public sealed class Source
{
    public required string Id { get; init; }
    public string Title { get; set; } = "";
    public string? Author { get; set; }
    public string? Publication { get; set; }
    public string? Text { get; set; }
    public List<string> Notes { get; } = new();
}

public sealed class Tree
{
    public Dictionary<string, Person> People { get; } = new();
    public Dictionary<string, Family> Families { get; } = new();
    public Dictionary<string, Source> Sources { get; } = new();
    public Dictionary<string, string> SharedNotes { get; } = new();

    public Person? Person(string? id) => id is not null && People.TryGetValue(id, out var p) ? p : null;
    public Family? Family(string? id) => id is not null && Families.TryGetValue(id, out var f) ? f : null;

    public IEnumerable<Person> ParentsOf(Person p) =>
        p.ChildOfFamilies.Select(Family).Where(f => f is not null)
            .SelectMany(f => f!.SpouseIds).Select(Person).Where(x => x is not null)!;

    public IEnumerable<Person> ChildrenOf(Person p) =>
        p.SpouseInFamilies.Select(Family).Where(f => f is not null)
            .SelectMany(f => f!.ChildIds).Select(Person).Where(x => x is not null)!;

    public IEnumerable<Person> SpousesOf(Person p) =>
        p.SpouseInFamilies.Select(Family).Where(f => f is not null)
            .Select(f => Person(f!.OtherSpouse(p.Id))).Where(x => x is not null)!;
}
