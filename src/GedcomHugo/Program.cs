using System.Text;
using GedcomHugo;
using GedcomHugo.Gedcom;
using GedcomHugo.Model;

const string Usage = """
    usage: gedcom-hugo --gedcom <tree.ged> --out <dir> [options]

      --gedcom <file>       GEDCOM 5.5.1 export (UTF-8), e.g. from Family Historian
      --out <dir>           output folder, rebuilt on every run (conventionally site/_generated);
                            Hugo content is written to <dir>/content, site data to <dir>/data
      --overrides <file>    publish/withhold decisions that beat the automatic privacy rules
      --living-years <n>    treat anyone born in the last n years with no death record as
                            possibly living (default 110)
      --report              list every withheld person and why
      --summary <file>      append a Markdown summary (e.g. "$GITHUB_STEP_SUMMARY")
    """;

var opts = ParseArgs(args);
if (opts is null || opts.ContainsKey("help"))
{
    Console.Error.WriteLine(Usage);
    return 2;
}

try
{
    var gedcom = Required("gedcom");
    var outDir = Required("out");
    var livingYears = opts.TryGetValue("living-years", out var ly) && int.TryParse(ly, out var n) ? n : 110;

    var warnings = new List<string>();
    var roots = GedcomReader.Read(gedcom, warnings.Add);
    var tree = TreeBuilder.Build(roots, warnings.Add);

    var privacy = new PrivacyFilter(DateTime.UtcNow.Year, livingYears).Apply(tree);
    var overrides = Overrides.Load(opts.GetValueOrDefault("overrides"), warnings.Add);
    var (forcedPublic, forcedPrivate) = overrides.Apply(tree, privacy, warnings.Add);

    OutputDirectory.Prepare(outDir);
    var (people, sources) = new HugoWriter(tree, outDir, DateTime.UtcNow).Write();

    Console.WriteLine($"Read {tree.People.Count} people, {tree.Families.Count} families, {tree.Sources.Count} sources.");
    Console.WriteLine($"Wrote {people} person pages and {sources} source pages to {Path.GetFullPath(outDir)}.");
    Console.WriteLine($"Withheld {privacy.Withheld.Count} people ({forcedPublic} published and {forcedPrivate} withheld by override).");
    foreach (var w in warnings) Console.WriteLine($"warning: {w}");

    if (opts.ContainsKey("report"))
        foreach (var (p, reason) in privacy.Withheld.OrderBy(w => w.Person.SortName))
            Console.WriteLine($"  withheld {p.Id,-8} {p.FullName,-40} {reason}");

    if (opts.TryGetValue("summary", out var summaryPath) && !string.IsNullOrWhiteSpace(summaryPath))
    {
        // Counts only: build logs and summaries are not a place for names of the living.
        var reasons = privacy.Withheld
            .GroupBy(w => w.Reason.StartsWith("may be living") ? "may be living" : w.Reason)
            .OrderByDescending(g => g.Count());
        var md = new StringBuilder()
            .AppendLine("### gedcom-hugo")
            .AppendLine()
            .AppendLine("| | Count |").AppendLine("|---|---:|")
            .AppendLine($"| People in GEDCOM | {tree.People.Count} |")
            .AppendLine($"| Person pages | {people} |")
            .AppendLine($"| Source pages | {sources} |")
            .AppendLine($"| Withheld | {privacy.Withheld.Count} |");
        foreach (var g in reasons) md.AppendLine($"| &nbsp;&nbsp;{g.Key} | {g.Count()} |");
        md.AppendLine($"| Warnings | {warnings.Count} |").AppendLine();
        File.AppendAllText(summaryPath, md.ToString());
    }

    return 0;
}
catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

string Required(string key) =>
    opts!.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)
        ? v
        : throw new ArgumentException($"--{key} is required.\n\n{Usage}");

static Dictionary<string, string>? ParseArgs(string[] args)
{
    var flags = new HashSet<string> { "report", "help" };
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--")) return null;
        var key = args[i][2..];
        if (flags.Contains(key)) { result[key] = "true"; continue; }
        if (i + 1 >= args.Length) return null;
        result[key] = args[++i];
    }
    return result;
}
