<script lang="ts">
  import '../app.css';
  import { AppBar, Tabs, Progress } from '@skeletonlabs/skeleton-svelte';
  import DetailsPanel from '../components/DetailsPanel.svelte';
  import { moiraiStore } from '$lib/connection';
  import { moiraiViewStore } from '$lib';
  import { shortcut } from '$lib/shortcut';
  import { page } from '$app/stores';
  import { resolve } from '$app/paths';
  import type { Pathname } from '$app/types';
  import type { HTMLAnchorAttributes } from 'svelte/elements';

  import { QueryClient, QueryClientProvider } from '@tanstack/svelte-query';
  import ActionList from '../components/ActionList.svelte';

  let { children } = $props();

  let yearInput: HTMLInputElement | undefined = $state();
  let yearValue: number | undefined = $state(undefined);
  let passYearsCount = $state(100);
  const passYearsProgress = $derived($moiraiStore.passYearsProgress);
  const passYearsRunning = $derived(passYearsProgress !== undefined);

  function gotoLine() {
    yearInput?.focus();
    yearInput?.select();
  }

  // Details/Events panel tabs. Skeleton v5 tab values are strings.
  const PANEL_TABS = ['details', 'events'];
  let activeTab = $state('details');
  function switchTab() {
    activeTab = PANEL_TABS[(PANEL_TABS.indexOf(activeTab) + 1) % PANEL_TABS.length];
  }

  const queryClient = new QueryClient();

  // Keep the selection/filter state (e/f/t search params) when switching tabs.
  const search = $derived($page.url.search);

  const NAV_TABS: { href: Pathname; label: string }[] = [
    { href: '/records', label: 'Records' },
    { href: '/changesets', label: 'Changesets' },
    { href: '/query', label: 'Query' },
    { href: '/family', label: 'Family' },
  ];
</script>

<!-- App Shell -->
<QueryClientProvider client={queryClient}>
  <div
    class="grid grid-cols-4 grid-flow-row auto-rows-max h-full w-full"
    use:shortcut={{ control: true, code: 'KeyG', callback: gotoLine }}
    use:shortcut={{ control: true, code: 'KeyD', callback: switchTab }}
  >
    <!-- App Bar -->
    <AppBar class="col-span-4">
      <AppBar.Toolbar class="grid-cols-[auto_1fr]">
        <AppBar.Lead class="flex items-center">
          <img src="/icon.png" alt="Moirai" class="w-8 h-8 mr-2" />
          <strong class="text-xl mr-4 font-serif">Moirai</strong>
        </AppBar.Lead>
        <AppBar.Headline class="flex flex-wrap items-center gap-2">
          <Tabs value={$page.url.pathname} class="w-auto">
            <Tabs.List class="mb-0 pb-0 border-b-0">
              {#each NAV_TABS as tab (tab.href)}
                <Tabs.Trigger value={tab.href}>
                  {#snippet element(attributes)}
                    <!-- Trigger types its attrs for <button>; we render an <a> for real navigation. -->
                    <a
                      {...attributes as unknown as HTMLAnchorAttributes}
                      href="{resolve(tab.href)}{search}">{tab.label}</a
                    >
                  {/snippet}
                </Tabs.Trigger>
              {/each}
            </Tabs.List>
          </Tabs>
          <span class="vr"></span>
          <span class="mx-2">Year {$moiraiStore.year}</span>
          <form
            class="inline"
            onsubmit={(e) => {
              e.preventDefault();
              $moiraiViewStore.gotoYear = yearValue;
              yearValue = undefined;
            }}
          >
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
          <button
            type="button"
            class="btn preset-filled-surface-500"
            onclick={() => moiraiStore.reset()}>Reset</button
          >
          <div class="field-group grid-cols-[auto_1fr] w-44 inline-grid align-middle">
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
              class="btn preset-filled-surface-500 whitespace-nowrap"
              disabled={passYearsRunning}
              onclick={() => moiraiStore.passYears(passYearsCount)}>Pass years</button
            >
          </div>
          {#if passYearsRunning}
            <div class="w-24 mx-2 inline-block align-middle">
              <Progress value={passYearsProgress} max={passYearsCount}>
                <Progress.Track>
                  <Progress.Range class="bg-primary-500" />
                </Progress.Track>
              </Progress>
            </div>
          {/if}
        </AppBar.Headline>
      </AppBar.Toolbar>
    </AppBar>
    <aside class="m-4 flex-auto overflow-hidden h-full">
      <div class="card p-4 mb-2 h-full max-h-[88vh] overflow-y-auto">
        <Tabs value={activeTab} onValueChange={(e) => (activeTab = e.value ?? 'details')}>
          <Tabs.List>
            <Tabs.Trigger value="details">Details</Tabs.Trigger>
            <Tabs.Trigger value="events">Events</Tabs.Trigger>
          </Tabs.List>
          <Tabs.Content value="details">
            <DetailsPanel />
          </Tabs.Content>
          <Tabs.Content value="events">
            {#if $moiraiStore.clientData}
              <ActionList />
            {/if}
          </Tabs.Content>
        </Tabs>
      </div>
    </aside>
    <main class="flex-auto col-span-3 space-y-4 p-4 pl-0 h-full">
      {@render children?.()}
    </main>
  </div>
</QueryClientProvider>
