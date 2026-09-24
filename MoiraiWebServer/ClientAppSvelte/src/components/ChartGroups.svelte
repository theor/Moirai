<script lang="ts">
  import type { ChartGroup } from '$lib/family';
  import type { SvelteSet } from 'svelte/reactivity';
  import ChartBranch from './ChartBranch.svelte';

  /** The column of children hanging off one card, each group headed by the other parent when needed. */
  let {
    groups,
    focus,
    collapsed,
  }: { groups: ChartGroup[]; focus: number; collapsed: SvelteSet<number> } = $props();
</script>

<div class="fkids">
  {#each groups as g (g.key)}
    {#if g.label}
      <div class="fitem flabel">{g.label}</div>
    {/if}
    {#each g.branches as b (b.node.id)}
      <div class="fitem"><ChartBranch branch={b} {focus} {collapsed} /></div>
    {/each}
  {/each}
</div>
