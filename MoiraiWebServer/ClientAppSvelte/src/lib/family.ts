import type { FamilyTreeNode } from './types';

/**
 * Reading a family tree.
 *
 * The engine hands back a flat list of nodes, each naming its parents by id, so every view has to turn
 * that into lookups. Both the Family page and the Life page did it inline and identically, which is the
 * usual sign it belongs somewhere both can reach.
 */

/** Index the tree by entity id, for resolving a parent reference to the node itself. */
export function byId(nodes: FamilyTreeNode[]): Map<number, FamilyTreeNode> {
  return new Map(nodes.map((n) => [n.id, n]));
}

/**
 * The nodes whose parent1 or parent2 is `parent`.
 *
 * A parent id of 0 means "none, or beyond the depth the tree was cut at", so it never matches: asking
 * for the children of 0 would otherwise return every node that has no parents at all.
 */
export function childrenOf(nodes: FamilyTreeNode[], parent: number): FamilyTreeNode[] {
  if (parent <= 0) return [];
  return nodes.filter((n) => n.p1 === parent || n.p2 === parent);
}

/** "769–837", "769–?" (dead, year unknown), "b. 769" (alive), or "" when nothing is known. */
export function lifespan(n: FamilyTreeNode): string {
  const from = n.born > 0 ? String(n.born) : '?';
  if (n.died > 0) return `${from}–${n.died}`;
  if (n.dead) return `${from}–?`;
  return n.born > 0 ? `b. ${n.born}` : '';
}

/** A brother or sister: `half` when they share only one parent. */
export type Sibling = { node: FamilyTreeNode; half: boolean };

/**
 * Everyone who shares a parent with `id`, eldest first. Parents of 0 never match, so two people with
 * no known parents are not siblings.
 */
export function siblingsOf(nodes: FamilyTreeNode[], id: number): Sibling[] {
  const me = nodes.find((n) => n.id === id);
  if (!me) return [];
  const mine = [me.p1, me.p2].filter((p) => p > 0);
  if (mine.length === 0) return [];
  return nodes
    .filter((n) => n.id !== id && [n.p1, n.p2].some((p) => p > 0 && mine.includes(p)))
    .map((n) => ({ node: n, half: !sameParents(me, n) }))
    .sort((a, b) => a.node.born - b.node.born || a.node.id - b.node.id);
}

function sameParents(a: FamilyTreeNode, b: FamilyTreeNode) {
  const set = (n: FamilyTreeNode) =>
    [n.p1, n.p2]
      .filter((p) => p > 0)
      .sort()
      .join(',');
  return set(a) === set(b);
}

/**
 * The family chart: one column per generation, read left to right, ancestors before the person the
 * chart is centred on and descendants after. These types are the chart's shape with every lookup
 * already done, so the components only draw.
 */

/** One card: a person and the spouse shown with them. */
export type Couple = { node: FamilyTreeNode; spouse?: FamilyTreeNode };

/** A run of children under one card. `label` names the other parent when that is not obvious. */
export type ChartGroup = { key: number; label: string; branches: ChartBranch[] };

/** A card and everything below it. `descendants` counts the cards a collapse would hide. */
export type ChartBranch = Couple & { groups: ChartGroup[]; descendants: number };

/** A couple of parents and, further left, the couples that were their parents. */
export type Ancestry = { couple: Couple; above: Ancestry[] };

function nameWithYears(n: FamilyTreeNode): string {
  const years = lifespan(n);
  return years ? `${n.name} (${years})` : n.name;
}

/** Index a flat tree as parent -> children, eldest first, so a walk down never rescans the list. */
export function childIndex(nodes: FamilyTreeNode[]): Map<number, FamilyTreeNode[]> {
  const index = new Map<number, FamilyTreeNode[]>();
  for (const n of nodes)
    for (const p of new Set([n.p1, n.p2]))
      if (p > 0) {
        const list = index.get(p);
        if (list) list.push(n);
        else index.set(p, [n]);
      }
  for (const list of index.values()) list.sort((a, b) => a.born - b.born || a.id - b.id);
  return index;
}

/**
 * `id` and every generation below them, grouped by the other parent.
 *
 * The card shows the person's partner, or when they have none on record (a widow, say) the one person
 * they had children with. A group is labelled only when the card does not already say who the other
 * parent was: when there is more than one, or when it is not the spouse on the card.
 *
 * A person can be reached twice when two descendants marry each other; they are then drawn under both,
 * as genealogies do. `depth` bounds the walk regardless.
 */
