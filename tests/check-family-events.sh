#!/usr/bin/env bash
# Checks family events in the built sample site. Run from the repo root, after
# ./starter/build.sh has built tests/fixtures/sample.ged.
set -euo pipefail

people=starter/public/people

fail() {
  echo "::error::$1"
  exit 1
}

# The labels of a person's Life events, in page order.
rows() {
  grep -o '<strong>[^<]*</strong>' "$people/$1/index.html" | sed 's/<[^>]*>//g' | paste -sd '|' -
}

expect_rows() {
  local actual
  actual="$(rows "$1")"
  [[ "$actual" == "$2" ]] || fail "$1 has events '$actual', expected '$2'"
}

# In date order among the person's own events, on both spouses' pages. I3's second
# MARR must appear, and the undated licence sorts last, as undated events do.
expect_rows i1 'Birth|Banns with|Marriage to|Occupation|Death'
expect_rows i2 'Christening|Banns with|Marriage to|Death'
expect_rows i3 'Birth|Marriage to|Marriage to|Death|Marriage licence with'
expect_rows i6 'Birth|Engagement to|Marriage to|Marriage settlement with|Divorce from|Death'

# I8's second family has a withheld spouse, so none of its events may appear.
expect_rows i8 'Birth|Death'
[[ ! -e starter/public/sources/s5 ]] || fail "A source cited only by a withheld family was published"
grep -q 'With <span class=private>Private</span>' "$people/i8/index.html" \
  || fail "I8's withheld spouse should still show as Private under Family"

# Names the spouse, with the place and citation.
grep -q 'Marriage to</strong> <a href=/people/i2/>Hannah Metcalfe</a>' "$people/i1/index.html" \
  || fail "I1's marriage should name and link their spouse"
grep -qE 'Aysgarth, Yorkshire, England</span><sup class=cites><a [^>]*>[0-9]+</a></sup>' "$people/i1/index.html" \
  || fail "I1's marriage should show its place and citation"

# Each marriage is shown once.
if grep -rq 'Married ' "$people"; then
  fail "The Married line should have gone from the Family section"
fi
