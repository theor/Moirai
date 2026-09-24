<script lang="ts">
  import { moiraiStore, settledYear } from '$lib/connection';
  import {
    ancestry,
    ancestryDepth,
    byId,
    childIndex,
    descendantChart,
    descendantDepth,
    generationName,
    siblingGroups,
  } from '$lib/family';
  import type { FamilyTreeNode } from '$lib/types';
  import { SvelteSet } from 'svelte/reactivity';
  import { notable } from '$lib/notable';
  import { page } from '$app/stores';
  import { selectedEntity } from '$lib/utils';
  import ChartAncestry from '../../components/ChartAncestry.svelte';
  import ChartBranch from '../../components/ChartBranch.svelte';
  import ChartGroups from '../../components/ChartGroups.svelte';
  import NotableList from '../../components/NotableList.svelte';

  const maxDepth = 5;

  const selected = $derived(selectedEntity($page).getNumber());

  // Refetch when the selection or the settled year changes (new people may have been born), and when
  // `attempt` is bumped by the retry button in the error branch. The settled year rather than the raw
  // one: a tree walk is not cheap and a pass would otherwise trigger one per feed tick.
  let attempt = $state(0);
  const tree = $derived(familyTreeFor(selected, $settledYear, attempt));
  function familyTreeFor(sel: number, _year: number, _attempt: number) {
    return sel > 0 ? $moiraiStore.conn?.getFamilyTree(sel, maxDepth) : undefined;
  }

  // Folded branches, by id. Kept here rather than in each branch because the tree is refetched, and
  // every card rebuilt, whenever the year settles.
  const collapsed = new SvelteSet<number>();

  /**
   * Lay the tree out as columns of generations: the parents' ancestry to the left, then the column of
   * the person and their brothers and sisters, then one column per generation of descendants.
   */
  function chartFor(list: FamilyTreeNode[], id: number) {
    const map = byId(list);
    const index = childIndex(list);
    const up = ancestry(id, map);
    const siblings = up ? siblingGroups(id, map, index) : [];
    const self = up ? undefined : descendantChart(id, map, index);
    const branches = up ? siblings.flatMap((g) => g.branches) : self ? [self] : [];
    const down = Math.max(0, ...branches.map(descendantDepth));
    const alone = branches.length <= 1;
    const columns = [];
    for (let g = -ancestryDepth(up); g <= down; g++)
      columns.push(g === 0 && alone ? (map.get(id)?.name ?? '') : generationName(g));
    return { ancestry: up, siblings, self, columns };
  }

  // Only types with parents have a tree, so only they are worth suggesting.
  const withFamily = $derived($notable.filter((g) => g.hasFamily));

  // A hub method that throws reaches us as a HubException whose message is the server's generic
  // "unexpected error" text, so show whatever we get rather than inventing a friendlier line.
  function errorText(err: unknown): string {
    return err instanceof Error ? err.message : String(err);
  }
</script>

