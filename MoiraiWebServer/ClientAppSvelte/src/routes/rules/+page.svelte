<script lang="ts">
  import { moiraiStore } from '$lib/connection';
  import type { RuleCoverageReport } from '$lib/types';
  import { hitRate, problemCounts, sortRules, statusOf, type RuleStatus } from '$lib/coverage';
  import { Switch } from '@skeletonlabs/skeleton-svelte';
  import { onMount } from 'svelte';
  import { get } from 'svelte/store';

  // Fetched on demand rather than derived from the store, so the per-second record stream doesn't
  // refetch; we refresh when the simulation year advances (same pattern as DetailsPanel).
  let report: RuleCoverageReport | undefined = $state();
  let loading = $state(true);
  let onlyProblems = $state(false);

  async function refresh() {
    const conn = get(moiraiStore).conn;
    if (!conn) return;
    report = await conn.getRuleCoverage();
    loading = false;
  }

  onMount(() => {
    let prevYear = -1;
    return moiraiStore.subscribe((s) => {
      if (s.conn && s.year !== prevYear) {
        prevYear = s.year;
        void refresh();
      }
    });
  });

  // Status is carried by a glyph and a word as well as by colour -- the two problem hues sit under the
  // chroma floor on this theme, so neither may be the only signal.
  const STATUS: Record<RuleStatus, { glyph: string; label: string; colour: string }> = {
    'never-ran': { glyph: '\u2715', label: 'never ran', colour: 'var(--viz-critical)' },
    'never-completed': { glyph: '!', label: 'never completed', colour: 'var(--viz-warning)' },
    ok: { glyph: '\u2713', label: 'ok', colour: 'var(--viz-muted)' },
  };

  const rules = $derived(report?.rules ?? []);
  const counts = $derived(problemCounts(rules));
  const sorted = $derived(sortRules(rules).filter((r) => !onlyProblems || statusOf(r) !== 'ok'));

  const fmt = (n: number) => n.toLocaleString();
</script>

<div class="viz-root h-full overflow-auto">
  <div class="flex items-baseline gap-3 flex-wrap mb-1">
    <h1 class="h2">Rules</h1>
    {#if report}
      <p class="text-sm opacity-70">
        {rules.length} rules over {fmt(report.year)} years
      </p>
    {/if}
  </div>

  <p class="text-sm opacity-70 mb-3 max-w-3xl">
    A rule that never ran is dead code in the story; one that never completed always aborts — an
    event whose <code class="code">pick</code> finds nothing, or a trigger whose predicate never matches.
    Neither shows up in the records feed, because neither produces records.
  </p>

  {#if loading}
    <p class="opacity-60">Loading…</p>
  {:else if rules.length === 0}
    <p class="opacity-60">No rules in this story.</p>
  {:else}
    <div class="flex items-center gap-4 mb-3 flex-wrap">
      <span class="badge preset-tonal" style="color: var(--viz-critical)">
        ✕ {counts.neverRan} never ran
      </span>
      <span class="badge preset-tonal" style="color: var(--viz-warning)">
        ! {counts.neverCompleted} never completed
      </span>
      <Switch
        name="onlyProblems"
        checked={onlyProblems}
        onCheckedChange={(e) => (onlyProblems = e.checked)}
      >
        <Switch.HiddenInput />
        <Switch.Control><Switch.Thumb /></Switch.Control>
        <Switch.Label>Only problems</Switch.Label>
      </Switch>
    </div>

    <table class="table caption-bottom">
      <thead>
        <tr>
          <th class="w-40">Status</th>
          <th>Rule</th>
          <th class="w-56">Fires on</th>
          <th class="w-24 text-right">Attempts</th>
          <th class="w-24 text-right">Completed</th>
          <th class="w-44">Hit rate</th>
        </tr>
      </thead>
      <tbody>
        {#each sorted as r (r.kind + r.id)}
          {@const s = STATUS[statusOf(r)]}
          <tr>
            <td style="color: {s.colour}">
              <span aria-hidden="true" class="font-bold mr-1">{s.glyph}</span>{s.label}
            </td>
            <td>
              <span class="font-semibold">{r.name}</span>
              <span class="opacity-60 text-xs ml-1">{r.kind}</span>
              {#each r.tags as tag (tag)}
                <span class="badge preset-tonal-secondary ml-1 text-xs">{tag}</span>
              {/each}
            </td>
            <td class="opacity-70 text-sm"><code class="code">{r.schedule}</code></td>
            <td class="text-right tabular-nums">{fmt(r.attempts)}</td>
            <td class="text-right tabular-nums">{fmt(r.successes)}</td>
            <td>
              <div class="flex items-center gap-2">
                <div
                  class="viz-meter grow"
                  title="{(hitRate(r) * 100).toFixed(1)}% of attempts completed"
                >
                  <div style="width: {hitRate(r) * 100}%"></div>
                </div>
                <span class="tabular-nums text-xs w-12 text-right"
                  >{(hitRate(r) * 100).toFixed(1)}%</span
                >
              </div>
            </td>
          </tr>
        {/each}
      </tbody>
    </table>
  {/if}
</div>
