<script lang="ts">
  import { moiraiStore } from '$lib/connection';
  import { allTags, visibleRecords } from '$lib/records';
  import {
    createTable,
    FlexRender,
    renderComponent,
    tableFeatures,
    columnSizingFeature,
    columnVisibilityFeature,
  } from '@tanstack/svelte-table';
  import type { ColumnDef } from '@tanstack/svelte-table';
  import MoiraiText from '../../components/MoiraiText.svelte';
  import RuleCell from '../../components/RuleCell.svelte';
  import CauseDialog from '../../components/CauseDialog.svelte';
  import { readWhy } from '$lib/why';
  import { rememberWhy } from '$lib/why-history';
  import { page } from '$app/state';
  import type { Record } from '$lib/types';
  import { createVirtualizer } from '@tanstack/svelte-virtual';
  import { filteredEntity, filteredTag, selectedEntity } from '$lib/utils';
  import { moiraiViewStore } from '$lib';
  import { unquote } from '$lib/format';
  import binarysearch from 'binary-search';

  const selected = $derived(selectedEntity(page).getNumber());
  const filtered = $derived(filteredEntity(page).getNumber());
  const tagFilter = $derived(filteredTag(page).get() ?? '');

  // Union of all tags seen across loaded records, for the chronicle filter bar.
  const tags = $derived(allTags($moiraiStore.records));

  // The record whose "why?" is open, by its firing. One dialog for the page: rows are reused as it
  // scrolls. It starts from the URL, which is how Back from a story line finds it again ($lib/why).
  let why: number | null = $state(readWhy(window.location.search));
  // The record to bring into view once the rows exist: the one a Back returned to.
  let reveal: number | null = readWhy(window.location.search);

  function showWhy(firing: number | null) {
    why = firing;
    rememberWhy(firing);
  }

  function toggleTag(tag: string) {
    const param = filteredTag(page);
    param.set(tagFilter === tag ? '' : tag);
  }

  // v9 requires features to be registered explicitly; we only need sizing
  // (header.getSize) and visibility (row.getVisibleCells).
  const features = tableFeatures({ columnSizingFeature, columnVisibilityFeature });

  const columns: ColumnDef<typeof features, Record>[] = [
    {
      header: 'Year',
      accessorKey: 'year',
      size: 64,
      // Only where the year changes: a column of identical numbers is noise, and a gap reads as "same
      // year" at a glance.
      cell: (info) => {
        const year = info.cell.getValue<number>();
        const prev = rowData[info.row.index - 1];
        return prev && prev.year === year ? '' : String(year);
      },
    },
    {
      header: 'Record',
      accessorKey: 'text',
      cell: (info) =>
        renderComponent(MoiraiText, { text: info.cell.getValue() as string, selected }),
    },
    {
      // The rule that wrote the record -- the trigger itself, where actionId is the event it belongs to
      // -- as a button that asks why it ran.
      header: 'Rule',
      size: 180,
      id: 'actionId',
      accessorKey: 'actionId',
      cell: (info) => {
        const r = info.row.original;
        const rule =
          r.rule ?? $moiraiStore.clientData?.actions.find((a) => a.id === r.actionId)?.name ?? '';
        return renderComponent(RuleCell, { rule, firing: r.firing, onwhy: showWhy });
      },
    },
  ];

  const hiddenActionIds = $derived(
    new Set(($moiraiStore.clientData?.actions ?? []).filter((a) => a.hidden).map((a) => a.id)),
  );

  const rowData = $derived(
    visibleRecords($moiraiStore.records, {
      entity: filtered,
      tag: tagFilter,
      hiddenActionIds,
    }).map((r) => ({ ...r, selected })),
  );

  let virtualListEl: HTMLDivElement | undefined = $state();

  const table = createTable({
    features,
    columns,
    get data() {
      return rowData;
    },
  });

  const rows = $derived(table.getRowModel().rows);

  let offset = 0;

  const virtualizer = $derived.by(() => {
    // Read virtualListEl here (not just inside getScrollElement) so the
    // virtualizer is rebuilt once bind:this has attached the scroll container.
    const scrollEl = virtualListEl ?? null;
    return createVirtualizer<HTMLDivElement, HTMLTableRowElement>({
      count: rows.length,
      onChange: (range) => {
        offset = range.scrollOffset ?? 0;
      },
      getScrollElement: () => scrollEl,
      initialOffset: offset,

      estimateSize: () => 44,
      overscan: 20,
    });
  });

  function measure(node: HTMLTableRowElement) {
    // The virtualizer keeps a ResizeObserver on the row from here, keyed by its data-index.
    $virtualizer.measureElement(node);
  }

  // Back from a story line: scroll the asked-about record into the middle once its row exists. Records
  // arrive through the feed, so the first run may be too early; `reveal` is plain, not $state, so this
  // settles when the row turns up instead of re-running on its own write.
  $effect(() => {
    if (reveal === null) return;
    const index = rows.findIndex((r) => r.original.firing === reveal);
    if (index < 0) return;
    reveal = null;
    $virtualizer.scrollToIndex(index, { align: 'center' });
  });

  $effect(() => {
    if ($moiraiViewStore.gotoYear) {
      const index = binarysearch(
        $moiraiStore.records,
        { year: $moiraiViewStore.gotoYear },
        (a, b) => a.year - b.year,
      );
      const scrollOffset = $virtualizer.getOffsetForIndex(index);
      if (scrollOffset) $virtualizer.scrollToOffset(scrollOffset[0]);
      $moiraiViewStore.gotoYear = undefined;
    }
  });
