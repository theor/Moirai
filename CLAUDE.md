# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Moirai is a procedural narrative / world-simulation engine driven by its own domain-specific language (`.sg` / `.moi` files). A story file declares entity types, enums, scheduled `event`s and reactive `trigger`s; the engine simulates year-by-year and emits a stream of narrative `record`s plus a queryable world state. The web server hosts an interactive viewer; a VS Code extension provides language support.

The solution targets **.NET 10** (`global.json` pins SDK 10.0 with `rollForward: latestMajor`). The frontend is **SvelteKit + Skeleton UI + Tailwind**, talking to the server over **SignalR**.

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

# Start from a specific RNG seed (default 42). The simulation is deterministic per seed, so
# seed + story file is the whole identity of a run. Also settable live from the app bar Seed box.
dotnet run --project MoiraiWebServer -- --seed 1234 MoiraiCli/w.sg

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

# Build the WebAssembly engine and stage it into static/ (requires the wasm workload, see below)
yarn wasm:build
yarn wasm:smoke        # boot the staged engine under Node and exercise every export
# Then run the client against it instead of the .NET host:
yarn dev:wasm          # or just yarn dev and open http://localhost:3000/records?backend=wasm
```

The WASM engine needs a one-off `dotnet workload install wasm-tools wasm-experimental` (it is not part of
`Moirai.sln`, so a plain `dotnet build` never requires it). After `yarn wasm:build`, a plain `yarn build`
emits a **fully static, serverless site** (~8 MB) — `build/` then contains the runtime and `w.sg`
alongside the SPA.

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

**Rule coverage vs. profiling.** Separately from the profiler, every `EventTrigger` carries always-on cumulative `Attempts`/`Successes` counters (incremented in `Database.RunAction` and `Database.RunTriggers` next to the profiler's own recording sites). They are two increments with no timestamp, so they need no flag, and unlike the profiler they are **not** reset per `PassYears` — they cover the whole life of the world. `WorldSession.GetRuleCoverage` projects them for the web UI's **Rules** page, which flags rules that never ran (dead code in the story) and rules that never completed (an event whose `pick` finds nothing, a trigger whose predicate never matches). Neither shows up in the records feed, because neither emits records. `TestProject1/RuleCoverageTests` pins the counters against the profiler and asserts `w.sg` has no permanently dead rules.

Mechanics (for changing the profiler): `Database.ProfilingEnabled` gates it; `ExecuteContext.PassYears` allocates a fresh `ExecutionProfiler` per run and prints `Report()`; recording happens in `Database.RunAction` (events) and `Database.RunTriggers` (triggers). See `Moirai/Core/ExecutionProfiler.cs`. Tests/tools can profile directly by setting `db.ProfilingEnabled = true` before `db.Ctx.PassYears(...)` and reading `db.ExecProfiler`. This is separate from the older `[Conditional("DEBUG")]` `Profiler` in `Entity.cs`, which counts per-property get/set hits.

## Architecture

The pipeline is: **`.sg` text → tokenize/parse → AstVisitor builds engine objects → `Database` simulates → SignalR streams results to the Svelte client.**

### `Moirai/` — the simulation engine (core, no parsing)
- **`Core/Database.cs`** is the world. It holds `Types`, `Enums`, `Actions` (scheduled events), `Triggers` (reactive), all `Entities`, and `History`. It is the central API: `RunAction`, `FindAll`/`PickRandom` (query), `AllocateEntity`, `SetProperty`, `Mark`/`GetLastMarked`.
  - World state lives **in-memory only** (the `Entity` list). Queries (`pick` / `each` predicates) are evaluated directly against the predicate tree's `IValue.IsTrue` — `PickRandom` (reservoir sampling) and `FindAll` scan per-type entity buckets (`_perTypeEntities`), narrowed by a lightweight bool index (`_boolIndex`) for the common `prop`/`prop = true` filter. (This replaced an in-memory SQLite mirror; `IValueSql` is now just a marker for predicates usable as query filters.)
- **`Core/ExecuteContext.cs`** drives time (`PassYears`) and holds the runtime value stack (`SetArgument`/`Argument`, scoped via `RunScope`) and the RNG (`Pcg32`, seeded — simulation is deterministic per seed).
- **`Effect/`** — imperative instructions (`IInstruction`) that events/triggers execute: `CreateEntity`, `SetProperty`, `AssignPick` (`pick`/`each`), `CallRule`, `Sequence`, etc.
- **`Predicate/`** — the expression/filter tree (`IValue`, `IFilter`): `PropertyPath`, `BinaryOperator`, `Literal`, `MathUnary`, … These are evaluated in-memory via `Compute`/`IsTrue` for both queries and trigger predicates.
- **`Core/WorldSeries.cs`** derives time series from `History` **after the fact**. A closed changeset holds a full clone of every entity it touched, so replaying the log reconstructs any property's history exactly — that is how "population over time" and "average prosperity over time" get answered without the engine tracking either. Bools become a count of entities holding them true, numbers become a mean. Series run from `Database.StartYear` (the `Time` singleton's year at the end of `Init`, 764 for w.sg) to the present, not from year 0, and downsample to ~400 buckets: a *rate* takes the bucket mean so the axis still reads "per year", a *level* takes its last sample. Feeds the web UI's **World** page via `WorldSession.GetWorldOverview` / `GetPropertySeries`.
- **`Changeset.cs` / `History`** — every `RunAction` opens a `Changeset` recording entity creates/sets. After an action's changeset closes, `Database.RunTriggers` replays it against all `Triggers` (matching `when` / `when_created` + predicate), which is how reactive rules (`inherit`, `born`, …) fire. History is the changeset log the UI browses.

### `Moirai.Parser/` — the DSL front end
- Hand-rolled, no code generation, no ANTLR/JVM dependency. `MoiraiTokenizer.cs` is a stateful char-by-char scanner (mode stack for string interpolation) producing a Superpower `TokenList<MoiraiTokenKind>`; `Ast/MoiraiGrammar*.cs` are hand-written Superpower `TokenListParser` combinators (one file per grammar area: atoms/paths, expressions, statements, type/enum/table definitions, call/value dispatch) building the AST types in `Ast/AstNodes.cs` directly as their parse result — one node type per construct actually consumed downstream, not a blanket 1:1 grammar mirror.
- **`StoryParser.cs`** is the entry point: `Parse(text)` → `Database` (tokenizes, chunks the token stream at top-level def boundaries so one broken def doesn't blank out the whole file, parses each chunk independently), `ParseExpr(...)` for one-off query expressions. It also defines `Functions` — the table of built-in DSL functions (`create`, `each`, `pick`, `random`, `record`, `call`, `mark`, `not`, `floor`, …) and the `ErrorCode` enum.
- **`AstVisitor.cs`** walks the AST and constructs the `Effect`/`Predicate` objects on the `Database`.

### `MoiraiWebServer/` — host + viewer
- **`Program.cs`**: ASP.NET Core. Parses CLI options (`CommandLineParser`), watches the input `.sg` file (debounced) and triggers reload, configures SignalR JSON by handing the payload serializer to `MoiraiWireJson.Configure`, and in Development spawns the Svelte dev server and proxies `/` to `http://localhost:3000`.
- **The nav tabs render `<button>`, not `<a>`, and that is load-bearing.** Skeleton's `Tabs` is controlled and its value is `$page.url.pathname`, so every navigation changes it — and the component reacts by re-activating the matching trigger, which it does by dispatching `new MouseEvent('click')` at the element. That event is `cancelable: false` and does not bubble, so it cannot be prevented and neither SvelteKit's router (listening on `<html>`) nor a Svelte `onclick` ever sees it, because Svelte delegates bubble-phase click handlers to one root listener. The browser, however, still runs an anchor's default navigation. The effect was that a real click's soft navigation was immediately followed by a **full page load of the same URL** — harmless with the server, where the world lives elsewhere, but it threw away the in-browser world on every tab switch. A button has no default navigation, so the synthetic click does nothing and `goto` is the only path. The cost is a real link's affordances on the tab bar: no middle-click, no ctrl-click, no "copy link address".
- **`Hubs/ChatHub.cs`** is now only the SignalR *transport*: the wire names, plus the lock. Every method is `acquire → delegate to WorldSession → release`. **State is `static` (`_session`) shared across all connections and guarded by a single `SemaphoreSlim Mutex`** — this is a single-world, single-tenant server. The per-method timeouts are deliberate: a read that cannot get the lock returns empty rather than making the UI queue behind a running simulation (`Wait(500)` on `GetEntityDetails`/`GetFamilyTree`, `Wait(100)` on `PassYears`), while a write waits indefinitely because dropping it would lose work. `Stream` still runs the 500 ms feed loop, but the batch it sends comes from `WorldSession.DrainFeed`, so the browser sees the identical message sequence.

