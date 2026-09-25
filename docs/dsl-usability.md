# Moirai DSL — usability & expressiveness suggestions

Notes on improving the `.sg` language's expressiveness and clarity, captured from a review of
`MoiraiCli/w.sg` and the grammar (`Moirai.Parser/moirai_lexer.g4`, `MoiraiParser.g4`).

Status legend: ✅ done · 🟡 partial · ⬜ open

---

## Expressiveness

### ✅ Multi-valued / collection properties
`prop xs: [T]` with built-in `add` / `remove` / `contains` / `count`, stored in a SQLite
`collection` child table (set semantics). Lets entities model multi-valued relationships instead of
fixed scalar slots + reverse-scan predicates.

Outcome for `parents` specifically: once multi-parameter functions were made SQL-compilable (below),
scalar `parent1`/`parent2` + a `is_child_of` helper proved ~5–9% faster at 1000y (inlined
`parent1 = X or parent2 = X` beats the collection's `EXISTS` subquery) and equally DRY, so w.sg uses
that. **Collections stay in the engine** for genuinely variable-arity / iterable relations
(party members, inventory, N children) where they earn their keep.

### ✅ Multi-parameter functions in SQL predicates
Functions with >1 parameter used to throw `IndexOutOfRange` in `pick`/`each`. Now inlined into the
caller scope, so bool helpers like `is_child_of($ch, $parent)` DRY repeated predicates. Two
follow-ups surfaced:
- Global functions aren't resolvable inside `@display` attributes (parsed before functions register).
- ~~No void/effect functions~~ ✅ A function with no declared return type is now a **procedure**: its
  body is effects (create/set/record/call) and any trailing value is ignored. See `event` keyword below.

### ✅ Aggregates over a query
`count T $v: (pred...)` and `sum|avg|min|max T $v: (pred..., value)` fold a query into a number. The
arguments before the value are the predicate, joined by `and` like a pick's, and it is narrowed the same way
(`Database.Aggregate` scans `Candidates`); `$v` exists only inside the call. So "a country's prosperity is
the mean of its citizens'" is `avg Person $p: (alive, place = $c, $p.prosperity)`. Result types: count is a
number; avg of whole numbers is a float; a sum of percentages is a float (it runs past 100); otherwise the
value's own type. Over no matches every kind gives 0 -- use `count` to tell "none" from "zero". An aggregate
draws no random numbers of its own (its value expression could), and may sit inside another query's
predicate (`pick Place $pl: (count Person $p: (place = $pl) > 3)`). `count($e.coll)` still counts a collection. Pinned by `DslFeatureTests`.

### ✅ Optional `pick` with explicit failure
`pick T $v: (...) else { ... }` runs the block when nothing matches, then stops the rule, exactly as a bare
failed pick would -- a guard clause, so the rule's own flow stays flat. It is written on the pick's line
and only at statement level, so an `if`'s `else` is never taken for one; the block cannot see `$v`. A rule
that takes it still counts as *not completed* on the Rules page. `if (pick ...) { } else { }` still works
for a fallback that should carry on.

### ✅ `random_weighted` total is manual / fragile
`random_weighted { 3 => ...  1 => ... }` with no total draws from the sum of the weights (the printer
leaves it out again). `_` is an error there, since the remainder is always zero. `chance(p)` is the
single-branch sugar: `if chance(3%) { ... }`, or as a statement `chance(3%) { ... }`, which carries on
when it misses. It always draws exactly once, and is an error inside a pick/each predicate (narrowing
decides which candidates a predicate sees, so a draw there would make the world depend on the index).
w.sg keeps its explicit totals: rewriting one to `chance` changes its draws, and so the world.

### ✅ Declared singletons
Added a `singleton` type keyword (`singleton World { prop turn: number }`) that marks a type as a
singleton: the instance id is cached on creation, so `#World.turn` is an O(1) lookup instead of an
entity scan (`GetSingleton` falls back to a scan if the cache misses, so it's purely an optimization
layer). The built-in `Time` type is now marked singleton too, speeding up the very common
`#Time.year` reads. Round-trips through the printer; highlighted by the LSP.

Not yet done: hard uniqueness *enforcement* (creating a second instance currently just updates the
cache, last-wins, rather than erroring). Deferred to avoid edge cases around reload/deserialize; easy
follow-up if desired.

### ✅ Parametrized / unified events
`event name($a: T, ...)` takes parameters, passed by `call(name, $x, ...)` (`EventParamTests`); the count
form `call(name, n)` remains for events without parameters. Arguments accept the same conversions `set` does
(a number into a percentage, `null` into a reference) for events and functions alike.

## Clarity / consistency

### ⬜ Two spellings of AND in predicates
Comma vs `and` are used interchangeably: `(alive, age = Age.Child)` vs `(alive and partner = null)`.
Pick a canonical form (lint the other) or at least document that comma ≡ `and`.

### ⬜ `record('x')` vs `record 'x'`, empty-predicate `pick`
Both call forms (`record(…)` and `record …`) and both empty-pick spellings (`pick T $v` and
`pick T $v: ()`) appear. A formatter rule could normalize.

### ✅ Redundant `type = T` inside a typed `each`
Warned by the visitor (`RedundantTypeFilter`), which the language server shows faded as unnecessary.

### ✅ Implicit set-target after `create`
Was: `create Country $c` then bare `set prosperity = …` targeted the *last declared variable*
(`ParsePath` → `_current.Count - 1`), which breaks subtly if a `pick`/`create` interleaves.
Addressed with a C#-style object-initializer block using the `:=` operator (the previously-unused
`COLON_EQ` token), scoping the assignments to the new entity:

