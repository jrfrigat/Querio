# What 0.0.1 had not earned yet, and where each of those stands now

**Status:** worked through for 0.1.0. Raised 2026-08-07, when `Flare.Components.Query` became the
first real consumer of the published packages; closed out 2026-08-23.

0.0.1 was released and green: 264 tests, seven packages, four target frameworks, zero third-party
dependencies. That was not the same as validated, and the version stayed 0.x because the things
below were unproven. They are proven now, at 335 tests - and three of the six turned up a real
defect on the way, which is the argument for having written them down rather than assumed them.

None of this makes the version 1.0. It makes the case for 1.0 arguable, which it was not before.

---

## 1. The serialized shape of `QuerySpec` - **closed**

A stored query is the part of this library that outlives every release of it, and it had only ever
been written and read back by Querio's own tests, in the same process, on the same version.

Now: `tests/Querio.Tests/Corpus/` holds eight documents covering every feature - joins of each kind,
aggregates including percentiles, date truncation, relative time, group filters, paging, declared
functions of both kinds - read **from disk** and compared byte for byte in both directions. They are
regenerated only deliberately (`QUERIO_CORPUS_REGENERATE=1`), so the diff of a format change is the
compatibility break, stated in full.

The coverage requirement is derived from the model rather than from a list: the test walks the
`QuerySpec` type graph, finds every enum it can reach, and fails if any value is absent from the
corpus. Adding a join kind or an operator and forgetting to store one fails there.

**One correction to what this item originally asked for.** It wanted a forward-compatibility
decision - ignore an unknown member, or fail - implemented and written down. Querio cannot implement
either: it references no serializer at all, which is what keeps it dependency-free, so the behaviour
belongs to the consumer's `JsonSerializerOptions`. What Querio owns is the promise the shape is
designed around - unknown members are ignored, existing ones are never repurposed - and that is now
stated on `QuerySpec` itself and asserted both ways in the corpus tests.

**Trimming turned up something the item did not predict.** It expected the risk to be the trimmer
stripping constructor parameter names, which the shipped ILLink descriptor prevents. The actual
first failure was different: `PublishTrimmed` disables `System.Text.Json`'s reflection path outright,
before it looks at any type, so a trimmed consumer gets `InvalidOperationException` regardless of the
descriptor. That is a deployment constraint consumers have to know about, so it is documented in
`docs/trimming.md` and pointed at from `QuerySpec`. `tests/Querio.TrimmingProbe` now publishes
trimmed and self-contained, carries the corpus inside the binary, and reads every document back by
**both** routes a consumer can take - reflection and the source generator. CI runs it.

## 2. Running against a real database - **closed for SQL, honestly not for 1C**

Every SQL test asserted on generated text. Nothing had executed it.

Now: `tests/Querio.Sql.IntegrationTests` creates the tables, seeds known rows and asserts the
**result set** against SQL Server and PostgreSQL in containers and SQLite in a file. The cases are
the ones where the dialects diverge most - a percentile that is a window function on one engine and
an aggregate on another, a truncation each spells differently, a relative window resolved by the
server's own clock, `LIMIT` against `TOP`, and an outer join through a foreign key that finds
nothing.

SQLite needs no container and runs in the ordinary test pass, so a real database is in the default
signal. The two container engines are opt-in (`QUERIO_INTEGRATION=1`) and have their own CI job,
because pulling a SQL Server image costs more than the rest of CI put together.

1C is still covered by text assertions only. Covering it this way needs a 1C instance, which CI
cannot provide; rather than let that difference go unsaid, it is now stated in the `Querio.OneC`
README where a prospective user reads it.

## 3. Two-way targets - **closed**

`Write(Read(Write(x))) == Write(x)` is the right invariant and is not sufficient alone: a reader that
drops a member entirely still satisfies it, because the writer then never writes that member back.

Now: `TwoWayFidelityTests` compares the spec that went in against the spec that came back as a flat
set of facts - every select, condition, join, sort, bound, with its operator, aggregate and
connector. Nesting is deliberately not a fact, because folding away a group the text never carried is
legitimate. Every target declares what it may lose, and a second test asserts every declared loss is
one it really has, so a permission cannot outlive its reason.

