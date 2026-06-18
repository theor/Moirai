<script lang="ts">
  import { filteredEntity, selectedEntity } from '$lib/utils';
  import { page } from '$app/stores';

  export let id: number;
  export let label: string;
  export let active: boolean;

  const sel = selectedEntity($page);
  const filter = filteredEntity($page);

  function onClick(e: MouseEvent) {
    // Shift+click filters the records to this entity; a plain click selects it.
    if (e.shiftKey) {
      filter.setNumber(filter.getNumber() === id ? -1 : id);
    } else {
      sel.setNumber(id);
    }
  }
</script>

<button
  type="button"
  class="badge mr-1 [&>*]:pointer-events-none"
  class:variant-filled-secondary={active}
  class:variant-ghost-secondary={!active}
  title={`Click to select #${id} · Shift+click to filter`}
  on:click={onClick}
>
  {label ?? id}
</button>
