// Publishes the Moirai engine to WebAssembly and stages it where the SPA can serve it.
//
// The output lands in `static/`, not `src/`, on purpose: files there are copied verbatim rather than
// processed, so none of Vite's asset handling touches the runtime — and `dotnet.js` keeps resolving its
// siblings relative to itself, which is how it expects to find the assemblies and the .wasm.
import { execFileSync } from 'node:child_process';
import { cpSync, existsSync, mkdirSync, rmSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const clientRoot = resolve(here, '..');
const repoRoot = resolve(clientRoot, '..', '..');

const project = join(repoRoot, 'Moirai.Wasm', 'Moirai.Wasm.csproj');
const appBundle = join(
  repoRoot,
  'Moirai.Wasm',
  'bin',
  'Release',
  'net10.0-browser',
  'browser-wasm',
  'AppBundle',
);
const staticDir = join(clientRoot, 'static');

console.log('Publishing Moirai.Wasm…');
execFileSync('dotnet', ['publish', project, '-c', 'Release'], { stdio: 'inherit', cwd: repoRoot });

const framework = join(appBundle, '_framework');
if (!existsSync(framework)) {
  console.error(`Expected a published runtime at ${framework}, but it is not there.`);
  process.exit(1);
}

const target = join(staticDir, '_framework');
rmSync(target, { recursive: true, force: true });
mkdirSync(staticDir, { recursive: true });

// Source maps are for stepping through the runtime's own JavaScript, which a visitor never does. They
// are safe to leave behind because nothing references them but devtools — unlike the native symbol map,
// which is listed in the boot manifest and so has to be suppressed at publish time instead (see
// WasmEmitSymbolMap in Moirai.Wasm.csproj).
const DEBUG_ONLY = /\.map$/;
cpSync(framework, target, { recursive: true, filter: (src) => !DEBUG_ONLY.test(src) });

// The engine's boot script sits alongside the runtime it imports.
cpSync(join(appBundle, 'main.js'), join(target, 'main.js'));

// The world the browser builds. The server reads this same file from disk.
cpSync(join(repoRoot, 'MoiraiCli', 'w.sg'), join(staticDir, 'w.sg'));

console.log(`Staged the WebAssembly engine in ${target} and the story in ${staticDir}/w.sg`);
