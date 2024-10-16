<script lang="ts">
  import { filteredEntity, selectedEntity, urlParam } from '$lib/utils';
  import { page } from '$app/stores';
  import { moiraiStore } from '$lib/connection';
  import EntityChip from './EntityChip.svelte';
  import MoiraiText from './MoiraiText.svelte';
  import { SlideToggle } from '@skeletonlabs/skeleton';

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
</script>

<div class="card p-4">
  <div class="flex">
    {#if selected > 0}
      <h3 class="h3 grow">
        Entity #{selected}
      </h3>
      <SlideToggle checked={filter} on:click={toggleFilter} name="filter" size="sm" label="Filter" class="mt-1">Filter</SlideToggle>
    {:else}
      No entity selected
    {/if}
  </div>
  {#await details}
    <p>Loading...</p>
  {:then details}
    {#if details}
      <div class="px-4 py-1 sm:px-0 grid grid-cols-3 gap-x-4 gap-y-1">
        {#each details as detail}
          <div class="text-sm justify-self-end font-semibold leading-6 text-gray-900 capitalize">
            {detail.label}
          </div>
          <div class="mt-1 col-span-2 text-sm leading-6 text-gray-700 sm:mt-0">
            <MoiraiText text={detail.value} {selected} />
          </div>
        {/each}
      </div>
    {/if}
  {/await}
</div>
