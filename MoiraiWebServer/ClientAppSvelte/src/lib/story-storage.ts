/**
 * Where an edited story lives between page loads.
 *
 * The in-browser engine builds its world from a string, and that string is normally `/w.sg` fetched at
 * boot. Once you have edited it, it is your work, so it goes in **localStorage** rather than the
 * sessionStorage the world's identity uses: closing the tab should lose the world (it can be rebuilt from
 * seed and year in seconds) but not the story.
 *
 * **Two stories, kept apart on purpose.** The *draft* is whatever is in the editor, saved as you type, so
 * it is routinely half-written and does not parse. The *applied* story is one the engine accepted, and it
 * is the only one a page load builds a world from. They used to be one key, which meant closing the tab
 * mid-edit left a draft the next boot tried to build — and a story with no clock in it locked the app on
 * "Starting the engine…" at every reload, with the editor that could fix it unreachable behind it.
 *
 * The draft keeps the old key, so a draft saved before the split is still there to edit; it just no
 * longer decides what world you boot into.
 *
 * Kept apart from `wasm-api.ts` because it is the one piece of this worth testing on its own, and because
 * both the backend (which boots from the applied story) and the editor page (which saves the draft as you
 * type) need it.
 */
const DRAFT_KEY = 'moirai.story';
const APPLIED_KEY = 'moirai.story.applied';

function read(key: string): string | null {
  try {
    return window.localStorage.getItem(key);
  } catch {
    // Private browsing can refuse storage. Edits then last as long as the page does.
    return null;
  }
}

function write(key: string, text: string) {
  try {
    window.localStorage.setItem(key, text);
  } catch {
    // As above — nothing to do but let the edit be temporary.
  }
}

function remove(key: string) {
  try {
    window.localStorage.removeItem(key);
  } catch {
    // As above.
  }
}

/** The story the engine last accepted, or null if the shipped one is in use. What a page load builds. */
export function storedStory(): string | null {
  return read(APPLIED_KEY);
}

export function storeStory(text: string) {
  write(APPLIED_KEY, text);
}

/** Forget the applied story, so the next boot builds the one the build shipped. */
export function clearStoredStory() {
  remove(APPLIED_KEY);
}

/** What was in the editor, parsed or not, or null if it matches the world's story. */
export function storedDraft(): string | null {
  return read(DRAFT_KEY);
}

export function storeDraft(text: string) {
  write(DRAFT_KEY, text);
}

export function clearDraft() {
  remove(DRAFT_KEY);
}
