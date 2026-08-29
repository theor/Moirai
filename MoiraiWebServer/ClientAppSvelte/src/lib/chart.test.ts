import { describe, it, expect } from 'vitest';
import { compact, nearestIndex, niceTicks } from './chart';

describe('niceTicks', () => {
  it('lands on 1/2/5 times a power of ten', () => {
    expect(niceTicks(97)).toEqual([0, 50, 100]);
    expect(niceTicks(23)).toEqual([0, 10, 20, 30]);
    expect(niceTicks(4)).toEqual([0, 2, 4]);
  });

  it('always covers the maximum, so the line fits under the top gridline', () => {
    for (const max of [1, 3, 7, 42, 99, 100, 101, 1234, 999999]) {
      const ticks = niceTicks(max);
      expect(ticks[ticks.length - 1]).toBeGreaterThanOrEqual(max);
    }
  });

  it('does not spill an extra tick when the max is exactly a multiple of the step', () => {
    expect(niceTicks(100)).toEqual([0, 50, 100]);
  });

  it('gives a usable domain for an all-zero or empty series', () => {
    expect(niceTicks(0)).toEqual([0, 1]);
    expect(niceTicks(Number.NaN)).toEqual([0, 1]);
  });

  it('starts at zero, because a truncated baseline lies about magnitude', () => {
    expect(niceTicks(500)[0]).toBe(0);
  });
});

describe('compact', () => {
  it('leaves readable numbers alone', () => {
    expect(compact(0)).toBe('0');
    expect(compact(1284)).toBe('1,284');
  });

  it('abbreviates only past ten thousand, so four-digit counts stay exact', () => {
    expect(compact(9999)).toBe('9,999');
    expect(compact(12900)).toBe('12.9K');
    expect(compact(4200000)).toBe('4.2M');
  });

  it('drops a trailing .0', () => {
    expect(compact(12000)).toBe('12K');
  });

  it('keeps precision on small fractions, where a mean actually lives', () => {
    expect(compact(0.375)).toBe('0.38');
    expect(compact(63.42)).toBe('63.4');
  });
});

describe('nearestIndex', () => {
  it('snaps to the closest sample', () => {
    expect(nearestIndex(0, 5)).toBe(0);
    expect(nearestIndex(1, 5)).toBe(4);
    expect(nearestIndex(0.5, 5)).toBe(2);
  });

  it('clamps a pointer dragged outside the plot', () => {
    expect(nearestIndex(-0.4, 5)).toBe(0);
    expect(nearestIndex(1.4, 5)).toBe(4);
  });

  it('handles a single-sample series', () => {
    expect(nearestIndex(0.7, 1)).toBe(0);
  });
});
