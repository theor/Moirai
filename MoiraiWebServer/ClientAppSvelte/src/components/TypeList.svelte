<script lang="ts">
  import { ListBox, ListBoxItem } from '@skeletonlabs/skeleton';

  import SelectAll from 'virtual:icons/mdi/select-all';
  import Select from 'virtual:icons/mdi/select';
  export let typeNames: string[];
  let visibleTypes: string[] | undefined = [...typeNames];

  $: console.log(visibleTypes);
</script>

<div class="card p-4 mb-2">
  <div class="flex">
    <h3 class="h3 grow">Types</h3>
    <div class="mb-2 btn-group variant-soft">
      <button class="btn-sm" on:click={() => (visibleTypes = [])}><Select /></button>
      <button class="btn-sm" on:click={() => (visibleTypes = [...typeNames])}><SelectAll /></button>
    </div>
  </div>

  <ListBox padding="px-4 py-1" multiple active="variant-filled-primary">
    {#each typeNames as type}
      <ListBoxItem bind:group={visibleTypes} name="medium" value={type} class="text-sm"
        >{type}</ListBoxItem
      >
    {/each}
  </ListBox>
</div>
