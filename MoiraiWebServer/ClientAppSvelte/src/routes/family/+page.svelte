<script lang="ts">
  import { moiraiStore, settledYear } from '$lib/connection';
  import { byId, childrenOf } from '$lib/family';
  import type { FamilyTreeNode } from '$lib/connection';
  import { page } from '$app/stores';
  import { selectedEntity } from '$lib/utils';
  import FamilyNode from '../../components/FamilyNode.svelte';

  const maxDepth = 5;

  $: selected = selectedEntity($page).getNumber();

  // Refetch when the selection or the settled year changes (new people may have been born), and when
  // `attempt` is bumped by the retry button in the error branch. The settled year rather than the raw
  // one: a tree walk is not cheap and a pass would otherwise trigger one per feed tick.
  let attempt = 0;
  let tree: Promise<FamilyTreeNode[]> | undefined;
  $: tree = familyTreeFor(selected, $settledYear, attempt);
  function familyTreeFor(sel: number, _year: number, _attempt: number) {
    return sel > 0 ? $moiraiStore.conn?.getFamilyTree(sel, maxDepth) : undefined;
  }

  // A hub method that throws reaches us as a HubException whose message is the server's generic
  // "unexpected error" text, so show whatever we get rather than inventing a friendlier line.
  function errorText(err: unknown): string {
    return err instanceof Error ? err.message : String(err);
  }

  function select(id: number) {
    selectedEntity($page).setNumber(id);
  }
</script>

<div class="ftree-page">
  {#if selected <= 0}
    <p class="opacity-60 p-4">
      No entity selected. Select a Person (click an entity chip in the Records page or the Details
      panel) to view their family tree.
    </p>
  {:else}
    {#await tree}
      <p class="opacity-60 p-4">Loading family tree…</p>
    {:then list}
      {#if !list || list.length === 0}
        <p class="opacity-60 p-4">No family data for #{selected}.</p>
      {:else}
        {@const map = byId(list)}
        {@const kids = childrenOf(list, selected)}
        <div class="ftree-scroll">
          <h4 class="h4 mb-2 opacity-70">Ancestors</h4>
          <div class="ftree-ancestors">
            <FamilyNode nodeId={selected} nodes={map} focus={selected} />
          </div>

          {#if kids.length > 0}
            <hr class="!my-4" />
            <h4 class="h4 mb-2 opacity-70">Children</h4>
            <div class="ftree-children">
              {#each kids as kid (kid.id)}
                <button
                  type="button"
                  class="chip preset-tonal-secondary"
                  on:click={() => select(kid.id)}
                  title={`#${kid.id} — click to re-root`}
                >
                  {kid.name}
                </button>
              {/each}
            </div>
          {/if}
        </div>
      {/if}
    {:catch error}
      <div class="p-4">
        <aside class="card preset-filled-error-500 p-4">
          <p class="font-bold">Could not load the family tree for #{selected}.</p>
          <p class="text-sm">{errorText(error)}</p>
        </aside>
        <button type="button" class="btn preset-tonal mt-3" on:click={() => (attempt += 1)}>
          Retry
        </button>
      </div>
    {/await}
  {/if}
</div>

<style>
  .ftree-page {
    height: 100%;
  }
  .ftree-scroll {
    height: 100%;
    overflow: auto;
    padding: 1rem;
  }
  .ftree-ancestors {
    display: flex;
    justify-content: center;
    min-width: min-content;
    padding: 1rem;
  }
  .ftree-children {
    display: flex;
    flex-wrap: wrap;
    gap: 0.5rem;
  }
</style>
