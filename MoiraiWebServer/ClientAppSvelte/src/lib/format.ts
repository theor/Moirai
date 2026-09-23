/**
 * Turning the engine's spelling into a reader's.
 *
 * The engine reports values the way the story file names them: `Title.King`, `GodTypes.The_Sea`,
 * `null -> 50%`, and labels like `first_name` or `'Children'` (a @display label keeps its quotes). All
 * correct, and all noise to someone reading a life. These helpers only touch plain text — entity links
 * are split out by `parseEntityLink` before anything here sees them — so a name is never rewritten.
 */

/** `first_name` → `First name`, `'Children'` → `Children`. */
export function humanLabel(label: string): string {
  const s = unquote(label).replace(/_/g, ' ').trim();
  return s.charAt(0).toUpperCase() + s.slice(1).toLowerCase();
}

/** A tag as written in the story is a string literal, quotes included: `'faith'` → `faith`. */
export function unquote(s: string): string {
  return s.replace(/^'(.*)'$/, '$1');
}

// `Enum.Member` — both halves start with a letter and the member has no space, so "St. Anne" and "1.5"
// are left alone.
const ENUM_MEMBER = /\b[A-Z][A-Za-z0-9]*\.([A-Za-z][A-Za-z0-9_]*)\b/g;

/**
 * One plain-text piece of a property value, made readable.
 *
 * `whole` says the piece is the entire value rather than the text between two links, which is the only
 * case where a bare `true`, `false` or `null` can be read as the value itself.
 */
export function formatValueText(text: string, whole: boolean): string {
  if (whole) {
    const t = text.trim();
    if (t === 'null') return '—';
    if (t === 'true') return 'yes';
    if (t === 'false') return 'no';
  }
  return (
    text
      // A first assignment reads "null -> x"; the arrow into it says nothing.
      .replace(/^\s*null -> /, '')
      .replace(/ -> null\s*$/, ' → —')
      .replace(/ -> /g, ' → ')
      .replace(ENUM_MEMBER, (_, member: string) => member.replace(/_/g, ' '))
      .replace(/^(true|false)(?= →)|(?<=→ )(true|false)$/g, (b) => (b === 'true' ? 'yes' : 'no'))
  );
}
