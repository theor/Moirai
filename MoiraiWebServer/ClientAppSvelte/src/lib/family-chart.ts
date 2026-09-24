import type { FamilyTreeNode } from './types';
import { lifespan } from './family';

/**
 * The family chart: one column per generation, read left to right. Ancestors come first, then the
 * person the chart is centred on among their brothers and sisters, then a column per generation of
 * descendants.
 *
 * A family is not a tree. Two descendants marry, or both sides of a family share an ancestor, and a
 * tree has to draw the same couple twice, with everything above or below it. So this is a graph:
 * every couple is one card, drawn once, and a card reached along a second line of descent gets a
 * second line in instead of a second copy. The layout is computed here, in pixels, because nested
 * boxes cannot draw a line into a card that belongs to another branch.
 */

/** The geometry every card and line agrees on. The chart hands these to CSS, so they cannot drift. */
export const GEOMETRY = {
  cardWidth: 188,
  /** One person on a card: name, years, and a 1px rule above every row after the first. */
  rowHeight: 45,
  /** Where a line meets a row, measured from the row's top: the middle of the name and years. */
  stub: 23,
  /** The gap between two columns of cards, which the lines run through. */
  gapX: 44,
  /** Between two cards in the same column. */
  gapY: 8,
  /** The "with X" line above the first child of a group. */
  labelHeight: 18,
  border: 1,
};

const G = GEOMETRY;
const pitch = G.cardWidth + G.gapX;

export type Card = {
  /** The couple's ids, sorted: the same two people are the same card whichever side reached them. */
  key: string;
  rows: FamilyTreeNode[];
  /** The children, in the order drawn, each entering this card's line at their own row. */
  kids: Link[];
  /** For each row, the card of that person's parents, when the chart goes up from here. */
  ups: (Card | undefined)[];
};

/** A line into `card` at `row`; `label` heads a group of children and sits above the first. */
export type Link = { card: Card; row: number; label: string };

export type Placed = {
  card: Card;
  col: number;
  x: number;
  top: number;
  height: number;
  label: string;
  /** Cards descended from this one, counted once; a fold hides those not reached another way. */
  below: number;
};

export type Edge = {
  from: Placed;
  to: Placed;
  row: number;
  /** False for a card already drawn from elsewhere: the line that makes the family a loop. */
  primary: boolean;
  /** On a loop line, the card drawn once instead of twice: the child side below, the parent above. */
  shared?: Placed;
};

export type Layout = {
  cards: Placed[];
  edges: Edge[];
  /** [first column, last column], relative to the centre's column 0. */
  cols: [number, number];
  width: number;
  height: number;
};

const cardKey = (rows: FamilyTreeNode[]) =>
  rows
    .map((r) => r.id)
    .sort((a, b) => a - b)
    .join(',');

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

export type Graph = {
  /** The column the chart is centred on: the person among their siblings, or alone. */
  centre: Link[];
  /** The couple that were the centre's parents, when known: every centre card hangs from it. */
  parents?: Card;
  focus: number;
};

/**
 * Turn the flat node list into couples and the lines between them.
 *
 * A person's card shows them with their partner, or when there is none on record (a widow, say) the
 * one person they had children with. Children are grouped by their pair of parents; a group is
 * labelled when the card does not already say who the other parent was.
 */
