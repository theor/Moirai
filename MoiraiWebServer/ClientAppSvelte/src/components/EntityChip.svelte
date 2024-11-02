<script lang="ts">
  import { filteredEntity, selectedEntity } from '$lib/utils';
  import { page } from '$app/stores';

  export let id: number;
  export let label: string;
  export let active: boolean;

  const sel = selectedEntity($page);
  const filter = filteredEntity($page);

  // const popupHover: PopupSettings = {
  //   event: 'hover',
  //   target: 'popupHover',
  //   placement: 'bottom'
  // };
  // export function makeEntityChip(id: number, label: string, selectedEntity: GetSetProperty<number>, filteredEntity: GetSetProperty<number>) {
</script>

<!-- <span> -->
  <a
    class="badge mr-1 [&>*]:pointer-events-none"
    class:variant-filled-secondary={active}
    class:variant-ghost-secondary={!active}
    href={undefined}
    on:dblclick={(e) => {
      filter.set(id + '');
    }}
    on:click={(e) => {
      setTimeout(() => {
        sel.set(id + '');
      }, 300);
    }}
  >
    {label ?? id}
  </a>

  <!-- <div class="card p-2 variant-filled-secondary z-50" data-popup="popupHover">
    <p>#{id}</p>
    <div class="arrow variant-filled-secondary" />
  </div> -->
<!-- </span> -->
