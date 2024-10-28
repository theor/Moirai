<script lang="ts">
  import '../app.postcss';
  import { AppBar } from '@skeletonlabs/skeleton';
  import DetailsPanel from '../components/DetailsPanel.svelte';
  import { computePosition, autoUpdate, offset, shift, flip, arrow } from '@floating-ui/dom';
  import { storePopup } from '@skeletonlabs/skeleton';
  import { moiraiStore } from '$lib/connection';
  import { moiraiViewStore } from '$lib';
  import { shortcut } from '$lib/shortcut';
  import { TabGroup, Tab, TabAnchor } from '@skeletonlabs/skeleton';
  import { page } from '$app/stores';
  
  import { QueryClient, QueryClientProvider } from '@tanstack/svelte-query'

  let yearInput: HTMLInputElement | undefined;
  let yearValue: number | undefined = undefined;
  storePopup.set({ computePosition, autoUpdate, offset, shift, flip, arrow });
  function gotoLine() {
    console.log('gotoLine');
    yearInput?.focus();
    yearInput?.select();
  }
  const queryClient = new QueryClient()
</script>

<!-- App Shell -->
<QueryClientProvider client={queryClient}>
<div
  class="flex flex-col h-full"
  use:shortcut={{ control: true, code: 'KeyG', callback: gotoLine }}
>
  <header>
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
          <!-- ... -->
        </TabGroup>
        <span class="divider-vertical" />
        <span>Year {$moiraiStore.year}</span>
          <form class="inline"
            on:submit|preventDefault={() => {
              $moiraiViewStore.gotoYear = yearValue;
              yearValue = undefined;
            }}>
        <label class="label inline-block">
          <input
            placeholder="Go to"
            bind:this={yearInput}
          
            bind:value={yearValue}
            class="input w-30"
            type="text"
          />
        </label>
          </form>
        <button type="button" class="btn variant-filled-surface" on:click={() => moiraiStore.reset()}>Reset</button>
        <button type="button" class="btn variant-filled-surface" on:click={() => moiraiStore.passYears(100)}
          >Pass years</button
        >
        <div class="mr-4" />
      </svelte:fragment>
    </AppBar>
  </header>
  <div class="flex-1 h-full flex flex-row">
    <aside class="m-4 basis-1/4 overflow-auto">
      <DetailsPanel />
    </aside>
    <main class="flex-1 space-y-4 p-4 pl-0">
      <slot />
    </main>
  </div>
</div>
</QueryClientProvider>