export function buildGraph(list: FamilyTreeNode[], focus: number): Graph {
  const map = new Map(list.map((n) => [n.id, n]));
  const index = childIndex(list);
  const cards = new Map<string, Card>();

  function spouseOf(n: FamilyTreeNode): FamilyTreeNode | undefined {
    const partner = map.get(n.partner);
    if (partner) return partner;
    const others = new Set((index.get(n.id) ?? []).map((k) => (k.p1 === n.id ? k.p2 : k.p1)));
    return others.size === 1 ? map.get([...others][0]) : undefined;
  }

  const nameWithYears = (n: FamilyTreeNode) => {
    const years = lifespan(n);
    return years ? `${n.name} (${years})` : n.name;
  };

  /** Children of the card's people, grouped by their two parents, the card's own couple first. */
  function kidsOf(card: Card): Link[] {
    const ids = card.rows.map((r) => r.id);
    const kids = [
      ...new Map(ids.flatMap((id) => index.get(id) ?? []).map((k) => [k.id, k])).values(),
    ];
    const groups = new Map<string, FamilyTreeNode[]>();
    for (const k of kids.sort((a, b) => a.born - b.born || a.id - b.id)) {
      const g = [k.p1, k.p2]
        .filter((p) => p > 0)
        .sort((a, b) => a - b)
        .join(',');
      const list = groups.get(g);
      if (list) list.push(k);
      else groups.set(g, [k]);
    }
    const own = card.key;
    const ordered = [...groups].sort(([a, ak], [b, bk]) =>
      a === own ? -1 : b === own ? 1 : ak[0].born - bk[0].born,
    );
    const obvious = groups.size === 1 && (ordered[0][0] === own || card.rows.length === 1);
    return ordered.flatMap(([, children]) => {
      const k = children[0];
      // Whose child this is on the card, and who the other parent was.
      const mine = card.rows.find((r) => r.id === k.p1 || r.id === k.p2)!;
      const other = map.get(k.p1 === mine.id ? k.p2 : k.p1);
      let label = '';
      if (!obvious) {
        const who = other ? `with ${nameWithYears(other)}` : 'other parent unknown';
        label = card.rows.length > 1 && mine !== card.rows[0] ? `${mine.name}, ${who}` : who;
      }
      return children.map((c, i) => {
        const card = personCard(c);
        return {
          card,
          row: card.rows.findIndex((r) => r.id === c.id),
          label: i === 0 ? label : '',
        };
      });
    });
  }

  /** The card a descendant (or the centre, or a sibling) is drawn on. */
  function personCard(n: FamilyTreeNode): Card {
    const spouse = spouseOf(n);
    const rows = spouse ? [n, spouse] : [n];
    const key = cardKey(rows);
    // The spouse's own card, reached first along another line: the same couple.
    const existing = cards.get(key);
    if (existing) return existing;
    const card: Card = { key, rows, kids: [], ups: [] };
    cards.set(key, card);
    card.kids = kidsOf(card);
    return card;
  }

  /** The card of `n`'s parents, and theirs above it. */
  function parentsCard(n: FamilyTreeNode): Card | undefined {
    const rows = [n.p1, n.p2]
      .filter((p, i, all) => p > 0 && all.indexOf(p) === i)
      .map((p) => map.get(p))
      .filter((p): p is FamilyTreeNode => !!p);
    if (rows.length === 0) return undefined;
    const key = `a${cardKey(rows)}`;
    const existing = cards.get(key);
    if (existing) return existing;
    const card: Card = { key, rows, kids: [], ups: [] };
    cards.set(key, card);
    card.ups = rows.map(parentsCard);
    return card;
  }

  const me = map.get(focus);
  if (!me) return { centre: [], focus };
  const parents = parentsCard(me);
  if (!parents) {
    const card = personCard(me);
    return { centre: [{ card, row: 0, label: '' }], focus };
  }

  // The centre column: every child of either parent, whole siblings first, half-siblings labelled
  // by the parent they share (the tree does not carry a half-sibling's other parent).
  const mine = parents.rows.map((r) => r.id);
  const siblings = [
    ...new Map(mine.flatMap((id) => index.get(id) ?? []).map((k) => [k.id, k])).values(),
  ];
  const whole = (k: FamilyTreeNode) => mine.every((p) => k.p1 === p || k.p2 === p);
  const groups = [siblings.filter(whole), siblings.filter((k) => !whole(k))];
  const centre = groups.flatMap((g, gi) =>
    g.map((k, i): Link => {
      const card = personCard(k);
      let label = '';
      if (i === 0 && groups[1].length > 0) {
        const shared = mine.filter((p) => k.p1 === p || k.p2 === p);
        label =
          gi === 0 ? 'full' : `half, through ${shared.map((p) => map.get(p)!.name).join(' & ')}`;
      }
      return { card, row: card.rows.findIndex((r) => r.id === k.id), label };
    }),
  );
  return { centre, parents, focus };
}

