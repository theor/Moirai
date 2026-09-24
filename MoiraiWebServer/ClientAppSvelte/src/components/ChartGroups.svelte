<script lang="ts">
  import type { ChartGroup, ChartView } from '$lib/family';
  import ChartBranch from './ChartBranch.svelte';

  /** The column of children hanging off one card, each group headed by the other parent when needed. */
  let { groups, view }: { groups: ChartGroup[]; view: ChartView } = $props();
</script>

<div class="fkids">
  {#each groups as g (g.key)}
    {#if g.label}
      <div class="fitem flabel">{g.label}</div>
    {/if}
    {#each g.branches as b (b.node.id)}
      <div class="fitem"><ChartBranch branch={b} {view} /></div>
    {/each}
  {/each}
</div>
