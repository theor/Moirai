<script lang="ts">
  import '../app.postcss';
  import { AppBar } from '@skeletonlabs/skeleton';
  import DetailsPanel from '../components/DetailsPanel.svelte';
  import { computePosition, autoUpdate, offset, shift, flip, arrow } from '@floating-ui/dom';
  import { storePopup } from '@skeletonlabs/skeleton';
  storePopup.set({ computePosition, autoUpdate, offset, shift, flip, arrow });
</script>

<!-- App Shell -->
<div class="grid h-screen grid-rows-[auto_1fr_auto]">
  <header>
    <!-- App Bar -->
    <AppBar>
      <svelte:fragment slot="lead">
        <img src="/icon.png" alt="Moirai" class="w-8 h-8 mr-2" />
        <strong class="text-xl uppercase">Moirai</strong>
      </svelte:fragment>
      <svelte:fragment slot="trail">
        <a href="/records">Records</a>
        <a href="/records">Changesets</a>
      </svelte:fragment>
    </AppBar>
  </header>
  <div class="grid grid-cols-1 md:grid-cols-[1fr_3fr]">
    <aside class="p-4">
      <DetailsPanel />
    </aside>
    <main class="space-y-4 p-4">
      <slot />
    </main>
  </div>
</div>