**The result is better than the item assumed.** All three targets - `Querio.Http`, `Querio.Text`,
`Querio.Language` - lose nothing at all. The declared-loss lists are empty.

One equivalence had to be settled rather than declared: naming a selected output whose value is a
bare field, and naming that field, are the same request, and a sentence has no room to distinguish
them. Both sides are normalised before comparison, which keeps the comparison exact about everything
else instead of relaxing it.

The cross-target check the architecture notes ask for is there too: a query that has been through a
two-way target renders to the same SQL, parameters included, as the one that went in.

## 4. `QueryChoices` against what the renderers reject - **closed, and it found two defects**

Nothing checked that the narrowing and the renderers agreed.

Now `QueryChoicesAgreeWithRenderersTests` takes everything the choices offer, for every target,
applies it, renders it, and asserts nothing is refused - about 165 probes per target. It found:

- **`ValueKindsFor` offered a value list for a date field.** A designer targeting anything would have
  drawn a "one of" editor for a timestamp, while no operator offered for a date accepts a list -
  `QueryDefaults` deliberately gives dates ranges instead. Fixed: the list is offered only where some
  offered operator would consume one.
- **`Periods` offered every truncation to SQLite, which has no quarter.** The user picks "group by
  quarter" and finds out at render time, which is precisely the discovery-in-production the
  capability model exists to prevent.
- A third, found by widening the probes rather than by the first run: **SQL Server declares
  `Percentile` but refuses it in a query that groups**, because `PERCENTILE_CONT` is a window
  function there.

The last two needed the capability model to say something it could not say before. `QueryFeature` is
deliberately coarse - it is what greys out a whole control - but support is sometimes partial, either
by value or by query shape. Two small optional interfaces now carry that: `IQueryPeriodCapabilities`
and `IQueryAggregateCapabilities`. A target implements whichever applies; the coarse flags are
unchanged, so nothing that existed had to move. A second test asserts that anything a refinement
withdraws is genuinely refused, so a refinement cannot become a cheap way to pass the first test.

## 5. Performance - **closed**

There was no benchmark of any kind.

Now `tests/Querio.Benchmarks` measures rendering per target, plus validation, `QueryChoices`, and
reading and writing the query language, with the numbers recorded in `docs/benchmarks.md`.

The answer: every text target renders a realistic saved query in three to four microseconds, and
`QueryChoices` costs about one, so a designer may rebuild both on every keystroke. LINQ is three
orders of magnitude more expensive because it compiles an expression tree - which is not a defect,
but is worth knowing before putting it on an interactive path.

## 6. Public API review - **done; findings below**

The pass happened. Most of the surface held up. What it changed:

- **`QueryLanguage` had no public `Capabilities`**, alone among the targets. A consumer could not
  narrow `QueryChoices` to the query language at all - the one gap that made a documented feature
  unreachable rather than merely awkward. Added.
- **`QueryClrValue` read as an internal helper that had leaked out.** It is not: a caller registering
  a function through `QueryFunctionLibrary` builds an expression over argument types it did not
  choose, and needs exactly these conversions. It was the documentation that was wrong, describing an
  internal purpose for a public type, and that is what changed.

Two things were deliberately **not** changed, and are noted here so the decision is on the record
rather than rediscovered:

- **The render entry points are named four different ways** - `SqlRenderer.Render`,
  `QueryHttp.Render`, `QueryLanguage.Write`, `QueryDescriber.Describe`, `QueryExecutor.Execute`. Each
  target's own pair is coherent (`Render`/`Parse`, `Write`/`Read`, `Describe`/`Parse`), and
  `Execute` genuinely executes rather than rendering. Unifying them would be a visible break for the
  one real consumer in exchange for tidiness. If it is ever done, 0.x is the time.
- **`QuerioIndependenceTests` was a deny-list** guarding an allow-list mandate: a reference to a
  package nobody had thought to forbid would have passed. That one was cheap enough to fix rather
  than note, and now both tests run - the named temptations for their error message, and the general
  rule for everything else.
