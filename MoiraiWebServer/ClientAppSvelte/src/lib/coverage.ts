import type { RuleCoverage } from './types';

/**
 * How a rule reads on the Rules page.
 *
 * `never-ran` means the engine never invoked it: for a scheduled event its frequency never came due,
 * for a trigger nothing it watches ever changed (or property gating always excluded it).
 * `never-completed` means it always aborted — an event whose `pick` finds nothing, or a trigger whose
 * predicate never matched. Neither shows up in the records feed, because neither emits records.
 */
export type RuleStatus = 'never-ran' | 'never-completed' | 'ok';

export function statusOf(r: RuleCoverage): RuleStatus {
  if (r.attempts === 0) return 'never-ran';
  if (r.successes === 0) return 'never-completed';
  return 'ok';
}

export function hitRate(r: RuleCoverage): number {
  return r.attempts === 0 ? 0 : r.successes / r.attempts;
}

const RANK: Record<RuleStatus, number> = {
  'never-ran': 0,
  'never-completed': 1,
  ok: 2,
};

/** Problems first, then the busiest rules — the two things on this page worth reading. */
export function sortRules(rules: RuleCoverage[]): RuleCoverage[] {
  return [...rules].sort(
    (a, b) =>
      RANK[statusOf(a)] - RANK[statusOf(b)] ||
      b.attempts - a.attempts ||
      a.name.localeCompare(b.name),
  );
}

export function problemCounts(rules: RuleCoverage[]) {
  return {
    neverRan: rules.filter((r) => statusOf(r) === 'never-ran').length,
    neverCompleted: rules.filter((r) => statusOf(r) === 'never-completed').length,
  };
}
