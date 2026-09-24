<script lang="ts">
  import type { FamilyTreeNode } from '$lib/types';
  import {
    GEOMETRY as G,
    buildGraph,
    edgePath,
    generationName,
    layoutChart,
    peopleOnSeveralCards,
    sharedCards,
    type Edge,
  } from '$lib/family-chart';
  import ChartCard from './ChartCard.svelte';

  /**
   * The family chart, drawn: cards at the positions `layoutChart` computed, over one SVG holding
   * every line. Tree lines are grey elbows; a loop line, into a card already drawn from elsewhere, is
   * a curve in that card's hue, faint until the pointer is on either end, because a family that
   * intermarries has dozens of them and they would otherwise bury the tree. Hovering a card brings
   * out every line it has, tree and loop alike.
   *
   * The loop colours are a switch, off by default: in a family that intermarries a lot they are most
   * of what you see. Off, the chart is still merged and the loop lines are still there, in grey,
   * because a merged couple with no line from its second parent would read as having only one.
   *
   * `collapsed` holds card keys and lives on the page, which outlasts the refetch each settled year
   * triggers.
   */
  let {
    list,
    focus,
    collapsed,
    highlight = $bindable(false),
  }: {
    list: FamilyTreeNode[];
    focus: number;
    collapsed: Set<string>;
    highlight?: boolean;
  } = $props();

  const graph = $derived(buildGraph(list, focus));
  const layout = $derived(layoutChart(graph, collapsed));
  const shared = $derived(sharedCards(layout));
  const repeats = $derived(peopleOnSeveralCards(layout));

  // Far enough apart on the wheel to tell ten apart; an eleventh reuses the first, and hovering
  // tells the two apart.
  const hues = [25, 150, 265, 80, 330, 200, 110, 295, 55, 235];
  const hueOf = (slot: number) => hues[slot % hues.length];
  // People on several cards take the hues after the shared cards', so the two never coincide early.
  const repeat = (id: number) => {
    const r = highlight ? repeats.get(id) : undefined;
    return r ? { hue: hueOf(shared.size + r.slot), count: r.count } : undefined;
  };

  let hover = $state('');
  const touches = (e: Edge, key: string) =>
    key !== '' && (e.from.card.key === key || e.to.card.key === key);
  // The hovered card's lines take its hue when it has one, and the theme's accent when it does not.
  const hoverHue = $derived.by(() => {
    const s = highlight ? shared.get(hover) : undefined;
    return s ? hueOf(s.slot) : undefined;
  });
  // A shared card lights up with its lines, from either end.
  const hotCards = $derived(
    new Set(
      layout.edges.filter((e) => e.shared && touches(e, hover)).map((e) => e.shared!.card.key),
    ),
  );

  const columns = $derived.by(() => {
    const alone = !layout.cards.some(
      (p) => p.col === 0 && !p.card.rows.some((r) => r.id === focus),
    );
    const name = list.find((n) => n.id === focus)?.name ?? '';
    return Array.from({ length: layout.cols[1] - layout.cols[0] + 1 }, (_, i) => {
      const g = layout.cols[0] + i;
      return g === 0 && alone ? name : generationName(g);
    });
  });

  function toggle(key: string) {
    if (collapsed.has(key)) collapsed.delete(key);
    else collapsed.add(key);
  }
</script>

<div
  class="fchart"
  style:--card-w="{G.cardWidth}px"
  style:--row-h="{G.rowHeight}px"
  style:--stub="{G.stub}px"
  style:--pitch="{G.cardWidth + G.gapX}px"
