<script lang="ts">
  import type { FamilyTreeNode } from '$lib/types';
  import { lifespan } from '$lib/family';
  import { selectedEntity } from '$lib/utils';
  import { page } from '$app/state';

  /** One person in a family tree: name, years, and whether they are still alive. Click to re-root. */
  let {
    node,
    focus = false,
    note = '',
  }: { node: FamilyTreeNode; focus?: boolean; note?: string } = $props();

  const years = $derived(lifespan(node));
</script>

<button
  type="button"
  class="person"
  class:focus
  class:dead={node.dead}
  title={`#${node.id} · click to centre the tree here`}
  onclick={() => selectedEntity(page).setNumber(node.id)}
>
  <span class="block font-medium leading-tight whitespace-nowrap">{node.name}</span>
  {#if years || note}
    <span class="block text-xs tabular-nums text-surface-600">
      {years}{#if note}{years ? ' · ' : ''}<em>{note}</em>{/if}
    </span>
  {/if}
</button>

<style>
  .person {
    display: inline-block;
    text-align: center;
    padding: 0.25rem 0.625rem;
    border: 1px solid var(--color-surface-200);
    border-radius: 0.375rem;
    background: white;
    font-size: 0.875rem;
    cursor: pointer;
  }
  .person:hover {
    border-color: var(--color-surface-400);
  }
  /* The dead stay readable but step back: ink on the name, not a grey smear over the whole card. */
  .person.dead {
    background: var(--color-surface-50);
    border-style: dashed;
  }
  /* After .dead, so the person the tree is centred on always reads as the centre, living or not. */
  .person.focus {
    border-style: solid;
    border-color: var(--color-primary-500);
    box-shadow: 0 0 0 1px var(--color-primary-500);
  }
</style>
