<script lang="ts">
  import { ListBox, ListBoxItem } from '@skeletonlabs/skeleton';

  import SelectAll from 'virtual:icons/mdi/select-all';
  import Select from 'virtual:icons/mdi/select';
  import Play from 'virtual:icons/mdi/play';
  import type { ActionData } from '$lib/types';
    import { moiraiStore } from '$lib/connection';
    import { moiraiViewStore } from '$lib';
    import { onMount } from 'svelte';
    import { SlideToggle } from '@skeletonlabs/skeleton';
    
    export let actionNames: ActionData[];
  // let visibleActions: ActionData[] | undefined = [...actionNames];
  let visibleActions: ActionData[] = [];
  onMount(() => {
    visibleActions = [...actionNames];
    console.log('onMount', visibleActions);
  });

  $: {
    // console.log(visibleActions);
    // $moiraiStore.clientData!.actions = $moiraiStore.clientData!.actions.map((a) => ({ ...a, visible: visibleActions!.includes(a) }));
  }

  function runAction(a: ActionData) {
    console.log('running action', a);
    $moiraiStore.conn?.runAction(a.id);
  }
</script>

<div class="card p-4 mb-2 h-1/3 flex flex-col">
  <div class="flex flex-wrap">
    <h3 class="h3 grow">Events</h3>
    <div class="mb-2 btn-group variant-soft">
      <button class="btn-sm" on:click={() => (visibleActions = [])}>
          <Select class="w-4" />
      </button>
      <button class="btn-sm" on:click={() => (visibleActions = [...actionNames])}
        ><SelectAll  class="w-4" />
      </button>
    </div>
  </div>
  <div class="overflow-auto flex-auto">
  <!-- <ListBox padding="px-4 py-1" multiple active="variant-filled-primary"> -->
    {#each actionNames as action}
      <!-- <ListBoxItem       bind:group={visibleActions} -->
        <div class="flex">
            <button on:click={() => runAction(action)} class="variant-outline hover:variant-ghost active:variant-filled rounded-lg flex-shrink-0">
          <Play class="w-6 flex-shrink-0"/>
            </button>
          <span class="flex-auto truncate mx-0 px-1">
          {action.name}
        </span>
        <SlideToggle name="slider" checked active="bg-primary-500" size="sm">
          <style>
            .slide-toggle-track {
                @apply w-8 h-4;
            }
          </style>
        
      </SlideToggle>
    </div>
      <!-- </ListBoxItem> -->
    {/each}
    </div>
  <!-- </ListBox> -->
</div>

<style>
    .btn-group {
        @apply rounded-lg;
    }
  .btn-group button {
      @apply w-10;
      @apply px-2;
  }
  
</style>
