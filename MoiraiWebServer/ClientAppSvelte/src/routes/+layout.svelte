<script lang="ts">
  import '../app.postcss';
  import { AppBar } from '@skeletonlabs/skeleton';
  import DetailsPanel from '../components/DetailsPanel.svelte';
  import { computePosition, autoUpdate, offset, shift, flip, arrow } from '@floating-ui/dom';
  import { storePopup } from '@skeletonlabs/skeleton';
  import { moiraiStore } from '$lib/connection';
  import { moiraiViewStore } from '$lib';
  import { shortcut } from '$lib/shortcut';
  import { TabGroup, Tab, TabAnchor, ProgressBar } from '@skeletonlabs/skeleton';
  import { page } from '$app/stores';

  import { QueryClient, QueryClientProvider } from '@tanstack/svelte-query'
  import ActionList from "../components/ActionList.svelte";
  import {urlParam} from "$lib/utils";

  let yearInput: HTMLInputElement | undefined;
  let yearValue: number | undefined = undefined;
  let passYearsCount = 100;
  $: passYearsProgress = $moiraiStore.passYearsProgress;
  $: passYearsRunning = passYearsProgress !== undefined;
  storePopup.set({ computePosition, autoUpdate, offset, shift, flip, arrow });
  function gotoLine() {
    yearInput?.focus();
    yearInput?.select();
  }
  function switchTab() {
      activeTab = (activeTab + 1) % 2;
  }
  const queryClient = new QueryClient()
  const activeTabParam = urlParam($page, 'tab');
  let activeTab = 0;
</script>

<!-- App Shell -->
<QueryClientProvider client={queryClient}>
<div
  class="grid grid-cols-4 grid-flow-row auto-rows-max h-full w-full"
  use:shortcut={{ control: true, code: 'KeyG', callback: gotoLine }}
  use:shortcut={{ control: true, code: 'KeyD', callback: switchTab }}
>
  <header class="col-span-4">
    <!-- App Bar -->
    <AppBar>
      <svelte:fragment slot="lead">
        <img src="/icon.png" alt="Moirai" class="w-8 h-8 mr-2" />
        <strong class="text-xl mr-4 font-serif">Moirai</strong>
      </svelte:fragment>
      <svelte:fragment slot="default">
        <TabGroup class="w-100 inline-block">
          <TabAnchor href="/records" selected={$page.url.pathname === '/records'}>Records</TabAnchor>
          <TabAnchor href="/changesets" selected={$page.url.pathname === '/changesets'}>Changesets</TabAnchor>
          <TabAnchor href="/query" selected={$page.url.pathname === '/query'}>Query</TabAnchor>
          <TabAnchor href="/family" selected={$page.url.pathname === '/family'}>Family</TabAnchor>
          <!-- ... -->
        </TabGroup>
        <span class="divider-vertical mr-1" />
        <span class="mx-2">Year {$moiraiStore.year}</span>
          <form class="inline"
            on:submit|preventDefault={() => {
              $moiraiViewStore.gotoYear = yearValue;
              yearValue = undefined;
            }}>
        <label class="label inline-block">
          <span class="sr-only">Go to year</span>
          <input
            placeholder="Go to"
            bind:this={yearInput}
            bind:value={yearValue}
            name="gotoYear"
            aria-label="Go to year"
            class="input w-30"
            type="number"
          />
        </label>
          </form>
        <button type="button" class="btn variant-filled-surface" on:click={() => moiraiStore.reset()}>Reset</button>
        <div class="input-group input-group-divider grid-cols-[auto_1fr] w-44 inline-grid align-middle">
          <input
            type="number"
            min="1"
            name="passYearsCount"
            aria-label="Number of years to pass"
            bind:value={passYearsCount}
            class="input"
          />
          <button
            type="button"
            class="btn variant-filled-surface whitespace-nowrap"
            disabled={passYearsRunning}
            on:click={() => moiraiStore.passYears(passYearsCount)}
          >Pass years</button>
        </div>
        {#if passYearsRunning}
          <div class="w-24 mx-2 inline-block align-middle">
            <ProgressBar value={passYearsProgress} max={passYearsCount} meter="bg-primary-500" />
          </div>
        {/if}
        <div class="mr-4" />
      </svelte:fragment>
    </AppBar>
  </header>
    <aside class="m-4 flex-auto overflow-hidden h-full">
        <div class="card p-4 mb-2 h-full max-h-[88vh] overflow-y-auto">
        
        <TabGroup>
            <Tab value={0} bind:group={activeTab} name="details">Details</Tab>
            <Tab value={1} bind:group={activeTab} name="events">Events</Tab>

            <svelte:fragment slot="panel">
                {#if activeTab === 0}
                    <DetailsPanel />
                {:else if activeTab === 1}
                    {#if $moiraiStore.clientData}
                        <ActionList />
                    {/if}
               
                {/if}
            </svelte:fragment>
       
        </TabGroup>
        </div>
    </aside>
    <main class="flex-auto col-span-3 space-y-4 p-4 pl-0 h-full">
      <slot />
    </main>
  </div>
</QueryClientProvider>
