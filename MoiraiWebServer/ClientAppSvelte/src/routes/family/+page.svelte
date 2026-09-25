<script lang="ts">
  import { moiraiStore, settledYear } from '$lib/connection';
  import { SvelteSet } from 'svelte/reactivity';
  import { notable } from '$lib/notable';
  import { page } from '$app/state';
  import { selectedEntity } from '$lib/utils';
  import FamilyChart from '../../components/FamilyChart.svelte';
  import NotableList from '../../components/NotableList.svelte';

  const maxDepth = 5;

  const selected = $derived(selectedEntity(page).getNumber());

  // Refetch when the selection or the settled year changes (new people may have been born), and when
  // `attempt` is bumped by the retry button in the error branch. The settled year rather than the raw
  // one: a tree walk is not cheap and a pass would otherwise trigger one per feed tick.
  let attempt = $state(0);
  const tree = $derived(familyTreeFor(selected, $settledYear, attempt));
  function familyTreeFor(sel: number, _year: number, _attempt: number) {
    return sel > 0 ? $moiraiStore.conn?.getFamilyTree(sel, maxDepth) : undefined;
  }

  // Folded cards, by card key. Kept here rather than in the chart because the tree is refetched, and
  // the chart rebuilt, whenever the year settles.
  const collapsed = new SvelteSet<string>();

  // Whether loops in the family are coloured in. A viewer's preference, not the world's, so it is
  // remembered in this browser; storage can be missing or refuse (private windows), and then the
  // switch simply starts off.
  const LOOPS_KEY = 'moirai.family.highlightLoops';
  let highlightLoops = $state(readLoops());
  function readLoops() {
    try {
      return localStorage.getItem(LOOPS_KEY) === '1';
    } catch {
      return false;
    }
  }
  $effect(() => {
    try {
      localStorage.setItem(LOOPS_KEY, highlightLoops ? '1' : '0');
    } catch {
      // Not remembered; nothing else depends on it.
    }
  });

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
          #{selected} has no family tree: its type has no parents. Name them with
          <code class="code">@parents(a, b)</code> on the type, or call them parent1 and parent2.
        </p>
      {:else}
        <FamilyChart {list} focus={selected} {collapsed} bind:highlight={highlightLoops} />
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
