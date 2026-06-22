<script lang="ts">
    import {filteredEntity, selectedEntity} from '$lib/utils';
    import {page} from '$app/stores';
    import {moiraiStore, type EntityChangeDisplay} from '$lib/connection';
    import MoiraiText from './MoiraiText.svelte';
    import PreChip from './PreChip.svelte';
    import {SlideToggle} from '@skeletonlabs/skeleton';
    import {onMount} from 'svelte';
    import {get} from 'svelte/store';

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
    let expanded: Set<string> = new Set();
    // Reset which groups are expanded whenever the selected entity changes.
    $: selected, (expanded = new Set());

    type DetailGroup = { label: string; values: string[] };
    function groupByLabel(items: { label: string; value: string }[]): DetailGroup[] {
        const out: DetailGroup[] = [];
        for (const d of items) {
            const last = out[out.length - 1];
            if (last && last.label === d.label) last.values.push(d.value);
            else out.push({ label: d.label, values: [d.value] });
        }
        return out;
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
    function refreshChangesets() {
        changesets =
            selected > 0 ? get(moiraiStore).conn?.getEntityChangesets(selected) : undefined;
    }
    $: selected, refreshChangesets();

    onMount(() => {
        let prevYear = get(moiraiStore).year;
        return moiraiStore.subscribe((s) => {
            if (s.year !== prevYear) {
                prevYear = s.year;
                if (selected > 0) refreshChangesets();
            }
        });
    });

    function toggleFilter() {
        filter = !filter;
        let filterParam = filteredEntity($page);
        filterParam.setNumber(filter ? selected : -1);
    }
</script>

<div class="flex">
    {#if selected > 0}
        <h3 class="h3 grow">
            Entity #{selected}
        </h3>
        <SlideToggle
                checked={filter}
                on:click={toggleFilter}
                name="filter"
                size="sm"
                label="Filter"
                title="Filter the records feed to this entity"
                class="mt-1">Filter
        </SlideToggle>
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
                {#each (expanded.has(g.label) ? g.values : g.values.slice(0, ITEM_LIMIT)) as value, i}
                    <div class="text-sm lg:justify-self-end font-semibold leading-6 text-gray-900 capitalize">
                        {#if i === 0}
                            {g.label}
                        {/if}
                    </div>
                    <div class="mt-1 col-span-2 text-sm leading-6 text-gray-700 sm:mt-0">
                        <MoiraiText text={value} {selected} />
                    </div>
                {/each}
                {#if g.values.length > ITEM_LIMIT}
                    <div />
                    <div class="col-span-2">
                        <button
                            type="button"
                            class="text-xs text-primary-500 hover:underline"
                            on:click={() => toggle(g.label)}
                        >
                            {expanded.has(g.label)
                                ? 'Show less'
                                : `Show ${g.values.length - ITEM_LIMIT} more`}
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
                {#each changesets as cs}
                    <div class="py-1 border-b border-surface-500/20 text-sm">
                        <div class="flex items-center gap-2 text-xs opacity-70">
                            <PreChip text={cs.year} />
                            <span class="truncate">{cs.actionName}</span>
                        </div>
                        <div class="flex flex-wrap gap-x-3 gap-y-1 mt-0.5">
                            {#each cs.changes as change}
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
