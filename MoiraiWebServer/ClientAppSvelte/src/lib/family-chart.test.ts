import { describe, expect, it } from 'vitest';
import {
  GEOMETRY as G,
  buildGraph,
  childIndex,
  edgePath,
  generationName,
  layoutChart,
  peopleOnSeveralCards,
  sharedCards,
  type Layout,
} from './family-chart';
import type { FamilyTreeNode } from './types';

const node = (
  id: number,
  name: string,
  p1 = 0,
  p2 = 0,
  more: Partial<FamilyTreeNode> = {},
): FamilyTreeNode => ({ id, name, p1, p2, born: 0, died: 0, dead: false, partner: 0, ...more });

const chart = (list: FamilyTreeNode[], focus: number, collapsed: string[] = []) =>
  layoutChart(buildGraph(list, focus), new Set(collapsed));

const names = (l: Layout) => l.cards.map((p) => p.card.rows.map((r) => r.name).join(' & '));

/** What must hold of any layout, whatever the family. */
function invariants(l: Layout) {
  // Each card once.
  const keys = l.cards.map((p) => p.card.key);
  expect(new Set(keys).size).toBe(keys.length);
  // Lines run left to right, from a parent's column to a later one.
  for (const e of l.edges) expect(e.to.col).toBeGreaterThan(e.from.col);
  // Every card is reached by one tree line at most: one per row on the ancestor side, where each
  // row has parents of its own, and one per card below.
  const primaries = l.edges.filter((e) => e.primary);
  const ends = primaries.map((e) =>
    e.to.card.key.startsWith('a') || e.to.col < 0 ? `${e.to.card.key}:${e.row}` : e.to.card.key,
  );
  expect(new Set(ends).size).toBe(ends.length);
  // No two cards in a column overlap, counting the label above a card.
  const byCol = new Map<number, [number, number][]>();
  for (const p of l.cards) {
    const band = p.label ? G.labelHeight : 0;
    byCol.set(p.col, [...(byCol.get(p.col) ?? []), [p.top - band, p.top + p.height]]);
  }
  for (const spans of byCol.values()) {
    spans.sort((a, b) => a[0] - b[0]);
    for (let i = 1; i < spans.length; i++)
      expect(spans[i][0]).toBeGreaterThanOrEqual(spans[i - 1][1]);
  }
  // Two parents' tree lines never share a stretch of the same gap.
  const spines = new Map<string, [number, number]>();
  for (const e of primaries) {
    const k = e.from.card.key;
    const y = e.to.top + e.row * G.rowHeight + G.stub;
    const s = spines.get(k) ?? [e.from.top + G.stub, e.from.top + G.stub];
    spines.set(k, [Math.min(s[0], y), Math.max(s[1], y)]);
  }
  const byGap = new Map<number, [number, number][]>();
  for (const [k, s] of spines) {
    const from = l.cards.find((p) => p.card.key === k)!;
    byGap.set(from.col, [...(byGap.get(from.col) ?? []), s]);
  }
  for (const spans of byGap.values()) {
    spans.sort((a, b) => a[0] - b[0]);
    for (let i = 1; i < spans.length; i++) expect(spans[i][0]).toBeGreaterThan(spans[i - 1][1]);
  }
}

// 1 + 2 -> 3, 4;  3 + 5 -> 6.
const simple = [
  node(1, 'Arwen'),
  node(2, 'Aislinn'),
  node(3, 'Evander', 1, 2, { partner: 5 }),
  node(4, 'Rhian', 1, 2),
  node(5, 'Corinda'),
  node(6, 'Rosabelle', 3, 5),
];

