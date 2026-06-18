<script lang="ts">
    import SelectAll from 'virtual:icons/mdi/select-all';
    import Select from 'virtual:icons/mdi/select';
    import Play from 'virtual:icons/mdi/play';
    import type {ActionData} from '$lib/types';
    import {moiraiStore} from '$lib/connection';
    import {SlideToggle} from '@skeletonlabs/skeleton';

    let actionNames: ActionData[] = $moiraiStore.clientData!.actions;

    function setAll(hide: boolean) {
        actionNames = actionNames.map(a => ({...a, hidden: hide}));
        moiraiStore.update(x => {
            x.clientData!.actions = actionNames;
            return x;
        })
    }

    function toggleAction(a: ActionData, e: Event) {
        actionNames = actionNames.map(al => al.id === a.id ? {
            ...al,
            hidden: !(e.target as HTMLInputElement).checked
        } : al);
        moiraiStore.update(x => {
            x.clientData!.actions = actionNames;
            return x;
        })
    }

    function toggle(action: ActionData) {
        actionNames = actionNames.map(a => a.id === action.id ? {...a, hidden: !a.hidden} : a);
        moiraiStore.update(x => {
            x.clientData!.actions = actionNames;
            return x;
        });
    }

    function runAction(a: ActionData) {
        $moiraiStore.conn?.runAction(a.id);
    }
</script>

<div class="flex flex-wrap">
    <h3 class="h3 grow">Events</h3>
    <div class="mb-2 btn-group variant-soft">
        <button class="btn-sm" on:click={() => setAll(true)}>
            <Select class="w-4"/>
        </button>
        <button class="btn-sm" on:click={() => setAll(false)}
        >
            <SelectAll class="w-4"/>
        </button>
    </div>
</div>
<div class="overflow-auto flex-auto">
    <!-- <ListBox padding="px-4 py-1" multiple active="variant-filled-primary"> -->
    {#each actionNames as action,i}
        <!-- <ListBoxItem       bind:group={visibleActions} -->
        <div class="flex items-center">
            <button on:click={() => runAction(action)}
                    title={'Run ' + action.name}
                    aria-label={'Run ' + action.name}
                    class="variant-outline hover:variant-ghost active:variant-filled rounded-lg flex-shrink-0">
                <Play class="w-6 flex-shrink-0"/>
            </button>
            <button type="button"
                  class="flex-auto truncate mx-0 px-1 text-left"
                  title={action.name}
                  on:click={() => toggle(action)}>
          {action.name}
        </button>
            <SlideToggle on:change={(e) => toggleAction(action,e)} name="slider" checked={!action.hidden}
                         active="bg-primary-500" size="sm" />
            <!-- visibility toggle -->
        </div>
        <!-- </ListBoxItem> -->
    {/each}
</div>
<!-- </ListBox> -->

<style>
    .btn-group {
        @apply rounded-lg;
    }

    .btn-group button {
        @apply w-10;
        @apply px-2;
    }

</style>
