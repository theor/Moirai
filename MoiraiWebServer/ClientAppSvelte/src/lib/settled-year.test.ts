import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { get, writable } from 'svelte/store';
import { createSettledYear, type YearSource } from './settled-year';

type State = { year: number; passYearsPercent?: number; conn?: unknown };

const CONN = {}; // any non-undefined value means "a backend answered"

const harness = (options?: { delayMs?: number; maxWaitMs?: number }) => {
  const source = writable<State>({ year: 0 });
  const settled = createSettledYear(source as YearSource, options);
  const seen: number[] = [];
  const stop = settled.subscribe((y) => seen.push(y));
  return { source, seen, stop, settled };
};

beforeEach(() => vi.useFakeTimers());
afterEach(() => vi.useRealTimers());

describe('createSettledYear', () => {
  it('reports nothing until a backend exists', () => {
    const { source, seen } = harness();
    expect(seen).toEqual([0]); // the store's own initial value

    source.set({ year: 764 });
    vi.advanceTimersByTime(5000);
    expect(seen).toEqual([0]);
  });

  it('reports the first year at once, without waiting', () => {
    const { source, seen } = harness();
    source.set({ year: 764, conn: CONN });
    expect(seen).toEqual([0, 764]);
  });

  it('coalesces a burst of years into a single report', () => {
    const { source, seen } = harness();
    source.set({ year: 764, conn: CONN });

    for (const year of [765, 766, 767, 768]) {
      source.set({ year, conn: CONN, passYearsPercent: 10 });
      vi.advanceTimersByTime(50);
    }
    expect(seen).toEqual([0, 764]); // nothing yet — still moving

    vi.advanceTimersByTime(400);
    expect(seen).toEqual([0, 764, 768]); // one report, the latest value
  });

  it('keeps reporting during a long pass rather than going silent', () => {
    // Without a maximum wait, a pass that ticks faster than the quiet period would never settle and the
    // pages would show nothing at all until it finished.
    const { source, seen } = harness({ delayMs: 400, maxWaitMs: 1000 });
    source.set({ year: 764, conn: CONN });

    for (let i = 1; i <= 40; i++) {
      source.set({ year: 764 + i, conn: CONN, passYearsPercent: i });
      vi.advanceTimersByTime(100);
    }

    // 4 s of continuous change at a 1 s ceiling: several reports, far fewer than the 40 changes.
    expect(seen.length).toBeGreaterThan(3);
    expect(seen.length).toBeLessThan(10);
    expect(seen).toEqual([...seen].sort((a, b) => a - b));
  });

  it('flushes immediately when a pass ends', () => {
    const { source, seen } = harness();
    source.set({ year: 764, conn: CONN });
    source.set({ year: 900, conn: CONN, passYearsPercent: 90 });
    expect(seen).toEqual([0, 764]);

    // The pass completes: passYearsPercent goes away.
    source.set({ year: 1064, conn: CONN });
    expect(seen).toEqual([0, 764, 1064]);
  });

  it('does not report the same year twice', () => {
    const { source, seen } = harness();
    source.set({ year: 764, conn: CONN });
    source.set({ year: 764, conn: CONN, passYearsPercent: 5 });
    source.set({ year: 764, conn: CONN });
    vi.advanceTimersByTime(2000);
    expect(seen).toEqual([0, 764]);
  });

  it('reports a reset backwards, not just forwards', () => {
    const { source, seen } = harness();
    source.set({ year: 764, conn: CONN });
    source.set({ year: 1064, conn: CONN });
    vi.advanceTimersByTime(400);
    source.set({ year: 764, conn: CONN }); // Reset rebuilt the world
    vi.advanceTimersByTime(400);
    expect(seen).toEqual([0, 764, 1064, 764]);
  });

  it('drops a pending report when the last subscriber leaves', () => {
    const { source, seen, stop } = harness();
    source.set({ year: 764, conn: CONN });
    source.set({ year: 900, conn: CONN });
    stop();
    vi.advanceTimersByTime(5000);
    expect(seen).toEqual([0, 764]);
  });

  it('starts clean for a later subscriber', () => {
    const { source, stop, settled } = harness();
    source.set({ year: 764, conn: CONN });
    stop();

    source.set({ year: 1200, conn: CONN });
    expect(get(settled)).toBe(1200);
  });
});
