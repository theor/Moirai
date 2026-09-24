import { describe, expect, it } from 'vitest';
import {
  ancestry,
  ancestryDepth,
  byId,
  childIndex,
  childrenOf,
  descendantChart,
  descendantDepth,
  generationName,
  lifespan,
  repeatedPeople,
  siblingGroups,
  siblingsOf,
} from './family';
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

describe('descendantChart', () => {
  const chart = (nodes: FamilyTreeNode[], id: number) =>
    descendantChart(id, byId(nodes), childIndex(nodes))!;

  it('walks every generation and counts what a collapse hides', () => {
    const b = chart(tree, 1);
    expect(b.groups.map((g) => g.branches.map((x) => x.node.name))).toEqual([['Evander', 'Rhian']]);
    expect(b.groups[0].branches[0].groups[0].branches[0].node.name).toBe('Rosabelle');
    expect(b.descendants).toBe(3);
    expect(descendantDepth(b)).toBe(2);
  });

  it('shows the only co-parent as the spouse, and then needs no label', () => {
    // Arwen has no partner on record, but every child is Aislinn's too.
    const b = chart(tree, 1);
    expect(b.spouse?.name).toBe('Aislinn');
    expect(b.groups[0].label).toBe('');
  });

  it('labels every group when there was more than one other parent', () => {
    const family = [
      node(1, 'Arwen', 0, 0, { partner: 5 }),
      node(2, 'Aislinn'),
      node(5, 'Corinda'),
      node(3, 'Evander', 1, 2, { born: 800 }),
      node(4, 'Rhian', 1, 5, { born: 810 }),
      node(6, 'Orla', 1, 0, { born: 820 }),
    ];
    const b = chart(family, 1);
    expect(b.spouse?.name).toBe('Corinda');
    // The partner's children first, then the rest in the order they began.
    expect(b.groups.map((g) => g.label)).toEqual([
      'with Corinda',
      'with Aislinn',
      'other parent unknown',
    ]);
  });

  it('labels a single group whose other parent is not the spouse on the card', () => {
    const family = [
      node(1, 'Arwen', 0, 0, { partner: 5 }),
      node(2, 'Aislinn'),
      node(5, 'Corinda'),
      node(3, 'Evander', 1, 2),
    ];
    expect(chart(family, 1).groups[0].label).toBe('with Aislinn');
  });

  it('ignores a partner the tree does not contain', () => {
    const family = [node(1, 'Arwen', 0, 0, { partner: 99 })];
    expect(chart(family, 1).spouse).toBeUndefined();
  });

  it('draws a child of two descendants under both, and stays bounded', () => {
    // 2 and 3 are both children of 1, and 4 is their child: reachable along two paths.
    const family = [node(1, 'Root'), node(2, 'A', 1), node(3, 'B', 1), node(4, 'C', 2, 3)];
    const b = chart(family, 1);
    const grandchildren = b.groups.flatMap((g) => g.branches).flatMap((x) => x.groups);
    expect(grandchildren.flatMap((g) => g.branches.map((x) => x.node.id))).toEqual([4, 4]);
    expect(descendantChart(1, byId(family), childIndex(family), 1)!.descendants).toBe(2);
  });
});

describe('ancestry', () => {
  it('pairs the parents and climbs through both', () => {
    const family = [
      node(1, 'GrandA'),
      node(2, 'GrandB'),
      node(3, 'Father', 1, 2),
      node(4, 'Mother'),
      node(5, 'Me', 3, 4),
    ];
    const a = ancestry(5, byId(family))!;
    expect([a.couple.node.name, a.couple.spouse?.name]).toEqual(['Father', 'Mother']);
    expect(a.above.map((x) => [x.couple.node.name, x.couple.spouse?.name])).toEqual([
      ['GrandA', 'GrandB'],
    ]);
    expect(ancestryDepth(a)).toBe(2);
  });

  it('is undefined for someone with no known parents', () => {
    expect(ancestry(1, byId(tree))).toBeUndefined();
    expect(ancestryDepth(undefined)).toBe(0);
  });
});

describe('siblingGroups', () => {
  it('puts the full siblings first and labels the half ones by the parent shared', () => {
    const family = [
      node(1, 'Father'),
      node(2, 'Mother'),
      node(5, 'Stepmother'),
      node(3, 'Me', 1, 2, { born: 800 }),
      node(4, 'Brother', 1, 2, { born: 790 }),
      node(6, 'Half', 1, 5, { born: 810 }),
    ];
    const groups = siblingGroups(3, byId(family), childIndex(family));
    expect(groups.map((g) => g.label)).toEqual(['full', 'half, through Father']);
    expect(groups[0].branches.map((b) => b.node.name)).toEqual(['Brother', 'Me']);
  });

  it('leaves an only group unlabelled', () => {
    const groups = siblingGroups(3, byId(tree), childIndex(tree));
    expect(groups.map((g) => g.label)).toEqual(['']);
    // Only the person the chart is centred on carries their descendants.
    expect(groups[0].branches.map((b) => b.descendants)).toEqual([1, 0]);
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

describe('repeatedPeople', () => {
  const column = (nodes: FamilyTreeNode[], id: number) => [
    { key: 0, label: '', branches: [descendantChart(id, byId(nodes), childIndex(nodes))!] },
  ];

  it('marks two descendants who married, but not the children drawn under both', () => {
    // 2 and 3 are cousins through 1; they married and had 4, who had 5.
    const family = [
      node(1, 'Root'),
      node(10, 'X', 1),
      node(11, 'Y', 1),
      node(2, 'A', 10, 0, { partner: 3 }),
      node(3, 'B', 11, 0, { partner: 2 }),
      node(4, 'C', 2, 3),
      node(5, 'D', 4),
    ];
    const repeats = repeatedPeople(undefined, column(family, 1), 0);
    expect([...repeats.keys()]).toEqual([2, 3]);
    // One marriage, one colour.
    expect(repeats.get(2)).toEqual({ slot: 0, count: 2 });
    expect(repeats.get(3)).toEqual({ slot: 0, count: 2 });
  });

  it('marks an ancestor shared by both sides', () => {
    // 3 and 4 are siblings through 1 + 2; their children 5 and 6 married and had 7.
    const family = [
      node(1, 'G1'),
      node(2, 'G2'),
      node(3, 'A', 1, 2),
      node(4, 'B', 1, 2),
      node(5, 'Father', 3),
      node(6, 'Mother', 4),
      node(7, 'Me', 5, 6),
    ];
    const repeats = repeatedPeople(ancestry(7, byId(family)), [], 0);
    expect([...repeats.keys()]).toEqual([1, 2]);
    expect(repeats.get(1)?.count).toBe(2);
  });

  it('marks nobody in a family that never loops', () => {
    expect(repeatedPeople(undefined, column(tree, 1), 0).size).toBe(0);
    expect(repeatedPeople(ancestry(6, byId(tree)), [], 0).size).toBe(0);
  });
});
