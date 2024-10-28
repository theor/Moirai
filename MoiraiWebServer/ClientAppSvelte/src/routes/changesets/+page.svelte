<script lang="ts">
  import { moiraiStore } from '$lib/connection';

    import ChangesetList from '../../components/ChangesetList.svelte';
  // let changesets: EntityChangeDisplay[] = [];



  // function subscribe_changesets(node: HTMLElement) {
  //   let buffer: EntityChangeDisplay[] = [];
  //   const interval = setInterval(() => {
  //     if (buffer.length > 0) {
  //       changesets = [...buffer];
  //       buffer = [];
  //     }
  //   }, 500);
  //   const sub = $moiraiStore.conn?.getChangesets().subscribe({
  //     next: (changeset) => {
  //       // console.log(changeset);
  //       buffer.push(changeset);
  //     },
  //     complete: () => {
  //       console.log('complete');
  //     },
  //     error: (err) => {
  //       console.error(err);
  //     },
  //   });
  //   return {
  //     destroy: () => {
  //       clearInterval(interval);
  //       sub?.dispose();
  //     },
  //   };
  // }
</script>

<div  class=" h-full overflow-auto">
  <h1 class="h1">Changesets</h1>
  {#if $moiraiStore.conn}
  <h3>connected</h3>
  <ChangesetList/>
  {/if}

</div>
