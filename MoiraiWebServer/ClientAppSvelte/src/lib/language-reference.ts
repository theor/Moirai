import reference from './language-reference.json';

/**
 * The language reference, for the Story page's hover and completion.
 *
 * `language-reference.json` is generated: the documentation lives beside each built-in in
 * `Moirai.Parser/StoryParser.cs`, and `Moirai.Tests/LanguageReferenceTests` writes it out to
 * `docs/language-reference.json` and to this copy, and fails if either is stale. The Markdown reference in
 * `docs/` is rendered from the same file, so the editor and the docs cannot say different things.
 *
 * Only `story-editor.ts` imports this, which keeps the table in the `/story` route chunk.
 */
export interface ReferenceEntry {
  kind: 'function' | 'attribute';
  name: string;
  category: string;
  signatures: string[];
  summary: string;
  example?: string;
  targets?: string[];
}

const entries = reference.entries as ReferenceEntry[];
const functions = new Map(entries.filter((e) => e.kind === 'function').map((e) => [e.name, e]));
const attributes = new Map(entries.filter((e) => e.kind === 'attribute').map((e) => [e.name, e]));

export const referenceEntries: readonly ReferenceEntry[] = entries;

/** The entry for a built-in function, or for an attribute when `attribute` (the word followed an `@`). */
export function lookup(name: string, attribute: boolean): ReferenceEntry | undefined {
  return (attribute ? attributes : functions).get(name);
}

export interface WordAt {
  from: number;
  to: number;
  word: string;
  /** Written as `@word`: an attribute, not a function. */
  attribute: boolean;
}

const WORD_CHAR = /\w/;

/**
 * The word under `pos` in `line`, if it could name a built-in. A word after `$`, `#` or `.` is a variable,
 * a singleton or a property, which may well be called `count` or `age` without being the built-in.
 */
export function wordAt(line: string, pos: number): WordAt | null {
  let from = pos;
  let to = pos;
  while (from > 0 && WORD_CHAR.test(line[from - 1])) from--;
  while (to < line.length && WORD_CHAR.test(line[to])) to++;
  if (from === to) return null;
  const before = line[from - 1];
  if (before === '$' || before === '#' || before === '.') return null;
  return { from, to, word: line.slice(from, to), attribute: before === '@' };
}

/** A summary split on its backticks, so the code spans can be set in code without trusting any HTML. */
export function summaryParts(summary: string): { code: boolean; text: string }[] {
  return summary
    .split('`')
    .map((text, i) => ({ code: i % 2 === 1, text }))
    .filter((p) => p.text !== '');
}
