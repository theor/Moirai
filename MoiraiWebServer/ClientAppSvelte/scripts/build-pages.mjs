// Builds the static site for GitHub Pages: the SPA, the WebAssembly engine, and the story, with no
// server anywhere in it.
//
// Two things make a Pages deployment different from `yarn build`, and both are silent failures if
// missed. First, a project site is served under the repository name, so every absolute URL needs that
// prefix — SvelteKit handles its own with `paths.base`, and the three the app hardcodes read `base`
// (see wasm-api.ts). Second, Pages has no SPA rewrite: a deep link like /Moirai/story is a file that
// does not exist, and the server answers with 404.html — so the fallback page is copied there, which is
// what makes the app's own router pick the request up.
import { execFileSync } from 'node:child_process';
import { copyFileSync, existsSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const clientRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const build = join(clientRoot, 'build');

// The engine is staged into static/ by `yarn wasm:build`, and it is gitignored, so a fresh checkout has
// none. Without this the site builds perfectly and then shows a blank page in the browser.
if (!existsSync(join(clientRoot, 'static', '_framework', 'main.js'))) {
  console.error('No WebAssembly engine in static/_framework. Run `yarn wasm:build` first.');
  process.exit(1);
}

// The repository name, because that is the path a GitHub Pages project site is served under. Overridable
// so a custom domain — which serves from the root — is one empty environment variable away.
const basePath = process.env.BASE_PATH ?? '/Moirai';

console.log(`Building the site for a base path of '${basePath}'…`);
execFileSync(
  process.execPath,
  [join(clientRoot, 'node_modules', 'vite', 'bin', 'vite.js'), 'build'],
  {
    cwd: clientRoot,
    stdio: 'inherit',
    env: { ...process.env, BASE_PATH: basePath, VITE_MOIRAI_BACKEND: 'wasm' },
  },
);

copyFileSync(join(build, 'index.html'), join(build, '404.html'));
console.log(`Wrote ${build}, with 404.html as the SPA fallback.`);
