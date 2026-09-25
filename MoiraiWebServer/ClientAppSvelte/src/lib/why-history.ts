import { replaceState } from '$app/navigation';
import { withWhy } from './why';

/**
 * Record the open "why?" (or its closing) on the current history entry, so Back finds it again. Kept
 * apart from `$lib/why` because it needs the router, which the unit tests do not have.
 *
 * SvelteKit's replaceState rather than the browser's, like the layout's own address writer, so the
 * router's idea of the current entry stays the one the browser has.
 */
export function rememberWhy(firing: number | null) {
  const next = withWhy(new URL(window.location.href), firing);
  // `next` is the current absolute URL with one parameter changed, so it is already resolved; the rule
  // only recognises a literal resolve() call (see the layout's address writer).
  // eslint-disable-next-line svelte/no-navigation-without-resolve
  if (next.href !== window.location.href) replaceState(next, {});
}
