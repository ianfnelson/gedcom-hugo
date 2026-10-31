# gedcom-hugo

Turns a GEDCOM family tree into a static website with [Hugo](https://gohugo.io): one page per person and per source, surname and place indexes, a pedigree chart on every person page, schema.org structured data, and a sitemap. People who may still be living are withheld automatically.

It was built for [Family Historian](https://www.family-historian.co.uk) exports but reads any GEDCOM 5.5.1 file.

```
src/GedcomHugo/     the converter (.NET 8+, no package dependencies)
theme/              the Hugo theme: layouts and CSS
starter/            a ready-to-run site using the theme; copy it to make your own
tests/fixtures/     synthetic GEDCOMs only; never commit a real tree here
```

## Quick start

You need the [.NET SDK](https://dotnet.microsoft.com/download) (8 or later) and [Hugo](https://gohugo.io/installation/) (0.128 or later; the standard edition is fine).

```sh
git clone <this repo> && cd gedcom-hugo/starter

./build.sh ../tests/fixtures/sample.ged serve        # macOS / Linux
.\build.ps1 ..\tests\fixtures\sample.ged -Serve      # Windows
```

Open http://127.0.0.1:1313 to see the sample tree. Then point the script at your own GEDCOM.

## Making the site your own

Everything you edit lives in the starter folder. Lines to change are marked ✎.

| File | What it's for |
|---|---|
| `hugo.toml` | Site title, address, author, and an optional link for your name in the footer |
| `content/_index.md` | Your homepage text |
| `content/people/_index.md` | Optional intro for the People page (`content/sources/_index.md` works the same way) |
| `content/anything-else.md` | Any extra pages: stories, research notes, about |
| `overrides.txt` | Privacy decisions that beat the automatic rules |
| `assets/css/custom.css` | Extra styles, loaded after the theme's own (create it) |
| `layouts/partials/footer.html` | Your own footer (create it; the same goes for `head-extra.html`) |

Any theme template can be overridden by putting a file at the same path under your site's `layouts/`. Prefer the small hooks above where they'll do: an overridden template stops receiving theme improvements.

To keep your site outside this repo, copy `starter/` elsewhere, set `themesDir` in `hugo.toml` to the folder containing this repo's `theme/`, and set `GEDCOM_HUGO_PROJECT` to `src/GedcomHugo` before running the build script.

**Never edit `_generated/`.** It's the converter's output, deleted and rewritten on every run, and Hugo merges it with your `content/` at build time.

## The converter

```sh
dotnet run --project src/GedcomHugo -c Release -- \
  --gedcom tree.ged --out site/_generated [--overrides overrides.txt] [--report] [--summary file]
```

- `--report` lists every withheld person and why. Read it in full before your first publish.
- `--summary` appends a Markdown table of counts, no names, to a file. Pass `"$GITHUB_STEP_SUMMARY"` in GitHub Actions.
- `--out` refuses to clear a non-empty folder that it didn't create, so a typo can't wipe your content.

## Privacy

A person is published only if they have a death, burial, cremation, probate or will record, or an estimated birth year more than 110 years ago (`--living-years` to change). Birth years are estimated from baptism, marriage (−20 years), parents (+25), children (−25) and spouses, up to three steps away, taking the *latest* candidate. Anyone with no datable relative within three steps is withheld. `RESN privacy` or `RESN confidential` always withholds.

Withheld people appear on relatives' pages as an unlinked "Private". A marriage or other family event appears in both spouses' Life events, but one with a withheld spouse isn't shown at all, and neither its place nor its sources are published. Sources are published only when they support something that is published.

`overrides.txt` takes one decision per line:

```
publish  I123   # undated, but born in the 1700s
withhold I456   # asked not to appear
```

**Always build into an empty `public/`.** Hugo doesn't delete pages it didn't render this time, so a person withheld since your last build would otherwise linger. The build scripts do this for you.

## Decisions worth knowing about

- **URLs are keyed on the record id** (`/people/i123/`). Correcting a name never breaks a link or a search-index entry. Renumbering records in your genealogy program changes every URL, so don't do it once the site is live.
- **Front matter is JSON**, which Hugo reads natively. No amount of `Mary Ann "Polly" O'Neill` can break it.
- **Dates are rendered as recorded.** `BEF 1871` becomes "before 1871" in the text and `bef.1871` in lifespans. Only exact Gregorian dates go into schema.org data. Julian and dual-dated values (`1751/52`) are shown as written and sorted by their New Style year.
- **Notes become page prose**, rendered as Markdown with raw HTML disabled.
- **"Last updated" means the research, not the build.** The homepage shows the latest `CHAN` on a published person, source or family, falling back to the GEDCOM's export date and then the build time. A family counts only if everyone in it is published, so editing a living person can't move the date, even indirectly. To show it elsewhere, such as your footer, use `{{ partial "last-updated.html" . }}`.
- **A person's "last updated" date** is the latest `CHAN` on their own record and on published families they belong to, by the same rule. It appears in their page's footer, in the sitemap and in the homepage's "Recently updated" list. People with neither have no date and aren't listed. If you override `footer.html`, add the date yourself from `.Params.updated`. Dates are capped at the build time, because Hugo silently drops a page dated in the future and `CHAN` times are local.

## Not yet supported

- Media (`OBJE` records are ignored).
- Family Historian 7+ templated sources, beyond their title. Give sources meaningful, unique titles.
- Place hierarchies: the full place string is one index term, so tidy place names in your genealogy program first.
- Search. [Pagefind](https://pagefind.app) works well: `npx pagefind --site public`.
- GEDCOM 7 and ANSEL-encoded files. Export as GEDCOM 5.5.1 in UTF-8.

## Getting indexed by Google

1. Deploy, then add the site to Google Search Console and submit `/sitemap.xml`.
2. Link to it from somewhere already indexed. A single inbound link does more than any amount of metadata.

## Licence

MIT