<div class="h-full overflow-auto">
  {#if selected <= 0}
    <div class="max-w-4xl">
      <h1 class="h4 mb-1">Family</h1>
      <p class="text-sm text-surface-600 mb-5">
        Pick someone to see their whole family: ancestors, siblings, partner, and every generation
        after them. These are the people the story mentions most.
      </p>
      <NotableList groups={withFamily} />
    </div>
  {:else}
    {#await tree}
      <p class="text-surface-600 p-4">Loading family tree…</p>
    {:then list}
      {#if !list || list.length === 0}
        <p class="text-surface-600 p-4">
          #{selected} has no family tree: its type declares no parent1/parent2.
        </p>
      {:else}
        {@const chart = chartFor(list, selected)}
        <div class="fchart" style:--cols={chart.columns.length}>
          <div class="fgens" aria-hidden="true">
            {#each chart.columns as name, i (i)}
              <div class="fgen">{name}</div>
            {/each}
          </div>
          <div class="fbranch">
            {#if chart.ancestry}
              <ChartAncestry ancestry={chart.ancestry} />
              <ChartGroups groups={chart.siblings} focus={selected} {collapsed} />
            {:else if chart.self}
              <ChartBranch branch={chart.self} focus={selected} {collapsed} />
            {/if}
          </div>
        </div>
      {/if}
    {:catch error}
      <div class="p-4">
        <aside class="card preset-filled-error-500 p-4">
          <p class="font-bold">Could not load the family tree for #{selected}.</p>
          <p class="text-sm">{errorText(error)}</p>
        </aside>
        <button type="button" class="btn preset-tonal mt-3" onclick={() => (attempt += 1)}>
          Retry
        </button>
      </div>
    {/await}
  {/if}
</div>

<style>
  /*
   * The chart is nested flex rows, one card and then the column of whatever hangs off it, so every
   * card being the same width is what makes the generations line up. The connectors are borders on
   * the items of each column: a stub into each card and a spine joining them, both at --stub-y, the
   * height of a card's first name, so a line runs straight from a parent to its eldest child.
   */
  .fchart {
    --card-w: 11.75rem;
    --out: 0.75rem;
    --stub: 0.875rem;
    --stub-y: 1.4rem;
    --pitch: calc(var(--card-w) + var(--out) + var(--stub));
    --line: var(--color-surface-400);
    width: max-content;
    padding: 0 1rem 2rem;
  }
  .fgens {
    position: sticky;
    top: 0;
    z-index: 2;
    display: flex;
    margin-bottom: 0.75rem;
    padding: 0.75rem 0 0.375rem;
    background: var(--color-surface-50);
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
  .fchart :global(.fbranch) {
    display: flex;
    align-items: flex-start;
  }
  .fchart :global(.fkids) {
    position: relative;
    display: flex;
    flex-direction: column;
    padding-left: var(--out);
  }
  /* The line out of the parent's card, to the spine. */
  .fchart :global(.fkids::before) {
    content: '';
    position: absolute;
    left: 0;
    top: var(--stub-y);
    width: var(--out);
    border-top: 1px solid var(--line);
  }
  .fchart :global(.fitem) {
    position: relative;
    padding: 0 0 0.5rem var(--stub);
  }
  .fchart :global(.fitem:last-child) {
    padding-bottom: 0;
  }
  /* The stub into this card. */
  .fchart :global(.fitem:not(.flabel)::before) {
    content: '';
    position: absolute;
    left: 0;
    top: var(--stub-y);
    width: var(--stub);
    border-top: 1px solid var(--line);
  }
  /* The spine: from the first stub to the last, through everything between. */
  .fchart :global(.fitem::after) {
    content: '';
    position: absolute;
    left: 0;
    top: 0;
    bottom: 0;
    border-left: 1px solid var(--line);
  }
  .fchart :global(.fitem:first-child::after) {
    top: var(--stub-y);
  }
  .fchart :global(.fitem:last-child::after) {
    bottom: auto;
    height: var(--stub-y);
  }
  .fchart :global(.fitem:first-child:last-child::after) {
    display: none;
  }
  .fchart :global(.flabel) {
    min-height: calc(var(--stub-y) + 0.25rem);
    padding-top: 0.5rem;
    padding-bottom: 0.25rem;
    font-size: 0.75rem;
    font-style: italic;
    color: var(--color-surface-600);
  }
  .fchart :global(.flabel:first-child) {
    padding-top: 0;
  }

  /* Ancestry is the same chart mirrored: the columns grow leftwards and right-align on the card. */
  .fchart :global(.fup) {
    align-items: flex-end;
    padding-left: 0;
    padding-right: var(--out);
  }
  .fchart :global(.fup::before) {
    left: auto;
    right: 0;
  }
  .fchart :global(.fup > .fitem) {
    padding-left: 0;
    padding-right: var(--stub);
  }
  .fchart :global(.fup > .fitem::before) {
    left: auto;
    right: 0;
  }
  .fchart :global(.fup > .fitem::after) {
    left: auto;
    right: 0;
  }
</style>
