<script lang="ts">
  import type { Chronicle } from '$lib/types';
  import { describeSpan, eraAt, eraBand } from '$lib/chronicle';
  import { unquote } from '$lib/format';
  import LineChart from './LineChart.svelte';
  import MoiraiText from './MoiraiText.svelte';

  /**
   * The whole world on one card: how long it has run, how its people rose and fell, the ages it went
   * through and the handful of moments that shaped it. It is the first thing a shared link opens on, so
   * it has to tell a story to someone who has never seen Moirai, before they learn to read the feed.
   *
   * The turning points are the story's own heaviest records (`record('…', weight)`), chosen on the
   * engine side (WorldSession.GetChronicle). Nothing here ranks anything.
   */
  // seed is still passed by the page but no longer shown on the card.
  let { chronicle, seed: _seed }: { chronicle: Chronicle; seed: number | undefined } = $props();

  const band = $derived(eraBand(chronicle.eras, chronicle.startYear, chronicle.year));
  const span = $derived(Math.max(1, chronicle.year - chronicle.startYear));
  const topTags = $derived(chronicle.tags.slice(0, 6));
  const tagMax = $derived(topTags.length ? topTags[0].records : 1);
</script>

<section class="card preset-outlined-surface-200-800 p-5 space-y-6 viz-root">
  <header class="flex flex-wrap items-baseline justify-between gap-x-6 gap-y-1">
    <h2 class="h4 font-serif">Major events</h2>
    <p class="text-sm text-surface-600 tabular-nums">
      {describeSpan(chronicle.startYear, chronicle.year)} · {chronicle.records.toLocaleString()} records
    </p>
  </header>

  {#if band.length > 0}
    <div>
      <h3 class="text-xs font-semibold uppercase tracking-wide text-surface-600 mb-1.5">Ages</h3>
      <!--
        Each age is as wide as it lasted. The present, open age is filled; closed ones are outlined, so
        the band reads as history running up to now. A name too long for its segment is truncated and
        carried whole by the title.
      -->
      <div class="relative h-12 rounded-base overflow-hidden flex">
        {#each band as s (s.era.id)}
          <div
            class="era absolute inset-y-0 px-2 py-1 flex flex-col justify-center min-w-0 overflow-hidden"
            class:era-open={s.era.open}
            style="left: {s.left * 100}%; width: {s.width * 100}%"
            title="{s.era.name}, {s.era.start}–{s.era.open ? 'now' : s.era.end}"
          >
            <span class="text-xs font-semibold truncate">{s.era.name}</span>
            <span class="text-[10px] tabular-nums truncate opacity-80">
              {s.era.start}–{s.era.open ? 'now' : s.era.end}
            </span>
          </div>
        {/each}
        <!-- Where the turning points fall, so the list below can be read against the ages. -->
        {#each chronicle.turningPoints as t, i (i)}
          <span
            class="tick absolute bottom-0 w-px h-2"
            style="left: {((t.year - chronicle.startYear) / span) * 100}%"
          ></span>
        {/each}
      </div>
    </div>
  {/if}

  <div class="grid gap-6 md:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
    <div>
      <h3 class="text-xs font-semibold uppercase tracking-wide text-surface-600 mb-2">
        Turning points
      </h3>
      {#if chronicle.turningPoints.length === 0}
        <p class="text-sm text-surface-600">Nothing has happened yet. Pass some years.</p>
      {:else}
        <ol class="space-y-2">
          {#each chronicle.turningPoints as t, i (i)}
            {@const age = eraAt(chronicle.eras, t.year)}
            <li class="grid grid-cols-[3.5rem_minmax(0,1fr)] gap-x-3 text-sm">
              <span class="tabular-nums font-semibold text-right">{t.year}</span>
              <span>
                <MoiraiText text={t.text} selected={0} />
                {#if age && chronicle.eras.length > 1}
                  <span class="block text-xs text-surface-500">{age.name}</span>
                {/if}
              </span>
            </li>
          {/each}
        </ol>
        {#if !chronicle.weighted}
          <p class="mt-3 text-xs text-surface-600">
            This story does not weigh its records, so these are only a sample across time. Give the
            ones that matter a weight — <code class="code">record('…', 3)</code> — and they will be chosen
            instead.
          </p>
        {/if}
      {/if}
    </div>

    <div class="space-y-6">
      {#if chronicle.population.values.length > 1}
        <LineChart title={chronicle.population.label} series={chronicle.population} height={110} />
      {/if}

      {#if topTags.length > 0}
        <div>
          <h3 class="text-xs font-semibold uppercase tracking-wide text-surface-600 mb-2">
            What it has been about
          </h3>
          <ul class="space-y-1.5">
            {#each topTags as t (t.tag)}
              <li class="grid grid-cols-[6rem_minmax(0,1fr)_2.5rem] items-center gap-2 text-sm">
                <span class="truncate">{unquote(t.tag)}</span>
                <div class="viz-meter" aria-hidden="true">
                  <div style="width: {(t.records / tagMax) * 100}%"></div>
                </div>
                <span class="text-xs text-surface-600 tabular-nums text-right">{t.records}</span>
              </li>
            {/each}
          </ul>
        </div>
      {/if}
    </div>
  </div>
</section>

<style>
  /* Closed ages share the band's track colour; the open one takes the series colour, so "now" is the
   * one filled segment. Both are steps of the theme (see .viz-root in app.css). */
  .era {
    background: color-mix(in oklab, var(--viz-track) 35%, var(--viz-surface));
    border-right: 2px solid var(--viz-surface);
    color: var(--color-surface-950);
  }
  .era-open {
    background: var(--viz-series);
    color: var(--color-surface-50);
  }
  .tick {
    background: var(--color-surface-950);
    opacity: 0.55;
  }
</style>
