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
