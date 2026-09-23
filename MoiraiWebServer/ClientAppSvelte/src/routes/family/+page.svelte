<script lang="ts">
  import { moiraiStore, settledYear } from '$lib/connection';
  import { byId, siblingsOf } from '$lib/family';
  import { notable } from '$lib/notable';
  import { page } from '$app/stores';
  import { selectedEntity } from '$lib/utils';
  import FamilyNode from '../../components/FamilyNode.svelte';
  import Descendants from '../../components/Descendants.svelte';
  import NotableList from '../../components/NotableList.svelte';

  const maxDepth = 5;

  const selected = $derived(selectedEntity($page).getNumber());

  // Refetch when the selection or the settled year changes (new people may have been born), and when
  // `attempt` is bumped by the retry button in the error branch. The settled year rather than the raw
  // one: a tree walk is not cheap and a pass would otherwise trigger one per feed tick.
  let attempt = $state(0);
  const tree = $derived(familyTreeFor(selected, $settledYear, attempt));
  function familyTreeFor(sel: number, _year: number, _attempt: number) {
    return sel > 0 ? $moiraiStore.conn?.getFamilyTree(sel, maxDepth) : undefined;
  }

  // Only types with parents have a tree, so only they are worth suggesting.
  const withFamily = $derived($notable.filter((g) => g.hasFamily));

  // A hub method that throws reaches us as a HubException whose message is the server's generic
  // "unexpected error" text, so show whatever we get rather than inventing a friendlier line.
  function errorText(err: unknown): string {
    return err instanceof Error ? err.message : String(err);
  }
</script>

<div class="h-full overflow-auto">
  {#if selected <= 0}
    <div class="max-w-4xl">
      <h1 class="h4 mb-1">Family</h1>
      <p class="text-sm text-surface-600 mb-5">
        Pick someone to see their whole family: ancestors, siblings, partner, and every generation
        after them. These are the people the story mentions most.
      </p>
      <NotableList groups={withFamily} />
    </div>
  {:else}
    {#await tree}
      <p class="text-surface-600 p-4">Loading family tree…</p>
    {:then list}
      {#if !list || list.length === 0}
        <p class="text-surface-600 p-4">
          #{selected} has no family tree: its type declares no parent1/parent2.
        </p>
      {:else}
        {@const map = byId(list)}
        <div class="ftree">
          <FamilyNode
            nodeId={selected}
            nodes={map}
            focus={selected}
            withPartner
            siblings={siblingsOf(list, selected)}
          />

          {#if list.some((n) => n.p1 === selected || n.p2 === selected)}
            <div class="fdown"></div>
            <div class="fbroods">
              <Descendants parentId={selected} nodes={list} {map} top />
            </div>
          {:else}
            <p class="text-sm text-surface-500 mt-6">No children.</p>
          {/if}
        </div>
      {/if}
    {:catch error}
      <div class="p-4">
        <aside class="card preset-filled-error-500 p-4">
          <p class="font-bold">Could not load the family tree for #{selected}.</p>
          <p class="text-sm">{errorText(error)}</p>
        </aside>
        <button type="button" class="btn preset-tonal mt-3" onclick={() => (attempt += 1)}>
          Retry
        </button>
      </div>
    {/await}
  {/if}
</div>

<style>
  .ftree {
    display: flex;
    flex-direction: column;
    align-items: center;
    min-width: min-content;
    padding: 1rem;
  }
  /* The line from the couple down to their children. */
  .fdown {
    width: 1px;
    height: 1.25rem;
    background: var(--color-surface-300);
  }
  .fbroods {
    padding-top: 0.75rem;
    border-top: 1px solid var(--color-surface-300);
  }
</style>
