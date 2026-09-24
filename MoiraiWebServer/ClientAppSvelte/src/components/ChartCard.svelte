<script lang="ts">
  import type { Couple } from '$lib/family';
  import { lifespan } from '$lib/family';
  import { selectedEntity } from '$lib/utils';
  import { page } from '$app/stores';

  /**
   * One card of the family chart: a person, the spouse shown with them, and when there is anything
   * below it, the handle that folds it away. Either name re-centres the chart on that person.
   *
   * Every card is the same width, which is what lines the generations up in columns.
   */
  let {
    couple,
    focus = 0,
    below = 0,
    collapsed = false,
    ontoggle,
  }: {
    couple: Couple;
    focus?: number;
    below?: number;
    collapsed?: boolean;
    ontoggle?: () => void;
  } = $props();

  const people = $derived(couple.spouse ? [couple.node, couple.spouse] : [couple.node]);

  /** The chart re-centres on click, so the card it is centred on should be on screen. */
  function reveal(el: HTMLElement) {
    el.scrollIntoView({ block: 'nearest', inline: 'center' });
  }
</script>

<div class="fcard">
  {#each people as p, i (p.id)}
    {@const years = lifespan(p)}
    {#if p.id === focus}
      <button
        type="button"
        class="who focus"
        class:dead={p.dead}
        class:spouse={i > 0}
        title={`#${p.id}`}
        use:reveal
      >
        {@render person(p.name, years, i > 0)}
      </button>
    {:else}
      <button
        type="button"
        class="who"
        class:dead={p.dead}
        class:spouse={i > 0}
        title={`#${p.id} · click to centre the chart here`}
        onclick={() => selectedEntity($page).setNumber(p.id)}
      >
        {@render person(p.name, years, i > 0)}
      </button>
    {/if}
  {/each}
  {#if below > 0 && ontoggle}
    <button
      type="button"
      class="fold"
      class:collapsed
      title={collapsed ? `Show ${below} descendants` : `Hide ${below} descendants`}
      aria-expanded={!collapsed}
      onclick={ontoggle}
    >
      {collapsed ? `+${below}` : '−'}
    </button>
  {/if}
</div>

{#snippet person(name: string, years: string, spouse: boolean)}
  <span class="name"
    >{#if spouse}<span class="amp" aria-label="partner">&amp;</span>{/if}{name}</span
  >
  {#if years}<span class="years">{years}</span>{/if}
{/snippet}

<style>
  .fcard {
    position: relative;
    width: var(--card-w);
    border: 1px solid var(--color-surface-300);
    border-radius: 0.375rem;
    background: white;
    text-align: left;
  }
  .who {
    display: block;
    width: 100%;
    padding: 0.25rem 0.5rem;
    border-radius: 0.3rem;
    text-align: left;
    cursor: pointer;
  }
  .who:hover {
    background: var(--color-surface-100);
  }
  .who.spouse {
    border-top: 1px solid var(--color-surface-200);
    border-radius: 0 0 0.3rem 0.3rem;
  }
  .who.focus {
    cursor: default;
    background: var(--color-primary-50);
    box-shadow: inset 3px 0 0 var(--color-primary-500);
  }
  .name {
    display: block;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    font-size: 0.875rem;
    line-height: 1.25rem;
    font-weight: 500;
  }
  .who.spouse .name {
    font-weight: 400;
  }
  .amp {
    margin-right: 0.25rem;
    color: var(--color-surface-500);
  }
  .years {
    display: block;
    font-size: 0.75rem;
    line-height: 1rem;
    font-variant-numeric: tabular-nums;
    color: var(--color-surface-600);
  }
  /* The living are the exception in a chart that spans centuries, so they are the ones marked. */
  .who:not(.dead) .years::after {
    content: ' · living';
    color: var(--color-success-700);
  }
  /* Sits where the line to the children leaves the card. */
  .fold {
    position: absolute;
    right: -0.7rem;
    top: calc(var(--stub-y) - 0.6rem);
    z-index: 1;
    min-width: 1.2rem;
    height: 1.2rem;
    padding: 0 0.25rem;
    border: 1px solid var(--color-surface-400);
    border-radius: 999px;
    background: white;
    font-size: 0.7rem;
    line-height: 1.1rem;
    font-variant-numeric: tabular-nums;
    color: var(--color-surface-700);
    cursor: pointer;
  }
  .fold:hover {
    border-color: var(--color-primary-500);
  }
  .fold.collapsed {
    right: auto;
    left: calc(100% + 0.3rem);
    padding: 0 0.4rem;
    border-color: var(--color-primary-400);
    background: var(--color-primary-50);
    font-size: 0.75rem;
    font-weight: 600;
    color: var(--color-primary-700);
  }
</style>