- **`ClientAppSvelte/`**: SvelteKit SPA (`adapter-static` + `ssr = false`, so it is a standalone SPA that can be served by anything). `src/lib/api.ts` declares `MoiraiApi` — the transport-independent client surface — and `src/lib/backend.ts` picks an implementation: `signalr-api.ts` (the .NET host) or `wasm-api.ts` (the engine in the browser), chosen by `?backend=wasm` then `VITE_MOIRAI_BACKEND`. The choice is **remembered in `sessionStorage`**, which is load-bearing: the nav links do real navigation, so every page change is a fresh document with no query string, and without stickiness clicking "World" after opening `?backend=wasm` silently drops you onto the other backend — invisible whenever a server happens to be running. `src/lib/connection.ts` no longer knows about either; it holds the `moiraiStore` Svelte store the whole UI subscribes to. `wasm-api.test.ts` drives a fake engine to cover what only the host can get wrong: that a long pass is split into chunks and yields between them, that records arrive *during* a pass rather than all at the end, that cancellation stops at a chunk boundary, and that the feed backlog stops the first tick (the reset notice plus every `@start` record) being lost between the world existing and the store subscribing. `backend.test.ts` pins the sticky backend choice.

  **`$lib/settled-year`** is why a pass does not thrash the pages. Five places re-query the world when the year changes — rule coverage, the world overview and its series, an entity's changesets, a family tree, a biography — and with the in-browser engine those queries are synchronous work on the thread that is trying to paint. `settledYear` reports the year only once it stops moving: a change is held for 400 ms of quiet, never longer than 1.5 s (so a long pass still shows progress rather than freezing), and flushed at once when a pass ends. Measured over a 400-year pass: 37 year updates became 2 refetches. It reports nothing until a backend exists, which gives each page exactly one trigger at startup instead of one for the empty world and another for the real one. **No page or component names a transport** — they call `conn.getBiography(...)` and get whichever backend loaded. `src/routes/` are the pages (main, `records`, `life`, `changesets`, `query`, `family`, `world`, `rules`); `src/components/` the widgets. Chart and meter colours live in the `.viz-root` token block at the bottom of `src/app.css` — every value is a step of the active Skeleton theme, and the steps were validated against the theme's surface, so **re-validate them if the theme changes**. Page logic that is worth testing lives in `$lib` (e.g. `coverage.ts`, `chart.ts`) with a vitest file beside it, not in the `.svelte` file. The **Life** page (`GetBiography`) is the one place the three per-entity sources meet: records mentioning it, changesets touching it, and its family tree. Records and changes interleave by `(year, changesetId)` — a record carries the id of the changeset that produced it, so that ordering is causal — with a **stable** sort, because an event and the triggers it fires share a changeset id. `components/LineChart.svelte` is a hand-rolled SVG line chart (no chart library): it measures its own width so a 2px line is 2px at any size, and carries a crosshair, a direct end-label, an `aria-label` summary and an optional table view.

