import { describe, expect, it } from 'vitest';
import { formatBecause, ruleLabel, stepHeading } from './cause';
import type { CauseStep } from './types';

const step = (over: Partial<CauseStep>): CauseStep => ({
  firing: 1,
  rule: 'r',
  kind: 'event',
  line: 10,
  year: 800,
  because: '',
  records: [],
  ...over,
});

describe('formatBecause', () => {
  it('reads every change in a list, not only the last', () => {
    expect(formatBecause('<#52>Lancelot</>: age Age.Adult -> Age.Old, alive true -> false')).toBe(
      '<#52>Lancelot</>: age Adult → Old, alive yes → no',
    );
  });

  it('drops a first assignment from null, and names a cleared value', () => {
    expect(formatBecause('<#23>Aislinn</>: killer null -> <#45>Morgana</>')).toBe(
      '<#23>Aislinn</>: killer → <#45>Morgana</>',
    );
    expect(formatBecause('<#9>X</>: partner <#4>Y</> -> null')).toBe(
      '<#9>X</>: partner <#4>Y</> → none',
    );
  });

  it('never rewrites a name inside a link', () => {
    expect(formatBecause('<#3>Title.King true</> was created')).toBe(
      '<#3>Title.King true</> was created',
    );
  });
});

describe('stepHeading', () => {
  it('says how each kind of step came to run', () => {
    expect(stepHeading(step({ kind: 'trigger', rule: 'succession' }))).toBe('trigger succession');
    expect(stepHeading(step({ kind: 'event', rule: 'murder' }))).toBe('event murder');
    expect(stepHeading(step({ kind: 'call', rule: 'feast' }))).toBe(
      'event feast, called by the rule below',
    );
    expect(stepHeading(step({ kind: 'scheduled', rule: 'schedule@475', line: 475 }))).toBe(
      'scheduled by the rule below',
    );
  });
});

describe('ruleLabel', () => {
  it('names a schedule body by its line, and leaves named rules alone', () => {
    expect(ruleLabel('schedule@489')).toBe('scheduled · line 489');
    expect(ruleLabel('succession')).toBe('succession');
  });
});
