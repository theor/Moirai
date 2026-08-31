<script lang="ts">
  import { filteredEntity, groupByLabel, selectedEntity } from '$lib/utils';
  import { page } from '$app/stores';
  import { moiraiStore, settledYear, type EntityChangeDisplay } from '$lib/connection';
  import MoiraiText from './MoiraiText.svelte';
  import PreChip from './PreChip.svelte';
  import { Switch } from '@skeletonlabs/skeleton-svelte';
  import { onMount } from 'svelte';
  import { get } from 'svelte/store';

  let selected = -1;
  let filter = false;
  $: {
    let selParam = selectedEntity($page);
    selected = selParam.getNumber();
    let filterParam = filteredEntity($page);
    filter = filterParam.getNumber() > 0;
  }
  $: details = selected > 0 ? $moiraiStore.conn?.getEntityDetails(selected) : undefined;

  // A @display field (e.g. "Members", "Settlements") yields one details row per item, all sharing a
  // label. Collapse long runs to ITEM_LIMIT with a "Show N more" toggle so the panel stays readable.
  const ITEM_LIMIT = 5;
  // This component is in legacy (non-runes) mode, where template updates are
  // driven by assignment invalidation. SvelteSet's fine-grained signals do not
  // reach that machinery, so swapping it in silently stops the toggle below from
  // re-rendering -- verified in the browser. Plain Set plus a reassign is correct
  // here until the component is ported to runes.
  // eslint-disable-next-line svelte/prefer-svelte-reactivity
  let expanded: Set<string> = new Set();
  // A new selection starts fully collapsed.
  $: expanded = collapsedFor(selected);
  function collapsedFor(_selected: number) {
    return new Set<string>();
  }

  function toggle(label: string) {
    if (expanded.has(label)) expanded.delete(label);
    else expanded.add(label);
    expanded = expanded; // reassign to trigger Svelte reactivity
  }

  // Changesets that touched the selected entity. Fetched on demand (not derived
  // from the store) so the per-second record stream doesn't trigger refetches;
  // we refresh on selection change and whenever the simulation year advances.
  let changesets: Promise<EntityChangeDisplay[]> | undefined;
  function changesetsFor(sel: number) {
    return sel > 0 ? get(moiraiStore).conn?.getEntityChangesets(sel) : undefined;
  }
  $: changesets = changesetsFor(selected);

  // The settled year, not every year: this scans the whole changeset log for one entity.
  // See $lib/settled-year.
  onMount(() =>
    settledYear.subscribe(() => {
      if (selected > 0) changesets = changesetsFor(selected);
    }),
  );

  function setFilter(checked: boolean) {
    filter = checked;
    let filterParam = filteredEntity($page);
    filterParam.setNumber(filter ? selected : -1);
  }
</script>

<div class="flex">
  {#if selected > 0}
    <h3 class="h3 grow">
      Entity #{selected}
    </h3>
    <Switch
      class="switch-sm mt-1"
      name="filter"
      checked={filter}
      onCheckedChange={(e) => setFilter(e.checked)}
      title="Filter the records feed to this entity"
    >
      <Switch.HiddenInput />
      <Switch.Control>
        <Switch.Thumb />
      </Switch.Control>
      <Switch.Label>Filter</Switch.Label>
    </Switch>
  {:else}
    No entity selected
  {/if}
</div>
{#await details}
  <p>Loading...</p>
{:then details}
  {#if details}
    <div class="overflow-auto px-4 py-1 sm:px-0 grid grid-cols-1 lg:grid-cols-3 gap-x-4 gap-y-1">
      {#each groupByLabel(details) as g, gi (gi)}
        {#each expanded.has(g.label) ? g.values : g.values.slice(0, ITEM_LIMIT) as value, i (i)}
          <div class="text-sm lg:justify-self-end font-semibold leading-6 capitalize">
            {#if i === 0}
              {g.label}
            {/if}
          </div>
          <div class="mt-1 col-span-2 text-sm leading-6 opacity-80 sm:mt-0">
            <MoiraiText text={value} {selected} />
          </div>
        {/each}
        {#if g.values.length > ITEM_LIMIT}
          <div></div>
          <div class="col-span-2">
            <button
              type="button"
              class="text-xs text-primary-500 hover:underline"
              on:click={() => toggle(g.label)}
            >
              {expanded.has(g.label) ? 'Show less' : `Show ${g.values.length - ITEM_LIMIT} more`}
            </button>
          </div>
        {/if}
      {/each}
    </div>
  {/if}
{/await}

{#if selected > 0}
  <hr class="!my-3" />
  {#await changesets}
    <h4 class="h4 mb-1">Changesets</h4>
    <p class="text-sm opacity-60">Loading…</p>
  {:then changesets}
    <h4 class="h4 mb-1">Changesets ({changesets?.length ?? 0})</h4>
    {#if !changesets || changesets.length === 0}
      <p class="text-sm opacity-60">No changesets for this entity yet.</p>
    {:else}
      <div class="overflow-auto max-h-[45vh] pr-1">
        {#each changesets as cs, csi (csi)}
          <div class="py-1 border-b border-surface-500/20 text-sm">
            <div class="flex items-center gap-2 text-xs opacity-70">
              <PreChip text={cs.year} />
              <span class="truncate">{cs.actionName}</span>
            </div>
            <div class="flex flex-wrap gap-x-3 gap-y-1 mt-0.5">
              {#each cs.changes as change, ci (ci)}
                <span class="inline-flex items-center gap-1">
                  <kbd class="kbd">{change.label}</kbd>
                  <MoiraiText text={change.value} {selected} />
                </span>
              {/each}
            </div>
          </div>
        {/each}
      </div>
    {/if}
  {/await}
{/if}
