<script lang="ts">
  import { moiraiStore } from '$lib/connection';
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
  import { page } from '$app/stores';
  import type { Record } from '$lib/types';
  import { createVirtualizer } from '@tanstack/svelte-virtual';
  import PreChip from '../../components/PreChip.svelte';
  import { filteredEntity, filteredTag, selectedEntity } from '$lib/utils';
  import { moiraiViewStore } from '$lib';
  import binarysearch from 'binary-search';

  const selected = $derived(selectedEntity($page).getNumber());
  const filtered = $derived(filteredEntity($page).getNumber());
  const tagFilter = $derived(filteredTag($page).get() ?? '');

  // Union of all tags seen across loaded records, for the chronicle filter bar.
  const allTags = $derived(
    Array.from(new Set($moiraiStore.records.flatMap((r) => r.tags ?? []))).sort(),
  );

  function toggleTag(tag: string) {
    const param = filteredTag($page);
    param.set(tagFilter === tag ? '' : tag);
  }

  // v9 requires features to be registered explicitly; we only need sizing
  // (header.getSize) and visibility (row.getVisibleCells).
  const features = tableFeatures({ columnSizingFeature, columnVisibilityFeature });

  const columns: ColumnDef<typeof features, Record>[] = [
    {
      header: 'Year',
      accessorKey: 'year',
      size: 70,
      cell: (info) => renderComponent(PreChip, { text: info.cell.getValue() as string }),
    },
    {
      header: 'Event',
      minSize: 75,
      id: 'actionId',
      accessorKey: 'actionId',
      cell: (info) => {
        const actionId = info.cell.getValue<number>();
        const action = $moiraiStore.clientData!.actions.find((a) => a.id === actionId);
        return renderComponent(MoiraiText, { text: action?.name ?? '', selected });
      },
    },
    {
      header: 'Text',
      accessorKey: 'text',
      cell: (info) =>
        renderComponent(MoiraiText, { text: info.cell.getValue() as string, selected }),
    },
  ];

  const rowData = $derived(
    $moiraiStore.records
      .filter(
        (r) =>
          (filtered < 0 ||
            (r.participants?.includes(filtered) ?? r.text.indexOf('#' + filtered + '>') !== -1)) &&
          (tagFilter === '' || (r.tags?.includes(tagFilter) ?? false)) &&
          $moiraiStore.clientData!.actions.find((a) => a.id === r.actionId)?.hidden !== true,
      )
      .map((r) => ({
        ...r,
        selected,
      })),
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

<div class="h-full flex flex-col min-h-0">
  {#if allTags.length > 0}
    <div class="tag-bar shrink-0">
      {#each allTags as tag (tag)}
        <button
          type="button"
          class="chip {tagFilter === tag ? 'preset-filled-primary-500' : 'preset-tonal'}"
          onclick={() => toggleTag(tag)}
        >
          {tag}
        </button>
      {/each}
      {#if tagFilter !== ''}
        <button type="button" class="chip preset-tonal" onclick={() => toggleTag(tagFilter)}>
          clear
        </button>
      {/if}
    </div>
  {/if}
  <div class="table-wrap scroll-container bg-surface-200-800" bind:this={virtualListEl}>
    <div style="position: relative; height: {$virtualizer.getTotalSize()}px;">
      <table class="table table-fixed w-full" style="overflow:unset">
        <thead>
          {#each table.getHeaderGroups() as headerGroup (headerGroup.id)}
            <tr>
              {#each headerGroup.headers as header, idx (header.id)}
                <th
                  style={idx !== headerGroup.headers.length - 1
                    ? `width: ${header.getSize()}px`
                    : ''}
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
          {#each $virtualizer.getVirtualItems() as row, idx (row.index)}
            <tr
              class:odd={row.index % 2 === 0}
              style="height: {row.size + 1}px; transform: translateY({row.start -
                idx * row.size}px);"
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
  /* Tailwind 4 needs the theme in scope for @apply inside component styles. */
  @reference "../../app.css";

  .table tbody tr {
    border-bottom-width: 0px;
  }
  .table {
    background-color: transparent;
  }
  .table tbody tr {
    background-color: var(--color-surface-100);
  }
  .table tbody tr.odd {
    background-color: color-mix(in oklab, var(--color-surface-500) 5%, transparent);
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
    gap: 0.25rem;
    padding: 0.25rem 0.5rem;
  }
  .tag-bar .chip {
    cursor: pointer;
  }

  :global(td.actionId span) {
    @apply inline-block;
    @apply text-ellipsis;
    @apply overflow-hidden;
    @apply w-full;
  }
</style>
