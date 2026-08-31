import { readable, type Readable } from 'svelte/store';

/** What {@link createSettledYear} needs from the store — kept narrow so it is trivial to test. */
export type YearSource = Readable<{ year: number; passYearsPercent?: number; conn?: unknown }>;

/** Quiet period before a changed year is reported. */
const DEFAULT_DELAY_MS = 400;

/**
 * Longest a change can be withheld. Without this, a pass that reports a new year every hundred
 * milliseconds would never go quiet and the pages would show nothing until it ended.
 */
const DEFAULT_MAX_WAIT_MS = 1500;

/**
 * The simulation year, reported only once it has stopped moving.
 *
 * Every page re-queries the world when the year changes — rule coverage, a property series, an entity's
 * changesets — and with the WebAssembly engine those queries run synchronously on the same thread that is
 * trying to paint. A pass advances the year continuously, so subscribing to it directly means one full
 * re-query per feed tick, each one competing with the simulation for the main thread.
 *
 * So this coalesces: a change is held for {@link DEFAULT_DELAY_MS} of quiet, but never longer than
 * {@link DEFAULT_MAX_WAIT_MS}, which keeps a long pass visibly progressing rather than frozen. When a
 * pass ends the pending value is flushed at once, so the settled state is never stale.
 *
 * Nothing is reported until a backend exists, which gives every page exactly one trigger at startup
 * rather than one for the empty world and another for the real one.
 */
export function createSettledYear(
  source: YearSource,
  options: { delayMs?: number; maxWaitMs?: number } = {},
): Readable<number> {
  const delayMs = options.delayMs ?? DEFAULT_DELAY_MS;
  const maxWaitMs = options.maxWaitMs ?? DEFAULT_MAX_WAIT_MS;

  return readable(0, (set) => {
    /** The last value handed to subscribers, or null before the first one. */
    let reported: number | null = null;
    /** A year seen but not yet reported. */
    let pending: number | null = null;
    let timer: ReturnType<typeof setTimeout> | null = null;
    let pendingSince = 0;
    let wasRunning = false;

    const clear = () => {
      if (timer !== null) clearTimeout(timer);
      timer = null;
    };

    const flush = () => {
      clear();
      if (pending !== null && pending !== reported) {
        reported = pending;
        set(reported);
      }
      pending = null;
    };

    const unsubscribe = source.subscribe((state) => {
      // Before a backend answers there is no world to query, so there is nothing worth reporting.
      if (state.conn === undefined) return;

      const running = state.passYearsPercent !== undefined;

      if (reported === null) {
        reported = state.year;
        wasRunning = running;
        set(reported);
        return;
      }

      if (state.year !== reported) {
        if (pending === null) pendingSince = Date.now();
        pending = state.year;
      }

      // A pass ending is the one moment we know the number has stopped for good.
      const passJustEnded = wasRunning && !running;
      wasRunning = running;
      if (passJustEnded) {
        flush();
        return;
      }

      if (pending === null) return;

      clear();
      const waited = Date.now() - pendingSince;
      timer = setTimeout(flush, Math.max(0, Math.min(delayMs, maxWaitMs - waited)));
    });

    return () => {
      clear();
      unsubscribe();
    };
  });
}
