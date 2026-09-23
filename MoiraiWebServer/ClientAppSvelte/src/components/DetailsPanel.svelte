<script lang="ts">
  import { filteredEntity, groupByLabel, selectedEntity } from '$lib/utils';
  import { page } from '$app/stores';
  import { moiraiStore, settledYear, type EntityChangeDisplay } from '$lib/connection';
  import MoiraiText from './MoiraiText.svelte';
  import { humanLabel } from '$lib/format';
  import Close from 'virtual:icons/mdi/close';
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

  // The Name and Type rows become the heading, so they are not repeated in the list under it.
  const HEADING_ROWS = new Set(['name', 'type']);
  function rowOf(rows: { label: string; value: string }[] | undefined, label: string) {
    return rows?.find((r) => r.label.toLowerCase() === label)?.value;
  }

  function close() {
    selectedEntity($page).setNumber(-1);
  }

  function setFilter(checked: boolean) {
    filter = checked;
    let filterParam = filteredEntity($page);
    filterParam.setNumber(filter ? selected : -1);
  }
</script>

<div class="flex items-start gap-2">
  {#if selected > 0}
    {#await details then rows}
      <div class="grow min-w-0">
        <h3 class="h4 leading-tight">{rowOf(rows, 'name') ?? `#${selected}`}</h3>
        <p class="text-xs text-surface-600">{rowOf(rows, 'type') ?? 'Entity'} #{selected}</p>
      </div>
    {/await}
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
    <button
      type="button"
      class="btn-icon btn-icon-sm hover:preset-tonal"
      title="Close"
      aria-label="Close"
      on:click={close}><Close /></button
    >
  {/if}
</div>
{#await details}
  <p>Loading...</p>
{:then details}
  {#if details}
    <div class="overflow-auto mt-3 grid grid-cols-[auto_1fr] gap-x-3 gap-y-0.5">
      {#each groupByLabel(details.filter((d) => !HEADING_ROWS.has(d.label.toLowerCase()))) as g, gi (gi)}
        {#each expanded.has(g.label) ? g.values : g.values.slice(0, ITEM_LIMIT) as value, i (i)}
          <div class="text-sm text-right leading-6 text-surface-600">
            {#if i === 0}
              {humanLabel(g.label)}
            {/if}
          </div>
          <div class="text-sm leading-6 min-w-0">
            <MoiraiText text={value} {selected} value />
          </div>
        {/each}
        {#if g.values.length > ITEM_LIMIT}
          <div></div>
          <div>
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
    <h4 class="text-sm font-semibold mb-1">Changes</h4>
    <p class="text-sm opacity-60">Loading…</p>
  {:then changesets}
    <h4 class="text-sm font-semibold mb-1">Changes ({changesets?.length ?? 0})</h4>
    {#if !changesets || changesets.length === 0}
      <p class="text-sm opacity-60">No changesets for this entity yet.</p>
    {:else}
      <div class="overflow-auto max-h-[45vh] pr-1">
        {#each changesets as cs, csi (csi)}
          <div class="py-1.5 border-b border-surface-200 text-xs">
            <div class="flex items-baseline gap-2 text-surface-600">
              <span class="year-mark">{cs.year}</span>
              <span class="truncate">{cs.actionName}</span>
            </div>
            <div class="flex flex-wrap gap-x-3 gap-y-0.5 mt-0.5 leading-6">
              {#each cs.changes as change, ci (ci)}
                <span>
                  <span class="font-medium">{humanLabel(change.label)}</span>
                  <MoiraiText text={change.value} {selected} value />
                </span>
              {/each}
            </div>
          </div>
        {/each}
      </div>
    {/if}
  {/await}
{/if}
