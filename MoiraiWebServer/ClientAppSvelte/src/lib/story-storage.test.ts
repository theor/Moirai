import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { clearStoredStory, storedStory, storeStory } from './story-storage';

// `window` is faked rather than pulling in a DOM environment, as backend.test.ts does: the surface here
// is three localStorage calls, and stubbing them exactly says what the module depends on.
const store = new Map<string, string>();

const withStorage = (throwing = false) =>
  vi.stubGlobal('window', {
    localStorage: {
      getItem: (k: string) => {
        if (throwing) throw new Error('denied');
        return store.get(k) ?? null;
      },
      setItem: (k: string, v: string) => {
        if (throwing) throw new Error('denied');
        store.set(k, v);
      },
      removeItem: (k: string) => {
        if (throwing) throw new Error('denied');
        store.delete(k);
      },
    },
  });

beforeEach(() => store.clear());
afterEach(() => vi.unstubAllGlobals());

describe('story storage', () => {
  it('has nothing until a story is stored', () => {
    withStorage();
    expect(storedStory()).toBeNull();
  });

  it('round-trips a story', () => {
    withStorage();
    storeStory('event a {}');
    expect(storedStory()).toBe('event a {}');
  });

  it('forgets an edit when it is cleared', () => {
    withStorage();
    storeStory('event a {}');
    clearStoredStory();
    expect(storedStory()).toBeNull();
  });

  it('survives storage being refused', () => {
    // Private browsing throws on every call. The editor must still work, just not across reloads.
    withStorage(true);
    expect(() => storeStory('event a {}')).not.toThrow();
    expect(() => clearStoredStory()).not.toThrow();
    expect(storedStory()).toBeNull();
  });
});
