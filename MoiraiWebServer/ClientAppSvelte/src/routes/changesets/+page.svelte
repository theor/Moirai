<script lang="ts">
  import { moiraiStore } from '$lib/connection';
  import ChangesetList from '../../components/ChangesetList.svelte';
</script>

<div class="h-full flex flex-col min-h-0">
  <h1 class="h2 mb-2 shrink-0">Changesets</h1>
  {#if $moiraiStore.conn}
    <ChangesetList />
  {:else}
    <p class="opacity-60">Connecting…</p>
  {/if}
</div>
