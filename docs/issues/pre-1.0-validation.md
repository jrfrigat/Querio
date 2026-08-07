# What 0.0.1 has not earned yet, and what has to happen before 1.0

**Status:** open. Raised 2026-08-07, when `Flare.Components.Query` became the first real consumer of the
published packages.

0.0.1 is released and green: 264 tests, seven packages, four target frameworks, zero third-party
dependencies. That is not the same as validated. The version stays 0.x precisely because the things below
are unproven, and each one is cheap to change now and expensive to change once somebody's saved queries
depend on it.

## 1. The serialized shape of `QuerySpec` is a contract nobody has stress-tested

A stored query is the part of this library that outlives every release of it. Today the shape has only
ever been written and read back by Querio's own tests and by the Flare designer, in the same process, on
the same version.

Required before 1.0:

- **A round-trip corpus checked into the repo.** A set of `QuerySpec` JSON documents covering every
  feature - joins of each kind, aggregates including percentiles, date truncation, relative time, group
  filters, paging, declared functions both value and table - deserialized and re-serialized by the tests,
  with the JSON compared byte for byte. Today's round-trip tests build the spec in code first, so they
  cannot catch a change that breaks documents written by an older version.
- **A forward-compatibility decision, written down.** What happens when a document carries a member this
  build does not know: ignore, or fail? Both are defensible; silently doing one of them is not.
- **Trimming coverage in the corpus run.** The ILLink descriptor exists because a trimmed publish
  otherwise strips the positional-record constructor parameter names the serializer matches on - a
  failure that only appears in a Release publish. The two guard tests assert the descriptor is present;
  nothing yet deserializes the corpus **from a trimmed publish**, which is the case that actually broke.

## 2. No target has run against a real database

Every SQL test asserts on generated text. Nothing has executed the SQL. Text that reads correctly can
still be rejected by a server, or be accepted and mean something else.

Required before 1.0: an integration suite that executes the generated SQL against real SQL Server,
PostgreSQL and SQLite instances (containers in CI), asserting the **result set**, not the string. Start
with the cases where the dialects diverge most - percentiles, date truncation, relative time, `LIMIT` vs
`TOP`, LEFT JOIN through an empty foreign key - because those are where a text-only test is weakest.

The 1C target cannot be covered this way without a 1C instance. Say so in the docs rather than implying
parity of confidence between targets.

## 3. Two-way targets are proven text-stable, not semantically stable

`Querio.Http`, `Querio.Text` and `Querio.Language` are checked with `Write(Read(Write(x))) == Write(x)`.
That is the right invariant and it is not sufficient on its own: a reader that drops a member entirely
still satisfies it, because the writer then never writes that member back.

Required before 1.0: for each two-way target, assert that reading produces a spec **equal in the fields
that matter** to the one written - or document precisely which members each target is allowed to lose,
and test that list.

## 4. `QueryChoices` is not tested against what the renderers actually reject

`QueryChoices` narrows what may be offered next through `IQueryCapabilities`, so a designer never
suggests something that will throw at render time. Nothing checks that the narrowing and the renderers
agree.

Required before 1.0: a test that, for every target, takes everything `QueryChoices` offers, applies it,
renders it, and asserts no `QueryRenderException`. A feature a renderer rejects but `QueryChoices` still
offers is a broken designer, and today only a human would notice.

## 5. Performance is unmeasured

There is no benchmark of any kind. Rendering is on the interactive path of any designer built on this -
the Flare editor re-reads the text on every keystroke - so parse and render cost matter, and "it feels
fine" is not a number.

Required before 1.0: a small benchmark over a representative schema and query - parse, validate, render
per target - with the numbers recorded so a regression is visible.

## 6. Public API review

Nothing here has been through a deliberate "is this the API we want to keep" pass. While on 0.x that is
free; at 1.0 it stops being. Do the pass before the version implies stability.
