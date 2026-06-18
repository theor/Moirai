<script lang="ts">
    import {filteredEntity, selectedEntity} from '$lib/utils';
    import {page} from '$app/stores';
    import {moiraiStore} from '$lib/connection';
    import MoiraiText from './MoiraiText.svelte';
    import PreChip from './PreChip.svelte';
    import {SlideToggle} from '@skeletonlabs/skeleton';

    let selected = -1;
    let filter = false;
    $: {
        let selParam = selectedEntity($page);
        selected = selParam.getNumber();
        let filterParam = filteredEntity($page);
        filter = filterParam.getNumber() > 0;
    }
    $: details = selected > 0 ? $moiraiStore.conn?.getEntityDetails(selected) : undefined;

    // All streamed records that reference the selected entity. Record text encodes
    // entity links as `<#42>Label</>`, so `#42>` uniquely matches that entity.
    $: entityRecords =
        selected > 0
            ? $moiraiStore.records.filter((r) => r.text.includes(`#${selected}>`))
            : [];

    function actionName(actionId: number): string {
        return $moiraiStore.clientData?.actions[actionId - 1]?.name ?? '';
    }

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
            {#each details as detail, idx}
                    <div class="text-sm lg:justify-self-end font-semibold leading-6 text-gray-900 capitalize">
                        {#if idx === 0 || details[idx - 1].label !== detail.label}
                        {detail.label}
                        {/if}
                    </div>
                <div class="mt-1 col-span-2 text-sm leading-6 text-gray-700 sm:mt-0">
                    <MoiraiText text={detail.value} {selected}/>
                </div>
            {/each}
        </div>
    {/if}
{/await}

{#if selected > 0}
    <hr class="!my-3" />
    <h4 class="h4 mb-1">Records ({entityRecords.length})</h4>
    {#if entityRecords.length === 0}
        <p class="text-sm opacity-60">No records mention this entity yet.</p>
    {:else}
        <div class="overflow-auto max-h-[45vh] pr-1">
            {#each entityRecords as r}
                <div class="py-1 border-b border-surface-500/20 text-sm">
                    <div class="flex items-center gap-2 text-xs opacity-70">
                        <PreChip text={r.year} />
                        <span class="truncate">{actionName(r.actionId)}</span>
                    </div>
                    <MoiraiText text={r.text} {selected} />
                </div>
            {/each}
        </div>
    {/if}
{/if}