/**
 * Place every card, in pixels.
 *
 * Columns first: the centre is column 0, a descendant sits one column right of the latest of its
 * parents, an ancestor one column left of the earliest of its children. A couple reached at two
 * depths gets one column, so the headings are exact only along one of its lines.
 *
 * Then rows, walking the chart in the order the old tree drew it. A card lines up with the card that
 * first reached it, but never above anything already placed in the columns from its parent's next
 * one outwards. That is what keeps each branch a block: a younger sibling starts below the whole of
 * the elder one's descent, so two parents' lines never share a stretch of the same gap. A card
 * reached a second time is not placed again; the walk records the extra line and stops there.
 */
export function layoutChart(graph: Graph, collapsed: ReadonlySet<string> = new Set()): Layout {
  const heightOf = (c: Card) => c.rows.length * G.rowHeight + 2 * G.border;

  // Columns, over the whole graph so a fold never moves anything sideways.
  const col = new Map<Card, number>();
  const centreCards = new Set(graph.centre.map((l) => l.card));
  for (const c of centreCards) col.set(c, 0);
  const incoming = new Map<Card, Card[]>();
  const seen = new Set<Card>();
  const walk = (c: Card) => {
    if (seen.has(c)) return;
    seen.add(c);
    for (const k of c.kids) {
      if (!incoming.has(k.card)) incoming.set(k.card, []);
      incoming.get(k.card)!.push(c);
      walk(k.card);
    }
  };
  centreCards.forEach(walk);
  const right = (c: Card, stack = new Set<Card>()): number => {
    const known = col.get(c);
    if (known !== undefined) return known;
    if (stack.has(c)) return 0; // A loop in the data itself; genealogies have none.
    stack.add(c);
    const n = Math.max(...(incoming.get(c) ?? []).map((p) => right(p, stack))) + 1;
    col.set(c, n);
    return n;
  };
  seen.forEach((c) => right(c));

  if (graph.parents) {
    // An ancestor sits left of the earliest of the cards it is a parent in.
    const below = new Map<Card, Card[]>([[graph.parents, []]]);
    const up = (c: Card) =>
      c.ups.forEach((a) => {
        if (!a) return;
        const first = !below.has(a);
        if (first) below.set(a, []);
        below.get(a)!.push(c);
        if (first) up(a);
      });
    up(graph.parents);
    col.set(graph.parents, -1);
    const left = (a: Card): number => {
      const known = col.get(a);
      if (known !== undefined) return known;
      const n = Math.min(...below.get(a)!.map(left)) - 1;
      col.set(a, n);
      return n;
    };
    below.forEach((_, a) => left(a));
  }

  // Everything a fold would hide, counted once however many lines lead to it.
  const reach = new Map<Card, Set<Card>>();
  const reachable = (c: Card): Set<Card> => {
    let r = reach.get(c);
    if (r) return r;
    r = new Set();
    reach.set(c, r);
    for (const k of c.kids) {
      r.add(k.card);
      reachable(k.card).forEach((x) => r!.add(x));
    }
    return r;
  };

  const placed = new Map<Card, Placed>();
  const edges: Edge[] = [];
  const bottomRight = new Map<number, number>();
  const bottomLeft = new Map<number, number>();
  const colsOf = (m: Map<number, number>, ok: (k: number) => boolean) =>
    Math.max(0, ...[...m].filter(([k]) => ok(k)).map(([, v]) => v));

  const place = (card: Card, top: number, label: string): Placed => {
    const c = col.get(card)!;
    const p: Placed = {
      card,
      col: c,
      x: 0,
      top,
      height: heightOf(card),
      label,
      below: reachable(card).size,
    };
    placed.set(card, p);
    return p;
  };

  // Right: the centre column and every generation after it.
  const down = (link: Link, from: Placed | undefined) => {
    const existing = placed.get(link.card);
    if (existing) {
      if (from) edges.push({ from, to: existing, row: link.row, primary: false, shared: existing });
      return;
    }
    const c = col.get(link.card)!;
    const lo = from ? from.col + 1 : c;
    const band = link.label ? G.labelHeight : 0;
    const anchor = (from ? from.top : 0) - link.row * G.rowHeight;
    const top =
      Math.max(
        anchor,
        colsOf(bottomRight, (k) => k >= lo),
      ) + band;
    const p = place(link.card, top, link.label);
    for (let k = lo; k <= c; k++) bottomRight.set(k, top + p.height + G.gapY);
    if (from) edges.push({ from, to: p, row: link.row, primary: true });
    if (!collapsed.has(link.card.key)) for (const k of link.card.kids) down(k, p);
  };

  // Left: the ancestors, mirrored.
  const upward = (card: Card | undefined, to: Placed, row: number) => {
    if (!card) return;
    const existing = placed.get(card);
    if (existing) {
      edges.push({ from: existing, to, row, primary: false, shared: existing });
      return;
    }
    const c = col.get(card)!;
    const hi = to.col - 1;
    const top = Math.max(
      to.top + row * G.rowHeight,
      colsOf(bottomLeft, (k) => k <= hi),
    );
    const p = place(card, top, '');
    for (let k = c; k <= hi; k++) bottomLeft.set(k, top + p.height + G.gapY);
    edges.push({ from: p, to, row, primary: true });
    card.ups.forEach((a, i) => upward(a, p, i));
  };

  if (graph.parents) {
    const p = place(graph.parents, 0, '');
    bottomLeft.set(-1, p.height + G.gapY);
    for (const link of graph.centre) down(link, p);
    graph.parents.ups.forEach((a, i) => upward(a, p, i));
  } else {
    for (const link of graph.centre) down(link, undefined);
  }

  const cards = [...placed.values()];
  const cols: [number, number] = [
    Math.min(0, ...cards.map((p) => p.col)),
    Math.max(0, ...cards.map((p) => p.col)),
  ];
  for (const p of cards) p.x = (p.col - cols[0]) * pitch;
  return {
    cards,
    edges,
    cols,
    width: (cols[1] - cols[0] + 1) * pitch - G.gapX,
    height: Math.max(0, ...cards.map((p) => p.top + p.height)),
  };
}

