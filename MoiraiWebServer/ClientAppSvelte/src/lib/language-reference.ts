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
  /** An attribute's parameters, from which its signature was generated. */
  parameters?: ReferenceParameter[];
  /** A built-in function's forms, from which its signatures were generated. */
  forms?: ReferenceForm[];
}

/** One way to write a built-in: `Form` in `Moirai.Parser/LanguageReference.cs`. */
export interface ReferenceForm {
  kind: 'call' | 'binding';
  signature: string;
  parameters?: {
    name: string;
    kind: string;
    type: string;
    optional?: boolean;
    repeated?: boolean;
  }[];
  returns?: string;
  /** A binding form's head (`predicate`, `predicateAndValue`, ...) and how long its `$v` lives. */
  head?: string;
  lives?: 'rest' | 'block' | 'call';
  /** The blocks the form carries: when each runs and what it sees. */
  blocks?: { keyword?: string; runs: string; sees: string; describes: string }[];
}

/** One parameter of an attribute: `AttributeParam` in `Moirai.Parser/AttributeSchema.cs`. */
export interface ReferenceParameter {
  name: string;
  kind: 'number' | 'choice' | 'string' | 'text' | 'entityType' | 'query' | 'property';
  /** What it accepts, as a phrase a reader sees. */
  accepts: string;
  optional?: boolean;
  repeated?: boolean;
  choices?: string[];
  propertyKind?: 'selfReference' | 'number' | 'bool';
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

/** The parameter an attribute's argument at `index` binds to: a repeated last one takes the rest. */
export function parameterAt(entry: ReferenceEntry, index: number): ReferenceParameter | undefined {
  const params = entry.parameters ?? [];
  if (index < params.length) return params[index];
  const last = params[params.length - 1];
  return last?.repeated ? last : undefined;
}

export interface AttributeArgument {
  name: string;
  /** 0-based position among the attribute's arguments. */
  argument: number;
}

/**
 * The attribute argument `column` sits in, on a line holding a top-level `@name(...)`:
 * `@frequency(1, |` is `{ name: 'frequency', argument: 1 }`. Null outside the parentheses and inside a
 * string literal, where nothing is worth suggesting.
 */
export function attributeArgumentAt(line: string, column: number): AttributeArgument | null {
  const head = /^@(\w+)\(/.exec(line);
  if (!head || column < head[0].length) return null;
  let depth = 0;
  let argument = 0;
  let inString = false;
  for (let i = head[0].length; i < column; i++) {
    const c = line[i];
    if (c === "'") inString = !inString;
    else if (inString) continue;
    else if (c === '(') depth++;
    else if (c === ')') {
      if (depth === 0) return null;
      depth--;
    } else if (c === ',' && depth === 0) argument++;
  }
  return inString ? null : { name: head[1], argument };
}

/**
 * What to offer for an argument, read from the story's text: a choice parameter's choices, the story's
 * entity types, or the properties of the type the attribute annotates that fit a property parameter.
 * The text rather than a parse, because the definition being annotated is the one being typed, and does
 * not parse yet. Empty for parameters with no closed set of answers.
 */
export function argumentSuggestions(
  param: ReferenceParameter,
  lines: readonly string[],
  lineIndex: number,
): string[] {
  switch (param.kind) {
    case 'choice':
      return param.choices ?? [];
    case 'entityType':
    // A query, `each T $v: (...)`, starts with its type.
    case 'query':
      return lines.flatMap((l) => /^(?:entity|singleton)\s+([A-Z]\w*)/.exec(l)?.[1] ?? []);
    case 'property': {
      let i = lineIndex;
      let typeName: string | undefined;
      for (; i < lines.length && !typeName; i++)
        typeName = /^(?:entity|singleton)\s+(\w+)/.exec(lines[i])?.[1];
      const found: string[] = [];
      for (; i < lines.length && !/^}/.test(lines[i]); i++) {
        const prop = /^\s*prop\s+(\w+)\s*:\s*(\[?)\s*(\w+)/.exec(lines[i]);
        if (!prop || prop[2]) continue;
        const type = prop[3];
        const fits =
          param.propertyKind === 'selfReference'
            ? type === typeName
            : param.propertyKind === 'number'
              ? type === 'number' || type === 'float'
              : type === 'bool';
        if (fits) found.push(prop[1]);
      }
      return found;
    }
    default:
      return [];
  }
}
