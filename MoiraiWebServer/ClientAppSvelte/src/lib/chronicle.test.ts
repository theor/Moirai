import { describe, expect, it } from 'vitest';
import { describeSpan, eraAt, eraBand } from './chronicle';
import type { ChronicleEra } from './types';

const era = (id: number, start: number, end: number, open = false): ChronicleEra => ({
  id,
  name: `Era ${id}`,
  start,
  end,
  open,
});

describe('eraBand', () => {
  it('divides the span in proportion to each era', () => {
    const band = eraBand([era(1, 764, 777), era(2, 777, 885), era(3, 885, 964, true)], 764, 964);
    expect(band.map((s) => s.era.id)).toEqual([1, 2, 3]);
    expect(band[0].left).toBe(0);
    expect(band[1].left).toBeCloseTo(13 / 200);
    expect(band.reduce((sum, s) => sum + s.width, 0)).toBeCloseTo(1);
  });

  it('clamps eras to the span and drops those outside it', () => {
    const band = eraBand([era(1, 0, 700), era(2, 700, 800), era(3, 800, 900, true)], 764, 850);
    expect(band.map((s) => s.era.id)).toEqual([2, 3]);
    expect(band[0]).toMatchObject({ left: 0, width: 36 / 86 });
  });

  it('gives the open era the whole band in a world that has not moved', () => {
    const band = eraBand([era(1, 764, 764, true)], 764, 764);
    expect(band).toEqual([{ era: era(1, 764, 764, true), left: 0, width: 1 }]);
  });
});

describe('eraAt', () => {
  const eras = [era(1, 764, 777), era(2, 777, 885), era(3, 885, 964, true)];
  it('is the last era to have started by that year', () => {
    expect(eraAt(eras, 764)?.id).toBe(1);
    expect(eraAt(eras, 777)?.id).toBe(2);
    expect(eraAt(eras, 900)?.id).toBe(3);
  });
  it('is undefined before the first era', () => {
    expect(eraAt(eras, 700)).toBeUndefined();
  });
});

describe('describeSpan', () => {
  it('names the span and its length', () => {
    expect(describeSpan(764, 964)).toBe('764–964 · 200 years');
    expect(describeSpan(764, 765)).toBe('764–765 · 1 year');
    expect(describeSpan(764, 764)).toBe('764');
  });
});