/** The SVG path of one line: an elbow through the gap for a tree line, a curve for a loop. */
export function edgePath(e: Edge): string {
  const sx = e.from.x + G.cardWidth;
  const sy = e.from.top + G.stub;
  const ex = e.to.x;
  const ey = e.to.top + e.row * G.rowHeight + G.stub;
  if (e.primary) {
    const mid = sx + G.gapX / 2;
    return `M${sx} ${sy}H${mid}V${ey}H${ex}`;
  }
  const dx = Math.max(G.gapX, (ex - sx) / 2);
  return `M${sx} ${sy}C${sx + dx} ${sy} ${ex - dx} ${ey} ${ex} ${ey}`;
}

/**
 * The people still on more than one card once couples are merged: someone who married twice is
 * drawn with each partner. Slots go in the order the chart places them, so colours stay put.
 */
export function peopleOnSeveralCards(layout: Layout): Map<number, { slot: number; count: number }> {
  const count = new Map<number, number>();
  for (const p of layout.cards)
    for (const r of p.card.rows) count.set(r.id, (count.get(r.id) ?? 0) + 1);
  const out = new Map<number, { slot: number; count: number }>();
  for (const [id, n] of count) if (n > 1) out.set(id, { slot: out.size, count: n });
  return out;
}

/**
 * The cards drawn once instead of several times, with how many lines lead into (or, for an ancestor,
 * out of) each. Slots go in the order the chart places them, so colours stay put.
 */
export function sharedCards(layout: Layout): Map<string, { slot: number; lines: number }> {
  const out = new Map<string, { slot: number; lines: number }>();
  const order = new Map(layout.cards.map((p, i) => [p.card.key, i]));
  const shared = layout.edges
    .filter((e) => e.shared)
    .map((e) => e.shared!)
    .sort((a, b) => order.get(a.card.key)! - order.get(b.card.key)!);
  for (const p of shared) {
    const known = out.get(p.card.key);
    if (known) known.lines++;
    else out.set(p.card.key, { slot: out.size, lines: 2 });
  }
  return out;
}

/** A column heading, by generation counted from the person the chart is centred on. */
export function generationName(g: number): string {
  if (g === 0) return 'Siblings';
  const [one, two] = g < 0 ? ['Parents', 'Grandparents'] : ['Children', 'Grandchildren'];
  const n = Math.abs(g);
  if (n === 1) return one;
  if (n === 2) return two;
  const lower = two.toLowerCase();
  return n === 3 ? `Great-${lower}` : n === 4 ? `Great-great-${lower}` : `${n - 2}× great-${lower}`;
}
