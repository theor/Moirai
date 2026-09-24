<script lang="ts">
  import { moiraiStore, settledYear } from '$lib/connection';
  import { shareLink } from '$lib/world-address';
  import ContentCopy from 'virtual:icons/mdi/content-copy';
  import Check from 'virtual:icons/mdi/check';
  import { notable } from '$lib/notable';
  import NotableList from '../components/NotableList.svelte';
  import ChronicleCard from '../components/ChronicleCard.svelte';
  import type { Chronicle } from '$lib/types';
  import { get } from 'svelte/store';

  /**
   * The landing view: which world you are looking at, and a link to it.
   *
   * A world is entirely determined by its story, its seed and its year, so a link carrying those three
   * rebuilds it exactly on someone else's machine — no account, no server, no stored state. That is the
   * point of this page, with the other: somewhere to start reading. A world you have never seen gives no
   * clue where its story is, so the page lists who the story mentions most.
   */
  const connecting = $derived($moiraiStore.conn === undefined);
  const shareable = $derived($moiraiStore.conn?.worldInPage === true);
  const seed = $derived($moiraiStore.clientData?.seed);

  const TURNING_POINTS = 8;

  // Refetched on the settled year, like the notable list: it scans every record, and a pass would
  // otherwise ask for it on every tick. The connection is read with get(), not $moiraiStore, because
  // the store changes with every batch of records and the effect would re-run on each one. The ticket
  // drops a slow answer to an older question.
  let chronicle: Chronicle | null = $state(null);
  let asked = 0;
  $effect(() => {
    void $settledYear;
    const conn = get(moiraiStore).conn;
    if (!conn) return;
    const ticket = ++asked;
    conn.getChronicle(TURNING_POINTS).then(
      (c) => {
        if (ticket === asked && c) chronicle = c;
      },
      (err: unknown) => console.error('getChronicle failed', err),
    );
  });

  let copied = $state(false);
  let link = $state('');

  async function copyLink() {
    const conn = $moiraiStore.conn;
    if (!conn || seed === undefined) return;

    // Built here rather than kept in the address bar: an edited story compresses to several kilobytes,
    // and nobody wants that in front of them while they browse. An unedited world shares as a short
    // ?seed=&year=.
    const story = conn.story
      ? { current: await conn.story.get(), shipped: await conn.story.original() }
      : { current: '', shipped: '' };

    link = await shareLink(
      new URL(window.location.href),
      { seed: String(seed), year: $settledYear },
      story,
    );
    try {
      await navigator.clipboard.writeText(link);
      copied = true;
      setTimeout(() => (copied = false), 2000);
    } catch {
      // Clipboard access can be refused; the link is shown below either way, ready to select.
    }
  }
</script>

<div class="h-full overflow-auto">
  <div class="max-w-5xl space-y-8">
    <header>
      <h1 class="h2 font-serif">Moirai</h1>
      <p class="text-sm text-surface-600 max-w-2xl">
        A world simulated year by year from a story written in its own language.
      </p>
      {#if connecting}
        <p class="mt-3">Starting the engine…</p>
      {/if}
    </header>

    {#if !connecting && chronicle}
      <ChronicleCard {chronicle} {seed} />
    {/if}

    {#if $notable.length > 0}
      <section>
        <h2 class="h5 mb-1">Who the story is about</h2>
        <p class="text-sm text-surface-600 mb-4">
          The most mentioned of each kind. Click a name to read its life.
        </p>
        <NotableList groups={$notable} />
      </section>
    {/if}

    {#if !connecting}
      <section class="max-w-2xl">
        <h2 class="h5 mb-1">Share this world</h2>
        {#if shareable}
          <p class="text-sm text-surface-600 mb-3">
            It is not stored anywhere. A story, a seed and a year determine a world completely, so
            this link rebuilds this exact one on any machine — including the story, if you have
            edited it.
          </p>
          <div class="flex items-center gap-2 flex-wrap">
            <button
              type="button"
              class="btn preset-filled-primary-500"
              onclick={() => void copyLink()}
            >
              {#if copied}<Check />Copied{:else}<ContentCopy />Copy link to this world{/if}
            </button>
            {#if link}
              <input class="input flex-auto min-w-60 text-xs" readonly value={link} />
            {/if}
          </div>
        {:else}
          <p class="text-sm text-surface-600">
            This is the server's world, shared by everyone connected to it, so there is no link that
            would carry it elsewhere.
          </p>
        {/if}
      </section>
    {/if}
  </div>
</div>
