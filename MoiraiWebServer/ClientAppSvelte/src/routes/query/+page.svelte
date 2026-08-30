<script lang="ts">
  import Search from 'virtual:icons/mdi/search';
  import DatabaseSearch from 'virtual:icons/mdi/database-search';
  import PineTree from 'virtual:icons/mdi/pine-tree';
  import { moiraiStore, type QueryResult } from '$lib/connection';
  import MoiraiText from '../../components/MoiraiText.svelte';
  import { selectedEntity } from '$lib/utils';
  import { page } from '$app/stores';
  import { Accordion } from '@skeletonlabs/skeleton-svelte';
  import ChevronDown from 'virtual:icons/mdi/chevron-down';

  let query: string = 'pick Person $p';
  let results: Promise<QueryResult> = new Promise(() => [] as QueryResult[]);
  let selected = selectedEntity($page);

  class Debouncer {
    private timeout: ReturnType<typeof setTimeout> | undefined;
    private readonly callback: () => void;
    private readonly delay: number;

    constructor(callback: () => void, delay: number) {
      this.callback = callback;
      this.delay = delay;
    }

    public debounce() {
      if (this.timeout) {
        clearTimeout(this.timeout);
      }
      this.timeout = setTimeout(this.callback, this.delay);
    }
  }

  function runQuery() {
    if ($moiraiStore.conn) results = $moiraiStore.conn.query(query);
  }
  let debouncer = new Debouncer(runQuery, 500);

  // Run the initial query once the SignalR connection is ready (on a fresh page
  // load the connection often isn't up yet when the component first mounts).
  let ranInitial = false;
  $: if ($moiraiStore.conn && !ranInitial) {
    // Read again on the next run of this reactive block, which the rule's
    // single-pass flow analysis cannot see.
    // eslint-disable-next-line no-useless-assignment
    ranInitial = true;
    runQuery();
  }
</script>

<div class="h-full overflow-auto space-y-4">
  <form class="field-group grid-cols-[auto_1fr_auto]" on:submit|preventDefault={runQuery}>
    <label class="label" for="query">
      <Search />
    </label>
    <input
      id="query"
      class="input"
      bind:value={query}
      on:input={() => debouncer.debounce()}
      type="search"
      name="query"
      aria-label="Query"
      placeholder="Search..."
    />
    <button type="submit" class="btn preset-filled-primary-500">Submit</button>
  </form>
  {#await results then results}
    <div class="card p-4">
      <!-- Open state is internal: v2 bound astOpen/sqlOpen but never read them. -->
      <Accordion multiple>
        <Accordion.Item value="ast">
          <Accordion.ItemTrigger class="flex items-center gap-2">
            <PineTree />
            <span class="flex-auto">AST</span>
            <Accordion.ItemIndicator class="transition-transform data-[state=open]:rotate-180">
              <ChevronDown />
            </Accordion.ItemIndicator>
          </Accordion.ItemTrigger>
          <Accordion.ItemContent>
            <pre class="pre">{JSON.stringify(JSON.parse(results.query), null, 2)}</pre>
          </Accordion.ItemContent>
        </Accordion.Item>
        <Accordion.Item value="sql">
          <Accordion.ItemTrigger class="flex items-center gap-2">
            <DatabaseSearch />
            <span class="flex-auto">SQL</span>
            <Accordion.ItemIndicator class="transition-transform data-[state=open]:rotate-180">
              <ChevronDown />
            </Accordion.ItemIndicator>
          </Accordion.ItemTrigger>
          <Accordion.ItemContent>
            <pre class="pre">{results.sql}</pre>
          </Accordion.ItemContent>
        </Accordion.Item>
      </Accordion>
    </div>

    {#if results.errors && results.errors.length > 0}
      {#each results.errors as error, ei (ei)}
        <aside class="card preset-filled-error-500 p-4 my-2"><div>{error}</div></aside>
      {/each}
    {:else}
      <!--            <div class="w-full inline-block overflow-auto">-->
      <div class="table-wrap overflow-auto">
        <table class="table table-fixed overflow-auto" style="display: block">
          <tbody>
            {#each results.results as result, ri (ri)}
              <tr>
                <td>{result.eid}</td>
                {#each result.properties as prop, pi (pi)}
                  <!--                    <td>{JSON.stringify(result)}</td>-->
                  <td>
                    <div class="text-sm lg:justify-self-end font-semibold leading-6 capitalize">
                      {prop.label}
                    </div>
                    <MoiraiText text={prop.value} selected={selected.getNumber()} />
                  </td>
                {/each}
              </tr>
            {/each}
          </tbody>
        </table>
      </div>
      <!--            </div>-->
    {/if}
  {/await}
</div>
