<script lang="ts">
  import Search from 'virtual:icons/mdi/search';
  import DatabaseSearch from 'virtual:icons/mdi/database-search';
  import PineTree from 'virtual:icons/mdi/pine-tree';
  import { moiraiStore, type QueryResult } from '$lib/connection';
  import MoiraiText from '../../components/MoiraiText.svelte';
  import { selectedEntity } from '$lib/utils';
  import { page } from '$app/state';
  import { Accordion } from '@skeletonlabs/skeleton-svelte';
  import ChevronDown from 'virtual:icons/mdi/chevron-down';

  let query = $state('pick Person $p');
  let results = $state<Promise<QueryResult>>(new Promise(() => {}));
  let selected = selectedEntity(page);

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

  /**
   * Rows are revealed a few per frame, not all at once. The default query lists every Person, and at a
   * few centuries that is ~16k nodes — the better part of a second on the main thread, which held the
   * tab switch that opened this page for as long. In runes mode, too: legacy mode re-rendered every row
   * already shown on each step, so the steps grew from 100 ms to nearly 400.
   */
  const ROWS_PER_FRAME = 20;
  let shown = $state(ROWS_PER_FRAME);
  let revealRun = 0;

  function runQuery() {
    if (!$moiraiStore.conn) return;
    const run = ++revealRun;
    shown = ROWS_PER_FRAME;
    results = $moiraiStore.conn.query(query);
    results.then(
      (r) => {
        const grow = () => {
          if (run !== revealRun || shown >= (r.results?.length ?? 0)) return;
          shown += ROWS_PER_FRAME;
          requestAnimationFrame(grow);
        };
        requestAnimationFrame(grow);
      },
      () => {},
    );
  }
  let debouncer = new Debouncer(runQuery, 500);

  // Run the initial query once the SignalR connection is ready (on a fresh page
  // load the connection often isn't up yet when the component first mounts).
  let ranInitial = false;
  $effect(() => {
    if (!$moiraiStore.conn || ranInitial) return;
    ranInitial = true;
    // After the page's first paint, so arriving here is not held up by the query.
    setTimeout(runQuery);
  });
</script>

<div class="h-full overflow-auto space-y-4">
  <form
    class="field-group grid-cols-[auto_1fr_auto]"
    onsubmit={(e) => {
      e.preventDefault();
      runQuery();
    }}
  >
    <label class="label" for="query">
      <Search />
    </label>
    <input
      id="query"
      class="input"
      bind:value={query}
      oninput={() => debouncer.debounce()}
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
            <span class="flex-auto">Parsed as</span>
            <Accordion.ItemIndicator class="transition-transform data-[state=open]:rotate-180">
              <ChevronDown />
            </Accordion.ItemIndicator>
          </Accordion.ItemTrigger>
          <Accordion.ItemContent>
            <!-- The engine prints the parsed expression back as .sg with the precedence made
                 explicit, which is what answers "how was my text read?". It used to be a JSON dump of
                 the node graph, which read worse and was the last thing forcing reflection into the
                 WebAssembly build. -->
            <pre class="pre">{results.query}</pre>
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
            {#each results.results.slice(0, shown) as result, ri (ri)}
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
