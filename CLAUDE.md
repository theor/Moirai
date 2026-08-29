# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Moirai is a procedural narrative / world-simulation engine driven by its own domain-specific language (`.sg` / `.moi` files). A story file declares entity types, enums, scheduled `event`s and reactive `trigger`s; the engine simulates year-by-year and emits a stream of narrative `record`s plus a queryable world state. The web server hosts an interactive viewer; a VS Code extension provides language support.

The solution targets **.NET 8** (`global.json` pins SDK 8.0 with `rollForward: latestMajor`). The frontend is **SvelteKit + Skeleton UI + Tailwind**, talking to the server over **SignalR**.

## Commands

```powershell
# Build everything
dotnet build

# Run all tests (NUnit)
dotnet test
# Run a single test class / method by name filter
dotnet test --filter "FullyQualifiedName~EventTests"
dotnet test --filter "Name~MyTestMethod"

# Run the web server — REQUIRES a .sg input file as the first positional arg.
# In dev it auto-launches the Svelte dev server (yarn run dev on port 3000) and proxies to it,
# and hot-reloads when the .sg file changes on disk.
dotnet run --project MoiraiWebServer -- MoiraiCli/w.sg
# (if no/invalid file is passed, it prompts on stdin for a path)

# Same, but profile every simulation run (see "Profiling" below)
dotnet run --project MoiraiWebServer -- --profile MoiraiCli/w.sg

# Production publish — this also runs `yarn install` && `yarn build` in ClientAppSvelte
dotnet publish -c Release MoiraiWebServer/MoiraiWebServer.csproj
```

Frontend (run inside `MoiraiWebServer/ClientAppSvelte/`, package manager is **yarn classic**):

```powershell
yarn dev       # vite dev server on :3000
yarn build
yarn check     # svelte-check / type check
yarn lint      # prettier --check && eslint
yarn format    # prettier --write
yarn test      # vitest
```

VS Code language server: building `vscode-languageserver/Server/Server.csproj` outputs the LSP binary directly into `vscode-languageserver/client/server/` (see its `OutputPath`); the TS client lives in `vscode-languageserver/client/`.

CI (`.github/workflows/dotnet.yml`) on `main` / PRs: restore → build → `dotnet test` → publish the web server. Tags `v*` cut a GitHub release zip.

## Profiling

Pass `-p` / `--profile` to the web server to profile the simulation. When enabled, **every** `PassYears` run (each "pass N years" from the UI) prints a per-run report to the server console — it does not affect the HTTP/SignalR responses. Use this to find optimization targets; the profiler is plain runtime code (not `[Conditional("DEBUG")]`), so it works in Release builds too.

```
=== Execution profile: 50 years in 47.6 ms ===

Events (executed):
  name                              exec        ok   hit%    self ms    incl ms    avg us
  age_up                              50        50 100.0%      32.76      32.76     655.2
  spawn                               50        50 100.0%       4.31       4.31      86.2
  TOTAL                              100       100 100.0%      37.07      37.07

Triggers (attempted):
  name                          attempts        ok   hit%    self ms    incl ms    avg us
  on_death                          1275         0   0.0%       1.14       1.14       0.9
  on_birth                            50        50 100.0%       1.12       1.12      22.3
  TOTAL                             1325        50   3.8%       2.25       2.25

Coverage: events 37.1 ms + triggers 2.3 ms = 39.3 ms of 47.6 ms (82.6% self; remainder is scheduling/query overhead)
```

