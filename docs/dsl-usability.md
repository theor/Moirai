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

### 🟡 Aggregates over a query
`count` exists (for collections), but there's no `sum` / `avg` / `min` / `max` over a `pick`/`each`
result set. E.g. "country prosperity = average of its citizens' prosperity" is inexpressible. Maps
directly onto the SQL backend (`SELECT AVG(...)`). **High leverage — genuinely new modeling power.**

### ⬜ Optional `pick` with explicit failure
`pick` abort-vs-continue is implicit; the one site needing a fallback uses `if(pick …){}else{}`
(`inherit`). A first-class `pick T $v: (…) else { … }` would make the decision visible at every pick
and remove a footgun.

### ⬜ `random_weighted` total is manual / fragile
`random_weighted 100 { 3 => … }` requires hand-syncing the literal total with the branch weights, and
`_` silently absorbs the remainder. Infer the total from the branches, or add a `chance(3%) { … }`
sugar for the common single-branch case (`char_prestige_update`'s suicide check is exactly this written
the long way).

### ✅ Declared singletons
Added a `singleton` type keyword (`singleton World { prop turn: number }`) that marks a type as a
singleton: the instance id is cached on creation, so `#World.turn` is an O(1) lookup instead of an
entity scan (`GetSingleton` falls back to a scan if the cache misses, so it's purely an optimization
layer). The built-in `Time` type is now marked singleton too, speeding up the very common
`#Time.year` reads. Round-trips through the printer; highlighted by the LSP.

Not yet done: hard uniqueness *enforcement* (creating a second instance currently just updates the
cache, last-wins, rather than erroring). Deferred to avoid edge cases around reload/deserialize; easy
follow-up if desired.

### ⬜ Parametrized / unified events
`call(create_country, 10)` can only repeat an event N times — no arguments. Bridging events and
functions (events that take args, or schedulable rules) would let setup code share helpers.

---

## Clarity / consistency

### ⬜ Two spellings of AND in predicates
Comma vs `and` are used interchangeably: `(alive, age = Age.Child)` vs `(alive and partner = null)`.
Pick a canonical form (lint the other) or at least document that comma ≡ `and`.

### ⬜ `record('x')` vs `record 'x'`, empty-predicate `pick`
Both call forms (`record(…)` and `record …`) and both empty-pick spellings (`pick T $v` and
`pick T $v: ()`) appear. A formatter rule could normalize.

### ⬜ Redundant `type = T` inside a typed `each`
`each Person $p: (type = Person, …)` (`youngs_grow`) restates the type the `each Person` already pins —
leftover from the old untyped syntax. The visitor could warn.

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

### ⬜ Entity identity via the `id` pseudo-property
`id != $x`, `id != $x.parent1` leak an implicit `id`. Allowing direct ref comparison (`$y != $x`) reads
better. Flip side seen in practice: `id` is typed generic `ref`, not the entity type, so it can't be
passed to a typed function parameter — the typed query variable (`$y`/`$p`) must be used instead.

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

### ⬜ Comments
Only `//` line comments. No block (`/* */`) or doc comments (which could also feed LSP hovers).

---

## Resolved separately

### ✅ Scheduling-syntax drift
The original mismatch between the docs/sample (`@1 per N years`) and the grammar is resolved: w.sg now
uses the function-style attribute forms the grammar supports — `@frequency(1, PerXYear, 15)`,
`@tag('…')`, `@display(Type, 'label', predicate)`.

---

## Suggested priority

1. **Aggregates** (`avg`/`sum`/`min`/`max` over a query) — most new expressive power, fits the SQL backend.
2. **Optional `pick … else`** — removes a real control-flow footgun.
3. **Effect / void functions** — would let helpers factor out duplicated *effect* blocks, not just predicates.
