<script lang="ts">
  import { moiraiStore } from '$lib/connection';
  import { derived } from 'svelte/store';
  import { createSvelteTable, flexRender, getCoreRowModel } from '@tanstack/svelte-table';
  import type { ColumnDef, TableOptions } from '@tanstack/svelte-table'
  import MoiraiText from '../../components/MoiraiText.svelte';
  import { selectedEntity } from '$lib/utils';
  import { page } from '$app/stores';
    import type { Record } from '$lib/types';

  let sel;
  sel = selectedEntity($page);
  $: sel = selectedEntity($page);

  const columns: ColumnDef<Record>[] = [
    {
      header: 'Year2',
      accessorKey: 'year'
    },
    {
      header: 'Text',
      accessorKey: 'text',
    //   cell: (info) => {
    //     return createRender(MoiraiText, { text: record.value, selected: Number(sel.get()) });
    //   }
    }
  ];
  const options = derived([moiraiStore, page], ([$moiraiStore]) => {
    return {
      getCoreRowModel: getCoreRowModel(),
      columns,
      data: $moiraiStore.records.map((r) => ({ ...r, selected: Number(sel.get()) }))
    };
  });

//   const rerender = () => {
//     options.update((options) => ({
//       ...options,
//       data: defaultData
//     }));
//   };

  const table = createSvelteTable(options);
//   const { headerRows, rows, tableAttrs, tableBodyAttrs } = table.createViewModel(columns);
</script>

<div class="m-4">
  <button type="button" class="btn variant-filled">Reset</button>
  <button type="button" class="btn variant-filled" on:click={() => moiraiStore.passYears(100)}
    >Pass years</button
  >
  <div>
    hello {$moiraiStore.connected}
    {$moiraiStore.records.length}
  </div>
  <div>
    <table class="table table-hover table-compact table-auto w-full" >
        <thead>
            {#each $table.getHeaderGroups() as headerGroup}
              <tr>
                {#each headerGroup.headers as header}
                  <th>
                    {#if !header.isPlaceholder}
                      <svelte:component
                        this={flexRender(
                          header.column.columnDef.header,
                          header.getContext()
                        )}
                      />
                    {/if}
                  </th>
                {/each}
              </tr>
            {/each}
          </thead>
          <tbody>
            {#each $table.getRowModel().rows as row}
              <tr>
                {#each row.getVisibleCells() as cell}
                  <td>
                    <svelte:component
                      this={flexRender(cell.column.columnDef.cell, cell.getContext())}
                    />
                  </td>
                {/each}
              </tr>
            {/each}
          </tbody>
          <tfoot>
            {#each $table.getFooterGroups() as footerGroup}
              <tr>
                {#each footerGroup.headers as header}
                  <th>
                    {#if !header.isPlaceholder}
                      <svelte:component
                        this={flexRender(
                          header.column.columnDef.footer,
                          header.getContext()
                        )}
                      />
                    {/if}
                  </th>
                {/each}
              </tr>
            {/each}
          </tfoot>
    </table>
  </div>
</div>
