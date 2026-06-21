<script lang="ts">
  import { moiraiStore } from '$lib/connection';
  import { derived, writable } from 'svelte/store';
  import { createSvelteTable, flexRender, getCoreRowModel } from '@tanstack/svelte-table';
  import type { ColumnDef, TableOptions } from '@tanstack/svelte-table';
  import MoiraiText from '../../components/MoiraiText.svelte';
  import { page } from '$app/stores';
  import type { Record } from '$lib/types';
  import { createVirtualizer } from '@tanstack/svelte-virtual';
  import PreChip from '../../components/PreChip.svelte';
  import { filteredEntity, filteredTag, selectedEntity, urlParam } from '$lib/utils';
  import { moiraiViewStore } from '$lib';
  import binarysearch from 'binary-search';
  let selected: number = -1;
  let filtered: number = -1;
  let tagFilter: string = '';

  $: {
    let selParam = selectedEntity($page);
    selected = selParam.getNumber(); // Number($page.url.searchParams.get('e')) ?? -1;

    let filterParam = filteredEntity($page);
    filtered = filterParam.getNumber();

    tagFilter = filteredTag($page).get() ?? '';

    tableStore.update((store) => {
      return {
        ...store,
        selected: selected,
      };
    });
  }

  // Union of all tags seen across loaded records, for the chronicle filter bar.
  $: allTags = Array.from(
    new Set($moiraiStore.records.flatMap((r) => r.tags ?? [])),
  ).sort();

  function toggleTag(tag: string) {
    const param = filteredTag($page);
    param.set(tagFilter === tag ? '' : tag);
  }

  $: {
    if ($moiraiViewStore.gotoYear) {
      const index = binarysearch(
        $moiraiStore.records,
        { year: $moiraiViewStore.gotoYear },
        (a, b) => a.year - b.year,
      );
      const offset = $virtualizer.getOffsetForIndex(index);
      if (offset) $virtualizer.scrollToOffset(offset[0]);
      $moiraiViewStore.gotoYear = undefined;
    }
  }

  const columns: ColumnDef<Record>[] = [
    {
      header: 'Year',
      accessorKey: 'year',
      size: 70,
      cell: (info) => {
        return flexRender(PreChip, { text: info.cell.getValue() });
      },
    },
      {
          header: 'Event',
          minSize: 75,
          id: 'actionId',
          accessorKey: 'actionId',
          cell: (info) => {
              const actionId = info.cell.getValue<number>();
              const action = $moiraiStore.clientData!.actions.find((a) => a.id === actionId);
              return flexRender(MoiraiText, { text: action?.name ?? '', selected: selected });
          },
      },
    {
      header: 'Text',
      accessorKey: 'text',
      cell: (info) => {
        return flexRender(MoiraiText, { text: info.cell.getValue(), selected: selected });
      },
    },
  ];
  const tableStore = writable({ selected: selected });

  const options = derived([moiraiStore, tableStore], ([$moiraiStore]) => {
    return {
      getCoreRowModel: getCoreRowModel(),
      columns,

      data: $moiraiStore.records
        .filter((r) =>
            (filtered < 0 ||
              (r.participants?.includes(filtered) ?? r.text.indexOf('#' + filtered + '>') !== -1)) &&
            (tagFilter === '' || (r.tags?.includes(tagFilter) ?? false)) &&
            $moiraiStore.clientData!.actions.find(a => a.id === r.actionId)?.hidden !== true
        )
        .map((r) => ({
          ...r,
          selected: selected, // Number(sel.get())
        })),
    };
  });

  let virtualListEl: HTMLDivElement;
  const table = createSvelteTable(options);
  $: rows = $table.getRowModel().rows;

  let offset = 0;

  $: virtualizer = createVirtualizer<HTMLDivElement, HTMLTableRowElement>({
    count: rows.length,
    onChange: (range) => {
      offset = range.scrollOffset ?? 0;
    },
    getScrollElement: () => virtualListEl,
    initialOffset: offset,

    estimateSize: () => 44,
    overscan: 20,
  });
</script>

<div>
  {#if allTags.length > 0}
    <div class="tag-bar">
      {#each allTags as tag}
        <button
          type="button"
          class="chip {tagFilter === tag ? 'variant-filled-primary' : 'variant-soft'}"
          on:click={() => toggleTag(tag)}
        >
          {tag}
        </button>
      {/each}
      {#if tagFilter !== ''}
        <button type="button" class="chip variant-ghost" on:click={() => toggleTag(tagFilter)}>
          clear
        </button>
      {/if}
    </div>
  {/if}
  <div class="table-container scroll-container bg-surface-200-700-token" bind:this={virtualListEl}>
    <div style="position: relative; height: {$virtualizer.getTotalSize()}px;">
      <table class="table table-fixed table-hover table-compact w-full" style="overflow:unset">
        <thead>
          {#each $table.getHeaderGroups() as headerGroup}
            <tr>
              {#each headerGroup.headers as header, idx}
                <th
                  style={idx !== headerGroup.headers.length - 1
                    ? `width: ${header.getSize()}px`
                    : ''}
                >
                  {#if !header.isPlaceholder}
                    <svelte:component
                      this={flexRender(header.column.columnDef.header, header.getContext())}
                    />
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
                  <svelte:component
                    this={flexRender(cell.column.columnDef.cell, cell.getContext())}
                    data-index={row.index}
                  />
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
  .table tbody tr {
    border-bottom-width: 0px;
  }
  .table {
    background-color: transparent;
  }
  .table tbody tr {
    background-color: rgb(var(--color-surface-100));
  }
  .table tbody tr.odd {
    background-color: rgb(var(--color-surface-500) / 0.05);
  }
  .scroll-container {
    height: 84vh;
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
      @apply overflow-ellipsis;
      @apply overflow-hidden;
      @apply w-full;
  }
</style>
