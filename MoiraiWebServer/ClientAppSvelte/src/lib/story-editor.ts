import {
  autocompletion,
  completionKeymap,
  type Completion,
  type CompletionContext,
  type CompletionResult,
} from '@codemirror/autocomplete';
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
  hoverTooltip,
  keymap,
  lineNumbers,
} from '@codemirror/view';
import { tags as t } from '@lezer/highlight';
import { toCodeMirrorDiagnostics } from './diagnostics';
import {
  lookup,
  referenceEntries,
  summaryParts,
  wordAt,
  type ReferenceEntry,
} from './language-reference';
import { KEYWORDS, moiraiLanguage } from './moirai-language';
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
  '.cm-moirai-doc': { maxWidth: '32rem', padding: '0.25rem 0.5rem', fontSize: '0.8125rem' },
  '.cm-moirai-doc pre': {
    margin: '0 0 0.25rem',
    fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace',
    whiteSpace: 'pre-wrap',
  },
  '.cm-moirai-doc p': { margin: '0', lineHeight: '1.4' },
  '.cm-moirai-doc code': { fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace' },
});

/** The signatures as code, then the summary, with its `code spans` set as code. Built from text nodes,
 * never from HTML, so nothing in a summary can become markup. */
function renderDoc(entry: ReferenceEntry): HTMLElement {
  const root = document.createElement('div');
  root.className = 'cm-moirai-doc';
  const signatures = document.createElement('pre');
  signatures.textContent = entry.signatures.join('\n');
  root.appendChild(signatures);
  const summary = document.createElement('p');
  for (const part of summaryParts(entry.summary)) {
    const node = part.code ? document.createElement('code') : document.createElement('span');
    node.textContent = part.text;
    summary.appendChild(node);
  }
  root.appendChild(summary);
  return root;
}

/** Hovering a built-in or an `@attribute` shows its documentation. */
const referenceHover = hoverTooltip((view, pos) => {
  const line = view.state.doc.lineAt(pos);
  const word = wordAt(line.text, pos - line.from);
  const entry = word && lookup(word.word, word.attribute);
  if (!word || !entry) return null;
  return {
    pos: line.from + word.from - (word.attribute ? 1 : 0),
    end: line.from + word.to,
    above: true,
    create: () => ({ dom: renderDoc(entry) }),
  };
});

const functionOptions: Completion[] = referenceEntries
  .filter((e) => e.kind === 'function')
  .map((e) => ({
    label: e.name,
    type: 'function',
    detail: e.signatures[0],
    info: () => renderDoc(e),
  }));
const attributeOptions: Completion[] = referenceEntries
  .filter((e) => e.kind === 'attribute')
  .map((e) => ({
    label: e.name,
    type: 'keyword',
    detail: e.signatures[0],
    info: () => renderDoc(e),
  }));
const keywordOptions: Completion[] = [...KEYWORDS].map((k) => ({ label: k, type: 'keyword' }));

/**
 * Built-ins, keywords and, after an `@`, attributes. The story's own names are left out on purpose: they
 * come from a parse, and the parse the editor has is the engine's, which answers with diagnostics only.
 */
function completeReference(context: CompletionContext): CompletionResult | null {
  const match = context.matchBefore(/[@$#.]?\w*/);
  if (!match) return null;
  const sigil = match.text[0];
  if (sigil === '$' || sigil === '#' || sigil === '.') return null;
  const attribute = sigil === '@';
  if (!attribute && match.from === match.to && !context.explicit) return null;
  return {
    from: attribute ? match.from + 1 : match.from,
    options: attribute ? attributeOptions : [...functionOptions, ...keywordOptions],
    validFor: /^\w*$/,
  };
}

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
        referenceHover,
        autocompletion({ override: [completeReference] }),
        moiraiLanguage,
        syntaxHighlighting(highlight),
        // Tab indents rather than moving focus. A deliberate trade: it is the expected behaviour in a
        // code editor, and Escape-then-Tab still gets a keyboard user out.
        keymap.of([...completionKeymap, ...defaultKeymap, ...historyKeymap, indentWithTab]),
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

/**
 * Select a 1-based line and bring it to the top of the view, with a little of what comes before it
 * showing, e.g. to open the story at the rule a "why?" chain named. Clamps, like $lib/diagnostics:
 * the line comes from the world's story and the document may be shorter.
 */
export function revealLine(view: EditorView, line: number) {
  const target = view.state.doc.line(Math.min(Math.max(line, 1), view.state.doc.lines));
  view.dispatch({
    selection: { anchor: target.from, head: target.to },
    effects: EditorView.scrollIntoView(target.from, { y: 'start', yMargin: 48 }),
  });
  view.focus();
}
