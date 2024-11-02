<script lang="ts">
    import {filteredEntity, selectedEntity, urlParam} from '$lib/utils';
    import {page} from '$app/stores';
    import {moiraiStore} from '$lib/connection';
    import MoiraiText from './MoiraiText.svelte';
    import {SlideToggle} from '@skeletonlabs/skeleton';
    import TypeList from './TypeList.svelte';

    let selected = -1;
    let filter = false;
    $: {
        let selParam = selectedEntity($page);
        selected = selParam.getNumber();
        let filterParam = filteredEntity($page);
        filter = filterParam.getNumber() > 0;
    }
    $: details = selected > 0 ? $moiraiStore.conn?.getEntityDetails(selected) : undefined;

    function toggleFilter() {
        filter = !filter;
        let filterParam = filteredEntity($page);
        filterParam.setNumber(filter ? selected : -1);
    }

    $: typeNames = $moiraiStore.clientData?.types?.map((t) => t.name);
</script>
<div class="card p-4 mb-2 h-2/3 flex flex-col">
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
                    class="mt-1">Filter
            </SlideToggle
            >
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
</div>

<!-- {#if typeNames}
  <TypeList {typeNames} />
{/if} -->
