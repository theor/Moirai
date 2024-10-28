<script lang="ts">
  import MoiraiText from './MoiraiText.svelte';
  import EntityChip from './EntityChip.svelte';
  import { createInfiniteQuery } from '@tanstack/svelte-query';
  import { createVirtualizer } from '@tanstack/svelte-virtual';
  import { moiraiStore, type EntityChangeDisplay } from '$lib/connection';
  import PreChip from './PreChip.svelte';

  let virtualListEl: HTMLDivElement;

  let virtualItemEls: HTMLDivElement[] = [];

  const query = createInfiniteQuery({
    queryKey: ['projects'],
    queryFn: ({ pageParam }: { pageParam: number }) => fetchServerPage(40, pageParam),
    initialPageParam: 0,
    getNextPageParam: (_lastGroup, groups) => {
      console.log('next page param', groups.length, groups, _lastGroup);
      return (_lastGroup.rows?.length ?? 0) > 0 ? groups.length : undefined;
    },
  });

  $: allRows = ($query.data && $query.data.pages.flatMap((page) => page.rows)) || [];

  $: virtualizer = createVirtualizer<HTMLDivElement, HTMLDivElement>({
    count: 0,
    getScrollElement: () => virtualListEl,
    estimateSize: () => 44,
    overscan: 5,
  });

  $: items = $virtualizer.getVirtualItems();
  $: {
    if (virtualItemEls.length) virtualItemEls.forEach((el) => $virtualizer.measureElement(el));
  }
  $: {
    $virtualizer.setOptions({
      count: $query.hasNextPage ? allRows.length + 1 : allRows.length,
    });

    const [lastItem] = [...$virtualizer.getVirtualItems()].reverse();
    // console.warn("has next", $query.hasNextPage, "last item", lastItem)
    if (
      lastItem &&
      lastItem.index > allRows.length - 1 &&
      $query.hasNextPage &&
      !$query.isFetchingNextPage
    ) {
      console.warn('fetching next page');
      $query.fetchNextPage();
    }
  }

  async function fetchServerPage(
    limit: number,
    offset: number = 0,
  ): Promise<{ rows: EntityChangeDisplay[]; nextOffset: number }> {
    console.log('fetch', limit, offset, $moiraiStore.conn);
    const changesets = await $moiraiStore.conn?.getChangesets(offset * limit, limit)!;
    console.warn(changesets);
    return { rows: changesets, nextOffset: offset + 1 };
  }
</script>

{#if $query.isLoading}
  Loading...
{:else if $query.isError}
  <span>Error: {$query.error.message}</span>
{:else if $query.isSuccess}
  <div class="scroll-container bg-surface-200-700-token" bind:this={virtualListEl}>
    <div style="position: relative; height: {$virtualizer.getTotalSize()}px;">
      <div
        style="position: absolute; top: 0; left: 0; width: 100%; transform: translateY({items[0]
          ? items[0].start
          : 0}px);"
      >
        {#each items as row, idx (row.index)}
          <div
            bind:this={virtualItemEls[idx]}
            data-index={row.index}
            class:list-item-even={row.index % 2 === 0}
            class:list-item-odd={row.index % 2 === 1}
          >
          
            {#if row.index > allRows.length - 1}
              {#if $query.hasNextPage}
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
                {#each item.changes as change}
                  <span class=" px-2 m-1 flex-row">
                    <kbd class="kbd">{change.label}</kbd>
                    <MoiraiText text={change.value} selected={-1} />
                  </span>
                  <span class="divider-vertical" />
                {/each}
              </div>
            {/if}
          </div>
        {/each}
      </div>
    </div>
  </div>

{/if}
{#if $query.isFetching && !$query.isFetchingNextPage}
  <p>Background updating...</p>
{/if}

<style>
  .table tbody tr {
    overflow: unset;
  }
  .table {
    background-color: transparent;
  }
  .list-item-odd {
    background-color: rgb(var(--color-surface-500) / 0.05);
  }
  .scroll-container {
    height: 80vh;
    width: 100%;
    overflow: auto;

    contain: 'strict';
  }
</style>
