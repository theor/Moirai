<script lang="ts">
  import { moiraiStore } from '$lib/connection';
  import type { FamilyTreeNode } from '$lib/connection';
  import { page } from '$app/stores';
  import { selectedEntity } from '$lib/utils';
  import FamilyNode from '../../components/FamilyNode.svelte';

  const maxDepth = 5;

  let selected = -1;
  $: selected = selectedEntity($page).getNumber();

  // Refetch when the selection or the simulation year changes (new people may have been born).
  let tree: Promise<FamilyTreeNode[]> | undefined;
  $: selected,
    $moiraiStore.year,
    (tree =
      selected > 0 ? $moiraiStore.conn?.getFamilyTree(selected, maxDepth) : undefined);

  function buildMap(list: FamilyTreeNode[]): Map<number, FamilyTreeNode> {
    return new Map(list.map((n) => [n.id, n]));
  }

  // Children = nodes whose parent1/parent2 is the focus entity.
  function childrenOf(list: FamilyTreeNode[], focus: number): FamilyTreeNode[] {
    return list.filter((n) => n.p1 === focus || n.p2 === focus);
  }

  function select(id: number) {
    selectedEntity($page).setNumber(id);
  }
</script>

<div class="ftree-page">
  {#if selected <= 0}
    <p class="opacity-60 p-4">
      No entity selected. Select a Person (click an entity chip in the Records page or the
      Details panel) to view their family tree.
    </p>
  {:else}
    {#await tree}
      <p class="opacity-60 p-4">Loading family tree…</p>
    {:then list}
      {#if !list || list.length === 0}
        <p class="opacity-60 p-4">No family data for #{selected}.</p>
      {:else}
        {@const map = buildMap(list)}
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
                  class="chip variant-soft-secondary"
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
    {/await}
  {/if}
</div>

<style>
  .ftree-scroll {
    height: 86vh;
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
