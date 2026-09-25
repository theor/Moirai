import type { CauseStep } from './types';

/**
 * Reading a cause chain.
 *
 * The engine answers "why?" with the rules that ran, nearest first (WorldSession.GetCause). What is left
 * here is wording: a heading per step, and the engine's spelling of a change made readable.
 */

/** How a step came to run, as a heading: "trigger succession", "event murder", … */
export function stepHeading(step: CauseStep): string {
  switch (step.kind) {
    case 'trigger':
      return `trigger ${step.rule}`;
    case 'call':
      return `event ${step.rule}, called by the rule below`;
    case 'scheduled':
      return 'scheduled by the rule below';
    default:
      return `event ${step.rule}`;
  }
}

// An entity link as the printer writes it: `<#12>Aldric</>`. Split out so no rewrite touches a name.
const LINK = /(<#\d+>.*?<\/>)/;
const ENUM_MEMBER = /\b[A-Z][A-Za-z0-9]*\.([A-Za-z][A-Za-z0-9_]*)\b/g;

/**
 * `<#12>Aldric</>: age Age.Adult -> Age.Old, alive true -> false, killer null -> <#45>Morgana</>` →
 * `<#12>Aldric</>: age Adult → Old, alive yes → no, killer → <#45>Morgana</>`.
 *
 * A "because" lists several changes, so unlike `formatValueText` (one value) booleans are rewritten
 * wherever they sit next to an arrow, not only at the ends. Links pass through untouched.
 */
export function formatBecause(text: string): string {
  return text
    .split(LINK)
    .map((piece) =>
      LINK.test(piece)
        ? piece
        : piece
            .replace(/ null -> /g, ' → ')
            .replace(/ -> null\b/g, ' → none')
            .replace(/ -> /g, ' → ')
            .replace(ENUM_MEMBER, (_, member: string) => member.replace(/_/g, ' '))
            .replace(/\b(true|false)(?= →)|(?<=→ )(true|false)\b/g, (b) =>
              b === 'true' ? 'yes' : 'no',
            ),
    )
    .join('');
}

/**
 * A rule's name as a reader should see it. Named rules are their names; a `schedule(...)` body has none,
 * and the engine calls it `schedule@<line>`, which reads as `scheduled · line 489`.
 */
export function ruleLabel(rule: string): string {
  const m = /^schedule@(\d+)$/.exec(rule);
  return m ? `scheduled · line ${m[1]}` : rule;
}
