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
  import { urlParam } from '$lib/utils';
    import { moiraiViewStore } from '$lib';
    import binarysearch from "binary-search";
  let selected: number = -1;

  $: {
    let selParam = urlParam($page, 'e');
    selected = selParam.getNumber(); // Number($page.url.searchParams.get('e')) ?? -1;

    // selected = data.selected;
    tableStore.update((store) => {
      return {
        ...store,
        selected: selected,
      };
    });
  }

$: {
  if($moiraiViewStore.gotoYear){
    const index = binarysearch($moiraiStore.records, {year: $moiraiViewStore.gotoYear}, (a, b) => a.year - b.year);
    const offset = $virtualizer.getOffsetForIndex(index);
    if(offset)
      $virtualizer.scrollToOffset(offset[0]);
  }
}



  const columns: ColumnDef<Record>[] = [
    {
      header: 'Year2',
      accessorKey: 'year',
      size: 25,
      cell: (info) => {
        return flexRender(PreChip, { text: info.cell.getValue() });
      },
    },
    {
      header: 'Text',
      accessorKey: 'text',
      cell: (info) => {
        return flexRender(MoiraiText, { text: info.cell.getValue(), selected: selected });
      },
      //   cell: (info) => {
      //     return createRender(MoiraiText, { text: record.value, selected: Number(sel.get()) });
      //   }
    },
  ];
  const tableStore = writable({ selected: selected });

  const options = derived([moiraiStore, tableStore], ([$moiraiStore]) => {
    console.log('derived');
    return {
      getCoreRowModel: getCoreRowModel(),
      columns,
      data: $moiraiStore.records.map((r) => ({
        ...r,
        selected: selected, // Number(sel.get())
      })),
    };
  });

  let virtualListEl: HTMLDivElement;
  const table = createSvelteTable(options);
  $: rows = $table.getRowModel().rows;
  $: {
    console.log('rerender', selected);
  }

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
  <div class="table-container scroll-container bg-surface-200-700-token" bind:this={virtualListEl}>
    <div style="position: relative; height: {$virtualizer.getTotalSize()}px;">
      <!-- <table class="table table-hover table-compact table-auto w-full"> -->
      <table class="table table-hover table-compact w-full" style="overflow:unset">
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
                <td>
                  <!-- {idx}
                  {row.size}
                  {row.index} -->
                  <svelte:component
                    this={flexRender(cell.column.columnDef.cell, cell.getContext())}
                    data-index={row.index}
                  />
                </td>
              {/each}
            </tr>
          {/each}
        </tbody>
        <!-- <tfoot>
          {#each $table.getFooterGroups() as footerGroup}
            <tr>
              {#each footerGroup.headers as header}
                <th>
                  {#if !header.isPlaceholder}
                    <svelte:component
                      this={flexRender(header.column.columnDef.footer, header.getContext())}
                    />
                  {/if}
                </th>
              {/each}
            </tr>
          {/each}
        </tfoot> -->
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
    height: 88vh;
    width: 100%;
    overflow: auto;
    /* border: solid 1px black; */
  }
</style>
