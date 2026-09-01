import { defaultKeymap, history, historyKeymap, indentWithTab } from '@codemirror/commands';
import {
  HighlightStyle,
  bracketMatching,
  indentOnInput,
  syntaxHighlighting,
} from '@codemirror/language';
import { lintGutter, linter } from '@codemirror/lint';
import { EditorState } from '@codemirror/state';
import {
  EditorView,
  drawSelection,
  highlightActiveLine,
  highlightActiveLineGutter,
  keymap,
  lineNumbers,
} from '@codemirror/view';
import { tags as t } from '@lezer/highlight';
import { toCodeMirrorDiagnostics } from './diagnostics';
import { moiraiLanguage } from './moirai-language';
import type { StoryDiagnostic } from './types';

/**
 * The CodeMirror side of the Story page, kept out of the `.svelte` so the whole editor — and the six
 * CodeMirror packages behind it — lands in one route chunk that no other page, and no server
 * deployment, ever fetches.
 *
 * The interesting part is what is *not* here: no grammar. Squiggles come from
 * {@link StoryEditorOptions.validate}, which runs the engine's own parser over the text in the browser.
 * That is the whole reason this is worth building on the WebAssembly backend rather than guessing at
 * errors with regular expressions.
 */

/** How long to wait for typing to stop before parsing. A parse of w.sg is milliseconds; the delay is
 * about not redrawing squiggles under a moving caret. */
const LINT_DELAY_MS = 400;

// Colours come from the .cm-moirai token block in app.css, where the theme steps are chosen and their
// contrast recorded, rather than being hard-coded here — the same split the charts use.
const highlight = HighlightStyle.define([
  { tag: [t.keyword, t.brace], color: 'var(--syn-keyword)' },
  { tag: [t.typeName, t.namespace], color: 'var(--syn-type)' },
  { tag: t.variableName, color: 'var(--syn-local)' },
  { tag: t.string, color: 'var(--syn-string)' },
  { tag: [t.number, t.bool], color: 'var(--syn-literal)' },
  { tag: t.operator, color: 'var(--syn-operator)' },
  { tag: t.meta, color: 'var(--syn-meta)' },
  { tag: t.comment, color: 'var(--syn-comment)', fontStyle: 'italic' },
]);

const theme = EditorView.theme({
  '&': { height: '100%', fontSize: '0.875rem' },
  '.cm-scroller': {
    overflow: 'auto',
    fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace',
  },
  '&.cm-focused': { outline: 'none' },
});

export interface StoryEditorOptions {
  parent: HTMLElement;
  doc: string;
  /** Ask the engine what it makes of this text. */
  validate(text: string): Promise<StoryDiagnostic[]>;
  /** The parser's verdict, every time validation runs — for the summary line under the editor. */
  onDiagnostics(diagnostics: StoryDiagnostic[]): void;
  /** Every edit, so the page can keep the draft. */
  onChange(text: string): void;
}

export function createStoryEditor(options: StoryEditorOptions): EditorView {
  const lint = linter(
    async (view) => {
      const text = view.state.doc.toString();
      const diagnostics = await options.validate(text);
      options.onDiagnostics(diagnostics);
      return toCodeMirrorDiagnostics(view.state.doc, diagnostics);
    },
    { delay: LINT_DELAY_MS },
  );

  return new EditorView({
    parent: options.parent,
    state: EditorState.create({
      doc: options.doc,
      extensions: [
        lineNumbers(),
        highlightActiveLine(),
        highlightActiveLineGutter(),
        drawSelection(),
        history(),
        indentOnInput(),
        bracketMatching(),
        lintGutter(),
        lint,
        moiraiLanguage,
        syntaxHighlighting(highlight),
        // Tab indents rather than moving focus. A deliberate trade: it is the expected behaviour in a
        // code editor, and Escape-then-Tab still gets a keyboard user out.
        keymap.of([...defaultKeymap, ...historyKeymap, indentWithTab]),
        theme,
        EditorView.updateListener.of((u) => {
          if (u.docChanged) options.onChange(u.state.doc.toString());
        }),
      ],
    }),
  });
}

/** Replace the whole document, e.g. on a revert. */
export function setStoryText(view: EditorView, text: string) {
  view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: text } });
}
