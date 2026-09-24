import { describe, expect, it } from 'vitest';
import { byId, childrenOf, lifespan, siblingsOf } from './family';
import type { FamilyTreeNode } from './types';

const node = (
  id: number,
  name: string,
  p1 = 0,
  p2 = 0,
  more: Partial<FamilyTreeNode> = {},
): FamilyTreeNode => ({ id, name, p1, p2, born: 0, died: 0, dead: false, partner: 0, ...more });

// 1 + 2 -> 3, 4;  3 + 5 -> 6;  7 is unrelated and parentless.
const tree: FamilyTreeNode[] = [
  node(1, 'Arwen'),
  node(2, 'Aislinn'),
  node(3, 'Evander', 1, 2),
  node(4, 'Rhian', 1, 2),
  node(5, 'Corinda'),
  node(6, 'Rosabelle', 3, 5),
  node(7, 'Stranger'),
];

describe('byId', () => {
  it('indexes every node', () => {
    const map = byId(tree);
    expect(map.size).toBe(tree.length);
    expect(map.get(3)?.name).toBe('Evander');
  });

  it('is empty for an empty tree', () => {
    expect(byId([]).size).toBe(0);
  });
});

describe('childrenOf', () => {
  it('finds children through either parent slot', () => {
    expect(childrenOf(tree, 1).map((n) => n.name)).toEqual(['Evander', 'Rhian']);
    expect(childrenOf(tree, 2).map((n) => n.name)).toEqual(['Evander', 'Rhian']);
    expect(childrenOf(tree, 5).map((n) => n.name)).toEqual(['Rosabelle']);
  });

  it('returns nothing for someone with no children', () => {
    expect(childrenOf(tree, 7)).toEqual([]);
  });

  it('never treats 0 as a parent', () => {
    // 0 is the engine's "no parent, or beyond max depth" marker. Matching it would make every
    // parentless node a child of nobody-in-particular.
    expect(childrenOf(tree, 0)).toEqual([]);
    expect(childrenOf(tree, -1)).toEqual([]);
  });
});

describe('lifespan', () => {
  it('reads birth and death', () => {
    expect(lifespan(node(1, 'a', 0, 0, { born: 769, died: 837, dead: true }))).toBe('769–837');
  });
  it('marks the living by birth alone', () => {
    expect(lifespan(node(1, 'a', 0, 0, { born: 769 }))).toBe('b. 769');
  });
  it('keeps a death with no year', () => {
    expect(lifespan(node(1, 'a', 0, 0, { born: 769, dead: true }))).toBe('769–?');
  });
  it('says nothing when nothing is known', () => {
    expect(lifespan(node(1, 'a'))).toBe('');
  });
});

describe('siblingsOf', () => {
  // 1 + 2 -> 10, 11;  1 + 3 -> 12;  4 has no known parents, nor does 5.
  const family = [
    node(1, 'Father'),
    node(2, 'First wife'),
    node(3, 'Second wife'),
    node(10, 'Elder', 1, 2, { born: 800 }),
    node(11, 'Younger', 2, 1, { born: 805 }),
    node(12, 'Half', 1, 3, { born: 802 }),
    node(4, 'Orphan'),
    node(5, 'Stranger'),
  ];

  it('finds whole and half siblings, eldest first', () => {
    expect(siblingsOf(family, 11).map((s) => [s.node.name, s.half])).toEqual([
      ['Elder', false],
      ['Half', true],
    ]);
  });

  it('does not treat two people with no parents as siblings', () => {
    expect(siblingsOf(family, 4)).toEqual([]);
  });
});
