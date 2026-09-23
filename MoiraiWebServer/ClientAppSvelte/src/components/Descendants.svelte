<script lang="ts">
  import type { FamilyTreeNode } from '$lib/types';
  import { childrenByCoParent, lifespan } from '$lib/family';
  import PersonCard from './PersonCard.svelte';
  import Descendants from './Descendants.svelte';

  /**
   * A person's descendants, every generation the tree reaches, grouped into the families they came
   * from: "with <other parent>", then each child beside their own partner, then their children.
   *
   * The first generation spreads across the page, one column per child; below that each generation
   * nests under its parent. Spreading every generation sideways doubles the width at each step and a
   * five-generation family no longer fits any screen, while nesting keeps a branch readable top to
   * bottom.
   */
  let {
    parentId,
    nodes,
    map,
    top = false,
  }: {
    parentId: number;
    nodes: FamilyTreeNode[];
    map: Map<number, FamilyTreeNode>;
    top?: boolean;
  } = $props();

  const broods = $derived(childrenByCoParent(nodes, parentId, map.get(parentId)?.partner ?? 0));

  function withWhom(id: number) {
    const n = map.get(id);
    if (!n) return 'another parent';
    const years = lifespan(n);
    return years ? `${n.name} (${years})` : n.name;
  }
</script>

<div class="broods" class:top>
  {#each broods as b (b.coParent)}
    <section class="brood">
      {#if top || broods.length > 1}
        <h3 class="text-xs text-surface-600 mb-1.5">
          {b.coParent === 0 ? 'Other parent unknown' : `With ${withWhom(b.coParent)}`}
        </h3>
      {/if}
      <div class="kids">
        {#each b.children as kid (kid.id)}
          {@const partner = kid.partner ? map.get(kid.partner) : undefined}
          <div class="branch">
            <div class="flex items-center gap-1">
              <PersonCard node={kid} />
              {#if partner}
                <span class="text-surface-500" title="Partner">⚭</span>
                <PersonCard node={partner} />
              {/if}
            </div>
            {#if nodes.some((n) => n.p1 === kid.id || n.p2 === kid.id)}
              <div class="nested">
                <Descendants parentId={kid.id} {nodes} {map} />
              </div>
            {/if}
          </div>
        {/each}
      </div>
    </section>
  {/each}
</div>

<style>
  .broods {
    display: flex;
    flex-direction: column;
    gap: 0.5rem;
  }
  .broods.top {
    flex-direction: row;
    flex-wrap: wrap;
    justify-content: center;
    align-items: flex-start;
    gap: 1rem;
  }
  .broods.top > .brood {
    padding: 0.5rem 0.75rem;
    border-radius: 0.5rem;
    background: var(--color-surface-50);
  }
  .kids {
    display: flex;
    flex-direction: column;
    gap: 0.375rem;
  }
  .broods.top > .brood > .kids {
    flex-direction: row;
    flex-wrap: wrap;
    align-items: flex-start;
    gap: 0.75rem;
  }
  /* A generation hangs off its parent: indented, with a rule down the side to show whose it is. */
  .nested {
    margin: 0.375rem 0 0 0.75rem;
    padding-left: 0.75rem;
    border-left: 1px solid var(--color-surface-300);
  }
</style>
