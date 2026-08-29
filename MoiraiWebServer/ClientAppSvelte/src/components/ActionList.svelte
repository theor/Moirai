<script lang="ts">
    import SelectAll from 'virtual:icons/mdi/select-all';
    import Select from 'virtual:icons/mdi/select';
    import Play from 'virtual:icons/mdi/play';
    import type {ActionData} from '$lib/types';
    import {moiraiStore} from '$lib/connection';
    import {Switch} from '@skeletonlabs/skeleton-svelte';

    let actionNames: ActionData[] = $state($moiraiStore.clientData!.actions);

    function setAll(hide: boolean) {
        actionNames = actionNames.map(a => ({...a, hidden: hide}));
        moiraiStore.update(x => {
            x.clientData!.actions = actionNames;
            return x;
        })
    }

    function toggleAction(a: ActionData, checked: boolean) {
        actionNames = actionNames.map(al => al.id === a.id ? {
            ...al,
            hidden: !checked
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
    <!-- v2's .btn-group was dropped in v5; a flex row of buttons replaces it. -->
    <div class="mb-2 flex preset-tonal rounded-lg overflow-hidden">
        <button type="button" class="btn-sm w-10 px-2" onclick={() => setAll(true)}>
            <Select class="w-4"/>
        </button>
        <button type="button" class="btn-sm w-10 px-2" onclick={() => setAll(false)}>
            <SelectAll class="w-4"/>
        </button>
    </div>
</div>
<div class="overflow-auto flex-auto">
    {#each actionNames as action (action.id)}
        <div class="flex items-center">
            <button onclick={() => runAction(action)}
                    type="button"
                    title={'Run ' + action.name}
                    aria-label={'Run ' + action.name}
                    class="preset-outlined hover:preset-tonal active:preset-filled rounded-lg flex-shrink-0">
                <Play class="w-6 flex-shrink-0"/>
            </button>
            <button type="button"
                  class="flex-auto truncate mx-0 px-1 text-left"
                  title={action.name}
                  onclick={() => toggle(action)}>
          {action.name}
        </button>
            <Switch
                class="switch-sm"
                name="slider"
                checked={!action.hidden}
                onCheckedChange={(e) => toggleAction(action, e.checked)}
            >
                <Switch.HiddenInput />
                <Switch.Control>
                    <Switch.Thumb />
                </Switch.Control>
            </Switch>
            <!-- visibility toggle -->
        </div>
    {/each}
</div>
