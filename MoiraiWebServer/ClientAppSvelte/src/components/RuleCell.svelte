<script lang="ts">
  import { ruleLabel } from '$lib/cause';
  /**
   * The rule that wrote a record, as a button that asks why it ran. Rendered into the records table's
   * cells, which are reused as the table scrolls, so it owns no state: the click hands the firing up.
   */
  let { rule, firing, onwhy }: { rule: string; firing: number; onwhy: (firing: number) => void } =
    $props();
</script>

{#if firing > 0}
  <button
    type="button"
    class="rule hover:text-primary-700 hover:underline truncate max-w-full"
    title="Why did this happen?"
    onclick={() => onwhy(firing)}>{ruleLabel(rule)}</button
  >
{:else}
  <span>{ruleLabel(rule)}</span>
{/if}
