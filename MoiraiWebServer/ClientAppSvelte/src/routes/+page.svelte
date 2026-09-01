<script lang="ts">
  import { moiraiStore } from '$lib/connection';

  /**
   * The landing view. A placeholder: the root used to redirect straight to the records feed, and this
   * is the page that will eventually say something about the world before you start browsing it.
   */
  const connecting = $derived($moiraiStore.conn === undefined);
</script>

<div class="viz-root h-full overflow-auto">
  <h1 class="h1 font-serif mb-2">Moirai</h1>
  <p class="text-sm opacity-70 max-w-2xl mb-6">
    A world simulated year by year from a story written in its own language. Pick a tab to read the
    records it produced, follow one life through them, or edit the story itself.
  </p>

  <div class="card preset-tonal p-4 max-w-2xl">
    <p class="text-sm">
      {#if connecting}
        Starting the engine…
      {:else}
        The world stands at year {$moiraiStore.year}.
      {/if}
    </p>
    <p class="text-sm opacity-70 mt-2">This page is a placeholder — there is more to put here.</p>
  </div>
</div>
