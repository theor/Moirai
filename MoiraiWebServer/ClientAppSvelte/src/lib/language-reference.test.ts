import { describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { lookup, referenceEntries, summaryParts, wordAt } from './language-reference';

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..', '..', '..', '..');

describe('the language reference', () => {
  it('is the same file as the one in docs/', () => {
    // LanguageReferenceTests writes both; this catches a copy committed without the other.
    const here = readFileSync(
      join(repoRoot, 'MoiraiWebServer/ClientAppSvelte/src/lib/language-reference.json'),
      'utf8',
    );
    const docs = readFileSync(join(repoRoot, 'docs/language-reference.json'), 'utf8');
    expect(here.replace(/\r\n/g, '\n')).toBe(docs.replace(/\r\n/g, '\n'));
  });

  it('documents the built-ins and attributes a story uses most', () => {
    for (const name of ['pick', 'each', 'create', 'record', 'call', 'random', 'chance', 'count'])
      expect(lookup(name, false)?.summary, name).toBeTruthy();
    for (const name of ['start', 'frequency', 'tag', 'display', 'parents'])
      expect(lookup(name, true)?.summary, name).toBeTruthy();
    expect(referenceEntries.every((e) => e.signatures.length > 0)).toBe(true);
  });

  it('keeps functions and attributes apart', () => {
    expect(lookup('pick', true)).toBeUndefined();
    expect(lookup('start', false)).toBeUndefined();
  });
});

describe('wordAt', () => {
  it('finds the word around the caret, from either end', () => {
    const line = '    pick Person $p: (alive)';
    expect(wordAt(line, 4)).toEqual({ from: 4, to: 8, word: 'pick', attribute: false });
    expect(wordAt(line, 8)?.word).toBe('pick');
  });

  it('marks a word after @ as an attribute', () => {
    expect(wordAt('@frequency(1, PerXYear, 2)', 3)).toMatchObject({
      word: 'frequency',
      attribute: true,
    });
  });

  it('skips variables, singletons and properties, whatever they are called', () => {
    expect(wordAt('var $count: 3', 7)).toBeNull();
    expect(wordAt('#Time.year', 2)).toBeNull();
    expect(wordAt('$p.count', 5)).toBeNull();
  });

  it('finds nothing between words', () => {
    expect(wordAt('a  b', 2)).toBeNull();
  });
});

describe('summaryParts', () => {
  it('splits code spans out of the prose', () => {
    expect(summaryParts('Use `count` to tell none from zero.')).toEqual([
      { code: false, text: 'Use ' },
      { code: true, text: 'count' },
      { code: false, text: ' to tell none from zero.' },
    ]);
  });
});
