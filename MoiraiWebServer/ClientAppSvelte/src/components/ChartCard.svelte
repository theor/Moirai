<script lang="ts">
  import type { Placed } from '$lib/family-chart';
  import { lifespan } from '$lib/family';
  import { selectedEntity } from '$lib/utils';
  import { page } from '$app/state';

  /**
   * One card of the family chart: a couple, or one person, each on a row of fixed height, because the
   * lines are drawn to computed positions and a row that grew would leave them pointing at nothing.
   * Either name re-centres the chart on that person.
   *
   * `shared` marks a card drawn once where a tree would have drawn it several times; `repeat` a
   * person who is still on more than one card (someone who married twice, into the family both
   * times). Both carry a hue.
   */
  let {
    placed,
    focus,
    collapsed,
    shared,
    repeat,
    hot,
    ontoggle,
    onhover,
  }: {
    placed: Placed;
    focus: number;
    collapsed: boolean;
    shared?: { hue: number; lines: number };
    repeat: (id: number) => { hue: number; count: number } | undefined;
    hot: boolean;
    ontoggle: () => void;
    onhover: (on: boolean) => void;
  } = $props();

  const foldable = $derived(placed.below > 0 && placed.col >= 0);

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

<div
  class="fcard"
  class:shared
  class:hot
  style:--hue={shared?.hue}
  role="presentation"
  onmouseenter={() => onhover(true)}
  onmouseleave={() => onhover(false)}
>
  {#each placed.card.rows as p, i (p.id)}
    {@const years = lifespan(p)}
    {@const rep = repeat(p.id)}
    <div class="row" class:rep data-pid={p.id} style:--rep-hue={rep?.hue}>
      {#if p.id === focus}
        <button type="button" class="who focus" class:dead={p.dead} title={`#${p.id}`} use:reveal>
          {@render person(p.name, years, i > 0)}
        </button>
      {:else}
        <button
          type="button"
          class="who"
          class:dead={p.dead}
          title={`#${p.id} · click to centre the chart here`}
          onclick={() => selectedEntity(page).setNumber(p.id)}
        >
          {@render person(p.name, years, i > 0)}
        </button>
      {/if}
      {#if rep}
        <button
          type="button"
          class="badge again"
          title={`${p.name} is on ${rep.count} cards, one per partner · go to the next`}
          onclick={(e) => jump(e, p.id)}
        >
          ×{rep.count}
        </button>
      {:else if shared && i === 0}
        <span
          class="badge"
          title={placed.col < 0
            ? `An ancestor ${shared.lines} times over: drawn once, with a line to each descendant`
            : `Descended from this chart along ${shared.lines} lines: drawn once, with a line from each`}
        >
          {shared.lines} lines
        </span>
      {/if}
    </div>
  {/each}
  {#if foldable}
    <button
      type="button"
      class="fold"
      class:collapsed
      title={`${placed.below} cards descend from here; ${collapsed ? 'show' : 'fold'} them (any also reached through another line stays)`}
      aria-expanded={!collapsed}
      onclick={ontoggle}
    >
      {collapsed ? `+${placed.below}` : '−'}
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
  /* Drawn once where a tree would have drawn it twice: the loop lines share this hue. */
  .fcard.shared {
    border-color: oklch(0.62 0.16 var(--hue));
    box-shadow: inset 4px 0 0 oklch(0.62 0.16 var(--hue));
  }
  .fcard.shared.hot {
    box-shadow:
      inset 4px 0 0 oklch(0.62 0.16 var(--hue)),
      0 0 0 2px oklch(0.62 0.16 var(--hue));
  }
  .row {
    position: relative;
    height: var(--row-h);
    overflow: hidden;
  }
  .row + .row {
    border-top: 1px solid var(--color-surface-200);
  }
  .row.rep {
    background: oklch(0.95 0.04 var(--rep-hue));
  }
  .who {
    display: block;
    width: 100%;
    height: 100%;
    padding: 0.25rem 0.5rem;
    text-align: left;
    cursor: pointer;
  }
  .who:hover {
    background: var(--color-surface-100);
  }
  .who.focus {
    cursor: default;
    background: var(--color-primary-50);
    box-shadow: inset 3px 0 0 var(--color-primary-500);
  }
  .shared .row:first-child .who,
  .rep .who {
    padding-right: 3.25rem;
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
  .row + .row .name {
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
  .badge {
    position: absolute;
    top: 0.3rem;
    right: 0.3rem;
    padding: 0 0.3rem;
    border-radius: 999px;
    background: oklch(0.62 0.16 var(--hue));
    color: white;
    font-size: 0.68rem;
    font-weight: 600;
    line-height: 1rem;
    white-space: nowrap;
    font-variant-numeric: tabular-nums;
  }
  .badge.again {
    background: oklch(0.55 0.16 var(--rep-hue));
    cursor: pointer;
  }
  /* Sits where the line to the children leaves the card. */
  .fold {
    position: absolute;
    right: -0.7rem;
    top: calc(var(--stub) - 0.6rem);
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
