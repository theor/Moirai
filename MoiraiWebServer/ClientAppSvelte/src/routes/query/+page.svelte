<script lang="ts">
    import Search from 'virtual:icons/mdi/search';
    import DatabaseSearch from 'virtual:icons/mdi/database-search';
    import PineTree from 'virtual:icons/mdi/pine-tree';
    import {moiraiStore, type QueryResult} from "$lib/connection";
    import MoiraiText from "../../components/MoiraiText.svelte";
    import {selectedEntity} from "$lib/utils";
    import {page} from '$app/stores';
    import {Accordion, AccordionItem} from '@skeletonlabs/skeleton';

    let query: string = 'pick Person $p';
    let results: Promise<QueryResult> = new Promise(() => [] as QueryResult[]);
    let selected = selectedEntity($page);

    let astOpen = false;
    let sqlOpen = false;

    class Debouncer {
        private timeout: NodeJS.Timeout | undefined;
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
        if ($moiraiStore.conn)
            results = $moiraiStore.conn.query(query);
    }
    let debouncer = new Debouncer(runQuery, 500);

    // Run the initial query once the SignalR connection is ready (on a fresh page
    // load the connection often isn't up yet when the component first mounts).
    let ranInitial = false;
    $: if ($moiraiStore.conn && !ranInitial) {
        ranInitial = true;
        runQuery();
    }
</script>

<!--<div class="h-full w-full space-y-4  mb-4 ">-->

    <form class="input-group input-group-divider grid-cols-[auto_1fr_auto]" on:submit|preventDefault={runQuery}>
        <div class="input-group-shim">
            <Search/>
        </div>
        <input bind:value={query} on:input={() => debouncer.debounce()} type="search"
               name="query" aria-label="Query" placeholder="Search..."/>
        <button type="submit" class="variant-filled-primary">Submit</button>
    </form>
    {#await results}
    {:then results}
        <div class="card p-4">
            <Accordion>
                <AccordionItem bind:open={astOpen}>
                    <svelte:fragment slot="lead">
                        <PineTree/>
                    </svelte:fragment>
                    <svelte:fragment slot="summary">AST</svelte:fragment>
                    <svelte:fragment slot="content">
                        <pre class="pre">{JSON.stringify(JSON.parse(results.query), null, 2)}</pre>
                    </svelte:fragment>
                </AccordionItem>
                <AccordionItem bind:open={sqlOpen}>
                    <svelte:fragment slot="lead">
                        <DatabaseSearch/>
                    </svelte:fragment>
                    <svelte:fragment slot="summary">SQL</svelte:fragment>
                    <svelte:fragment slot="content">
                        <pre class="pre">{results.sql}</pre>
                    </svelte:fragment>
                </AccordionItem>
            </Accordion>
        </div>

        {#if results.errors && results.errors.length > 0}
            {#each results.errors as error}
                <aside class="alert variant-filled-error my-2"><div class="alert-message">{error}</div></aside>
            {/each}
        {:else}
<!--            <div class="w-full inline-block overflow-auto">-->
            <div class="table-container overflow-auto">
            <table class="table table-fixed overflow-auto" style="display: block">
                <tbody>
                {#each results.results as result}
                    <tr>
                        <td>{result.eid}</td>
                        {#each result.properties as prop}
                            <!--                    <td>{JSON.stringify(result)}</td>-->
                            <td>
                                <div class="text-sm lg:justify-self-end font-semibold leading-6 text-gray-900 capitalize">{prop.label}</div>
                                <MoiraiText text={prop.value} selected={selected.getNumber()}/>
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
<!--</div>-->
