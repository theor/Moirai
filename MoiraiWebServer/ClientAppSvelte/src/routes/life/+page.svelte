<script lang="ts">
  import { moiraiStore } from '$lib/connection';
  import type { FamilyTreeNode } from '$lib/connection';
  import type { Biography, BiographyEntry } from '$lib/types';
  import { groupByLabel, selectedEntity } from '$lib/utils';
  import { page } from '$app/stores';
  import MoiraiText from '../../components/MoiraiText.svelte';
  import PreChip from '../../components/PreChip.svelte';
  import FamilyNode from '../../components/FamilyNode.svelte';
  import { onMount } from 'svelte';
  import { get } from 'svelte/store';

  const MAX_DEPTH = 4;

  const selected = $derived(selectedEntity($page).getNumber());

  let bio: Biography | undefined = $state();
  let family: FamilyTreeNode[] = $state([]);
  let loading = $state(false);
  let showChanges = $state(true);

  async function load(id: number) {
    const conn = get(moiraiStore).conn;
    if (!conn || id <= 0) {
      bio = undefined;
      family = [];
      return;
    }
    loading = true;
    bio = await conn.getBiography(id);
    family = bio.hasFamily ? await conn.getFamilyTree(id, MAX_DEPTH) : [];
    loading = false;
  }

  // Reload on a new selection, and when the simulation year advances (a life is still being written).
  let loadedFor = -1;
  let loadedYear = -1;
  $effect(() => {
    const id = selected;
    const year = $moiraiStore.year;
    if (id !== loadedFor || year !== loadedYear) {
      loadedFor = id;
      loadedYear = year;
      void load(id);
    }
  });

  onMount(() => {
    void load(selected);
  });

  const timeline = $derived(
    (bio?.timeline ?? []).filter((e) => showChanges || e.kind === 'record'),
  );

  // One heading per year, so a life reads as a chronicle rather than a flat list.
  type YearGroup = { year: number; entries: BiographyEntry[] };
  const byYear = $derived(
    timeline.reduce<YearGroup[]>((acc, e) => {
      const last = acc[acc.length - 1];
      if (last && last.year === e.year) last.entries.push(e);
      else acc.push({ year: e.year, entries: [e] });
      return acc;
    }, []),
  );

  const span = $derived(
    bio && bio.timeline.length > 0
      ? { from: bio.timeline[0].year, to: bio.timeline[bio.timeline.length - 1].year }
      : undefined,
  );

  const familyMap = $derived(new Map(family.map((n) => [n.id, n])));
  const children = $derived(family.filter((n) => n.p1 === selected || n.p2 === selected));

  function select(id: number) {
    selectedEntity($page).setNumber(id);
  }
</script>

<div class="h-full overflow-auto pr-2">
  {#if selected <= 0}
    <p class="opacity-60 p-4">
      No entity selected. Click an entity chip in the Records feed or the Details panel to read its
      life.
    </p>
  {:else if loading && !bio}
    <p class="opacity-60 p-4">Loading…</p>
  {:else if !bio || bio.typeName === ''}
    <p class="opacity-60 p-4">No entity #{selected}.</p>
  {:else}
    <header class="mb-3">
      <h1 class="h2">{bio.name}</h1>
      <p class="text-sm opacity-70">
        {bio.typeName} #{bio.id}
        {#if span}· {span.from}–{span.to} · {bio.timeline.length} moments{/if}
      </p>
    </header>

    <div class="grid grid-cols-1 xl:grid-cols-[1fr_20rem] gap-6">
      <section>
        <div class="flex items-center gap-3 mb-2">
          <h2 class="h4">Life</h2>
          <label class="flex items-center gap-1 text-xs opacity-70">
            <input type="checkbox" class="checkbox" bind:checked={showChanges} />
            Show property changes
          </label>
        </div>

        {#if byYear.length === 0}
          <p class="text-sm opacity-60">Nothing has happened to {bio.name} yet.</p>
        {:else}
          {#each byYear as group (group.year)}
            <div class="flex gap-3 py-1">
              <div class="shrink-0 w-16 pt-0.5"><PreChip text={group.year} /></div>
              <div class="grow min-w-0 border-l border-surface-500/20 pl-3">
                {#each group.entries as e, i (i)}
                  {#if e.kind === 'record'}
                    <p class="text-sm py-0.5">
                      <MoiraiText text={e.text} {selected} />
                      {#each e.tags as tag (tag)}
                        <span class="badge preset-tonal-secondary text-xs ml-1">{tag}</span>
                      {/each}
                    </p>
                  {:else}
                    <p class="text-xs opacity-60 py-0.5">
                      <span class="italic mr-1">{e.actionName}</span>
                      {#each e.changes as c, ci (ci)}
                        <span class="inline-flex items-center gap-1 mr-2">
                          <kbd class="kbd">{c.label}</kbd>
                          <MoiraiText text={c.value} {selected} />
                        </span>
                      {/each}
                    </p>
                  {/if}
                {/each}
              </div>
            </div>
          {/each}
        {/if}
      </section>

      <aside>
        <h2 class="h4 mb-2">State</h2>
        <dl class="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-sm mb-5">
          {#each groupByLabel(bio.details) as g (g.label)}
            <dt class="font-semibold capitalize opacity-70 text-right">{g.label}</dt>
            <dd class="min-w-0">
              {#each g.values as v, i (i)}
                <div><MoiraiText text={v} {selected} /></div>
              {/each}
            </dd>
          {/each}
        </dl>

        {#if bio.hasFamily && family.length > 0}
          <h2 class="h4 mb-2">Family</h2>
          <div class="overflow-auto">
            <FamilyNode nodeId={selected} nodes={familyMap} focus={selected} />
          </div>
          {#if children.length > 0}
            <h3 class="text-sm font-semibold opacity-70 mt-3 mb-1">Children</h3>
            <div class="flex flex-wrap gap-1">
              {#each children as kid (kid.id)}
                <button
                  type="button"
                  class="chip preset-tonal-secondary"
                  onclick={() => select(kid.id)}
                  title={`#${kid.id} — click to read their life`}
                >
                  {kid.name}
                </button>
              {/each}
            </div>
          {/if}
        {/if}
      </aside>
    </div>
  {/if}
</div>
