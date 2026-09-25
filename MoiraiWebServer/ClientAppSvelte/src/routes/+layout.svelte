<script lang="ts">
  import '../app.css';
  import { Tabs, Progress } from '@skeletonlabs/skeleton-svelte';
  import Play from 'virtual:icons/mdi/play';
  import Dice from 'virtual:icons/mdi/dice-5';
  import Restart from 'virtual:icons/mdi/restart';
  import ListChecks from 'virtual:icons/mdi/format-list-checks';
  import DetailsPanel from '../components/DetailsPanel.svelte';
  import { moiraiStore, settledYear } from '$lib/connection';
  import { withAddress } from '$lib/world-address';
  import { selectedEntity } from '$lib/utils';
  import { moiraiViewStore } from '$lib';
  import { shortcut } from '$lib/shortcut';
  import { page } from '$app/state';
  import { goto, replaceState } from '$app/navigation';
  import { asset, resolve } from '$app/paths';
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
  // Read once the backend exists; an apply that succeeds clears it on the next store update.
  const bootNotice = $derived($moiraiStore.conn?.bootNotice ?? null);

  function gotoLine() {
    yearInput?.focus();
    yearInput?.select();
  }

  // Details/Events panel tabs. Skeleton v5 tab values are strings.
  const PANEL_TABS = ['details', 'events'];
  let activeTab = $state('details');
  function switchTab() {
    activeTab = PANEL_TABS[(PANEL_TABS.indexOf(activeTab) + 1) % PANEL_TABS.length];
    if (activeTab === 'events') eventsOpen = true;
  }

  /**
   * The side panel shows only when it has something to say: an entity is selected, or the event list was
   * asked for. It used to take a quarter of the screen on every page, mostly to read "No entity selected".
   * The Life page leaves details out, because its own State column already shows them.
   */
  let eventsOpen = $state(false);
  const selected = $derived(selectedEntity(page).getNumber());
  const onLife = $derived(page.url.pathname.endsWith('/life'));
  const showDetails = $derived(selected > 0 && !onLife);
  const panelOpen = $derived(eventsOpen || showDetails);
  $effect(() => {
    // Whichever tab has content wins when the other has none.
    if (!eventsOpen && activeTab === 'events') activeTab = 'details';
    if (!showDetails && eventsOpen) activeTab = 'events';
  });
  function toggleEvents() {
    eventsOpen = !eventsOpen;
    activeTab = eventsOpen ? 'events' : 'details';
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

  /**
   * Keep the query string — the selected entity, the filters, and the world's seed and year — when
   * switching tabs.
   *
   * Read from the live URL at the moment of the click, not from `page.url`. Shallow routing updates the
   * browser's URL and deliberately leaves `page.url` pointing at the last real navigation, so a derived
   * from it goes stale the moment the year is written below — and a tab click would then navigate to a
   * year the world had already passed, sending you backwards in time.
   */
  const currentSearch = () => window.location.search;

  /**
   * Keep the URL saying which world this is.
   *
   * A world is its story, its seed and its year, so those three in the address bar make every world a
   * link — and, incidentally, make it survive a reload without anything being stored. Only the backend
   * whose world lives in the page takes part: the server has one world of its own, and a link naming a
   * year would describe nothing the recipient could see.
   *
   * Driven by the settled year rather than the live one, because a pass changes the year continuously
   * and rewriting the URL per feed tick would be pointless churn. replaceState, not goto: this is the
   * same page, and it must not fill the back button with one entry per century. It is SvelteKit's
   * replaceState rather than the browser's so the router's history entry stays in step — but `page.url`
   * does not follow it, which is why the tabs read the live URL (`currentSearch` above).
   */
  $effect(() => {
    const seed = $moiraiStore.clientData?.seed;
    const year = $settledYear;
    if (!$moiraiStore.conn?.worldInPage || seed === undefined || year <= 0) return;

    const next = withAddress(new URL(window.location.href), { seed: String(seed), year });
    // `next` is the current absolute URL with two parameters changed, so it is already resolved —
    // resolving it again would double the base path. The rule only recognises a literal resolve()
    // call, hence the same narrow exemption urlParam() takes in $lib/utils.
    // eslint-disable-next-line svelte/no-navigation-without-resolve
    if (next.href !== window.location.href) replaceState(next, {});
  });

  const NAV_TABS: { href: Pathname; label: string }[] = [
    { href: '/', label: 'Home' },
    { href: '/records', label: 'Records' },
    { href: '/life', label: 'Life' },
    { href: '/changesets', label: 'Changesets' },
    { href: '/query', label: 'Query' },
    { href: '/family', label: 'Family' },
    { href: '/world', label: 'World' },
    { href: '/rules', label: 'Rules' },
  ];

  // Story editing is a backend capability, not a build-time one: the in-browser engine holds its story as
  // a string it can be handed another of, while the server's is a file on disk its watcher owns. Asking
  // `conn.story` rather than which backend loaded keeps the layout out of the transport business.
  const tabs = $derived(
    $moiraiStore.conn?.story
      ? [...NAV_TABS, { href: '/story' as Pathname, label: 'Story' }]
      : NAV_TABS,
  );

  /**
   * The tab a click asked for, until its page has arrived.
   *
   * `goto` resolves only once the new page has mounted, and with the in-browser engine a page's first
   * queries run synchronously on the thread that paints — 700 ms for Query's default `pick Person`. Driven
   * by the route alone, the tab bar sat unchanged for all of that, which read as a click that did not
   * land. So the click selects the tab at once, waits for that to paint, and only then starts the page.
   *
   * The route id rather than the pathname: the pathname carries the base path on GitHub Pages
   * (`/Moirai/records`), which no trigger's value matches.
   */
  let pendingTab = $state<string | null>(null);
  const selectedTab = $derived(pendingTab ?? page.route.id ?? '');

  async function openTab(href: Pathname) {
    pendingTab = href;
    // One frame to paint the selection, then a task so the navigation's work lands after that paint.
    await new Promise((r) => requestAnimationFrame(() => setTimeout(r)));
    try {
      // The href is already resolved; the rule only recognises a literal resolve() call as the
      // argument, which a template literal is not.
      // eslint-disable-next-line svelte/no-navigation-without-resolve
      await goto(`${resolve(href)}${currentSearch()}`);
    } finally {
      // A later click owns the selection now; leave it be.
      if (pendingTab === href) pendingTab = null;
    }
  }
</script>

<!-- App Shell -->
<QueryClientProvider client={queryClient}>
  <!--
    grid-rows-[auto_auto_1fr], not auto-rows-max: with content-sized rows the last row has no definite
    height, so `h-full` on <main> resolves to auto, a page's `h-full overflow-auto` never has anything
    to overflow, and body's `overflow: hidden` silently clips it. min-h-0 on the row items is the other
    half -- a grid item's default min-height:auto refuses to shrink below its content, which would push
    the row back to content height.

    grid-cols-[minmax(0,1fr)] is the same trap sideways: the implicit column is `auto`, which grows to
    the widest page's content, so a wide family chart made body itself scroll sideways, and the first
    scrollIntoView slid the whole app, sidebar and all, out of view.
  -->
  <div
    class="grid grid-rows-[auto_auto_1fr] grid-cols-[minmax(0,1fr)] h-full w-full bg-white"
    use:shortcut={{ control: true, code: 'KeyG', callback: gotoLine }}
    use:shortcut={{ control: true, code: 'KeyD', callback: switchTab }}
  >
    <!-- Row 1: where you are. -->
    <header class="flex items-center gap-4 px-4 h-12 border-b border-surface-200">
      <div class="flex items-center shrink-0">
        <img src={asset('/icon.png')} alt="" class="w-6 h-6 mr-2" />
        <strong class="text-lg font-serif">Moirai</strong>
      </div>
      <nav class="min-w-0 overflow-x-auto">
        <Tabs value={selectedTab} class="w-auto">
          <Tabs.List class="mb-0 pb-0 border-b-0 gap-0">
            {#each tabs as tab (tab.href)}
              <!-- Skeleton marks the selected trigger only through Tabs.Indicator; this is simpler. -->
              <Tabs.Trigger
                value={tab.href}
                class="px-3 py-1 text-sm data-[selected]:preset-filled-primary-500"
              >
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
                      openTab(tab.href);
                    }}>{tab.label}</button
                  >
                {/snippet}
              </Tabs.Trigger>
            {/each}
          </Tabs.List>
        </Tabs>
      </nav>
    </header>

    <!--
      Row 2: the world. Grouped by what the controls do -- time on the left, with the one primary action;
      the world's identity on the right, with Reset last and quiet because it throws work away.
    -->
    <div
      class="flex flex-wrap items-center gap-x-5 gap-y-2 px-4 py-2 border-b border-surface-200 bg-surface-50"
    >
      <div class="flex items-baseline gap-2">
        <span class="text-xs text-surface-600">Year</span>
        <span class="text-2xl font-semibold tabular-nums leading-none">
          {connecting ? '…' : $moiraiStore.year}
        </span>
      </div>

      <div class="flex items-center gap-2">
        <div class="field-group grid-cols-[4.5rem_auto] inline-grid">
          <input
            type="number"
            min="1"
            name="passYearsCount"
            aria-label="Number of years to pass"
            bind:value={passYearsCount}
            class="input text-sm"
          />
          <button
            type="button"
            class="btn btn-sm preset-filled-primary-500 whitespace-nowrap"
            disabled={passYearsRunning || connecting}
            onclick={() => moiraiStore.passYears(passYearsCount)}
            ><Play />Pass {passYearsCount === 1 ? 'year' : 'years'}</button
          >
        </div>
        {#if passYearsRunning}
          <div class="w-24">
            <Progress value={passYearsPercent} max={100}>
              <Progress.Track>
                <Progress.Range class="bg-primary-500" />
              </Progress.Track>
            </Progress>
          </div>
        {/if}
      </div>

      <form
        onsubmit={(e) => {
          e.preventDefault();
          $moiraiViewStore.gotoYear = yearValue;
          yearValue = undefined;
        }}
      >
        <input
          placeholder="Go to year"
          title="Scroll the records to a year (Ctrl+G)"
          bind:this={yearInput}
          bind:value={yearValue}
          name="gotoYear"
          aria-label="Go to year"
          class="input input-sm w-28 text-sm"
          type="number"
        />
      </form>

      <div class="grow"></div>

      <form
        class="flex items-center gap-1"
        onsubmit={(e) => {
          e.preventDefault();
          applySeed();
        }}
      >
        <label
          for="seed-input"
          class="text-xs text-surface-600"
          title="Base RNG seed — the world is deterministic per seed">Seed</label
        >
        <input
          id="seed-input"
          type="number"
          min="0"
          step="1"
          name="seed"
          aria-label="RNG seed"
          bind:value={seedValue}
          class="input w-24 text-sm"
        />
        {#if seedDirty}
          <button
            type="submit"
            class="btn btn-sm preset-filled-primary-500"
            disabled={connecting}
            title="Rebuild the world from this seed">Apply</button
          >
        {/if}
        <button
          type="button"
          class="btn-icon btn-icon-sm hover:preset-tonal"
          disabled={connecting}
          onclick={rollSeed}
          title="Pick a random seed and rebuild the world"
          aria-label="Random seed"><Dice /></button
        >
      </form>

      <button
        type="button"
        class="btn btn-sm hover:preset-tonal text-surface-700"
        disabled={connecting}
        onclick={() => moiraiStore.reset()}
        title="Rebuild this world from its start year"><Restart />Reset</button
      >
      <button
        type="button"
        class="btn btn-sm {eventsOpen ? 'preset-tonal-primary' : 'hover:preset-tonal'}"
        aria-pressed={eventsOpen}
        onclick={toggleEvents}
        title="Show the event list: run an event now, or hide its records (Ctrl+D)"
        ><ListChecks />Events</button
      >
      {#if bootNotice}
        <!--
          Inside the toolbar row, as a full-width wrap item, so the layout's three rows stay three. Only
          set when the in-browser engine could not build the story it was asked for (see WasmApi.boot).
        -->
        <p class="basis-full text-sm text-warning-900 flex items-baseline gap-3" role="status">
          <span>{bootNotice}</span>
          <button
            type="button"
            class="btn btn-sm hover:preset-tonal"
            onclick={() =>
              // Resolved; the rule only recognises a bare resolve() argument (see the nav tabs).
              // eslint-disable-next-line svelte/no-navigation-without-resolve
              goto(`${resolve('/story')}${currentSearch()}`)}>Open the story</button
          >
        </p>
      {/if}
    </div>

    <div class="flex min-h-0">
      {#if panelOpen}
        <aside class="w-80 shrink-0 min-h-0 overflow-y-auto border-r border-surface-200 p-4">
          {#if showDetails && eventsOpen}
            <div class="flex gap-1 p-1 mb-3 rounded-lg bg-surface-100" role="tablist">
              {#each PANEL_TABS as t (t)}
                <button
                  type="button"
                  role="tab"
                  aria-selected={activeTab === t}
                  class="flex-1 btn btn-sm capitalize {activeTab === t
                    ? 'bg-white shadow-sm'
                    : 'text-surface-600'}"
                  onclick={() => (activeTab = t)}>{t}</button
                >
              {/each}
            </div>
          {/if}
          {#if activeTab === 'details' && showDetails}
            <DetailsPanel />
          {:else if $moiraiStore.clientData}
            <!-- Closed from the Events button in the toolbar, which stays pressed while this is open. -->
            <ActionList />
          {/if}
        </aside>
      {/if}
      <main class="flex-1 min-w-0 min-h-0 h-full p-4 space-y-4">
        {@render children?.()}
      </main>
    </div>
  </div>
</QueryClientProvider>
