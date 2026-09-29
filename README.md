# Moirai

**Moirai is a language for the procedural history of worlds.** A story file declares the kinds of things a
world holds (people, places, titles), the events that happen to them on a schedule, and the rules that react
when something changes. The engine then simulates it year by year and writes the world's history as it goes:
a chronicle of births, marriages, successions and wars, plus a world state you can query and browse.

- **Try it in your browser:** <https://theor.github.io/Moirai/>. The whole engine runs client-side as
  WebAssembly, so there is nothing to install.
- **Read about it:** [Moirai: a language for procedural history of worlds](https://theor.xyz/moirai-a-language-for-procedural-history-of-worlds)

A taste of the language, from [`MoiraiCli/w.sg`](MoiraiCli/w.sg):

```
@frequency(1, PerXYear, 2)
event wedding {
    pick Person $x: (alive = true and partner = null and age > Age.Child and age < Age.Old)
    pick Person $y: (alive = true and partner = null and age > Age.Child and age < Age.Old and $y != $x and place = $x.place and not(related($x, $y, 6)))
    set $x.partner = $y
    set $y.partner = $x
    record('{$x.name} and {$y.name} get married')
}
```

A simulation is deterministic: the same story and the same seed always give the same history.

To write a story, start with [the language guide](docs/language.md). For each built-in function and
attribute, see [the language reference](docs/language-reference.md), which is generated from the engine's
code.

## Running it

**From a release.** Download `moirai-<version>.zip` from
[Releases](https://github.com/theor/Moirai/releases), unzip it and run `run.cmd` (Windows) or `./run.sh`
(Linux/macOS), then open <http://localhost:5000>. This requires the
[ASP.NET Core 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0). By default it loads the bundled
`w.sg`. To load another story, pass its path (`run.cmd my-story.sg`). The server watches the file and rebuilds
the world whenever you save it.

**From source.** This requires the .NET 10 SDK, Node and yarn classic.

```sh
cd MoiraiWebServer/ClientAppSvelte && yarn install && cd ../..
dotnet run --project MoiraiWebServer -- MoiraiCli/w.sg
```

In development, the server starts the Svelte dev server itself and proxies to it. Useful flags:
- `--seed 1234` picks the RNG seed.
- `--profile` prints a per-rule timing report to the console after every pass.
- `--debug-port 4711` opens a Debug Adapter Protocol endpoint for stepping through rules from VS Code.

## What's in the repo

| Path | What it is |
|---|---|
| `Moirai/` | The simulation engine: world state, scheduling, effects, predicates, the change history. |
| `Moirai.Parser/` | The `.sg` language front end: a hand-written tokenizer and Superpower grammar that lower to engine objects. |
| `Moirai.Api/` | `WorldSession`, which holds one world and answers every question a viewer can ask of it. Both hosts are thin shims over it. |
| `MoiraiWebServer/` | The ASP.NET Core host (SignalR) and the SvelteKit viewer in `ClientAppSvelte/`. |
| `Moirai.Wasm/` | The engine compiled to WebAssembly for the serverless GitHub Pages site. It is not in the solution (see below). |
| `Moirai.DebugAdapter/` | A step-through debugger for stories, speaking DAP. |
| `vscode-languageserver/` | VS Code extension: a C# language server (highlighting, completion, go-to-definition, formatting) and its TypeScript client. |
| `MoiraiCli/` | Sample stories. `w.sg` is the canonical large one. |
| `Moirai.Tests/`, `Moirai.LanguageServer.Tests/` | NUnit suites for the engine and language, and for the language server. |
| `Moirai.Benchmarks/` | BenchmarkDotNet benchmarks over `w.sg`. |

[`CLAUDE.md`](CLAUDE.md) has the detailed architecture notes.

## Testing

```sh
dotnet test                                        # everything
dotnet test --filter "FullyQualifiedName~EventTests"  # one class
```

Several suites are golden snapshots: token streams, the printed world, the record history, formatter output.
After an intended change, re-bless them with `UPDATE_GOLDENS=1 dotnet test`.

For the viewer, run these inside `MoiraiWebServer/ClientAppSvelte/`:

```sh
yarn test    # vitest
yarn check   # svelte-check
yarn lint    # prettier + eslint
```

For the WebAssembly build, first run `dotnet workload install wasm-tools wasm-experimental` (once). Then:

```sh
yarn wasm:build   # build the engine and stage it into static/
yarn wasm:smoke   # boot the staged engine under Node and exercise every export
yarn dev          # then open http://localhost:3000/records?backend=wasm
```

CI (`.github/workflows/dotnet.yml`) builds, tests and publishes the server on every push to `main` and on
every pull request.

## Releasing

Push a `v*` tag:

```sh
git tag -a v0.2.0 -m v0.2.0
git push origin v0.2.0
```

CI runs the tests, publishes the server with the built viewer, and attaches `moirai-v0.2.0.zip` to a GitHub
release for that tag. The zip is framework-dependent, so a single zip works on every OS. The GitHub Pages site
does not need a tag: `.github/workflows/pages.yml` redeploys it on every push to `main`.
