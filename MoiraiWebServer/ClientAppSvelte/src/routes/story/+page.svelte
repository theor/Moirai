<script lang="ts">
  import { moiraiStore } from '$lib/connection';
  import { errorCount } from '$lib/diagnostics';
  import { clearStoredStory, storeStory } from '$lib/story-storage';
  import type { StoryDiagnostic } from '$lib/types';
  import type { EditorView } from '@codemirror/view';

  /**
   * Editing the story the world is built from.
   *
   * Only reachable on the in-browser backend — the nav tab keys off `conn.story`, which the server's
   * implementation leaves null because its story is a file on disk that its own watcher owns.
   *
   * Squiggles are the engine's, not a guess: every pause in typing runs the real parser over the text
   * through WebAssembly. Apply then refuses anything with an error, so the worst a bad edit can do is
   * sit there being red.
   */

  let host: HTMLDivElement | undefined = $state();
  // $state because the toolbar's disabled bindings read it: the buttons are dead until the editor
  // has mounted, which with the WebAssembly backend is a visible moment rather than an instant.
  let view: EditorView | undefined = $state();

  let diagnostics: StoryDiagnostic[] = $state([]);
  let status = $state('');
  let busy = $state(false);
  let loadError = $state('');

  const story = $derived($moiraiStore.conn?.story ?? null);
  const errors = $derived(errorCount(diagnostics));
  const warnings = $derived(diagnostics.length - errors);
  const passRunning = $derived($moiraiStore.passYearsPercent !== undefined);
  let shipped = '';

  // Mounted once, when a backend that can edit turns up. The whole editor — CodeMirror and the mode —
  // is imported here rather than at the top of the module, so it lands in this route's chunk and a
  // deployment that never opens this page never fetches it.
  //
  // `view` is written here and never read: an effect that reads the state it assigns re-runs on its own
  // assignment, and this one would then tear the editor down and build another one, forever. The
  // browser that caught it had 347 editors stacked in the page and every button permanently disabled.
  $effect(() => {
    const editor = story;
    const parent = host;
    if (!editor || !parent) return;

    let live: EditorView | undefined;
    let disposed = false;
    void (async () => {
      try {
        const [{ createStoryEditor }, doc, original] = await Promise.all([
          import('$lib/story-editor'),
          editor.get(),
          editor.original(),
        ]);
        if (disposed) return;
        shipped = original;
        live = createStoryEditor({
          parent,
          doc,
          validate: (text) => editor.validate(text),
          onDiagnostics: (d) => (diagnostics = d),
          // The draft is kept as you type, which is why there is no "unsaved changes" prompt: leaving
          // the page, or reloading it, costs nothing. Storing the shipped story verbatim would be a
          // draft that says "no edit", so that case clears instead.
          onChange: (text) => (text === shipped ? clearStoredStory() : storeStory(text)),
        });
        view = live;
      } catch (err) {
        loadError = String(err);
      }
    })();

    return () => {
      disposed = true;
      live?.destroy();
      view = undefined;
    };
  });

  async function apply() {
    if (!view) return;
    busy = true;
    status = 'Applying…';
    try {
      const result = await moiraiStore.applyStory(view.state.doc.toString());
      if (!result) status = 'This backend does not edit stories.';
      else if (result.applied) status = `Applied. A new world at year ${result.year}.`;
      else status = `Not applied — the story does not parse. The world is still at ${result.year}.`;
    } catch (err) {
      status = String(err);
    } finally {
      busy = false;
    }
  }

  async function revert() {
    if (!view || !story) return;
    const { setStoryText } = await import('$lib/story-editor');
    setStoryText(view, await story.original());
    status = 'Reverted to the story the build shipped. Apply to rebuild the world from it.';
  }

  function goTo(d: StoryDiagnostic) {
    if (!view) return;
    const line = view.state.doc.line(Math.min(Math.max(d.line, 1), view.state.doc.lines));
    const pos = Math.min(line.from + Math.max(d.col, 0), line.to);
    view.dispatch({ selection: { anchor: pos }, scrollIntoView: true });
    view.focus();
  }
</script>

<div class="viz-root h-full grid grid-rows-[auto_1fr_auto] gap-2">
  <div class="flex items-baseline gap-3 flex-wrap">
    <h1 class="h2">Story</h1>
    <p class="text-sm opacity-70 flex-auto">
      The <code class="code">.sg</code> this world is built from. Errors come from the engine's own parser,
      running here in the page.
    </p>
    <button
      type="button"
      class="btn preset-tonal"
      disabled={!view || busy}
      onclick={() => void revert()}>Revert to w.sg</button
    >
    <button
      type="button"
      class="btn preset-filled-primary-500"
      disabled={!view || busy || passRunning || errors > 0}
      title={errors > 0
        ? 'The story does not parse'
        : passRunning
          ? 'A simulation pass is running'
          : 'Rebuild the world from this story'}
      onclick={() => void apply()}>Apply story</button
    >
  </div>

  {#if loadError}
    <div class="card preset-tonal-error p-4">Could not open the editor: {loadError}</div>
  {:else if !story}
    <div class="card p-4 opacity-70">
      This backend keeps its story in a file on disk, so there is nothing to edit here. Open the app
      with
      <code class="code">?backend=wasm</code> to edit the story in the browser.
    </div>
  {:else}
    <!-- cm-moirai carries the syntax colours; see the token block in app.css. -->
    <div bind:this={host} class="cm-moirai card overflow-hidden min-h-0"></div>
  {/if}

  <div class="text-sm min-h-8">
    {#if diagnostics.length === 0}
      <p class="opacity-70">{status || 'No problems.'}</p>
    {:else}
      <p class="mb-1 opacity-70">
        {errors}
        {errors === 1 ? 'error' : 'errors'}, {warnings}
        {warnings === 1 ? 'warning' : 'warnings'}{status ? ` — ${status}` : ''}
      </p>
      <ul class="max-h-40 overflow-auto space-y-0.5">
        {#each diagnostics as d, i (i)}
          <li>
            <button
              type="button"
              class="text-left hover:underline"
              onclick={() => goTo(d)}
              style:color={d.severity === 'Error' ? 'var(--viz-critical)' : 'var(--viz-warning)'}
            >
              {d.line}:{d.col}
              <span class="text-surface-950">{d.code}: {d.message}</span>
            </button>
          </li>
        {/each}
      </ul>
    {/if}
  </div>
</div>
