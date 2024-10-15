<script lang="ts">
  import { moiraiStore } from '$lib/connection';
  import { selectedEntity } from '$lib/utils';
  import { page } from '$app/stores';
  import { popup, type PopupSettings } from '@skeletonlabs/skeleton';

  export let id: number;
  export let label: string;
  export let active: boolean;

  const sel = selectedEntity($page);

  const popupHover: PopupSettings = {
    event: 'hover',
    target: 'popupHover',
    placement: 'bottom'
  };
  // export function makeEntityChip(id: number, label: string, selectedEntity: GetSetProperty<number>, filteredEntity: GetSetProperty<number>) {
</script>

<span>
  <a
    class="badge mx-1 [&>*]:pointer-events-none"
    use:popup={popupHover}
    class:variant-filled-secondary={active}
    class:variant-ghost-secondary={!active}
    href={undefined}
    on:click={(e) => {
      sel.set(id + '');
    }}
  >
    {label ?? id}
  </a>

  <div class="card p-2 variant-filled-secondary z-50" data-popup="popupHover">
    <p>#{id}</p>
    <div class="arrow variant-filled-secondary" />
  </div>
</span>