Reading the report:
- **Events** are scheduled actions (one row per `event`); **exec** = invocations, **ok** = ran to completion (a `pick` that finds nothing aborts the event → counts against the hit rate).
- **Triggers** are reactive rules (one row per `trigger`); **attempts** = times evaluated against a change, **ok** = predicate matched and effects ran. A low trigger **hit%** over many attempts is the prime optimization signal — e.g. `on_death` above is evaluated 1275× and matches 0×, all wasted work (triggers are currently re-checked for every change in a changeset).
- **self ms** excludes nested measured scopes (an event's `call()` into other events, and the triggers fired after an event); **incl ms** includes them. For leaf rules the two are equal. Sort is by self time descending.
- The **Coverage** line reconciles event + trigger self time against total wall time; the gap is per-year scheduling, the `Time.year` update, and query overhead (`PickRandom`/`FindAll` scans).

Mechanics (for changing the profiler): `Database.ProfilingEnabled` gates it; `ExecuteContext.PassYears` allocates a fresh `ExecutionProfiler` per run and prints `Report()`; recording happens in `Database.RunAction` (events) and `Database.RunTriggers` (triggers). See `Moirai/Core/ExecutionProfiler.cs`. Tests/tools can profile directly by setting `db.ProfilingEnabled = true` before `db.Ctx.PassYears(...)` and reading `db.ExecProfiler`. This is separate from the older `[Conditional("DEBUG")]` `Profiler` in `Entity.cs`, which counts per-property get/set hits.

## Architecture

The pipeline is: **`.sg` text → tokenize/parse → AstVisitor builds engine objects → `Database` simulates → SignalR streams results to the Svelte client.**

### `Moirai/` — the simulation engine (core, no parsing)
- **`Core/Database.cs`** is the world. It holds `Types`, `Enums`, `Actions` (scheduled events), `Triggers` (reactive), all `Entities`, and `History`. It is the central API: `RunAction`, `FindAll`/`PickRandom` (query), `AllocateEntity`, `SetProperty`, `Mark`/`GetLastMarked`.
  - World state lives **in-memory only** (the `Entity` list). Queries (`pick` / `each` predicates) are evaluated directly against the predicate tree's `IValue.IsTrue` — `PickRandom` (reservoir sampling) and `FindAll` scan per-type entity buckets (`_perTypeEntities`), narrowed by a lightweight bool index (`_boolIndex`) for the common `prop`/`prop = true` filter. (This replaced an in-memory SQLite mirror; `IValueSql` is now just a marker for predicates usable as query filters.)
- **`Core/ExecuteContext.cs`** drives time (`PassYears`) and holds the runtime value stack (`SetArgument`/`Argument`, scoped via `RunScope`) and the RNG (`Pcg32`, seeded — simulation is deterministic per seed).
- **`Effect/`** — imperative instructions (`IInstruction`) that events/triggers execute: `CreateEntity`, `SetProperty`, `AssignPick` (`pick`/`each`), `CallRule`, `Sequence`, etc.
- **`Predicate/`** — the expression/filter tree (`IValue`, `IFilter`): `PropertyPath`, `BinaryOperator`, `Literal`, `MathUnary`, … These are evaluated in-memory via `Compute`/`IsTrue` for both queries and trigger predicates.
- **`Changeset.cs` / `History`** — every `RunAction` opens a `Changeset` recording entity creates/sets. After an action's changeset closes, `Database.RunTriggers` replays it against all `Triggers` (matching `when` / `when_created` + predicate), which is how reactive rules (`inherit`, `born`, …) fire. History is the changeset log the UI browses.

### `Moirai.Parser/` — the DSL front end
- Hand-rolled, no code generation, no ANTLR/JVM dependency. `MoiraiTokenizer.cs` is a stateful char-by-char scanner (mode stack for string interpolation) producing a Superpower `TokenList<MoiraiTokenKind>`; `Ast/MoiraiGrammar*.cs` are hand-written Superpower `TokenListParser` combinators (one file per grammar area: atoms/paths, expressions, statements, type/enum/table definitions, call/value dispatch) building the AST types in `Ast/AstNodes.cs` directly as their parse result — one node type per construct actually consumed downstream, not a blanket 1:1 grammar mirror.
- **`StoryParser.cs`** is the entry point: `Parse(text)` → `Database` (tokenizes, chunks the token stream at top-level def boundaries so one broken def doesn't blank out the whole file, parses each chunk independently), `ParseExpr(...)` for one-off query expressions. It also defines `Functions` — the table of built-in DSL functions (`create`, `each`, `pick`, `random`, `record`, `call`, `mark`, `not`, `floor`, …) and the `ErrorCode` enum.
- **`AstVisitor.cs`** walks the AST and constructs the `Effect`/`Predicate` objects on the `Database`.
- **`Moirai.Parser.Antlr/`** is a frozen, ANTLR-based snapshot of this project taken before the migration above (grammar in `moirai_lexer.g4`/`MoiraiParser.g4`, generated via `Antlr4BuildTasks`) — referenced only by `vscode-languageserver/Server` (its semantic-tokens/formatting visitors and the ATN-based code-completion engine still need a real ANTLR parse tree; migrating those is a separate, not-yet-started effort). **Never edit `Moirai.Parser.Antlr` to add DSL features** — new syntax lands in `Moirai.Parser`'s grammar first, and the LSP is expected to lag until its own migration.

### `MoiraiWebServer/` — host + viewer
- **`Program.cs`**: ASP.NET Core. Parses CLI options (`CommandLineParser`), watches the input `.sg` file (debounced) and triggers reload, configures SignalR JSON (custom converters for `EntityId`/`PropertyId`/`EntityTypeId`/`ValueType`), and in Development spawns the Svelte dev server and proxies `/` to `http://localhost:3000`.
- **`Hubs/ChatHub.cs`** is the entire client API surface. Methods: `Reset`, `PassYears` (streamed progress), `Query`, `RunAction`, `GetChangesets(start,count)`, `GetFamilyTree`, `GetEntityDetails`, `GetClientData`, `Save`, and `Stream` (pushes record/year/reset messages). **State is `static` (`_db`) shared across all connections and guarded by a single `SemaphoreSlim Mutex`** — this is a single-world, single-tenant server.
- **`ClientAppSvelte/`**: SvelteKit SPA. `src/lib/connection.ts` wraps the SignalR hub and exposes the `moiraiStore` Svelte store the whole UI subscribes to; `src/routes/` are the pages (main, `changesets`, `query`, `records`); `src/components/` the widgets.

### Other projects
- **`vscode-languageserver/`** — C# LSP server (`Server/`, OmniSharp `LanguageServer`, with ANTLR-based code completion under `Server/CodeCompletion/`) + a TypeScript VS Code client. References the frozen `Moirai.Parser.Antlr`, not `Moirai.Parser` (see above).
- **`Moirai.SourceGenerators/`** — Roslyn source generators; no longer referenced by `Moirai.Parser` (only by `Moirai.Parser.Antlr`, for the ANTLR token-constant-to-enum stub).
- **`TestProject1/`** — main NUnit suite for the engine/DSL (`EventTests`, `FilterTests`, `MatchTests`, `ParsingTests`, …), plus the parser-migration test suites (`TokenizerDifferentialTests`, `GrammarStructuralTests`, `GrammarRuleTests`, `ParserDifferentialTests`, `ChunkedErrorRecoveryTests`) that diff `Moirai.Parser` against `Moirai.Parser.Antlr` via `extern alias` (both share the `Moirai.Parser` namespace). The `.sg` files in `MoiraiCli/` (`w.sg`, `test.sg`, `space.sg`) are sample stories used for manual runs.
- **`Moirai.LanguageServer.Tests/`** — NUnit tests for the code-completion core.

## The `.sg` DSL (quick reference)

`MoiraiCli/w.sg` is the canonical large example. Core constructs:
- `entity Person { prop age: Age  prop partner: Person ... }` — types and typed properties (refs, enums, `number`, `bool`, `percentage`, `string`).
- `enum Job { Farmer, Smith, ... }`
- `event name { ... }` with a scheduling attribute: `@start` (run once at init), `@frequency(X, PerXYear, Y)` (probabilistic, avg X occurrences per Y years), or `@frequency(X, EveryXYear, Y)` (deterministic, exactly X every Y years). An event with no scheduling attribute only runs when invoked via `call(...)`.
- `trigger name { when_created Person ... }` or `when Person and <predicate> { ... }` — reactive rules run after changesets; `$new` / `$old` reference the changed entity.
- Effects inside bodies: `create T $v: '...'`, `set $v.prop = ...`, `pick T $v: (predicate)`, `each T $v: (predicate) { ... }`, `if`, `match`, `random_weighted N { w => ... }`, `record('...')`, `call(event, count)`.
- `$var` are scoped locals; `#Time.year` reads the `Time` singleton.
- **Attributes** (all use the `@name(args...)` call form, one per line, immediately before the event/trigger/entity they annotate):
  - `@tag('a', 'b')` — categorize an event/trigger (string literals).
  - `@display(ReferencedType, 'Label', <predicate>, ['itemFmt'])` — derived/back-reference field on the preceding entity type, shown in the details panel; `$self` is the entity, `$other` the referenced one.
  - **Gotcha:** older sources used bare-word forms (`@1 per N years`, `event name tag {`, `@display Type (...)`) that the current grammar rejects — the parser emits errors but the web server *silently ignores them*, so a malformed annotation just drops its filter/tag/display with no visible failure. Always use the `@name(...)` call form.
