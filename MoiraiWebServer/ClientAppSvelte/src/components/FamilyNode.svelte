<script lang="ts">
  import { selectedEntity } from '$lib/utils';
  import { page } from '$app/stores';
  import type { FamilyTreeNode } from '$lib/connection';

  // Recursively renders one person and their ancestors (parents above the child),
  // producing a classic upward genealogy pyramid. The flat node list returned by
  // GetFamilyTree is indexed by id in `nodes`.
  export let nodeId: number;
  export let nodes: Map<number, FamilyTreeNode>;
  export let focus: number;

  $: node = nodes.get(nodeId);
  $: parents = node ? [node.p1, node.p2].filter((p) => p && nodes.has(p)) : [];

  function select(id: number) {
    selectedEntity($page).setNumber(id);
  }
</script>

{#if node}
  <div class="fnode">
    {#if parents.length > 0}
      <div class="fparents">
        {#each parents as pid (pid)}
          <svelte:self nodeId={pid} {nodes} {focus} />
        {/each}
      </div>
    {/if}
    <button
      type="button"
      class="fperson chip {nodeId === focus ? 'variant-filled-primary' : 'variant-soft-secondary'}"
      on:click={() => select(nodeId)}
      title={`#${nodeId} — click to re-root`}
    >
      {node.name}
    </button>
  </div>
{/if}

<style>
  .fnode {
    display: flex;
    flex-direction: column;
    align-items: center;
    margin: 0 0.5rem;
  }
  .fparents {
    display: flex;
    flex-direction: row;
    align-items: flex-end;
    justify-content: center;
    gap: 0.5rem;
    margin-bottom: 0.75rem;
    position: relative;
  }
  /* connector line from the parents row down to the child */
  .fparents::after {
    content: '';
    position: absolute;
    bottom: -0.75rem;
    left: 50%;
    width: 1px;
    height: 0.75rem;
    background: rgb(var(--color-surface-500) / 0.6);
  }
  .fperson {
    cursor: pointer;
    white-space: nowrap;
  }
</style>
