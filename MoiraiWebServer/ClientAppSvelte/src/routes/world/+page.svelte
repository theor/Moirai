<script lang="ts">
  import { moiraiStore, settledYear } from '$lib/connection';
  import type { ChartableProperty, TimeSeries, WorldOverview } from '$lib/types';
  import { compact } from '$lib/chart';
  import LineChart from '../../components/LineChart.svelte';
  import { onMount } from 'svelte';
  import { get } from 'svelte/store';

  // Fetched on demand rather than derived from the store, so the per-second record stream doesn't
  // refetch; we refresh when the simulation year advances (same pattern as DetailsPanel).
  let overview: WorldOverview | undefined = $state();
  let loading = $state(true);

  let picked: ChartableProperty | undefined = $state();
  let pickedSeries: TimeSeries | undefined = $state();

  async function refresh() {
    const conn = get(moiraiStore).conn;
    if (!conn) return;
    overview = await conn.getWorldOverview();
    loading = false;
    // Default to the first bool property: on most stories that is "who is alive", the one series
    // everything else is read against.
    picked ??= overview.properties.find((p) => p.kind === 'bool') ?? overview.properties[0];
    await loadPicked();
  }

  async function loadPicked() {
    const conn = get(moiraiStore).conn;
    if (!conn || !picked) return;
    pickedSeries = await conn.getPropertySeries(picked.typeId, picked.propertyName);
  }

  function pick(key: string) {
    picked = overview?.properties.find((p) => keyOf(p) === key);
    pickedSeries = undefined;
    void loadPicked();
  }

  const keyOf = (p: ChartableProperty) => `${p.typeId}.${p.propertyName}`;

  // The settled year, not every year: an overview replays the whole changeset log, and a series replays
  // it again per property. See $lib/settled-year.
  onMount(() => settledYear.subscribe(() => void refresh()));

  const tiles = $derived([
    { label: 'Year', value: overview?.year ?? 0 },
    { label: 'Entities', value: overview?.entities ?? 0 },
    { label: 'Records', value: overview?.records ?? 0 },
    { label: 'World changes', value: overview?.changesets ?? 0 },
  ]);
</script>

<div class="viz-root h-full overflow-auto pr-2">
  <h1 class="h2 mb-1">World</h1>
  <p class="text-sm opacity-70 mb-3 max-w-3xl">
    Everything here is replayed from the changeset log after the fact — the simulation records none
    of it. Each chart is its own facet with its own scale; nothing shares an axis.
  </p>

  {#if loading}
    <p class="opacity-60">Loading…</p>
  {:else if !overview}
    <p class="opacity-60">No world yet.</p>
  {:else}
    <div class="grid grid-cols-2 sm:grid-cols-4 gap-3 mb-5">
      {#each tiles as t (t.label)}
        <div class="card preset-tonal p-3">
          <div class="text-xs opacity-70">{t.label}</div>
          <div class="text-2xl font-semibold">{compact(t.value)}</div>
        </div>
      {/each}
    </div>

    <h2 class="h4 mb-2">Over time</h2>
    <div class="grid grid-cols-1 lg:grid-cols-2 xl:grid-cols-3 gap-x-6 gap-y-4 mb-6">
      {#each overview.series as s (s.label)}
        <div class="card p-3"><LineChart title={s.label} series={s} /></div>
      {/each}
    </div>

    {#if overview.properties.length > 0}
      <h2 class="h4 mb-1">Any property</h2>
      <p class="text-sm opacity-70 mb-2 max-w-3xl">
        A bool becomes the number of entities holding it true; a number becomes their mean. This is
        how you see a population, or an average prosperity, that nothing in the engine ever tracked.
      </p>
      <label class="label max-w-md mb-3">
        <span class="sr-only">Property to plot</span>
        <select
          class="select"
          value={picked ? keyOf(picked) : ''}
          onchange={(e) => pick(e.currentTarget.value)}
        >
          {#each overview.properties as p (keyOf(p))}
            <option value={keyOf(p)}>
              {p.typeName}.{p.propertyName} — {p.kind === 'bool' ? 'count where true' : 'mean'}
            </option>
          {/each}
        </select>
      </label>

      <div class="card p-4 mb-6">
        {#if pickedSeries}
          <LineChart title={pickedSeries.label} series={pickedSeries} height={220} tableView />
        {:else}
          <p class="text-sm opacity-60">Loading…</p>
        {/if}
      </div>
    {/if}
  {/if}
</div>
