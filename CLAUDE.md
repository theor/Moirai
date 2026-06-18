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

## Architecture

The pipeline is: **`.sg` text → ANTLR parse → AstVisitor builds engine objects → `Database` simulates → SignalR streams results to the Svelte client.**

### `Moirai/` — the simulation engine (core, no parsing)
- **`Core/Database.cs`** is the world. It holds `Types`, `Enums`, `Actions` (scheduled events), `Triggers` (reactive), all `Entities`, and `History`. It is the central API: `RunAction`, `FindAll`/`PickRandom` (query), `AllocateEntity`, `SetProperty`, `Mark`/`GetLastMarked`.
  - World state is held **both** in-memory (`Entity` list) **and** mirrored into an in-memory **SQLite** DB (`Data Source=:memory:`). Queries (`pick` / `each` predicates) are compiled to SQL — see `IValueSql.ToSql` — and run against SQLite; mutations write through to both. The schema is one wide `entity` table with `{TypeName}__{prop}` columns, rebuilt on `Init()`.
- **`Core/ExecuteContext.cs`** drives time (`PassYears`) and holds the runtime value stack (`SetArgument`/`Argument`, scoped via `RunScope`) and the RNG (`Pcg32`, seeded — simulation is deterministic per seed).
- **`Effect/`** — imperative instructions (`IInstruction`) that events/triggers execute: `CreateEntity`, `SetProperty`, `AssignPick` (`pick`/`each`), `CallRule`, `Sequence`, etc.
- **`Predicate/`** — the expression/filter tree (`IValue`, `IFilter`): `PropertyPath`, `BinaryOperator`, `Literal`, `MathUnary`, … These are what compile to SQL for queries.
- **`Changeset.cs` / `History`** — every `RunAction` opens a `Changeset` recording entity creates/sets. After an action's changeset closes, `Database.RunTriggers` replays it against all `Triggers` (matching `when` / `when_created` + predicate), which is how reactive rules (`inherit`, `born`, …) fire. History is the changeset log the UI browses.

### `Moirai.Parser/` — the DSL front end
- Grammar: **`moirai_lexer.g4`** + **`MoiraiParser.g4`**. ANTLR generates code into `Moirai.Parser/gen/` at build time via `Antlr4BuildTasks` — **these generated files are gitignored; do not edit them, change the `.g4` and rebuild.**
- **`StoryParser.cs`** is the entry point: `Parse(text)` → `Database`, `ParseExpr(...)` for one-off query expressions. It also defines `Functions` — the table of built-in DSL functions (`create`, `each`, `pick`, `random`, `record`, `call`, `mark`, `not`, `floor`, …) and the `ErrorCode` enum.
- **`AstVisitor.cs`** walks the ANTLR tree and constructs the `Effect`/`Predicate` objects on the `Database`.

### `MoiraiWebServer/` — host + viewer
- **`Program.cs`**: ASP.NET Core. Parses CLI options (`CommandLineParser`), watches the input `.sg` file (debounced) and triggers reload, configures SignalR JSON (custom converters for `EntityId`/`PropertyId`/`EntityTypeId`/`ValueType`), and in Development spawns the Svelte dev server and proxies `/` to `http://localhost:3000`.
- **`Hubs/ChatHub.cs`** is the entire client API surface. Methods: `Reset`, `PassYears` (streamed progress), `Query`, `RunAction`, `GetChangesets(start,count)`, `GetFamilyTree`, `GetEntityDetails`, `GetClientData`, `Save`, and `Stream` (pushes record/year/reset messages). **State is `static` (`_db`) shared across all connections and guarded by a single `SemaphoreSlim Mutex`** — this is a single-world, single-tenant server.
- **`ClientAppSvelte/`**: SvelteKit SPA. `src/lib/connection.ts` wraps the SignalR hub and exposes the `moiraiStore` Svelte store the whole UI subscribes to; `src/routes/` are the pages (main, `changesets`, `query`, `records`); `src/components/` the widgets.

### Other projects
- **`vscode-languageserver/`** — C# LSP server (`Server/`, OmniSharp `LanguageServer`, with ANTLR-based code completion under `Server/CodeCompletion/`) + a TypeScript VS Code client. Reuses `Moirai.Parser`.
- **`Moirai.SourceGenerators/`** — Roslyn source generators referenced as an analyzer by `Moirai.Parser`.
- **`TestProject1/`** — main NUnit suite for the engine/DSL (`EventTests`, `FilterTests`, `MatchTests`, `ParsingTests`, …). The `.sg` files in `MoiraiCli/` (`w.sg`, `test.sg`, `space.sg`) are sample stories used for manual runs.
- **`Moirai.LanguageServer.Tests/`** — NUnit tests for the code-completion core.

## The `.sg` DSL (quick reference)

`MoiraiCli/w.sg` is the canonical large example. Core constructs:
- `entity Person { prop age: Age  prop partner: Person ... }` — types and typed properties (refs, enums, `number`, `bool`, `percentage`, `string`).
- `enum Job { Farmer, Smith, ... }`
- `event name tags { ... }` with a scheduling annotation: `@start` (run once at init), `@1 per N years` (≈ once every N years, probabilistic), `@N every M year` (exactly N times every M years).
- `trigger name { when_created Person ... }` or `when Person and <predicate> { ... }` — reactive rules run after changesets; `$new` / `$old` reference the changed entity.
- Effects inside bodies: `create T $v: '...'`, `set $v.prop = ...`, `pick T $v: (predicate)`, `each T $v: (predicate) { ... }`, `if`, `match`, `random_weighted N { w => ... }`, `record('...')`, `call(event, count)`.
- `$var` are scoped locals; `#Time.year` reads the `Time` singleton; `@display Type ('Label', predicate, 'fmt')` configures derived fields shown in the entity details panel.
