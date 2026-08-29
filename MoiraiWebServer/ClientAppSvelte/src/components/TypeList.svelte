<script lang="ts">
  import { Listbox, useListCollection } from '@skeletonlabs/skeleton-svelte';

  import SelectAll from 'virtual:icons/mdi/select-all';
  import Select from 'virtual:icons/mdi/select';

  let { typeNames }: { typeNames: string[] } = $props();
  // Seeded from the initial prop value only, matching the v2 behaviour.
  // svelte-ignore state_referenced_locally
  let visibleTypes: string[] = $state([...typeNames]);

  const collection = $derived(
    useListCollection<string>({
      items: typeNames,
      itemToValue: (t) => t,
      itemToString: (t) => t,
    }),
  );
</script>

<div class="card p-4 mb-2">
  <div class="flex">
    <h3 class="h3 grow">Types</h3>
    <!-- v2's .btn-group was dropped in v5; a flex row of buttons replaces it. -->
    <div class="mb-2 flex preset-tonal rounded-lg overflow-hidden">
      <button type="button" class="btn-sm w-10 px-2" onclick={() => (visibleTypes = [])}>
        <Select />
      </button>
      <button
        type="button"
        class="btn-sm w-10 px-2"
        onclick={() => (visibleTypes = [...typeNames])}
      >
        <SelectAll />
      </button>
    </div>
  </div>

  <Listbox
    {collection}
    selectionMode="multiple"
    value={visibleTypes}
    onValueChange={(e) => (visibleTypes = e.value)}
  >
    <Listbox.Content>
      {#each collection.items as type (type)}
        <Listbox.Item item={type} class="px-4 py-1 text-sm">
          <Listbox.ItemText>{type}</Listbox.ItemText>
        </Listbox.Item>
      {/each}
    </Listbox.Content>
  </Listbox>
</div>
