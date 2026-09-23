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

/** The children `parent` had with one other parent (0 when that parent is unknown). */
export type Brood = { coParent: number; children: FamilyTreeNode[] };

/**
 * A parent's children, grouped by the other parent.
 *
 * A person who remarried has children with more than one partner, and one flat row hid that: the
 * grouping is what shows a family's shape. The current partner's brood comes first, the rest in the
 * order they began; within a brood, children go eldest first.
 */
export function childrenByCoParent(nodes: FamilyTreeNode[], parent: number, partner = 0): Brood[] {
  const broods = new Map<number, FamilyTreeNode[]>();
  const kids = [...childrenOf(nodes, parent)].sort((a, b) => a.born - b.born || a.id - b.id);
  for (const kid of kids) {
    const other = kid.p1 === parent ? kid.p2 : kid.p1;
    const list = broods.get(other);
    if (list) list.push(kid);
    else broods.set(other, [kid]);
  }
  const rank = (b: Brood) =>
    b.coParent !== 0 && b.coParent === partner ? -Infinity : b.children[0].born;
  return [...broods]
    .map(([coParent, children]) => ({ coParent, children }))
    .sort((a, b) => rank(a) - rank(b));
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
