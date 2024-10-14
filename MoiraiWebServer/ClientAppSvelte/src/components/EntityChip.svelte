<script lang="ts">
  import { moiraiStore } from '$lib/connection';
  import { selectedEntity } from '$lib/utils';
  import { page } from '$app/stores';

  export let id: number;
  export let label: string;
  export let active: boolean;
  
  const sel = selectedEntity($page);
  // export function makeEntityChip(id: number, label: string, selectedEntity: GetSetProperty<number>, filteredEntity: GetSetProperty<number>) {
</script>

<a
  class="badge mx-1"
  class:variant-filled-primary={active}
  class:variant-ghost-primary={!active}
  href={undefined}
  on:click={(e) => {sel.set(id + ''); }}
  >{label ?? id}</a
>
