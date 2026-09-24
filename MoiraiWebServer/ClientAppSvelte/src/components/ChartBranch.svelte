<script lang="ts">
  import type { ChartBranch, ChartView } from '$lib/family';
  import ChartCard from './ChartCard.svelte';
  import ChartGroups from './ChartGroups.svelte';

  /**
   * A card with its descendants in the columns to its right. The folds live in `view`, on the page,
   * keyed by id, because the tree is refetched whenever the year settles and a fold kept here would be
   * lost.
   */
  let { branch, view }: { branch: ChartBranch; view: ChartView } = $props();

  const shut = $derived(view.collapsed.has(branch.node.id));

  function toggle() {
    if (shut) view.collapsed.delete(branch.node.id);
    else view.collapsed.add(branch.node.id);
  }
</script>

<div class="fbranch">
  <ChartCard couple={branch} {view} below={branch.descendants} collapsed={shut} ontoggle={toggle} />
  {#if !shut && branch.groups.length > 0}
    <ChartGroups groups={branch.groups} {view} />
  {/if}
</div>
