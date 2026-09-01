import type { Diagnostic } from '@codemirror/lint';
import type { Text } from '@codemirror/state';
import type { StoryDiagnostic } from './types';

/**
 * Turning the engine's parser diagnostics into CodeMirror ones.
 *
 * Two coordinate systems meet here and neither is negotiable. `StoryParser.Error` is **1-based line,
 * 0-based column** — the ANTLR token convention the engine inherited — while CodeMirror wants absolute
 * character offsets into the document. This is the one place that converts, which is why it is a module
 * with a test rather than a few lines inside the editor component.
 *
 * The clamping is not defensive padding. Validation is debounced, so a diagnostic routinely describes the
 * text as it was a keystroke or two ago; pointing past the end of the current document then throws inside
 * `doc.line`, and a thrown linter shows no errors at all — the failure looks exactly like success.
 */
const SEVERITY = {
  Error: 'error',
  Warning: 'warning',
  Information: 'info',
} as const;

export function toCodeMirrorDiagnostics(doc: Text, diagnostics: StoryDiagnostic[]): Diagnostic[] {
  return diagnostics.map((d) => {
    const from = offsetOf(doc, d.line, d.col);
    const to = offsetOf(doc, d.lineEnd, d.colEnd);
    return {
      // A zero-width range draws nothing, so an error with no span would read as no error. Widening it
      // by one character is the difference between a squiggle and silence.
      from: Math.min(from, doc.length),
      to: Math.min(Math.max(to, from + 1), doc.length),
      severity: SEVERITY[d.severity] ?? 'error',
      message: `${d.code}: ${d.message}`,
      source: 'moirai',
    };
  });
}

/** A 1-based line and 0-based column as an absolute offset, clamped into the document. */
function offsetOf(doc: Text, line: number, col: number): number {
  const l = doc.line(Math.min(Math.max(line, 1), doc.lines));
  return Math.min(l.from + Math.max(col, 0), l.to);
}

/** How many of these stop a story being applied. Warnings do not. */
export const errorCount = (diagnostics: StoryDiagnostic[]) =>
  diagnostics.filter((d) => d.severity === 'Error').length;
