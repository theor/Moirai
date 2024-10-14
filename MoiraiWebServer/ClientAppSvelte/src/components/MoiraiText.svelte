<script lang="ts">
    import { parseEntityLink } from "$lib/utils";
    import EntityChip from "./EntityChip.svelte";

    export let text: string;
    export let selected: number;

    let parsed = parseEntityLink(text);
</script>

{#each parsed as elt}
    {#if elt.type === 'entity'}
        <EntityChip id={elt.id} label={elt.link} active={elt.id===selected} />
    {:else}
        <span>{elt.text}</span>
    {/if}
{/each}
<!-- <span>{text}</span> -->
