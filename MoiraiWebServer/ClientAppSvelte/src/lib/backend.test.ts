import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { chosenBackend } from './backend';

/**
 * The nav links do real navigation, so every page change is a fresh document with no query string. These
 * pin that a `?backend=` choice survives that — otherwise clicking through the app silently moves you to
 * the other backend, which is invisible whenever a server happens to be running.
 *
 * `window` is stubbed rather than pulling in a DOM environment: the two globals this reads are small
 * enough to fake exactly, and the test then says plainly what the function depends on.
 */
const store = new Map<string, string>();

const visit = (search: string) =>
  vi.stubGlobal('window', {
    location: { search },
    sessionStorage: {
      getItem: (k: string) => store.get(k) ?? null,
      setItem: (k: string, v: string) => void store.set(k, v),
    },
  });

beforeEach(() => store.clear());
afterEach(() => vi.unstubAllGlobals());

describe('chosenBackend', () => {
  it('defaults to the .NET host', () => {
    visit('');
    expect(chosenBackend()).toBe('signalr');
  });

  it('honours ?backend=wasm', () => {
    visit('?backend=wasm');
    expect(chosenBackend()).toBe('wasm');
  });

  it('remembers the choice once the query string is gone', () => {
    visit('?backend=wasm');
    expect(chosenBackend()).toBe('wasm');

    visit(''); // a nav link landed us on a fresh document
    expect(chosenBackend()).toBe('wasm');
  });

  it('lets a later query string override what was remembered', () => {
    visit('?backend=wasm');
    chosenBackend();

    visit('?backend=signalr');
    expect(chosenBackend()).toBe('signalr');

    visit('');
    expect(chosenBackend()).toBe('signalr');
  });

  it('ignores a nonsense value rather than trusting it', () => {
    visit('?backend=banana');
    expect(chosenBackend()).toBe('signalr');
  });

  it('still works when storage is unavailable, as in private browsing', () => {
    vi.stubGlobal('window', {
      location: { search: '?backend=wasm' },
      sessionStorage: {
        getItem: () => {
          throw new Error('denied');
        },
        setItem: () => {
          throw new Error('denied');
        },
      },
    });
    expect(chosenBackend()).toBe('wasm');
  });
});
