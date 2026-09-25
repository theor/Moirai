<script lang="ts">
  import { moiraiStore, settledYear } from '$lib/connection';
  import { byId, childrenOf } from '$lib/family';
  import type { FamilyTreeNode } from '$lib/connection';
  import type { Biography, BiographyEntry, EntityPropertyDisplay } from '$lib/types';
  import { notable } from '$lib/notable';
  import NotableList from '../../components/NotableList.svelte';
  import { groupByLabel, selectedEntity } from '$lib/utils';
  import { page } from '$app/stores';
  import MoiraiText from '../../components/MoiraiText.svelte';
  import FamilyNode from '../../components/FamilyNode.svelte';
  import EntityChip from '../../components/EntityChip.svelte';
  import CauseDialog from '../../components/CauseDialog.svelte';
  import { humanLabel, unquote } from '$lib/format';
  import { onMount } from 'svelte';
  import { get } from 'svelte/store';

  const MAX_DEPTH = 4;

  // The record whose "why?" is open, by its firing.
  let why: number | null = $state(null);

  const selected = $derived(selectedEntity($page).getNumber());

  let bio: Biography | undefined = $state();
  let family: FamilyTreeNode[] = $state([]);
  let loading = $state(false);
  // Off by default: a life reads as its records, and the property changes under them triple its length.
  let showChanges = $state(false);

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

  // Reload on a new selection, and when the settled year advances (a life is still being written). The
  // settled year rather than the raw one: a biography merges every record and changeset for the entity,
  // so a pass would otherwise rebuild it per feed tick. See $lib/settled-year.
  let loadedFor = -1;
  let loadedYear = -1;
  $effect(() => {
    const id = selected;
    const year = $settledYear;
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

  /**
   * The year the State column describes; undefined means now.
   *
   * A closed changeset keeps a full copy of the entity, so the engine can answer "what was this at the
   * end of year Y" without the world ever storing a snapshot (WorldSession.GetEntityAt). Clicking a year
   * in the life moves it there, and the entries after it fade, so the timeline and the state read as the
   * same moment.
   */
  let stateYear: number | undefined = $state();
  let pastDetails: EntityPropertyDisplay[] | undefined = $state();

  // A new person starts in the present. Writes stateYear, never reads it, so it cannot re-trigger itself.
  $effect(() => {
    void selected;
    stateYear = undefined;
  });

  // Debounced, because dragging the slider asks once per year crossed.
  $effect(() => {
    const id = selected;
    const year = stateYear;
    void $settledYear;
    if (year === undefined || id <= 0) {
      pastDetails = undefined;
      return;
    }
    const timer = setTimeout(async () => {
      const rows = await get(moiraiStore).conn?.getEntityAt(id, year);
      // Only if the question is still the one being asked.
      if (rows && stateYear === year && selected === id) pastDetails = rows;
    }, 120);
    return () => clearTimeout(timer);
  });

  const now = $derived($moiraiStore.year);
  const firstYear = $derived(span?.from ?? now);
  const showing = $derived(stateYear === undefined ? bio?.details : (pastDetails ?? bio?.details));

  function viewYear(year: number) {
    stateYear = year >= now ? undefined : year;
  }

  const familyMap = $derived(byId(family));
  const children = $derived(childrenOf(family, selected));
</script>

<CauseDialog firing={why} onclose={() => (why = null)} />

<div class="h-full overflow-auto pr-2">
  {#if selected <= 0}
    <div class="max-w-4xl">
      <h1 class="h4 mb-1">Life</h1>
      <p class="text-sm text-surface-600 mb-5">
        Pick anyone to read their life as one timeline. These are the ones the story mentions most.
      </p>
      <NotableList groups={$notable} />
    </div>
  {:else if loading && !bio}
    <p class="opacity-60 p-4">Loading…</p>
  {:else if !bio || bio.typeName === ''}
    <p class="opacity-60 p-4">No entity #{selected}.</p>
  {:else}
    <header class="mb-3">
      <h1 class="h3">{bio.name}</h1>
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
            <div
              class="flex gap-3 py-1 transition-opacity"
              class:opacity-35={stateYear !== undefined && group.year > stateYear}
            >
              <button
                type="button"
                class="shrink-0 w-12 pt-0.5 text-right year-mark hover:text-primary-600 hover:underline self-start"
                class:!text-primary-600={group.year === stateYear}
                title="Show the state at the end of {group.year}"
                onclick={() => viewYear(group.year)}>{group.year}</button
              >
              <div class="grow min-w-0 border-l border-surface-200 pl-3">
                {#each group.entries as e, i (i)}
                  {#if e.kind === 'record'}
                    <p class="record py-0.5 leading-7">
                      <MoiraiText text={e.text} {selected} />
                      {#each e.tags as tag (tag)}
                        <span class="tag ml-1">{unquote(tag)}</span>
                      {/each}
                      {#if e.firing > 0}
                        <button
                          type="button"
                          class="why ml-1 text-xs text-surface-500 hover:text-primary-700 hover:underline"
                          title="Why did this happen? ({e.actionName})"
                          onclick={() => (why = e.firing)}>why?</button
                        >
                      {/if}
                    </p>
                  {:else}
                    <p class="text-xs text-surface-600 py-0.5 leading-6">
                      {#each e.changes as c, ci (ci)}
                        <span class="mr-3 whitespace-nowrap">
                          <span class="font-medium">{humanLabel(c.label)}</span>
                          <MoiraiText text={c.value} {selected} value />
                        </span>
                      {/each}
                      <span class="opacity-60">· {e.actionName}</span>
                    </p>
                  {/if}
                {/each}
              </div>
            </div>
          {/each}
        {/if}
      </section>

      <aside>
        <div class="flex items-baseline gap-2 mb-1">
          <h2 class="h4">State</h2>
          <span class="text-sm text-surface-600">
            {stateYear === undefined ? 'now' : `at the end of ${stateYear}`}
          </span>
          {#if stateYear !== undefined}
            <button
              type="button"
              class="ml-auto text-xs text-primary-600 hover:underline"
              onclick={() => (stateYear = undefined)}>Back to now</button
            >
          {/if}
        </div>
        {#if firstYear < now}
          <input
            type="range"
            class="w-full mb-3 accent-primary-500"
            aria-label="Year to show the state at"
            min={firstYear}
            max={now}
            value={stateYear ?? now}
            oninput={(e) => viewYear(Number(e.currentTarget.value))}
          />
        {/if}
        {#if showing && showing.length === 0}
          <p class="text-sm text-surface-600 mb-5">{bio.name} did not exist yet in {stateYear}.</p>
        {/if}
        <dl class="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-sm mb-5">
          {#each groupByLabel(showing ?? []) as g (g.label)}
            <dt class="text-surface-600 text-right">{humanLabel(g.label)}</dt>
            <dd class="min-w-0">
              {#each g.values as v, i (i)}
                <div><MoiraiText text={v} {selected} value /></div>
              {/each}
            </dd>
          {/each}
        </dl>

        {#if bio.hasFamily && family.length > 0}
          <h2 class="h4 mb-2">Family</h2>
          <div class="overflow-auto">
            <FamilyNode nodeId={selected} nodes={familyMap} focus={selected} withPartner />
          </div>
          {#if children.length > 0}
            <h3 class="text-sm font-semibold opacity-70 mt-3 mb-1">Children</h3>
            <div class="flex flex-wrap gap-1">
              {#each children as kid (kid.id)}
                <EntityChip id={kid.id} label={kid.name} active={false} />
              {/each}
            </div>
          {/if}
        {/if}
      </aside>
    </div>
  {/if}
</div>

<style>
  /* "why?" on every line of a life is noise; it appears where the reader is looking -- the hovered line,
   * or wherever keyboard focus is -- and stays visible where there is no hover at all (touch). */
  @media (hover: hover) {
    .record .why {
      opacity: 0;
    }
    .record:hover .why,
    .record .why:focus-visible {
      opacity: 1;
    }
  }
</style>
