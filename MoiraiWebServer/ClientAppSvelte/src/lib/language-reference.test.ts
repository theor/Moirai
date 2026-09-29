import { describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  argumentSuggestions,
  attributeArgumentAt,
  lookup,
  parameterAt,
  referenceEntries,
  summaryParts,
  wordAt,
} from './language-reference';

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
    for (const name of ['pick', 'each', 'create', 'record', 'repeat', 'random', 'chance', 'count'])
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

describe('attribute arguments', () => {
  it('finds which argument the caret is on', () => {
    expect(attributeArgumentAt('@frequency(1, ', 14)).toEqual({ name: 'frequency', argument: 1 });
    expect(attributeArgumentAt('@frequency(', 11)).toEqual({ name: 'frequency', argument: 0 });
  });

  it('is nothing outside the parentheses, or inside a string', () => {
    expect(attributeArgumentAt('@frequency(1, PerXYear, 2)', 26)).toBeNull();
    expect(attributeArgumentAt('@frequency', 5)).toBeNull();
    expect(attributeArgumentAt("@tag('a, b", 9)).toBeNull();
    expect(attributeArgumentAt('    pick(', 9)).toBeNull();
  });

  it('counts commas only at the top level of the parentheses', () => {
    const line = "@display(Kin, 'A, B', contains($self.friends, $other), ";
    expect(attributeArgumentAt(line, line.length)?.argument).toBe(3);
  });

  it('binds a repeated last parameter to every argument from its position', () => {
    const tag = lookup('tag', true)!;
    expect(parameterAt(tag, 3)?.name).toBe('name');
    expect(parameterAt(lookup('born', true)!, 1)).toBeUndefined();
  });

  const story = [
    '@parents(',
    '@born(',
    'entity Kin {',
    '    prop mother: Kin',
    '    prop friends: [Kin]',
    '    prop born_in: number',
    '    prop alive: bool',
    '}',
    'entity Other {',
    '    prop year: number',
    '}',
  ];

  it("offers the annotated type's properties of the right kind", () => {
    const parents = parameterAt(lookup('parents', true)!, 0)!;
    const born = parameterAt(lookup('born', true)!, 0)!;
    expect(argumentSuggestions(parents, story, 0)).toEqual(['mother']);
    expect(argumentSuggestions(born, story, 1)).toEqual(['born_in']);
  });

  it("offers a choice's choices and the story's types", () => {
    const mode = parameterAt(lookup('frequency', true)!, 1)!;
    expect(argumentSuggestions(mode, story, 0)).toEqual(['PerXYear', 'EveryXYear']);
    const other = parameterAt(lookup('display', true)!, 0)!;
    expect(argumentSuggestions(other, story, 0)).toEqual(['Kin', 'Other']);
  });
});
