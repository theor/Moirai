import { StreamLanguage, type StreamParser } from '@codemirror/language';

/**
 * Colouring for `.sg`, as a CodeMirror stream parser.
 *
 * Colour only — no structure. The story's real grammar is the engine's, and the editor already has it:
 * every keystroke is validated by `MoiraiTokenizer` and the actual parser through the WebAssembly build,
 * which is what draws the squiggles. Reimplementing the grammar here would be a second, worse answer to a
 * question that already has a right one, so this stops at "which colour is this word".
 *
 * {@link KEYWORDS} mirrors `MoiraiTokenizer.Keywords` and `moirai-language.test.ts` fails the build if
 * the two drift, in the same spirit as the language server's `SyntaxHighlightingDriftTests`.
 */
export const KEYWORDS = new Set([
  'null',
  'event',
  'entity',
  'singleton',
  'trigger',
  'prop',
  'function',
  'enum',
  'table',
  'when',
  'when_created',
  'set',
  'var',
  'match',
  'random_weighted',
  'if',
  'else',
  'true',
  'false',
  'and',
  'or',
]);

/**
 * The tokenizer keeps a mode stack for string interpolation, and so does this — `'born in {$p.year}'`
 * is a string containing an expression, and an editor that paints the whole line as a string loses the
 * one part of it that can be wrong.
 */
interface StoryState {
  stack: ('code' | 'string')[];
}

const WORD = /^[A-Za-z_]\w*/;
const NUMBER = /^\d+(\.\d+)?%?/;
const OPERATOR = /^(==|!=|>=|<=|=>|\?\?|:=|[-+*/%<>=.,:!?])/;

/** Exported for the drift test, which runs it over a line without standing up an editor. */
export const moiraiStreamParser: StreamParser<StoryState> = {
  name: 'moirai',

  startState: () => ({ stack: ['code'] }),

  copyState: (state) => ({ stack: [...state.stack] }),

  token(stream, state) {
    const inString = state.stack[state.stack.length - 1] === 'string';

    if (inString) {
      if (stream.eat("'")) {
        state.stack.pop();
        return 'string';
      }
      if (stream.eat('{')) {
        state.stack.push('code');
        return 'brace';
      }
      // Everything up to the next quote or interpolation, and at least one character so the stream
      // always advances — a stream parser that returns without consuming hangs the editor.
      stream.next();
      while (!stream.eol() && !/['{]/.test(stream.peek() ?? '')) stream.next();
      return 'string';
    }

    if (stream.eatSpace()) return null;

    if (stream.match('//')) {
      stream.skipToEnd();
      return 'lineComment';
    }

    if (stream.eat("'")) {
      state.stack.push('string');
      return 'string';
    }

    // Only closes an interpolation, never a block: a `}` at the outermost level belongs to a definition.
    if (state.stack.length > 1 && stream.eat('}')) {
      state.stack.pop();
      return 'brace';
    }

    // The sigils. `$p` is a local, `#Time` the singleton, `@frequency` an attribute — and all three are
    // read at a glance in a story, which is most of the value of colouring one at all.
    if (stream.eat('$')) {
      stream.match(WORD);
      return 'variableName';
    }
    if (stream.eat('#')) {
      stream.match(WORD);
      return 'namespace';
    }
    if (stream.eat('@')) {
      stream.match(WORD);
      return 'meta';
    }

    if (stream.match(NUMBER)) return 'number';

    const word = stream.match(WORD);
    if (word) {
      const text = (word as RegExpMatchArray)[0];
      if (KEYWORDS.has(text)) return text === 'true' || text === 'false' ? 'bool' : 'keyword';
      // A leading capital is what makes a type name a type name in this grammar, not a symbol table.
      return /^[A-Z]/.test(text) ? 'typeName' : null;
    }

    if (stream.match(OPERATOR)) return 'operator';

    stream.next();
    return null;
  },

  languageData: {
    commentTokens: { line: '//' },
    closeBrackets: { brackets: ['(', '[', '{', "'"] },
  },
};

export const moiraiLanguage = StreamLanguage.define(moiraiStreamParser);
