import { describe, expect, it } from 'vitest';
import { NEUTRAL, slotOf, typeSlots } from './entity-style';

describe('typeSlots', () => {
  it('numbers types by the order their first entity appeared', () => {
    // entity:  0  1  2  3  4  5
    const types = [0, 7, 3, 3, 7, 9];
    expect([...typeSlots(types)]).toEqual([
      [7, 0],
      [3, 1],
      [9, 2],
    ]);
  });

  it('keeps a slot once given, however the counts change', () => {
    const early = typeSlots([0, 7, 3]);
    const later = typeSlots([0, 7, 3, 3, 3, 3, 3, 3]);
    expect(later.get(7)).toBe(early.get(7));
    expect(later.get(3)).toBe(early.get(3));
  });

  it('puts the types past the last hue on the neutral slot', () => {
    const types = [0, ...Array.from({ length: 10 }, (_, i) => i + 1)];
    const slots = typeSlots(types);
    expect(slots.get(10)).toBe(NEUTRAL);
    expect(new Set(slots.values()).size).toBe(NEUTRAL + 1);
  });
});

describe('slotOf', () => {
  it('is neutral for an entity newer than the table', () => {
    const types = [0, 7];
    expect(slotOf(1, types, typeSlots(types))).toBe(0);
    expect(slotOf(50, types, typeSlots(types))).toBe(NEUTRAL);
  });
});
