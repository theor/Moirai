<script lang="ts">
  import type { NotableGroup } from '$lib/types';
  import EntityChip from './EntityChip.svelte';

  /**
   * The entities the story talks about most, one column per type. A starting point for a world you
   * have never seen: every name is a chip, so one click opens its life.
   */
  let { groups }: { groups: NotableGroup[] } = $props();
</script>

<div class="grid gap-6 grid-cols-[repeat(auto-fill,minmax(15rem,1fr))]">
  {#each groups as g (g.typeId)}
    <section>
      <h3 class="text-xs font-semibold uppercase tracking-wide text-surface-600 mb-1.5">
        {g.typeName}
      </h3>
      <ol class="space-y-1">
        {#each g.top as e (e.id)}
          <li class="flex items-baseline gap-2 text-sm">
            <span class="min-w-0 truncate"
              ><EntityChip id={e.id} label={e.name} active={false} /></span
            >
            <span class="ml-auto shrink-0 text-xs text-surface-500 tabular-nums">
              {e.mentions}
              {e.mentions === 1 ? 'record' : 'records'}
            </span>
          </li>
        {/each}
      </ol>
    </section>
  {/each}
</div>
