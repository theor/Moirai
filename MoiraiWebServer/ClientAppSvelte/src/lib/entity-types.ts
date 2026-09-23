import { derived, get, writable } from 'svelte/store';
import { moiraiStore, settledYear } from './connection';
import { typeSlots } from './entity-style';

/**
 * The id → type table every entity chip reads its colour from, with the slots already worked out.
 *
 * Refetched on the settled year, like every other whole-world query (see $lib/settled-year): a chip for
 * an entity born during a pass shows neutral until the pass settles, which is cheaper than asking the
 * engine once per feed tick. A reset or a reseed moves the year too, so ids never point at a stale table
 * for long.
 */
const table = writable<number[]>([]);

let started = false;
function start() {
  if (started) return;
  started = true;
  let asked = 0;
  settledYear.subscribe(() => {
    const conn = get(moiraiStore).conn;
    if (!conn) return;
    const ticket = ++asked;
    conn.getEntityTypes().then(
      (types) => {
        // A slower answer to an older question must not overwrite a newer one.
        if (ticket === asked) table.set(types);
      },
      (err: unknown) => console.error('getEntityTypes failed', err),
    );
  });
}

export type EntityTypes = { types: number[]; slots: Map<number, number> };

export const entityTypes = derived<typeof table, EntityTypes>(
  table,
  (types, set) => {
    start();
    set({ types, slots: typeSlots(types) });
  },
  { types: [], slots: new Map() },
);
