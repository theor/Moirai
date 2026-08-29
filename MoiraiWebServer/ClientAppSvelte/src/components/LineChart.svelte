<script lang="ts">
  import type { TimeSeries } from '$lib/types';
  import { compact, nearestIndex, niceTicks } from '$lib/chart';

  interface Props {
    title: string;
    series: TimeSeries;
    /** Offers a "Show data" table under the chart. On by default only for the large explorer chart. */
    tableView?: boolean;
    height?: number;
  }

  let { title, series, tableView = false, height = 132 }: Props = $props();

  // Rendered in real pixels rather than a scaled viewBox, so a 2px line is 2px at every width.
  let width = $state(0);
  let showTable = $state(false);
  let hover: number | undefined = $state();

  const PAD = { top: 12, right: 56, bottom: 20, left: 48 };

  const values = $derived(series?.values ?? []);
  const years = $derived(series?.years ?? []);
  const ticks = $derived(niceTicks(values.length ? Math.max(...values) : 0));
  const domainTop = $derived(ticks[ticks.length - 1] || 1);
  const plotW = $derived(Math.max(1, width - PAD.left - PAD.right));
  const plotH = $derived(Math.max(1, height - PAD.top - PAD.bottom));

  const x = (i: number) =>
    PAD.left + (values.length <= 1 ? plotW / 2 : (i / (values.length - 1)) * plotW);
  const y = (v: number) => PAD.top + plotH - (v / domainTop) * plotH;

  const linePath = $derived(
    values.map((v, i) => `${i ? 'L' : 'M'}${x(i).toFixed(2)},${y(v).toFixed(2)}`).join(' '),
  );
  // The wash closes down to the baseline, never to the first data point.
  const areaPath = $derived(
    values.length > 1
      ? `${linePath} L${x(values.length - 1).toFixed(2)},${y(0).toFixed(2)} L${x(0).toFixed(2)},${y(0).toFixed(2)} Z`
      : '',
  );

  const last = $derived(values.length ? values[values.length - 1] : 0);
  const active = $derived(hover ?? values.length - 1);

  // Identity is not carried by colour here -- one series, named by the title -- but the shape still
  // needs a spoken summary for a reader who cannot see it.
  const summary = $derived(
    values.length
      ? `${title}: ${compact(values[0])} at year ${years[0]} rising and falling to ` +
          `${compact(last)} at year ${years[years.length - 1]}; peak ${compact(Math.max(...values))}.`
      : `${title}: no data.`,
  );

  // The whole svg is the hit target, not the 4px dot; the fraction is measured against the plot box
  // and clamped, so the axis gutters snap to the nearest end sample rather than doing nothing.
  function onmove(e: PointerEvent) {
    const rect = (e.currentTarget as SVGSVGElement).getBoundingClientRect();
    hover = nearestIndex((e.clientX - rect.left - PAD.left) / plotW, values.length);
  }
</script>

<figure class="viz-chart" bind:clientWidth={width}>
  <figcaption class="flex items-baseline justify-between gap-2">
    <span class="text-sm font-semibold truncate" {title}>{title}</span>
    <span class="text-sm tabular-nums shrink-0">{compact(last)}</span>
  </figcaption>

  {#if width > 0 && values.length > 0}
    <div class="relative">
      <!--
        The pointer layer only re-states what is already reachable without it: the end value is direct-
        labelled, aria-label carries the shape, and the explorer chart ships a table view.
      -->
      <svg
        {width}
        {height}
        role="img"
        aria-label={summary}
        onpointermove={onmove}
        onpointerleave={() => (hover = undefined)}
      >
        <!-- Gridlines: hairline, solid, one step off the surface. -->
        {#each ticks as t (t)}
          <line
            x1={PAD.left}
            x2={PAD.left + plotW}
            y1={y(t)}
            y2={y(t)}
            stroke="var(--viz-grid)"
            stroke-width="1"
          />
          <text
            x={PAD.left - 6}
            y={y(t) + 3}
            text-anchor="end"
            font-size="10"
            fill="var(--viz-muted)"
            style="font-variant-numeric: tabular-nums">{compact(t)}</text
          >
        {/each}

        <path d={areaPath} fill="var(--viz-series)" fill-opacity="0.1" />
        <path
          d={linePath}
          fill="none"
          stroke="var(--viz-series)"
          stroke-width="2"
          stroke-linejoin="round"
          stroke-linecap="round"
        />

        <!-- End marker, and the crosshair when the pointer is over the plot. -->
        {#if hover !== undefined}
          <line
            x1={x(active)}
            x2={x(active)}
            y1={PAD.top}
            y2={PAD.top + plotH}
            stroke="var(--viz-axis)"
            stroke-width="1"
          />
        {/if}
        <circle
          cx={x(active)}
          cy={y(values[active])}
          r="4"
          fill="var(--viz-series)"
          stroke="var(--color-surface-50)"
          stroke-width="2"
        />

        <!-- Direct label at the end: the one value worth naming without a hover. -->
        <text
          x={x(values.length - 1) + 9}
          y={y(last) + 3}
          font-size="10"
          fill="var(--viz-muted)"
          style="font-variant-numeric: tabular-nums">{compact(last)}</text
        >

        <text x={PAD.left} y={height - 5} font-size="10" fill="var(--viz-muted)">{years[0]}</text>
        <text
          x={PAD.left + plotW}
          y={height - 5}
          text-anchor="end"
          font-size="10"
          fill="var(--viz-muted)">{years[years.length - 1]}</text
        >
      </svg>

      {#if hover !== undefined}
        <div
          class="viz-tip"
          style="left: {Math.min(Math.max(x(hover), PAD.left), PAD.left + plotW)}px"
        >
          <span class="opacity-70">year {years[hover]}</span>
          <span class="tabular-nums font-semibold ml-2">{compact(values[hover])}</span>
        </div>
      {/if}
    </div>
  {:else}
    <div style="height: {height}px" class="grid place-items-center text-xs opacity-50">No data</div>
  {/if}

  {#if tableView}
    <button
      type="button"
      class="text-xs opacity-70 hover:underline mt-1"
      onclick={() => (showTable = !showTable)}
    >
      {showTable ? 'Hide data' : 'Show data'}
    </button>
    {#if showTable}
      <div class="max-h-64 overflow-auto mt-1">
        <table class="table table-fixed text-xs">
          <thead><tr><th>Year</th><th class="text-right">Value</th></tr></thead>
          <tbody>
            {#each values as v, i (i)}
              <tr>
                <td class="tabular-nums">{years[i]}</td>
                <td class="text-right tabular-nums">{compact(v)}</td>
              </tr>
            {/each}
          </tbody>
        </table>
      </div>
    {/if}
  {/if}
</figure>

<style>
  .viz-chart {
    margin: 0;
  }
  .viz-tip {
    position: absolute;
    top: 0;
    transform: translateX(-50%);
    background: var(--color-surface-100);
    border: 1px solid var(--viz-grid);
    border-radius: 4px;
    padding: 0.1rem 0.4rem;
    font-size: 0.7rem;
    white-space: nowrap;
    pointer-events: none;
  }
</style>
