<script lang="ts">
  import type { ChartBranch } from '$lib/family';
  import type { SvelteSet } from 'svelte/reactivity';
  import ChartCard from './ChartCard.svelte';
  import ChartGroups from './ChartGroups.svelte';

  /**
   * A card with its descendants in the columns to its right. `collapsed` lives on the page, keyed by
   * id, because the tree is refetched whenever the year settles and a fold kept here would be lost.
   */
  let {
    branch,
    focus,
    collapsed,
  }: { branch: ChartBranch; focus: number; collapsed: SvelteSet<number> } = $props();

  const shut = $derived(collapsed.has(branch.node.id));

  function toggle() {
    if (shut) collapsed.delete(branch.node.id);
    else collapsed.add(branch.node.id);
  }
</script>

<div class="fbranch">
  <ChartCard
    couple={branch}
    {focus}
    below={branch.descendants}
    collapsed={shut}
    ontoggle={toggle}
  />
  {#if !shut && branch.groups.length > 0}
    <ChartGroups groups={branch.groups} {focus} {collapsed} />
  {/if}
</div>