### `Moirai.Api/` — the client API, with no transport
`WorldSession` is one world plus every question a viewer can ask of it. Both hosts are shims over it, which is the point: the SignalR hub and the WebAssembly export cannot drift, because there is only one implementation to drift from.

- **Deliberately not thread-safe.** A world is a mutable object graph and a simulation pass walks all of it, so exclusion is the *host's* job — the server takes a semaphore per call, the browser needs nothing because its runtime is single-threaded. Locking inside would make the browser pay for a problem it cannot have.
- **`Reset()` order is load-bearing**: parse → `History = new()` → `ProfilingEnabled` → `SetSeed` → `Init()`. `SetSeed` must precede `Init` because `@start` events run inside `Init` and have to draw from the requested seed. The constructor takes a `Func<string>`, not a string, which is what makes the server's hot reload work — `Reset` re-reads the file.
- **`DrainFeed(cursor, out newCursor)`** builds the record feed (reset notice → new records → year heartbeat) and services a pending reload. Both hosts call it, so both feeds behave identically. A file edit only sets a flag; the rebuild happens here, on a thread allowed to touch the world.
- **`MoiraiWireJson`** is the one definition of the wire format. `IncludeFields` is load-bearing (`ClientData`, `QueryResult`, `Message` and `Database.Record` expose fields); `JsonStringEnumConverter` is why `MessageType` is a string union in the client; and `Options` sets camelCase **explicitly** because SignalR supplies it by default and the WASM host otherwise would not. Get the two out of step and nothing throws — properties simply arrive under names the client never reads. `WorldSessionTests` pins it.

