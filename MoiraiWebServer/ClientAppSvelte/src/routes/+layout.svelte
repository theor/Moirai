<script lang="ts">
  import '../app.css';
  import { AppBar, Tabs, Progress } from '@skeletonlabs/skeleton-svelte';
  import DetailsPanel from '../components/DetailsPanel.svelte';
  import { moiraiStore } from '$lib/connection';
  import { moiraiViewStore } from '$lib';
  import { shortcut } from '$lib/shortcut';
  import { page } from '$app/stores';
  import { goto } from '$app/navigation';
  import { resolve } from '$app/paths';
  import type { Pathname } from '$app/types';

  import { QueryClient, QueryClientProvider } from '@tanstack/svelte-query';
  import ActionList from '../components/ActionList.svelte';

  let { children } = $props();

  let yearInput: HTMLInputElement | undefined = $state();
  let yearValue: number | undefined = $state(undefined);
  let passYearsCount = $state(100);
  const passYearsPercent = $derived($moiraiStore.passYearsPercent);
  const passYearsRunning = $derived(passYearsPercent !== undefined);
  // The WebAssembly backend has a runtime to start and possibly a world to rebuild, so "connecting" lasts
  // seconds rather than milliseconds — long enough to click Reset before there is anything to reset.
  const connecting = $derived($moiraiStore.conn === undefined);

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

  // Seed box. The world is deterministic per seed, so re-seeding is the only way to get a different
  // world out of the same story file. Kept in sync with the server's seed, which arrives in ClientData.
  let seedValue: number | undefined = $state(undefined);
  const serverSeed = $derived($moiraiStore.clientData?.seed);
  $effect(() => {
    if (serverSeed !== undefined && seedValue === undefined) seedValue = serverSeed;
  });
  const seedDirty = $derived(
    seedValue !== undefined && serverSeed !== undefined && seedValue !== serverSeed,
  );

  function applySeed() {
    if (seedValue === undefined || !Number.isFinite(seedValue) || seedValue < 0) return;
    moiraiStore.reseed(Math.floor(seedValue));
  }

  function rollSeed() {
    // Kept well inside Number.MAX_SAFE_INTEGER: the seed round-trips through JSON as a number.
    seedValue = Math.floor(Math.random() * 1_000_000);
    applySeed();
  }

  // Keep the selection/filter state (e/f/t search params) when switching tabs.
  const search = $derived($page.url.search);

  const NAV_TABS: { href: Pathname; label: string }[] = [
    { href: '/records', label: 'Records' },
    { href: '/life', label: 'Life' },
    { href: '/changesets', label: 'Changesets' },
    { href: '/query', label: 'Query' },
    { href: '/family', label: 'Family' },
    { href: '/world', label: 'World' },
    { href: '/rules', label: 'Rules' },
  ];
</script>

<!-- App Shell -->
<QueryClientProvider client={queryClient}>
  <!--
    grid-rows-[auto_1fr], not auto-rows-max: with content-sized rows the second row has no definite
    height, so `h-full` on <main> resolves to auto, a page's `h-full overflow-auto` never has anything
    to overflow, and body's `overflow: hidden` silently clips it. min-h-0 on the two row items is the
    other half -- a grid item's default min-height:auto refuses to shrink below its content, which
    would push the row back to content height.
  -->
  <div
    class="grid grid-cols-4 grid-rows-[auto_1fr] h-full w-full"
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
                    <!--
                      A button, not an <a>, and that is load-bearing rather than a style choice.

                      Tabs is controlled and its value is the pathname, so every navigation changes it;
                      the component reacts by re-activating the matching trigger, which it does by
                      dispatching `new MouseEvent('click')` at the element. That event is
                      `cancelable: false` and does not bubble, so it cannot be prevented and neither
                      SvelteKit's router nor a Svelte `onclick` ever sees it — but the browser still runs
                      an anchor's default navigation. The result was that a real click's soft navigation
                      was immediately followed by a full page load of the same URL. Harmless with the
                      server, where the world lives elsewhere; fatal with the in-browser engine, which
                      lives in the page and was thrown away on every tab switch. A button has no default
                      navigation, so the synthetic click does nothing and `goto` is the only way here.

                      The cost is the affordances a real link has: no middle-click, no ctrl-click, no
                      "copy link address" on the tab bar.
                    -->
                    <button
                      {...attributes}
                      type="button"
                      onclick={(e) => {
                        attributes.onclick?.(e);
                        // The href is already resolved; the rule only recognises a literal resolve()
                        // call as the argument, which a template literal is not.
                        // eslint-disable-next-line svelte/no-navigation-without-resolve
                        goto(`${resolve(tab.href)}${search}`);
                      }}>{tab.label}</button
                    >
                  {/snippet}
                </Tabs.Trigger>
              {/each}
            </Tabs.List>
          </Tabs>
          <span class="vr"></span>
          <span class="mx-2">{connecting ? 'Starting…' : `Year ${$moiraiStore.year}`}</span>
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
            disabled={connecting}
            onclick={() => moiraiStore.reset()}>Reset</button
          >
          <form
            class="field-group grid-cols-[auto_auto_auto] inline-grid align-middle"
            onsubmit={(e) => {
              e.preventDefault();
              applySeed();
            }}
          >
            <span
              class="label preset-tonal"
              title="Base RNG seed — the world is deterministic per seed">Seed</span
            >
            <input
              type="number"
              min="0"
              step="1"
              name="seed"
              aria-label="RNG seed"
              bind:value={seedValue}
              class="input w-28"
            />
            <button
              type="submit"
              class="btn {seedDirty ? 'preset-filled-primary-500' : 'preset-filled-surface-500'}"
              disabled={!seedDirty || connecting}
              title="Rebuild the world from this seed">Apply</button
            >
          </form>
          <button
            type="button"
            class="btn preset-filled-surface-500"
            disabled={connecting}
            onclick={rollSeed}
            title="Pick a random seed and rebuild the world">Roll</button
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
              disabled={passYearsRunning || connecting}
              onclick={() => moiraiStore.passYears(passYearsCount)}>Pass years</button
            >
          </div>
          {#if passYearsRunning}
            <div class="w-24 mx-2 inline-block align-middle">
              <Progress value={passYearsPercent} max={100}>
                <Progress.Track>
                  <Progress.Range class="bg-primary-500" />
                </Progress.Track>
              </Progress>
            </div>
          {/if}
        </AppBar.Headline>
      </AppBar.Toolbar>
    </AppBar>
    <aside class="m-4 min-h-0 overflow-hidden">
      <div class="card p-4 h-full overflow-y-auto">
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
    <main class="col-span-3 min-h-0 space-y-4 p-4 pl-0 h-full">
      {@render children?.()}
    </main>
  </div>
</QueryClientProvider>