describe('layoutChart', () => {
  it('lays out a plain family as a tree', () => {
    const l = chart(simple, 1);
    invariants(l);
    expect(names(l)).toEqual(['Arwen & Aislinn', 'Evander & Corinda', 'Rosabelle', 'Rhian']);
    expect(l.cols).toEqual([0, 2]);
    expect(l.edges.every((e) => e.primary)).toBe(true);
  });

  it('puts the parents left and the siblings in the centre column', () => {
    const l = chart(simple, 3);
    invariants(l);
    const at = (name: string) => l.cards.find((p) => p.card.rows[0].name === name)!;
    expect([at('Arwen').col, at('Rhian').col, at('Evander').col, at('Rosabelle').col]).toEqual([
      -1, 0, 0, 1,
    ]);
  });

  it('draws two descendants who married as one card with a line from each side', () => {
    // 10 and 11 are children of 1; their children 2 and 3 married and had 4, who had 5.
    const list = [
      node(1, 'Root'),
      node(10, 'X', 1),
      node(11, 'Y', 1),
      node(2, 'A', 10, 0, { partner: 3 }),
      node(3, 'B', 11, 0, { partner: 2 }),
      node(4, 'C', 2, 3),
      node(5, 'D', 4),
    ];
    const l = chart(list, 1);
    invariants(l);
    const couple = l.cards.filter((p) => p.card.key === '2,3');
    expect(couple).toHaveLength(1);
    const into = l.edges.filter((e) => e.to === couple[0]);
    // One line into A's row from X, one into B's row from Y; only one is the tree line.
    expect(into.map((e) => [e.from.card.rows[0].name, e.to.card.rows[e.row].name])).toEqual([
      ['X', 'A'],
      ['Y', 'B'],
    ]);
    expect(into.map((e) => e.primary)).toEqual([true, false]);
    // Their descendants are drawn once.
    expect(names(l).filter((n) => n === 'C' || n === 'D')).toEqual(['C', 'D']);
    expect(peopleOnSeveralCards(l).size).toBe(0);
    expect([...sharedCards(l)]).toEqual([['2,3', { slot: 0, lines: 2 }]]);
  });

  it('draws an ancestor couple shared by both sides once', () => {
    // 1 + 2 had 3 and 4; 3's child 5 married 4's child 6, and they had 7.
    const list = [
      node(20, 'GG1'),
      node(1, 'G1', 20),
      node(2, 'G2'),
      node(3, 'A', 1, 2),
      node(4, 'B', 1, 2),
      node(5, 'Father', 3),
      node(6, 'Mother', 4),
      node(7, 'Me', 5, 6),
    ];
    const l = chart(list, 7);
    invariants(l);
    const shared = l.cards.filter((p) => p.card.key === 'a1,2');
    expect(shared).toHaveLength(1);
    expect(l.edges.filter((e) => e.from === shared[0]).map((e) => e.primary)).toEqual([
      true,
      false,
    ]);
    // And what is above it is drawn once too.
    expect(l.cards.filter((p) => p.card.key === 'a20')).toHaveLength(1);
    expect([...sharedCards(l)]).toEqual([['a1,2', { slot: 0, lines: 2 }]]);
  });

  it('keeps a merged card visible when only one of its parents is folded', () => {
    const list = [
      node(1, 'Root'),
      node(10, 'X', 1),
      node(11, 'Y', 1),
      node(2, 'A', 10, 0, { partner: 3 }),
      node(3, 'B', 11, 0, { partner: 2 }),
    ];
    const graph = buildGraph(list, 1);
    const xKey = graph.centre[0].card.kids[0].card.key;
    const l = layoutChart(graph, new Set([xKey]));
    invariants(l);
    const couple = l.cards.find((p) => p.card.key === '2,3')!;
    expect(couple).toBeDefined();
    // Now reached only from Y, which makes that the tree line.
    expect(l.edges.filter((e) => e.to === couple).map((e) => e.primary)).toEqual([true]);
  });

  it('counts what a fold hides once, however many lines lead there', () => {
    const list = [
      node(1, 'Root'),
      node(10, 'X', 1),
      node(11, 'Y', 1),
      node(2, 'A', 10, 0, { partner: 3 }),
      node(3, 'B', 11, 0, { partner: 2 }),
      node(4, 'C', 2, 3),
    ];
    const l = chart(list, 1);
    expect(l.cards.find((p) => p.card.key === '1')!.below).toBe(4);
  });

  it('labels children by the other parent only when the card does not say', () => {
    const list = [
      node(1, 'Arwen', 0, 0, { partner: 5 }),
      node(2, 'Aislinn'),
      node(5, 'Corinda'),
      node(3, 'Evander', 1, 2),
      node(4, 'Rhian', 1, 5),
      node(6, 'Orla', 1, 0),
    ];
    const l = chart(list, 1);
    invariants(l);
    expect(l.cards.filter((p) => p.label).map((p) => p.label)).toEqual([
      'with Corinda',
      'with Aislinn',
      'other parent unknown',
    ]);
  });

  it('labels half-siblings by the parent they share', () => {
    const list = [
      node(1, 'Father'),
      node(2, 'Mother'),
      node(5, 'Stepmother'),
      node(3, 'Me', 1, 2),
      node(4, 'Brother', 1, 2),
      node(6, 'Half', 1, 5),
    ];
    const l = chart(list, 3);
    invariants(l);
    expect(l.cards.filter((p) => p.label).map((p) => p.label)).toEqual([
      'full',
      'half, through Father',
    ]);
  });

  it('labels the children of an earlier marriage', () => {
    // 2 had a child with 3, then married 4 and had another.
    const list = [
      node(1, 'Root'),
      node(2, 'A', 1, 0, { partner: 4 }),
      node(3, 'First'),
      node(4, 'Second', 0, 0, { partner: 2 }),
      node(5, 'Kid1', 2, 3),
      node(6, 'Kid2', 2, 4),
    ];
    const l = chart(list, 1);
    invariants(l);
    expect(l.cards.filter((p) => p.label).map((p) => p.label)).toEqual([
      'with Second',
      'with First',
    ]);
  });

  it('keeps someone on two cards when the two relatives they married differ', () => {
    // A (via X) married B (via Y), but B's partner on record is now C: two couples, B on both.
    const list = [
      node(1, 'Root'),
      node(10, 'X', 1),
      node(11, 'Y', 1),
      node(2, 'A', 10, 0, { partner: 3 }),
      node(3, 'B', 11, 0, { partner: 4 }),
      node(4, 'C'),
    ];
    const l = chart(list, 1);
    invariants(l);
    expect([...peopleOnSeveralCards(l)]).toEqual([[3, { slot: 0, count: 2 }]]);
  });
});

describe('edgePath', () => {
  it('draws a tree line as an elbow through the gap and a loop as a curve', () => {
    const l = chart(simple, 1);
    const e = l.edges[0];
    expect(edgePath(e)).toMatch(/^M\d+ \d+H\d+V\d+H\d+$/);
    expect(edgePath({ ...e, primary: false })).toMatch(/^M\d+ \d+C/);
  });
});

describe('childIndex', () => {
  it('finds children through either slot, eldest first, never under 0', () => {
    const index = childIndex(simple);
    expect(index.get(1)!.map((n) => n.name)).toEqual(['Evander', 'Rhian']);
    expect(index.has(0)).toBe(false);
  });
});

describe('generationName', () => {
  it('names the columns around the centre', () => {
    expect([-3, -2, -1, 0, 1, 2, 3, 4, 5].map(generationName)).toEqual([
      'Great-grandparents',
      'Grandparents',
      'Parents',
      'Siblings',
      'Children',
      'Grandchildren',
      'Great-grandchildren',
      'Great-great-grandchildren',
      '3× great-grandchildren',
    ]);
  });
});