### `Moirai.Wasm/` — the browser host
The engine compiled to WebAssembly. Nothing in `Moirai/` or `Moirai.Parser/` had to change to make this possible: between them they have one NuGet dependency (`Superpower`, pure managed), no threading, no reflection, no P/Invoke, and — after this — no `System.IO` at all.

- **Not in `Moirai.sln`, on purpose.** It needs `dotnet workload install wasm-tools wasm-experimental`; listing it would make `dotnet build`/`dotnet test`/CI fail for anyone without the workload. Build it through `yarn wasm:build`, which targets the csproj directly.
- **`Interop.cs`** is the JS boundary. `[JSExport]` marshals primitives, not object graphs, so DTOs cross as JSON strings — serialized with `MoiraiWireJson.Options`, hence byte-identical to the server's. There is a **single `Invoke(method, argsJson)`** rather than one export per method: that is the same shape as SignalR's own `invoke`, which is what keeps the two client implementations near-identical, and adding a session method needs one `case` instead of a new export plus a new JS binding.
- **`InvariantGlobalization`** drops the ICU payload *and* fixes a latent bug — the parser's `float.Parse`/`int.Parse` calls pass no `IFormatProvider`, so a comma-decimal locale would misparse numeric literals.
- **Nothing reflects, and that is what makes it small.** The wire types are serialized by a source-generated `JsonSerializerContext` (`Moirai.Api/MoiraiJsonContext.cs`) and the query page prints the parsed expression with the engine's own `StoryPrinter` instead of dumping it through `JsonSerializer.Serialize(e, e.GetType(), …)`. `JsonSerializerIsReflectionEnabledByDefault=false` then holds the line: a wire type nobody declared is a build error here rather than a silent failure in someone's browser. **Every type a `WorldSession` method can return has to be listed in that context.**
- **The runtime runs on the page's main thread, and has to.** A Web Worker was the obvious home for a synchronous simulation loop, and it does not work: in a dedicated worker the runtime downloads every assembly, reaches `preInit`, and then never finishes starting — no exception, no failed request, just a promise that never settles (verified over 150 s). Emscripten derives `ENVIRONMENT_IS_WORKER` from `typeof importScripts`, which a `type: 'module'` worker does not define, so it takes the main-thread path; shimming `importScripts` does not help. Hosting the runtime off-thread is a multithreading feature (`WasmEnableThreads` + COOP/COEP), not something the single-threaded build supports. **As built, no COOP/COEP headers are needed** — which is what keeps the static-site deployment able to go anywhere.
- **So `passYears` chunks instead.** `wasm-api.ts` simulates a chunk of years, drains the record feed, and yields to the event loop, so a long pass reads as history unfolding rather than one silent freeze followed by a dump. This is only sound because chunking cannot change the outcome — the RNG streams live on `ExecuteContext` and the year is re-read from the `Time` singleton on entry — which `WorldSessionTests.ManySmallPassesAreIdenticalToOneLongPass` (and the uneven-chunk variant) pins.
- **The chunk size adapts.** A fixed size does not hold: a year costs more as the population grows, so 25 years went from ~20 ms in a young world to over 400 ms once there were a few thousand entities. `adjustChunkSize` aims each chunk at `TARGET_CHUNK_MS` (50) from the measured cost of the last one, and the timing brackets the feed drain as well as the simulation, because the drain's cost is proportional to the records produced.
- **Growth is capped, shrinking is not**, and that asymmetry matters. Every measurement describes a cheaper world than the next chunk will run in, so scaling up freely on an early measurement makes each subsequent chunk overshoot. Rising at most 25% per step while falling immediately keeps the estimate on the safe side of a moving cost. Worst observed stall over a 300-year pass: **415 ms → ~90 ms**.
- **What the remaining stall is not.** A CPU profile of a pass (`Profiler.start`/`stop` over CDP) put ~90% of the time inside `dotnet.native.wasm` and almost none in JS or rendering, with 7% idle. So the floor is the simulation itself, not the viewer — worth remembering before optimising the client again.
- **A world survives a page load.** Navigation itself no longer reloads (see the nav-tab note under `MoiraiWebServer/`), but a genuine reload — F5, or opening a deep link — still would. Nothing is serialized to survive it: a world is entirely determined by its story, seed and year, so `wasm-api.ts` remembers seed and year in `sessionStorage` (checkpointing mid-pass too) and rebuilds the identical world on load, fast-forwarding in the same chunks. Verified byte-identical — same entity, record and changeset counts — at roughly 170 years per second. This is the same trick the server's `_targetYears` uses after a story file changes on disk.
- **The app bar disables its controls while a backend connects** (`connecting` in `+layout.svelte`, and `moiraiStore` no longer asserts `conn` non-null). With SignalR that window is milliseconds; with WebAssembly it is seconds — a runtime to start and possibly centuries to replay — which was long enough to click Reset and get an uncaught `TypeError`.
- **The runtime import is hidden from Vite** behind `new Function('url', 'return import(url)')`. `@vite-ignore` is not enough: in dev Vite still wraps a statically visible dynamic import in `injectQuery(url, 'import')`, which routes the runtime through Vite's JS transform instead of serving it verbatim from `static/` — and the symptom is again `dotnet.create()` never settling.
- **Trimming**: `PublishTrimmed` + `TrimMode=full`, with no assemblies rooted, because nothing needs its metadata kept any more. Measured: untrimmed 25 MB, `partial` (whole assemblies only, nothing member-trimmed) 4.6 MB, full 3.5 MB — 1.1 MB brotli. Full trim is only possible because of the two changes above: it strips constructor parameter names, and reflection-based `System.Text.Json` cannot bind a record without them. The win is not mainly in `System.Text.Json` (343→192 K) but in what stops being reachable: `Superpower` 91→18 K, and `System.Text.RegularExpressions` (240 K) and `System.Private.Uri` (60 K) drop out entirely.
- **`WasmEmitSymbolMap=false`**, and `build-wasm.mjs` leaves `.map` files behind — half a megabyte that only helps someone debugging the runtime itself. The symbol map has to be suppressed at publish time rather than deleted afterwards: it is listed in the boot manifest, so deleting it turns the saving into a failed start.
- **Changing a trim setting needs `rm -rf Moirai.Wasm/bin obj`.** `dotnet publish` does not fully invalidate on it, and a mixed bundle fails in ways that look like a code bug.
- The published bundle is staged into `ClientAppSvelte/static/_framework` (gitignored) by `scripts/build-wasm.mjs`, along with `w.sg`. `static/` is copied verbatim rather than processed, so Vite never touches the runtime and `dotnet.js` keeps resolving its siblings relative to itself.
- **`yarn wasm:smoke`** (`scripts/smoke-wasm.mjs`) boots the *staged* bundle under Node — `dotnet.js` runs outside a browser — and exercises every export against `w.sg`. This exists because every failure mode here is quiet: trimming yields an empty JSON object rather than a build error, and a wrong `JsonSerializerOptions` renames every property so the client reads `undefined` everywhere. Neither shows up in a C# test, and both look like a blank page. It caught the shared-scratch-buffer bug that made any type with a `@display` attribute unqueryable.
- Determinism is verified across hosts: seed 42 + `w.sg` + 120 years gives year 884, 638 records, 253 entities and 854 changesets on **both** .NET and WebAssembly.

