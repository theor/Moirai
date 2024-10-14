<script lang="ts">
  import { moiraiStore } from '$lib/connection';
  import { createTable, Subscribe, Render, createRender } from 'svelte-headless-table';
    import { derived } from 'svelte/store';
    import MoiraiText from '../../components/MoiraiText.svelte';
    import { selectedEntity } from '$lib/utils';
import { page } from '$app/stores';
    
    let sel;
    sel = selectedEntity($page);
    $: sel = selectedEntity($page);


  const recordStore = derived([moiraiStore, page], ([$moiraiStore]) => {
    return $moiraiStore.records.map(r => ({...r, selected: Number(sel.get())}));
  });

  $: {
        console.warn("!!!", $page.url.searchParams)  
    }
  const table = createTable(recordStore);
  const columns = table.createColumns([
    table.column({
      header: 'Year',
      accessor: 'year',
    }),
    table.column({
      header: 'Text',
      accessor: 'text',cell: (record) => {
        return createRender(MoiraiText, { text: record.value, selected: Number(sel.get()) });
      }
    }),
  ]);
  const { headerRows, rows, tableAttrs, tableBodyAttrs,  } = table.createViewModel(columns);
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
    <table class="table table-hover table-compact table-auto w-full" {...$tableAttrs}>
      <thead>
        {#each $headerRows as headerRow (headerRow.id)}
          <Subscribe rowAttrs={headerRow.attrs()} let:rowAttrs>
            <tr {...rowAttrs}>
              {#each headerRow.cells as cell (cell.id)}
                <Subscribe attrs={cell.attrs()} let:attrs>
                  <th {...attrs}>
                    <Render of={cell.render()} />
                  </th>
                </Subscribe>
              {/each}
            </tr>
          </Subscribe>
        {/each}
      </thead>
      <tbody {...$tableBodyAttrs}>
        {#each $rows as row (row.id)}
          <Subscribe rowAttrs={row.attrs()} let:rowAttrs>
            <tr {...rowAttrs}>
              {#each row.cells as cell (cell.id)}
                <Subscribe attrs={cell.attrs()} let:attrs>
                  <td {...attrs}>
                    <Render of={cell.render()} />
                  </td>
                </Subscribe>
              {/each}
            </tr>
          </Subscribe>
        {/each}
      </tbody>
    </table>
  </div>
</div>
