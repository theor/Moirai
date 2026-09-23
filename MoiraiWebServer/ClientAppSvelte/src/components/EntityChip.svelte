<script lang="ts">
  import { filteredEntity, selectedEntity } from '$lib/utils';
  import { moiraiStore } from '$lib/connection';
  import { entityTypes } from '$lib/entity-types';
  import { slotOf } from '$lib/entity-style';
  import { page } from '$app/stores';

  let { id, label, active }: { id: number; label: string; active: boolean } = $props();

  // Colour says the type; see $lib/entity-style and `.ent` in app.css.
  const slot = $derived(slotOf(id, $entityTypes.types, $entityTypes.slots));
  const typeName = $derived(
    $moiraiStore.clientData?.types.find((t) => t.id === $entityTypes.types[id])?.name,
  );

  function onClick(e: MouseEvent) {
    // Shift+click filters the records to this entity; a plain click selects it.
    if (e.shiftKey) {
      const filter = filteredEntity($page);
      filter.setNumber(filter.getNumber() === id ? -1 : id);
    } else {
      selectedEntity($page).setNumber(id);
    }
  }
</script>

<button
  type="button"
  class="ent ent-{slot}"
  class:active
  title={`${typeName ?? 'Entity'} #${id} · click to select · shift+click to filter`}
  onclick={onClick}
>
  {label ?? id}
</button>
