using GedcomHugo.Model;

namespace GedcomHugo;

/// <summary>
/// Decides who is published. Errs firmly towards privacy: anyone who might be
/// alive is withheld, along with anyone whose life we cannot place in time at all.
/// </summary>
public sealed class PrivacyFilter(int currentYear, int maxLifespan = 110, int generationGap = 25, int marriageAge = 20)
{
    public PrivacyReport Apply(Tree tree)
    {
        EstimateBirthYears(tree);

        var report = new PrivacyReport();
        foreach (var p in tree.People.Values)
        {
            string? reason = null;
            if (p.RestrictedInFile) reason = "marked private in Family Historian (RESN)";
            else if (p.HasDeathEvidence) reason = null;
            else if (p.EstimatedBirthYear is null) reason = "no dates anywhere in their branch";
            else if (p.EstimatedBirthYear > currentYear - maxLifespan) reason = $"may be living (born c.{p.EstimatedBirthYear})";

            p.IsPrivate = reason is not null;
            if (reason is not null) report.Withheld.Add((p, reason));
        }
        return report;
    }

    /// <summary>
    /// Own birth/baptism year if known; otherwise iterate, inferring from
    /// parents (+gap), children (−gap), spouses (±0) and marriage (−marriageAge)
    /// until nothing changes. Inference chains are capped so a single dated
    /// ancestor cannot "date" a whole undated branch generations away.
    /// </summary>
    private void EstimateBirthYears(Tree tree)
    {
        var hops = new Dictionary<string, int>();
        foreach (var p in tree.People.Values)
        {
            var y = p.First("BIRT")?.Date?.Year ?? p.First("CHR", "BAPM")?.Date?.Year;
            if (y is null)
            {
                var marr = p.SpouseInFamilies.Select(tree.Family).Where(f => f is not null)
                    .Select(f => f!.Events.FirstOrDefault(e => e.Tag == "MARR")?.Date?.Year).FirstOrDefault(v => v is not null);
                if (marr is not null) y = marr - marriageAge;
            }
            p.EstimatedBirthYear = y;
            if (y is not null) hops[p.Id] = 0;
        }

        const int maxHops = 3;
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var p in tree.People.Values.Where(p => p.EstimatedBirthYear is null))
            {
                var candidates = new List<(int Year, int Hops)>();
                foreach (var par in tree.ParentsOf(p))
                    if (par.EstimatedBirthYear is { } y) candidates.Add((y + generationGap, hops[par.Id] + 1));
                foreach (var ch in tree.ChildrenOf(p))
                    if (ch.EstimatedBirthYear is { } y) candidates.Add((y - generationGap, hops[ch.Id] + 1));
                foreach (var sp in tree.SpousesOf(p))
                    if (sp.EstimatedBirthYear is { } y) candidates.Add((y, hops[sp.Id] + 1));

                var usable = candidates.Where(c => c.Hops <= maxHops).ToList();
                if (usable.Count == 0) continue;

                // Latest plausible year: the conservative choice for privacy.
                var best = usable.MaxBy(c => c.Year);
                p.EstimatedBirthYear = best.Year;
                hops[p.Id] = best.Hops;
                changed = true;
            }
        }
    }
}

public sealed class PrivacyReport
{
    public List<(Person Person, string Reason)> Withheld { get; } = new();
}
