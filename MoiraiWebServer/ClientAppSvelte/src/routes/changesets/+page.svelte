<script lang="ts">
  import { moiraiStore } from '$lib/connection';
  import ChangesetList from '../../components/ChangesetList.svelte';
</script>

<div class="h-full overflow-auto">
  <h1 class="h2 mb-2">Changesets</h1>
  {#if $moiraiStore.conn}
    <ChangesetList />
  {:else}
    <p class="opacity-60">Connecting…</p>
  {/if}
</div>