>
  {#if shared.size > 0 || repeats.size > 0}
    <label class="floops">
      <input type="checkbox" class="checkbox" bind:checked={highlight} />
      Highlight loops
      <span class="text-surface-600">
        ({[
          shared.size
            ? `${shared.size} ${shared.size === 1 ? 'couple' : 'couples'} reached twice or more`
            : '',
          repeats.size
            ? `${repeats.size} ${repeats.size === 1 ? 'person' : 'people'} on several cards`
            : '',
        ]
          .filter(Boolean)
          .join(', ')})
      </span>
    </label>
  {/if}
  {#if highlight && (shared.size > 0 || repeats.size > 0)}
    <p class="fnote">
      {#if shared.size > 0}
        This family loops back on itself: {shared.size === 1
          ? 'one couple is'
          : `${shared.size} couples are`} reached along more than one line, because relatives married
        or both sides share an ancestor. Each is drawn once, in a colour of its own, and its extra lines
        are the coloured curves; hover a card to bring them out.
      {/if}
      {#if repeats.size > 0}
        {repeats.size === 1 ? 'One person is' : `${repeats.size} people are`} on more than one card, one
        per partner; click the ×N to go to the next.
      {/if}
    </p>
  {/if}
  <div class="fgens" aria-hidden="true">
    {#each columns as name, i (i)}
      <div class="fgen">{name}</div>
    {/each}
  </div>
  <div class="fcanvas" style:width="{layout.width}px" style:height="{layout.height + 16}px">
    <svg class="flines" width={layout.width} height={layout.height + 16} aria-hidden="true">
      {#each layout.edges as e, i (i)}
        {#if e.primary}
          <path d={edgePath(e)} class="tree" />
        {/if}
      {/each}
      {#each layout.edges as e, i (i)}
        {#if !e.primary}
          <path
            d={edgePath(e)}
            class="loop"
            class:plain={!highlight}
            class:hot={touches(e, hover)}
            style:--hue={highlight ? hueOf(shared.get(e.shared!.card.key)!.slot) : undefined}
          />
        {/if}
      {/each}
      <!-- The hovered card's own tree lines, over everything else: a spine is shared by siblings, and
           the grey one underneath would otherwise hide which stretch of it belongs to this card. -->
      {#each layout.edges as e, i (i)}
        {#if e.primary && touches(e, hover)}
          <path
            d={edgePath(e)}
            class="tree hot"
            class:tinted={hoverHue !== undefined}
            style:--hue={hoverHue}
          />
        {/if}
      {/each}
    </svg>
    {#each layout.cards as p (p.card.key)}
      {@const s = shared.get(p.card.key)}
      {#if p.label}
        <div class="flabel" style:left="{p.x}px" style:top="{p.top - G.labelHeight}px">
          {p.label}
        </div>
      {/if}
      <div class="fslot" style:left="{p.x}px" style:top="{p.top}px">
        <ChartCard
          placed={p}
          {focus}
          collapsed={collapsed.has(p.card.key)}
          shared={highlight && s ? { hue: hueOf(s.slot), lines: s.lines } : undefined}
          {repeat}
          hot={hotCards.has(p.card.key)}
          ontoggle={() => toggle(p.card.key)}
          onhover={(on) => (hover = on ? p.card.key : hover === p.card.key ? '' : hover)}
        />
      </div>
    {/each}
  </div>
</div>

<style>
  .fchart {
    width: max-content;
    padding: 0 1rem 2rem;
  }
  .floops {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    margin-top: 0.75rem;
    font-size: 0.8125rem;
    cursor: pointer;
    width: max-content;
  }
  .fnote {
    max-width: 52rem;
    margin: 0.75rem 0 0;
    font-size: 0.8125rem;
    color: var(--color-surface-700);
  }
  .fgens {
    position: sticky;
    top: 0;
    z-index: 3;
    display: flex;
    margin-bottom: 0.75rem;
    padding: 0.75rem 0 0.375rem;
    background: white;
    border-bottom: 1px solid var(--color-surface-200);
  }
  .fgen {
    width: var(--pitch);
    flex: none;
    padding-left: 0.125rem;
    font-size: 0.75rem;
    font-weight: 600;
    letter-spacing: 0.02em;
    text-transform: uppercase;
    color: var(--color-surface-600);
  }
  .fcanvas {
    position: relative;
  }
  .flines {
    position: absolute;
    inset: 0;
    pointer-events: none;
    overflow: visible;
  }
  .flines path {
    fill: none;
  }
  .tree {
    stroke: var(--color-surface-400);
    stroke-width: 1;
  }
  .tree.hot {
    stroke: var(--color-primary-500);
    stroke-width: 2.5;
  }
  .tree.hot.tinted {
    stroke: oklch(0.62 0.16 var(--hue));
  }
  .loop {
    stroke: oklch(0.62 0.16 var(--hue));
    stroke-width: 1.5;
    stroke-dasharray: 5 4;
    opacity: 0.3;
  }
  .loop.hot {
    stroke-width: 2.5;
    opacity: 1;
  }
  .loop.plain {
    stroke: var(--color-surface-400);
    stroke-width: 1;
    opacity: 1;
  }
  .loop.plain.hot {
    stroke: var(--color-primary-500);
    stroke-width: 2.5;
  }
  .fslot,
  .flabel {
    position: absolute;
    z-index: 1;
  }
  .flabel {
    width: var(--card-w);
    height: 18px;
    overflow: hidden;
    white-space: nowrap;
    text-overflow: ellipsis;
    font-size: 0.75rem;
    line-height: 18px;
    font-style: italic;
    color: var(--color-surface-600);
  }
</style>