**Not available in the browser:** the DAP debugger, `.sg` hot reload, and the console profile report. All three stay server-only, which is why both backends coexist.

### Other projects
- **`vscode-languageserver/`** — C# LSP server (`Server/`, OmniSharp `LanguageServer`) + a TypeScript VS Code client. Builds on `Moirai.Parser`, so it tracks the language instead of lagging it.
  - `StoryParser.ParseForTooling` is the entry point: unlike `Parse` it hands back the full token list (trivia included), the surviving AST, the `Database` and the `AstVisitor`, and takes a factory for the `ILinker` that populates the symbol table during lowering.
  - `SourceLinker` *is* the symbol table (an interval tree of `MoiraiSymbol.Definition`), filled by `ILinker` callbacks rather than a separate walk. Go-to-definition, hover, find-references and the usage CodeLens all read it.
  - `MoiraiSemanticTokens` builds highlighting in three layers, each refining the last: token kind (keywords, operators, literals, comments), then the syntactic role of each identifier from the AST, then the linker's resolved symbol (which is what distinguishes an enum from an entity). See the `Layer` precedence note there — the AST anchors some variable declarations *on* a keyword, so the layers cannot simply overwrite each other.
  - `MoiraiFormatter` adjusts whitespace only. Structure comes from the AST, but the AST has no spans for punctuation, so anchors like `(` or the closing `}` are found by searching the token stream inside the owning node's span.
  - `MoiraiCompletion` works from the token stream, not the AST. The definition the caret sits in is the one that fails to parse — you are halfway through typing it — and with no error productions in the grammar, chunked recovery drops that whole definition, so the tree is empty exactly where completion is needed. The tokenizer never fails, so the caret's context comes from surrounding tokens (see the `Context` rule table) while the suggestions come from the definitions that *did* parse. Variables declared in the unparsed definition are recovered by scanning it for `$name`.
