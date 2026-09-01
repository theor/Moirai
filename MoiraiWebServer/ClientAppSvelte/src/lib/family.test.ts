import { describe, expect, it } from 'vitest';
import { byId, childrenOf } from './family';
import type { FamilyTreeNode } from './types';

const node = (id: number, name: string, p1 = 0, p2 = 0): FamilyTreeNode => ({ id, name, p1, p2 });

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