export function descendantChart(
  id: number,
  map: Map<number, FamilyTreeNode>,
  index: Map<number, FamilyTreeNode[]>,
  depth = 16,
): ChartBranch | undefined {
  const node = map.get(id);
  if (!node) return undefined;
  const kids = depth > 0 ? (index.get(id) ?? []) : [];

  const broods = new Map<number, FamilyTreeNode[]>();
  for (const kid of kids) {
    const other = kid.p1 === id ? kid.p2 : kid.p1;
    const list = broods.get(other);
    if (list) list.push(kid);
    else broods.set(other, [kid]);
  }

  const onlyCoParent = broods.size === 1 ? [...broods.keys()][0] : 0;
  const spouse = map.get(node.partner) ?? map.get(onlyCoParent);
  const spouseId = spouse?.id ?? 0;

  const rank = (coParent: number, children: FamilyTreeNode[]) =>
    coParent !== 0 && coParent === spouseId ? -Infinity : children[0].born;
  const groups = [...broods]
    .sort(([a, ak], [b, bk]) => rank(a, ak) - rank(b, bk))
    .map(([coParent, children]): ChartGroup => {
      const other = map.get(coParent);
      const obvious = broods.size === 1 && coParent === spouseId;
      return {
        key: coParent,
        label: obvious ? '' : other ? `with ${nameWithYears(other)}` : 'other parent unknown',
        branches: children
          .map((c) => descendantChart(c.id, map, index, depth - 1))
          .filter((b): b is ChartBranch => !!b),
      };
    });

  const descendants = groups
    .flatMap((g) => g.branches)
    .reduce((sum, b) => sum + 1 + b.descendants, 0);
  return { node, spouse, groups, descendants };
}

/** The parents of `id` as one couple, with theirs further up; undefined when none are known. */
export function ancestry(
  id: number,
  map: Map<number, FamilyTreeNode>,
  depth = 16,
): Ancestry | undefined {
  const me = map.get(id);
  if (!me || depth <= 0) return undefined;
  const parents = [me.p1, me.p2]
    .filter((p, i, all) => p > 0 && all.indexOf(p) === i)
    .map((p) => map.get(p))
    .filter((p): p is FamilyTreeNode => !!p);
  if (parents.length === 0) return undefined;
  return {
    couple: { node: parents[0], spouse: parents[1] },
    above: parents.map((p) => ancestry(p.id, map, depth - 1)).filter((a): a is Ancestry => !!a),
  };
}

/** How many generations of ancestry there are: 1 for parents alone. */
export function ancestryDepth(a: Ancestry | undefined): number {
  return a ? 1 + Math.max(0, ...a.above.map(ancestryDepth)) : 0;
}

/** How many generations hang below a branch: 0 for no children. */
export function descendantDepth(b: ChartBranch): number {
  const below = b.groups.flatMap((g) => g.branches).map(descendantDepth);
  return below.length ? 1 + Math.max(...below) : 0;
}

/**
 * `id` among their brothers and sisters, the column the chart is centred on, grouped by parents.
 *
 * The full siblings come first and go unlabelled when they are the only group; half-siblings are
 * labelled by the parent they share. Each sibling is a branch of their own, but the tree only
 * reaches the descendants of `id`, so the others are single cards.
 */
export function siblingGroups(
  id: number,
  map: Map<number, FamilyTreeNode>,
  index: Map<number, FamilyTreeNode[]>,
  depth = 16,
): ChartGroup[] {
  const me = map.get(id);
  if (!me) return [];
  const key = (n: FamilyTreeNode) => [n.p1, n.p2].filter((p) => p > 0).sort((a, b) => a - b);
  const mine = key(me);
  const all = [...new Map(mine.flatMap((p) => index.get(p) ?? []).map((n) => [n.id, n])).values()]
    .filter((n) => n.id === id || key(n).some((p) => mine.includes(p)))
    .sort((a, b) => a.born - b.born || a.id - b.id);

  const groups = new Map<string, FamilyTreeNode[]>();
  for (const n of all) {
    const k = key(n).join(',');
    const list = groups.get(k);
    if (list) list.push(n);
    else groups.set(k, [n]);
  }
  const full = mine.join(',');
  return [...groups]
    .sort(([a], [b]) => (a === full ? -1 : b === full ? 1 : 0))
    .map(([k, children], i): ChartGroup => {
      let label = '';
      if (k !== full) {
        // The tree does not carry a half-sibling's other parent, so name the one they share.
        const shared = key(children[0]).filter((p) => mine.includes(p));
        const names = shared.map((p) => map.get(p)?.name).filter((n) => !!n);
        label = names.length ? `half, through ${names.join(' & ')}` : 'half';
      } else if (groups.size > 1) label = 'full';
      return {
        key: i,
        label,
        branches: children
          .map((c) => (c.id === id ? descendantChart(c.id, map, index, depth) : leaf(c, map)))
          .filter((b): b is ChartBranch => !!b),
      };
    });
}

function leaf(n: FamilyTreeNode, map: Map<number, FamilyTreeNode>): ChartBranch {
  return { node: n, spouse: map.get(n.partner), groups: [], descendants: 0 };
}

/** A column heading, by generation counted from the person the chart is centred on. */
export function generationName(g: number): string {
  if (g === 0) return 'Siblings';
  const [one, two] = g < 0 ? ['Parents', 'Grandparents'] : ['Children', 'Grandchildren'];
  const n = Math.abs(g);
  if (n === 1) return one;
  if (n === 2) return two;
  const two_ = two.toLowerCase();
  return n === 3 ? `Great-${two_}` : n === 4 ? `Great-great-${two_}` : `${n - 2}× great-${two_}`;
}
