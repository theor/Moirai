/**
 * Where an edited story lives between page loads.
 *
 * The in-browser engine builds its world from a string, and that string is normally `/w.sg` fetched at
 * boot. Once you have edited it, it is your work, so it goes in **localStorage** rather than the
 * sessionStorage the world's identity uses: closing the tab should lose the world (it can be rebuilt from
 * seed and year in seconds) but not the story.
 *
 * Kept apart from `wasm-api.ts` because it is the one piece of this worth testing on its own, and because
 * both the backend (which prefers a stored story at boot) and the editor page (which saves as you type)
 * need it.
 */
const STORY_KEY = 'moirai.story';

/** The edited story, or null if the shipped one has never been changed. */
export function storedStory(): string | null {
  try {
    return window.localStorage.getItem(STORY_KEY);
  } catch {
    // Private browsing can refuse storage. Edits then last as long as the page does.
    return null;
  }
}

export function storeStory(text: string) {
  try {
    window.localStorage.setItem(STORY_KEY, text);
  } catch {
    // As above — nothing to do but let the edit be temporary.
  }
}

/** Forget the edit, so the next boot fetches the story the build shipped. */
export function clearStoredStory() {
  try {
    window.localStorage.removeItem(STORY_KEY);
  } catch {
    // As above.
  }
}