- **`TestProject1/`** — main NUnit suite for the engine/DSL (`EventTests`, `FilterTests`, `MatchTests`, `ParsingTests`, …). `WorldSessionTests` drives `Moirai.Api.WorldSession` the way a viewer does, with no server standing up — it is the safety net for the logic both hosts share, and it pins the wire format the client's hand-written TypeScript depends on. Plus the parser suites left over from the ANTLR→Superpower migration: `GrammarStructuralTests` (the corpus parses at all), `GrammarRuleTests` (one micro-fixture per grammar rule, so a regression localises to a rule instead of "somewhere in a 1000-line file"), `ChunkedErrorRecoveryTests` (one broken definition must not lose the rest of the file), and `TokenizerTests`/`ParserGoldenTests`. The last two are snapshot suites over the `.sg` corpus — token stream, printed `Database`, and the 50-year record history — and replace the differential suites that used to compare against the frozen ANTLR parser. Re-bless with `UPDATE_GOLDENS=1 dotnet test TestProject1`. The `.sg` files in `MoiraiCli/` (`w.sg`, `test.sg`, `space.sg`) are sample stories used for manual runs.
- **`Moirai.LanguageServer.Tests/`** — NUnit tests for the language server. Handlers are constructed directly against a primed `MoiraiCache`; no JSON-RPC transport is stood up. `LspGoldenTests` snapshots the formatter output and the semantic-token stream over the `.sg` corpus (re-bless with `UPDATE_GOLDENS=1 dotnet test Moirai.LanguageServer.Tests`) and additionally asserts that formatting is idempotent and preserves the program's meaning; `SyntaxHighlightingDriftTests` fails the build if a keyword added to `MoiraiTokenizer` has no example or goes unhighlighted; `CompletionSpec` covers what the caret should be offered at each kind of position, including inside a definition that does not parse.

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
