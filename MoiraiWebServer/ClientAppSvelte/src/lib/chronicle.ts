import type { ChronicleEra } from './types';

/** One segment of the era band, positioned as a fraction of the world's span. */
export interface EraSegment {
  era: ChronicleEra;
  /** Fraction of the span, 0..1, where the segment starts. */
  left: number;
  /** Fraction of the span, 0..1, that it covers. */
  width: number;
}

/**
 * Lays the eras out along a band running from `start` to `end`.
 *
 * Eras are clamped to the span, because a story may date an age before its own clock starts, and ones
 * that fall wholly outside it are dropped. A world that has not moved yet (`end <= start`) gives every
 * era the whole band, because there is nothing to divide between them.
 */
export function eraBand(eras: ChronicleEra[], start: number, end: number): EraSegment[] {
  const span = end - start;
  if (span <= 0) return eras.filter((e) => e.open).map((era) => ({ era, left: 0, width: 1 }));

  const segments: EraSegment[] = [];
  for (const era of eras) {
    const from = Math.max(start, era.start);
    const to = Math.min(end, era.end);
    if (to <= from) continue;
    segments.push({ era, left: (from - start) / span, width: (to - from) / span });
  }
  return segments;
}

/** The era a year falls in: the last one to have started by then. Undefined before the first. */
export function eraAt(eras: ChronicleEra[], year: number): ChronicleEra | undefined {
  let found: ChronicleEra | undefined;
  for (const era of eras) if (era.start <= year) found = era;
  return found;
}

/** "764–964 · 200 years", or just the year for a world that has not moved yet. */
export function describeSpan(start: number, end: number): string {
  const years = end - start;
  if (years <= 0) return `${start}`;
  return `${start}–${end} · ${years} ${years === 1 ? 'year' : 'years'}`;
}
