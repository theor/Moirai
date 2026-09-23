import { get, readable } from 'svelte/store';
import { moiraiStore, settledYear } from './connection';
import type { NotableGroup } from './types';

const PER_TYPE = 5;

/**
 * The most mentioned entities of each type, for the pages that need a place to start: Home, and the
 * Life and Family pages before anything is selected.
 *
 * One store rather than a fetch per page, because it counts every record in the world. Refetched on the
 * settled year (see $lib/settled-year), and only while some page is showing it.
 */
export const notable = readable<NotableGroup[]>([], (set) => {
  let asked = 0;
  return settledYear.subscribe(() => {
    const conn = get(moiraiStore).conn;
    if (!conn) return;
    const ticket = ++asked;
    conn.getNotable(PER_TYPE).then(
      (groups) => {
        // A slower answer to an older question must not overwrite a newer one.
        if (ticket === asked) set(groups);
      },
      (err: unknown) => console.error('getNotable failed', err),
    );
  });
});
