<script lang="ts">
  import { parseEntityLink } from '$lib/utils';
  import { formatValueText } from '$lib/format';
  import EntityChip from './EntityChip.svelte';

  /**
   * `value` marks the text as a property value rather than a sentence, which lets `Title.King` read as
   * "King" and `null -> 50%` as "50%" (see $lib/format). A record's text is left exactly as written.
   */
  let {
    text,
    selected,
    value = false,
  }: { text: string; selected: number; value?: boolean } = $props();

  // Derived, not computed once: the records table reuses this component for a different row as it
  // scrolls or filters, and a one-time parse kept showing the old row's sentence beside the new row's
  // year.
  const parsed = $derived(parseEntityLink(text));
</script>

{#each parsed as elt, i (i)}
  {#if elt.type === 'entity'}
    <EntityChip id={elt.id} label={elt.link} active={elt.id === selected} />
  {:else}
    <span>{value ? formatValueText(elt.text, parsed.length === 1) : elt.text}</span>
  {/if}
{/each}
