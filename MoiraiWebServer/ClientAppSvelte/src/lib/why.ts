/**
 * The open "why?" lives in the URL, as `?why=<firing>` on the page's own history entry.
 *
 * Page state alone does not survive the obvious next step: follow a step's line into the Story page,
 * press Back, and the Records or Life page comes back as a fresh page with no idea which record you had
 * asked about. With the firing in the URL, Back reopens the same chain and scrolls to its record. It is
 * written with replaceState, not a new entry, so opening and closing the dialog never adds to the back
 * button -- only leaving the page does.
 */
const WHY = 'why';

/** The firing a URL's query names, or null for none (or nonsense). */
export function readWhy(search: string): number | null {
  const n = Number(new URLSearchParams(search).get(WHY));
  return Number.isInteger(n) && n > 0 ? n : null;
}

/** `url` with its `why` set to `firing`, or removed for null. Everything else in the query is kept. */
export function withWhy(url: URL, firing: number | null): URL {
  const next = new URL(url.href);
  if (firing === null) next.searchParams.delete(WHY);
  else next.searchParams.set(WHY, String(firing));
  return next;
}
