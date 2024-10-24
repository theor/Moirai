<script lang="ts">
  import { moiraiStore, type EntityChangeDisplay } from '$lib/connection';
  import type { Changeset } from '$lib/types';
  import MoiraiText from '../../components/MoiraiText.svelte';
  import EntityChip from "../../components/EntityChip.svelte";

  let changesets: EntityChangeDisplay[] = [];
  function subscribe_changesets(node: HTMLElement) {
    let buffer: EntityChangeDisplay[] = [];
    const interval = setInterval(() => {
      if (buffer.length > 0) {
        changesets = [...buffer];
        buffer = [];
      }
    }, 500);
    const sub = $moiraiStore.conn?.getChangesets().subscribe({
      next: (changeset) => {
        // console.log(changeset);
        buffer.push(changeset);
      },
      complete: () => {
        console.log('complete');
      },
      error: (err) => {
        console.error(err);
      },
    });
    return {
      destroy: () => {
        clearInterval(interval);
        sub?.dispose();
      },
    };
  }
</script>

<div use:subscribe_changesets class=" h-full overflow-auto">
  <h1 class="h1">Changesets</h1>
  <table class="table">
    <thead>
      <tr>
        <th>Id</th>
        <th>Year</th>
        <th>Action</th>
        <th>Changes</th>
      </tr>
    </thead>
    <tbody>
      {#each changesets as changeset}
        <tr>
          <td>
              <EntityChip id={changeset.id} label={''+changeset.id} active={false} />
          </td>
          <td>{changeset.year}</td>
          <td>{changeset.actionName}</td>
          <td>
            <div class="flex flex-wrap">
              {#each changeset.changes as change}
                <span class=" px-2 m-1">
                  <pre>{change.label}</pre>
                  <MoiraiText text={change.value} selected={-1} />
                </span>
                <span class="divider-vertical" />
              {/each}
            </div>
          </td>
        </tr>
      {/each}
    </tbody>
  </table>
</div>
