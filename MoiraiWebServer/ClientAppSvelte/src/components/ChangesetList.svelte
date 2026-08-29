<script lang="ts">
  import MoiraiText from './MoiraiText.svelte';
  import EntityChip from './EntityChip.svelte';
  import { createInfiniteQuery } from '@tanstack/svelte-query';
  import { createVirtualizer } from '@tanstack/svelte-virtual';
  import { moiraiStore, type EntityChangeDisplay } from '$lib/connection';
  import PreChip from './PreChip.svelte';
  import { untrack } from 'svelte';

  let virtualListEl: HTMLDivElement | undefined = $state();

  // svelte-query v6 takes an accessor rather than a plain options object, and
  // returns a reactive object rather than a store (so `query`, not `$query`).
  const query = createInfiniteQuery(() => ({
    queryKey: ['changesets'],
    queryFn: ({ pageParam }: { pageParam: number }) => fetchServerPage(40, pageParam),
    initialPageParam: 0,
    getNextPageParam: (_lastGroup, groups) => {
      return (_lastGroup.rows?.length ?? 0) > 0 ? groups.length : undefined;
    },
  }));

  const allRows = $derived(query.data?.pages.flatMap((page) => page.rows) ?? []);

  const virtualizer = createVirtualizer<HTMLDivElement, HTMLDivElement>({
    count: 0,
    getScrollElement: () => virtualListEl ?? null,
    estimateSize: () => 44,
    overscan: 5,
  });

  const items = $derived($virtualizer.getVirtualItems());

  // Rows have variable height, so each one is measured. measureElement sets up a
  // ResizeObserver itself, so an action (one call per element) is enough. Doing
  // this in an $effect over a $state array instead self-triggers via bind:this.
  function measure(node: HTMLDivElement) {
    $virtualizer.measureElement(node);
  }

  // Keep the virtualizer's count in step with the loaded data. setOptions is
  // untracked: this effect must not subscribe to the store it writes to, or it
  // re-runs itself forever (effect_update_depth_exceeded).
  $effect(() => {
    if (!virtualListEl) return;
    const count = query.hasNextPage ? allRows.length + 1 : allRows.length;
    untrack(() => $virtualizer.setOptions({ count }));
  });

  // Load the next page once the trailing placeholder scrolls into view. This one
  // does track `items`, which is how scrolling drives it, but it only writes to
  // the query, never to the virtualizer.
  $effect(() => {
    const [lastItem] = [...items].reverse();
    if (!lastItem) return;
    if (lastItem.index > allRows.length - 1 && query.hasNextPage && !query.isFetchingNextPage) {
      query.fetchNextPage();
    }
  });

  async function fetchServerPage(
    limit: number,
    offset: number = 0,
  ): Promise<{ rows: EntityChangeDisplay[]; nextOffset: number }> {
    const conn = $moiraiStore.conn;
    if (!conn) return { rows: [], nextOffset: offset };
    const changesets = await conn.getChangesets(offset * limit, limit);
    return { rows: changesets, nextOffset: offset + 1 };
  }
</script>

{#if query.isLoading}
  Loading...
{:else if query.isError}
  <span>Error: {query.error.message}</span>
{:else if query.isSuccess}
  <div class="scroll-container bg-surface-200-800" bind:this={virtualListEl}>
    <div style="position: relative; height: {$virtualizer.getTotalSize()}px;">
      <div
        style="position: absolute; top: 0; left: 0; width: 100%; transform: translateY({items[0]
          ? items[0].start
          : 0}px);"
      >
        {#each items as row (row.index)}
          <div
            use:measure
            data-index={row.index}
            class:list-item-even={row.index % 2 === 0}
            class:list-item-odd={row.index % 2 === 1}
          >
            {#if row.index > allRows.length - 1}
              {#if query.hasNextPage}
                <span> Loading more... </span>
              {:else}
                <span> Nothing more to load </span>
              {/if}
            {:else}
              {@const item = allRows[row.index]}
              <div>
                <PreChip text={item.year} />

                <EntityChip id={item.id} label={'#' + item.id} active={false} />

                {item.actionName}
              </div>
              <div class="flex">
                {#each item.changes as change, ci (ci)}
                  <span class=" px-2 m-1 flex-row">
                    <kbd class="kbd">{change.label}</kbd>
                    <MoiraiText text={change.value} selected={-1} />
                  </span>
                  <span class="vr"></span>
                {/each}
              </div>
            {/if}
          </div>
        {/each}
      </div>
    </div>
  </div>
{/if}
{#if query.isFetching && !query.isFetchingNextPage}
  <p>Background updating...</p>
{/if}

<style>
  .list-item-odd {
    background-color: color-mix(in oklab, var(--color-surface-500) 5%, transparent);
  }
  .scroll-container {
    height: 80vh;
    width: 100%;
    overflow: auto;

    contain: 'strict';
  }
</style>
