import { describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { StringStream } from '@codemirror/language';
import { KEYWORDS, moiraiStreamParser } from './moirai-language';

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..', '..', '..', '..');
const tokenizer = join(repoRoot, 'Moirai.Parser', 'MoiraiTokenizer.cs');

/** Run the stream parser over a line and collect (text, style) pairs. */
function tokens(line: string): [string, string | null][] {
  const parser = moiraiStreamParser;
  const state = parser.startState!(2);
  const out: [string, string | null][] = [];
  const stream = new StringStream(line, 2, 2, 0);
  while (!stream.eol()) {
    const style = parser.token(stream, state);
    out.push([stream.current(), style]);
    stream.start = stream.pos;
  }
  return out.filter(([text]) => text.trim() !== '');
}

const styleOf = (line: string, text: string) => tokens(line).find(([t]) => t === text)?.[1];

describe('the Moirai mode', () => {
  it('knows exactly the keywords the tokenizer knows', () => {
    // The authoritative list is MoiraiTokenizer.Keywords. Reading it here means a keyword added to the
    // language fails this build rather than quietly going uncoloured, which is what the language
    // server's SyntaxHighlightingDriftTests does for the LSP.
    const source = readFileSync(tokenizer, 'utf8');
    const declared = new Set(
      [...source.matchAll(/\["(\w+)"\] = MoiraiTokenKind\./g)].map((m) => m[1]),
    );

    expect(declared.size).toBeGreaterThan(0);
    expect([...KEYWORDS].sort()).toEqual([...declared].sort());
  });

  it('colours keywords, types and locals apart', () => {
    const line = 'each Person $p: (alive) {';
    expect(styleOf(line, 'each')).toBeNull(); // `each` is a function, not a keyword
    expect(styleOf(line, 'Person')).toBe('typeName');
    expect(styleOf(line, '$p')).toBe('variableName');
  });

  it('colours the sigils', () => {
    expect(styleOf('#Time.year', '#Time')).toBe('namespace');
    expect(styleOf('@frequency(1, PerXYear, 4)', '@frequency')).toBe('meta');
  });

  it('treats a comment as running to the end of the line', () => {
    expect(tokens('set $x = 1 // and the rest').at(-1)).toEqual(['// and the rest', 'lineComment']);
  });

  it('keeps an interpolated expression out of the string around it', () => {
    // '...{$p}...' is a string containing an expression, and the expression is the part that can be
    // wrong — painting the whole line as a string is what loses it.
    const styles = tokens("record('born {$p.name} here')");
    expect(styles.find(([t]) => t === '$p')?.[1]).toBe('variableName');
    expect(styles.some(([t, s]) => t.includes('born') && s === 'string')).toBe(true);
  });

  it('colours numbers and percentages', () => {
    expect(styleOf('set $x = 42', '42')).toBe('number');
    expect(styleOf('set $x = 12.5%', '12.5%')).toBe('number');
  });
});
