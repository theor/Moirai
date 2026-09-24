<script lang="ts">
  import type { ChartView, Couple } from '$lib/family';
  import { lifespan } from '$lib/family';
  import { selectedEntity } from '$lib/utils';
  import { page } from '$app/stores';

  /**
   * One card of the family chart: a person, the spouse shown with them, and when there is anything
   * below it, the handle that folds it away. Either name re-centres the chart on that person.
   *
   * Every card is the same width, which is what lines the generations up in columns.
   *
   * Someone drawn in more than one place gets a colour of their own on every card, a count that
   * jumps to their next card, and lights up everywhere at once under the pointer.
   */
  let {
    couple,
    view,
    below = 0,
    collapsed = false,
    ontoggle,
  }: {
    couple: Couple;
    view: ChartView;
    below?: number;
    collapsed?: boolean;
    ontoggle?: () => void;
  } = $props();

  const people = $derived(couple.spouse ? [couple.node, couple.spouse] : [couple.node]);

  // Far enough apart on the wheel to tell ten couples apart; an eleventh reuses the first hue, and
  // hovering tells the two apart.
  const hues = [25, 150, 265, 80, 330, 200, 110, 295, 55, 235];

  /** Scroll to the next card for the same person, round to the first after the last. */
  function jump(e: MouseEvent, id: number) {
    const here = (e.currentTarget as HTMLElement).closest('[data-pid]');
    const all = [...document.querySelectorAll<HTMLElement>(`.fcard [data-pid="${id}"]`)];
    const next = all[(all.indexOf(here as HTMLElement) + 1) % all.length];
    next.scrollIntoView({ block: 'center', inline: 'center', behavior: 'smooth' });
    next.animate([{ transform: 'scale(1.06)' }, { transform: 'scale(1)' }], {
      duration: 500,
      easing: 'ease-out',
    });
  }

  /** The chart re-centres on click, so the card it is centred on should be on screen. */
  function reveal(el: HTMLElement) {
    el.scrollIntoView({ block: 'nearest', inline: 'center' });
  }
</script>

<div class="fcard">
  {#each people as p, i (p.id)}
    {@const years = lifespan(p)}
    {@const rep = view.repeats.get(p.id)}
    <div
      class="row"
      class:rep
      class:hot={rep && view.hover.id === p.id}
      class:spouse={i > 0}
      data-pid={p.id}
      style:--rep-h={rep ? hues[rep.slot % hues.length] : undefined}
      role="presentation"
      onmouseenter={() => rep && (view.hover.id = p.id)}
      onmouseleave={() => view.hover.id === p.id && (view.hover.id = 0)}
    >
      {#if p.id === view.focus}
        <button type="button" class="who focus" class:dead={p.dead} title={`#${p.id}`} use:reveal>
          {@render person(p.name, years, i > 0)}
        </button>
      {:else}
        <button
          type="button"
          class="who"
          class:dead={p.dead}
          title={`#${p.id} · click to centre the chart here`}
          onclick={() => selectedEntity($page).setNumber(p.id)}
        >
          {@render person(p.name, years, i > 0)}
        </button>
      {/if}
      {#if rep}
        <button
          type="button"
          class="again"
          title={`${p.name} appears ${rep.count} times in this chart · go to the next`}
          onclick={(e) => jump(e, p.id)}
        >
          ×{rep.count}
        </button>
      {/if}
    </div>
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
  .row {
    position: relative;
    border-radius: 0.3rem;
  }
  .row.spouse {
    border-top: 1px solid var(--color-surface-200);
    border-radius: 0 0 0.3rem 0.3rem;
  }
  /* Someone drawn more than once: their own hue, as a tint and a bar down the side. */
  .row.rep {
    background: oklch(0.95 0.04 var(--rep-h));
    box-shadow: inset 4px 0 0 oklch(0.62 0.16 var(--rep-h));
  }
  .row.rep .who {
    padding-right: 2rem;
  }
  .row.hot {
    background: oklch(0.88 0.09 var(--rep-h));
    outline: 2px solid oklch(0.55 0.18 var(--rep-h));
    outline-offset: -1px;
  }
  .again {
    position: absolute;
    top: 0.3rem;
    right: 0.3rem;
    padding: 0 0.3rem;
    border-radius: 999px;
    background: oklch(0.62 0.16 var(--rep-h));
    color: white;
    font-size: 0.7rem;
    font-weight: 600;
    line-height: 1rem;
    font-variant-numeric: tabular-nums;
    cursor: pointer;
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
  .row.spouse .name {
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
