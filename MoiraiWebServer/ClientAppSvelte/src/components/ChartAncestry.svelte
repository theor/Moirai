<script lang="ts">
  import type { Ancestry } from '$lib/family';
  import ChartCard from './ChartCard.svelte';
  import ChartAncestry from './ChartAncestry.svelte';

  /** A couple of parents with their own parents in the columns to the left: the chart, mirrored. */
  let { ancestry }: { ancestry: Ancestry } = $props();
</script>

<div class="fbranch">
  {#if ancestry.above.length > 0}
    <div class="fkids fup">
      {#each ancestry.above as up (up.couple.node.id)}
        <div class="fitem"><ChartAncestry ancestry={up} /></div>
      {/each}
    </div>
  {/if}
  <ChartCard couple={ancestry.couple} />
</div>
