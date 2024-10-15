<script lang="ts">
  import { urlParam } from '$lib/utils';
  import { page } from '$app/stores';
  import { moiraiStore } from '$lib/connection';
    import EntityChip from './EntityChip.svelte';
    import MoiraiText from './MoiraiText.svelte';

  let selected = -1;
  $: {
    let selParam = urlParam($page, 'e');
    selected = selParam.getNumber();
  }
  $: details = selected != -1 ? $moiraiStore.conn?.getEntityDetails(selected) : undefined;
</script>

<div class="card p-4">
<p>#{selected}</p>
  {#await details}
    <p>Loading...</p>
  {:then details}
    {#if details}
      {#each details as detail}
        <div>
          <strong>{detail.label}</strong>
          <MoiraiText text={detail.value} selected={selected}></MoiraiText>
        </div>
      {/each}
    {/if}
  {/await}
</div>
