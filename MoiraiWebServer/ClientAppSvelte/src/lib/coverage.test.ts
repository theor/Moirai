import { describe, it, expect } from 'vitest';
import { hitRate, problemCounts, sortRules, statusOf } from './coverage';
import type { RuleCoverage } from './types';

const rule = (name: string, attempts: number, successes: number): RuleCoverage => ({
  id: name.length,
  name,
  kind: 'event',
  schedule: 'call only',
  attempts,
  successes,
  tags: [],
});

describe('statusOf', () => {
  it('calls a rule that was never invoked never-ran', () => {
    expect(statusOf(rule('a', 0, 0))).toBe('never-ran');
  });

  it('calls a rule that always aborted never-completed', () => {
    expect(statusOf(rule('a', 1275, 0))).toBe('never-completed');
  });

  it('calls a rule that completed at least once ok, however rarely', () => {
    expect(statusOf(rule('a', 1275, 1))).toBe('ok');
  });
});

describe('hitRate', () => {
  it('is zero rather than NaN when a rule never ran', () => {
    expect(hitRate(rule('a', 0, 0))).toBe(0);
  });

  it('is successes over attempts', () => {
    expect(hitRate(rule('a', 50, 20))).toBe(0.4);
  });
});

describe('sortRules', () => {
  it('puts never-ran first, then never-completed, then the rest', () => {
    const sorted = sortRules([rule('ok', 10, 10), rule('aborts', 99, 0), rule('dead', 0, 0)]);
    expect(sorted.map((r) => r.name)).toEqual(['dead', 'aborts', 'ok']);
  });

  it('orders rules of equal status by attempts, busiest first', () => {
    const sorted = sortRules([rule('quiet', 5, 5), rule('busy', 500, 5)]);
    expect(sorted.map((r) => r.name)).toEqual(['busy', 'quiet']);
  });

  it('breaks ties by name so the table does not reshuffle between refreshes', () => {
    const sorted = sortRules([rule('b', 7, 7), rule('a', 7, 7)]);
    expect(sorted.map((r) => r.name)).toEqual(['a', 'b']);
  });

  it('does not mutate its input', () => {
    const input = [rule('ok', 10, 10), rule('dead', 0, 0)];
    sortRules(input);
    expect(input.map((r) => r.name)).toEqual(['ok', 'dead']);
  });
});

describe('problemCounts', () => {
  it('counts the two problem kinds separately', () => {
    const counts = problemCounts([
      rule('dead1', 0, 0),
      rule('dead2', 0, 0),
      rule('aborts', 12, 0),
      rule('ok', 3, 1),
    ]);
    expect(counts).toEqual({ neverRan: 2, neverCompleted: 1 });
  });
});
