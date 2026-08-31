import { get, writable } from 'svelte/store';
import type { MoiraiApi, MoiraiStreamSubscriber } from './api';
import { createSettledYear } from './settled-year';
import { createApi } from './backend';
import {
  type ClientData,
  type EntityChangeDisplay,
  type Message,
  MessageType,
  type Record,
} from './types';

// Re-exported so the pages that have always imported these from here keep working. The definitions
// moved to $lib/types (the wire contract) and $lib/api (the transport-independent surface).
export type { MoiraiApi } from './api';
export type { EntityChangeDisplay, FamilyTreeNode, QueryResult, Result } from './types';

interface State {
  year: number;
  connected: boolean;
  /** Undefined until a backend has connected. Pages gate their first fetch on this. */
  conn?: MoiraiApi;
  records: Record[];
  changesets: EntityChangeDisplay[];
  clientData?: ClientData;
  /**
   * How far a running `passYears` has got, as a **percentage** (0-100), or undefined when none is
   * running. Both backends report percent — the server because it streams `100 * i / years`, the browser
   * because it counts chunks — so a progress bar must use a max of 100, not the number of years asked
   * for. It read as years once, and the bar filled to a third on a 300-year pass.
   */
  passYearsPercent?: number;
  /** Set when connecting failed outright, so the UI can say so instead of waiting forever. */
  connectionError?: string;
  keyboardEvent?: KeyboardEvent;
}

// The year a hot reload interrupted, reported by the backend so the UI could offer to re-simulate to it.
let _targetYear: number = 0;

const writableStore = writable<State>(
  {
    year: 0,
    passYearsPercent: undefined,
    connected: false,
    records: [],
    changesets: [],
  },
  (set, update) => {
    createApi().then(
      ({ api, clientData, connected }) => {
        api.onConnectedChanged((c) => update((x) => ({ ...x, connected: c })));

        // Records arrive one per message but can arrive in bursts of thousands after a long pass, so
        // they are batched into the store on a timer rather than triggering a render each.
        let buffer: Record[] = [];
        setInterval(() => {
          if (buffer.length > 0) {
            update((x) => ({ ...x, records: [...x.records, ...buffer] }));
            buffer = [];
          }
        }, 500);

        api.streamRecords().subscribe({
          next(value: Message) {
            switch (value.type) {
              case MessageType.Reset:
                if (value.year !== 0) {
                  _targetYear = value.year;
                }
                update((x) => ({ ...x, year: 0, records: [] }));
                break;
              case MessageType.Record:
                buffer.push(value.record!);
                break;
              case MessageType.Year:
                if (value.year !== get(writableStore).year) {
                  update((x) => ({ ...x, year: value.year }));
                }
                if (_targetYear !== 0) {
                  _targetYear = 0;
                }
                break;
              default:
                console.error('UNKNOWN MESSAGE TYPE', value.type);
            }
          },
          error(err: unknown) {
            console.error(err);
          },
          complete() {},
        });

        update((s) => ({ ...s, conn: api, connected, clientData }));
      },
      // Without this the failure is an unhandled rejection and `conn` stays undefined forever, so every
      // page sits on its loading state with nothing to explain why.
      (err: unknown) => {
        console.error('Could not connect to a Moirai engine', err);
        update((x) => ({ ...x, connected: false, connectionError: String(err) }));
      },
    );
  },
);

/**
 * The live backend, or undefined while one is still connecting.
 *
 * The app bar disables its controls in that window, but a keyboard shortcut or a click that lands during
 * the gap can still get here — and with the WebAssembly backend the gap is seconds rather than
 * milliseconds, because it has a runtime to start and possibly a world to rebuild. Asserting non-null
 * here used to turn that race into an uncaught TypeError.
 */
const backend = () => get(writableStore).conn;

export const moiraiStore = {
  ...writableStore,
  reset: async () => {
    const conn = backend();
    if (!conn) return;
    const newYear = await conn.reset();
    writableStore.update((x) => ({ ...x, year: newYear, records: [] }));
  },
  // Rebuild the world from a different seed. The engine is deterministic per seed, so this is the
  // one knob that produces a genuinely different world from the same story file.
  reseed: async (seed: number) => {
    const conn = backend();
    if (!conn) return;
    const newYear = await conn.reseed(seed);
    writableStore.update((x) => ({
      ...x,
      year: newYear,
      records: [],
      clientData: x.clientData ? { ...x.clientData, seed } : x.clientData,
    }));
  },
  passYears: (amount: number) => {
    const subscriber: MoiraiStreamSubscriber<number> = {
      next(value: number) {
        writableStore.update((x) => ({ ...x, passYearsPercent: value }));
      },
      error(err: unknown) {
        console.error(err);
        writableStore.update((x) => ({ ...x, passYearsPercent: undefined }));
      },
      complete() {
        writableStore.update((x) => ({ ...x, passYearsPercent: undefined }));
      },
    };
    const conn = backend();
    if (!conn) return undefined;
    writableStore.update((x) => ({ ...x, passYearsPercent: 0 }));
    return conn.passYears(amount).subscribe(subscriber);
  },
  clearEvent: () => writableStore.update((x) => ({ ...x, keyboardEvent: undefined })),
  handleKeyPress: (e: KeyboardEvent): void => {
    writableStore.update((x) => ({ ...x, keyboardEvent: e }));
  },
  toggleActionFiltering: (id: number, active: boolean, switchAll: boolean) => {
    const clientData = get(writableStore).clientData!;
    if (switchAll) {
      writableStore.update((x) => ({
        ...x,
        clientData: {
          ...clientData,
          actions: clientData.actions.map((a) =>
            a.id === id ? { ...a, hidden: !active } : { ...a, hidden: active },
          ),
        },
      }));
      return;
    }
    clientData.actions[id - 1].hidden = !clientData.actions[id - 1].hidden;
    writableStore.update((x) => ({ ...x, clientData: { ...clientData } }));
  },
  getChangesets: (start: number, count: number) => {
    // An empty page rather than a throw: the infinite query will ask again once the backend is up.
    return backend()?.getChangesets(start, count) ?? Promise.resolve([]);
  },
};

/**
 * The simulation year, once it has stopped moving. Pages that re-query the world on every year change
 * should read this rather than `year`: with the in-browser engine those queries are synchronous work on
 * the thread that is trying to paint, and a pass changes the year continuously. See settled-year.ts.
 */
export const settledYear = createSettledYear(writableStore);
