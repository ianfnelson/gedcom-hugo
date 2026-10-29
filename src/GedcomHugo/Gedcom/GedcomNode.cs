namespace GedcomHugo.Gedcom;

/// <summary>
/// One GEDCOM line plus its subordinate lines. CONC/CONT continuations are
/// folded into <see cref="Value"/> by the reader, so consumers never see them.
/// </summary>
public sealed class GedcomNode
{
    public required int Level { get; init; }
    public string? XRef { get; init; }          // e.g. "@I123@" on a level-0 record
    public required string Tag { get; init; }    // e.g. "INDI", "BIRT", "_SHAR"
    public string Value { get; set; } = "";
    public List<GedcomNode> Children { get; } = new();

    public GedcomNode? Child(string tag) => Children.FirstOrDefault(c => c.Tag == tag);

    public IEnumerable<GedcomNode> All(string tag) => Children.Where(c => c.Tag == tag);

    public string? ChildValue(string tag)
    {
        var v = Child(tag)?.Value;
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }

    /// <summary>True when the value is a pointer such as "@S12@".</summary>
    public bool IsPointer => Value.Length > 2 && Value[0] == '@' && Value[^1] == '@';

    public override string ToString() => $"{Level} {XRef} {Tag} {Value}".Replace("  ", " ");
}
