import { describe, expect, it } from 'vitest';
import { Text } from '@codemirror/state';
import { errorCount, toCodeMirrorDiagnostics } from './diagnostics';
import type { StoryDiagnostic } from './types';

const doc = Text.of(['event age_up {', '  set $p.age = 1', '}']);

const diag = (over: Partial<StoryDiagnostic> = {}): StoryDiagnostic => ({
  severity: 'Error',
  code: 'UnknownProperty',
  line: 2,
  col: 9,
  lineEnd: 2,
  colEnd: 12,
  message: "'age'",
  ...over,
});

describe('toCodeMirrorDiagnostics', () => {
  it('maps a 1-based line and 0-based column onto absolute offsets', () => {
    const [d] = toCodeMirrorDiagnostics(doc, [diag()]);
    // Line 2 starts after "event age_up {\n" — 15 characters.
    expect(d.from).toBe(15 + 9);
    expect(d.to).toBe(15 + 12);
    expect(doc.sliceString(d.from, d.to)).toBe('age');
  });

  it('widens an empty range, which would otherwise draw no squiggle at all', () => {
    const [d] = toCodeMirrorDiagnostics(doc, [diag({ colEnd: 9 })]);
    expect(d.to).toBe(d.from + 1);
  });

  it('clamps a diagnostic that points past the end of the document', () => {
    // Validation is debounced, so a diagnostic can describe text that has since been deleted. An
    // out-of-range line throws inside doc.line, and a linter that throws reports nothing — which looks
    // identical to a clean story.
    const [d] = toCodeMirrorDiagnostics(doc, [
      diag({ line: 99, col: 400, lineEnd: 99, colEnd: 400 }),
    ]);
    expect(d.from).toBeLessThanOrEqual(doc.length);
    expect(d.to).toBeLessThanOrEqual(doc.length);
    expect(d.to).toBeGreaterThanOrEqual(d.from);
  });

  it('clamps a column past the end of its line to the line end', () => {
    const [d] = toCodeMirrorDiagnostics(doc, [diag({ col: 400, colEnd: 400 })]);
    expect(d.from).toBe(doc.line(2).to);
  });

  it('carries the severity across', () => {
    const [e, w, i] = toCodeMirrorDiagnostics(doc, [
      diag(),
      diag({ severity: 'Warning' }),
      diag({ severity: 'Information' }),
    ]);
    expect([e.severity, w.severity, i.severity]).toEqual(['error', 'warning', 'info']);
  });
});

describe('errorCount', () => {
  it('counts errors and ignores warnings', () => {
    expect(errorCount([diag(), diag({ severity: 'Warning' }), diag()])).toBe(2);
  });
});
