<script lang="ts">
  import { get } from 'svelte/store';
  import { goto } from '$app/navigation';
  import { resolve } from '$app/paths';
  import { moiraiStore } from '$lib/connection';
  import { formatBecause, stepHeading } from '$lib/cause';
  import type { Cause } from '$lib/types';
  import MoiraiText from './MoiraiText.svelte';

  /**
   * Why a record happened: the rules that ran, from the one that wrote it back to the event the schedule
   * started, each with what set it off and what it wrote.
   *
   * One dialog per page rather than one per row: the records table is virtualised and reuses its rows as
   * it scrolls, so a row cannot own anything that outlives it. `firing` is the record's (see
   * `Record.firing`); null closes it.
   */
  let { firing, onclose }: { firing: number | null; onclose: () => void } = $props();

  let dialog: HTMLDialogElement | undefined = $state();
  let cause: Cause | null = $state(null);
  let failed = $state(false);

  // The Story page exists only where the story can be edited in the page; elsewhere a line is text.
  const canOpenStory = $derived($moiraiStore.conn?.story != null);

  $effect(() => {
    const el = dialog;
    if (!el) return;
    if (firing === null) {
      if (el.open) el.close();
      return;
    }
    if (!el.open) el.showModal();

    cause = null;
    failed = false;
    const asked = firing;
    get(moiraiStore)
      .conn?.getCause(asked)
      .then(
        (c) => {
          if (asked !== firing) return;
          cause = c;
          failed = c === null;
        },
        () => (failed = true),
      );
  });

  function openLine(line: number) {
    // Keep the rest of the query -- the world's seed and year above all -- and replace any old line.
    const kept = window.location.search
      .replace(/^\?/, '')
      .split('&')
      .filter((p) => p !== '' && !p.startsWith('line='));
    onclose();
    // Resolved; the rule only recognises a bare resolve() argument (see the nav tabs).
    // eslint-disable-next-line svelte/no-navigation-without-resolve
    void goto(`${resolve('/story')}?${[...kept, `line=${line}`].join('&')}`);
  }
</script>

<dialog
  bind:this={dialog}
  class="cause card p-0 m-auto w-[min(40rem,calc(100vw-2rem))] max-h-[80vh] overflow-hidden"
  aria-labelledby="cause-title"
  {onclose}
>
  <div class="flex items-center justify-between px-5 py-3 border-b border-surface-200">
    <h2 id="cause-title" class="h5">Why did this happen?</h2>
    <button type="button" class="btn btn-sm hover:preset-tonal" onclick={onclose}>Close</button>
  </div>

  <div class="px-5 py-4 overflow-y-auto max-h-[calc(80vh-3.5rem)]">
    {#if failed}
      <p class="text-sm text-surface-600">
        The engine could not answer just now. If a simulation pass is running, ask again once it
        ends.
      </p>
    {:else if !cause}
      <p class="text-sm text-surface-600">Tracing…</p>
    {:else if cause.steps.length === 0}
      <p class="text-sm text-surface-600">
        No rule wrote this record, so there is nothing to trace.
      </p>
    {:else}
      <p class="text-sm text-surface-600 mb-4">
        From the rule that wrote it back to the event that started it all, most recent first.
      </p>
      <ol class="steps">
        {#each cause.steps as step, i (step.firing)}
          <li class="step" class:first={i === 0}>
            <div class="flex items-baseline gap-2 flex-wrap">
              <span class="tabular-nums font-semibold w-12 shrink-0">{step.year}</span>
              <span class="font-medium">{stepHeading(step)}</span>
              {#if step.line > 0}
                {#if canOpenStory}
                  <button
                    type="button"
                    class="text-xs text-primary-700 hover:underline"
                    title="Open the story at this rule"
                    onclick={() => openLine(step.line)}>line {step.line}</button
                  >
                {:else}
                  <span class="text-xs text-surface-500">line {step.line}</span>
                {/if}
              {/if}
            </div>
            <div class="pl-14 space-y-1 text-sm">
              {#if step.because}
                <p class="text-surface-700">
                  <span class="text-surface-500">because</span>
                  <MoiraiText text={formatBecause(step.because)} selected={0} />
                </p>
              {/if}
              {#each step.records as text, ri (ri)}
                <p class:font-medium={i === 0}><MoiraiText {text} selected={0} /></p>
              {/each}
            </div>
          </li>
        {/each}
      </ol>
    {/if}
  </div>
</dialog>

<style>
  .cause::backdrop {
    background: color-mix(in oklab, var(--color-surface-950) 35%, transparent);
  }
  /* A thread down the left joins the steps, so the chain reads as one line of causes. */
  .steps {
    position: relative;
  }
  .step {
    position: relative;
    padding: 0 0 0.9rem 0;
  }
  .step + .step::before {
    content: '';
    position: absolute;
    left: 1.4rem;
    top: -0.8rem;
    height: 0.7rem;
    border-left: 2px solid var(--color-surface-300);
  }
</style>