```
create Country $c: '{random(CountryName)} {random(Name)}' {
    prosperity := 50%
    health := CountryHealth.Neutral
}
```

`prop := value` sets `prop` on the current scope entity; usable in any scope, but its main use is
`create` initializer blocks. Round-trips faithfully through the printer. (The fragile bare-`set`
last-variable fallback still exists for back-compat; making it an error outside init/each/pick scopes
is a possible follow-up.)

### ✅ Entity identity via the `id` pseudo-property
References compare directly (`$y != $x`), and `id` read through a type is a reference to that type, so
`$a.id` can be passed where a `Person` is expected.

### ⬜ Implicit `$new` in `when`
In triggers, a bare property means `$new.<prop>` while `$old.<prop>` is explicit
(`when Item and owner != $old.owner`). Powerful but subtle — document the "bare prop = `$new`" rule
prominently.

### ✅ `event` keyword is overloaded
`function` now doubles as the procedural keyword: a no-return `function name() { ...effects... }` is a
subroutine, invoked via `call(name)` / `call(name, count)` (which now resolves functions, running them
inline in the caller's changeset) or directly as `name()`. w.sg's call-only `create_country` /
`create_god` are now functions, leaving `event` for scheduled actions. (Not enforced: a non-scheduled
`event` is still allowed; making that a warning is a possible follow-up.)

### ⬜ Number→enum implicit cast is opaque
`set $c.health = ($c.prosperity / 10 + 1) / 2` assigns a computed number to an enum-typed property
(`update_country_health`). Clever, but reads as a type error. A named helper or explicit cast syntax
would make it legible.

### ✅ Comments
`/* ... */` block comments, over as many lines as they like. A line break inside one is still a line
break -- statements end at newlines, and a comment crossing lines ends the statement before it just as
the lines would -- and each line of the comment is its own token, which is what editors can colour. An
unclosed `/*` is a lexer error. `///` lines directly above a definition (attributes may sit between) are its
doc comment, which the language server's hover shows; to the engine they are ordinary comments.

## Resolved separately

### ✅ Scheduling-syntax drift
The original mismatch between the docs/sample (`@1 per N years`) and the grammar is resolved: w.sg now
uses the function-style attribute forms the grammar supports — `@frequency(1, PerXYear, 15)`,
`@tag('…')`, `@display(Type, 'label', predicate)`.

---

## Suggested priority

What is left is clarity rather than power: one spelling of `and` in predicates, a formatter rule for the
`record`/empty-pick spellings, an explicit number→enum cast, and documenting the bare-property-is-`$new`
rule in `when`.