</script>

<CauseDialog firing={why} onclose={() => showWhy(null)} />

<div class="h-full flex flex-col min-h-0">
  {#if tags.length > 0}
    <div class="tag-bar shrink-0">
      {#each tags as tag (tag)}
        <button
          type="button"
          class="tag {tagFilter === tag ? 'on' : ''}"
          aria-pressed={tagFilter === tag}
          onclick={() => toggleTag(tag)}
        >
          {unquote(tag)}
        </button>
      {/each}
      {#if tagFilter !== ''}
        <button
          type="button"
          class="text-xs text-surface-600 hover:underline ml-1"
          onclick={() => toggleTag(tagFilter)}
        >
          Clear filter
        </button>
      {/if}
    </div>
  {/if}
  <div class="scroll-container" bind:this={virtualListEl}>
    <div style="position: relative; height: {$virtualizer.getTotalSize()}px;">
      <table class="table table-fixed w-full" style="overflow:unset">
        <thead>
          {#each table.getHeaderGroups() as headerGroup (headerGroup.id)}
            <tr>
              {#each headerGroup.headers as header (header.id)}
                <!-- The record takes whatever the fixed columns leave. -->
                <th
                  class={header.id}
                  style={header.id !== 'text' ? `width: ${header.getSize()}px` : ''}
                >
                  {#if !header.isPlaceholder}
                    <FlexRender {header} />
                  {/if}
                </th>
              {/each}
            </tr>
          {/each}
        </thead>
        <tbody>
          {#each $virtualizer.getVirtualItems() as row (row.index)}
            <!--
              Rows are measured, not assumed 44px: a record that wraps -- most of them, on a phone -- is
              taller, and with a fixed size the list jumped by the difference each time a row left the
              top. The rows stay in the table's flow, so they all move by the first one's offset.
            -->
            <tr
              data-index={row.index}
              use:measure
              class:asked={why !== null && rows[row.index].original.firing === why}
              style="transform: translateY({$virtualizer.getVirtualItems()[0].start}px);"
            >
              {#each rows[row.index].getVisibleCells() as cell (cell.id)}
                <td class={cell.column.id}>
                  <FlexRender {cell} />
                </td>
              {/each}
            </tr>
          {/each}
        </tbody>
      </table>
    </div>
  </div>
</div>

<style>
  .table {
    background-color: transparent;
  }
  .table thead th {
    font-size: 0.75rem;
    font-weight: 500;
    color: var(--color-surface-600);
    border-bottom: 1px solid var(--color-surface-200);
  }
  .table tbody tr {
    border-bottom: 1px solid var(--color-surface-100);
    background: transparent;
  }
  .table tbody tr:hover {
    background: var(--color-surface-50);
  }
  /* The record whose "why?" is open, so it is easy to find again behind the dialog and after a Back. */
  .table tbody tr.asked {
    background: var(--color-primary-50);
  }
  .table td {
    vertical-align: baseline;
    line-height: 1.75rem;
  }
  :global(td.year) {
    font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
    font-size: 0.8125rem;
    font-variant-numeric: tabular-nums;
    color: var(--color-surface-600);
  }
  :global(td.actionId) {
    font-size: 0.75rem;
    color: var(--color-surface-500);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  /* On a phone the fixed columns (64 + 180) left the record a third of the row, and its chips, which do
   * not wrap, ran under the rule. The rule keeps a stub: it is still the "why?" button. */
  @media (max-width: 639px) {
    .table :global(th.year),
    .table :global(td.year) {
      width: 2.75rem !important;
      padding-inline: 0 0.25rem;
    }
    .table :global(th.actionId),
    .table :global(td.actionId) {
      width: 5rem !important;
      padding-inline: 0.25rem 0;
    }
    .table :global(td.text) {
      padding-inline: 0.25rem;
    }
  }
  .scroll-container {
    flex: 1;
    min-height: 0;
    width: 100%;
    overflow: auto;
  }
  .tag-bar {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.25rem;
    padding: 0 0 0.5rem;
  }
  .tag-bar .tag {
    cursor: pointer;
  }
  .tag-bar .tag:hover {
    border-color: var(--color-surface-500);
  }
  .tag-bar .tag.on {
    background: var(--color-primary-500);
    border-color: var(--color-primary-500);
    color: white;
  }
</style>
