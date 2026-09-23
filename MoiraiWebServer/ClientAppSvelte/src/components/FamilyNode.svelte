<script lang="ts">
  import type { FamilyTreeNode } from '$lib/connection';
  import type { Sibling } from '$lib/family';
  import PersonCard from './PersonCard.svelte';
  import FamilyNode from './FamilyNode.svelte';

  /**
   * One person and their ancestors, parents above the child: a classic upward genealogy pyramid built
   * by recursion. The flat node list GetFamilyTree returns is indexed by id in `nodes`.
   *
   * `withPartner` and `siblings` are for the root only: the person the tree is centred on stands beside
   * their partner, between their elder and younger siblings, with their parents above the row.
   */
  let {
    nodeId,
    nodes,
    focus,
    withPartner = false,
    siblings = [],
  }: {
    nodeId: number;
    nodes: Map<number, FamilyTreeNode>;
    focus: number;
    withPartner?: boolean;
    siblings?: Sibling[];
  } = $props();

  const node = $derived(nodes.get(nodeId));
  const parents = $derived(node ? [node.p1, node.p2].filter((p) => p && nodes.has(p)) : []);
  const partner = $derived(withPartner && node?.partner ? nodes.get(node.partner) : undefined);
  const elder = $derived(node ? siblings.filter((s) => s.node.born <= node.born) : []);
  const younger = $derived(node ? siblings.filter((s) => s.node.born > node.born) : []);
</script>

{#if node}
  <div class="fnode">
    {#if parents.length > 0}
      <div class="fparents">
        {#each parents as pid (pid)}
          <FamilyNode nodeId={pid} {nodes} {focus} />
        {/each}
      </div>
    {/if}
    <div class="fcouple">
      {#each elder as s (s.node.id)}
        <PersonCard node={s.node} note={s.half ? 'half' : ''} />
      {/each}
      <PersonCard {node} focus={nodeId === focus} />
      {#if partner}
        <span class="text-surface-500" title="Partner">⚭</span>
        <PersonCard node={partner} />
      {/if}
      {#each younger as s (s.node.id)}
        <PersonCard node={s.node} note={s.half ? 'half' : ''} />
      {/each}
    </div>
  </div>
{/if}

<style>
  .fnode {
    display: flex;
    flex-direction: column;
    align-items: center;
    margin: 0 0.25rem;
  }
  .fparents {
    display: flex;
    flex-direction: row;
    align-items: flex-end;
    justify-content: center;
    gap: 0.5rem;
    margin-bottom: 1rem;
    position: relative;
  }
  /* connector line from the parents row down to the child */
  .fparents::after {
    content: '';
    position: absolute;
    bottom: -1rem;
    left: 50%;
    width: 1px;
    height: 1rem;
    background: var(--color-surface-300);
  }
  .fcouple {
    display: flex;
    align-items: center;
    gap: 0.375rem;
  }
</style>
