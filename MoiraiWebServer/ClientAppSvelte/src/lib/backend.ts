import type { MoiraiApiHandle } from './api';
import { decompress, readAddress, readStoryParam } from './world-address';

export type BackendKind = 'signalr' | 'wasm';

/** Where a `?backend=` choice is remembered for the rest of the tab's life. */
const STORAGE_KEY = 'moirai.backend';

const isKind = (v: string | null): v is BackendKind => v === 'wasm' || v === 'signalr';

/**
 * Which engine to talk to. `?backend=wasm` wins, so a single deployed build can be pointed either way
 * from the address bar; otherwise `VITE_MOIRAI_BACKEND` sets the default at build time, and failing that
 * we assume the .NET host, which is how the dev loop runs.
 *
 * The choice is remembered for the tab, which is load-bearing rather than a convenience: the nav links
 * do real navigation, so every page change is a fresh document with no query string. Without this,
 * clicking "World" after opening `?backend=wasm` would silently drop you onto the other backend — and if
 * a server happened to be running you would not even notice you had switched worlds.
 */
export function chosenBackend(): BackendKind {
  if (typeof window === 'undefined') return 'signalr';

  const q = new URLSearchParams(window.location.search).get('backend');
  if (isKind(q)) {
    try {
      window.sessionStorage.setItem(STORAGE_KEY, q);
    } catch {
      // Private browsing can refuse storage; the query param still works for this page.
    }
    return q;
  }

  try {
    const remembered = window.sessionStorage.getItem(STORAGE_KEY);
    if (isKind(remembered)) return remembered;
  } catch {
    // Ignore and fall through to the build-time default.
  }

  const env = import.meta.env.VITE_MOIRAI_BACKEND;
  return env === 'wasm' ? 'wasm' : 'signalr';
}

/**
 * Connect to whichever backend is selected. Both are loaded lazily so that a WASM-only deployment does
 * not ship the SignalR client, and a server deployment does not ship the worker.
 */
export async function createApi(): Promise<MoiraiApiHandle> {
  if (chosenBackend() === 'wasm') {
    const { WasmApi } = await import('./wasm-api');
    // The URL is where a world's identity lives, and this is the one place it is read. A link with no
    // year gets a couple of centuries of history rather than an empty world — see WasmApi.openTo.
    const { seed, year } = readAddress(new URL(window.location.href));
    return WasmApi.make({
      seed: seed ?? undefined,
      year,
      story: await storyFromLink(),
    });
  }
  const { SignalRApi } = await import('./signalr-api');
  return SignalRApi.make();
}

/**
 * The story a link carried, if it carried one. A corrupt fragment is ignored rather than fatal: the
 * visitor still gets a world, just not the sender's, which beats a blank page with a decoding error.
 */
async function storyFromLink(): Promise<string | undefined> {
  const param = readStoryParam(window.location.hash);
  if (param === null) return undefined;
  try {
    return await decompress(param);
  } catch (err) {
    console.error('The story in this link could not be read', err);
    return undefined;
  }
}
