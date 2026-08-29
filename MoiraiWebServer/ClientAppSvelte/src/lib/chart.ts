/** Axis and formatting maths for LineChart, kept out of the component so it can be tested. */

/**
 * Gridline values from 0 up to at least `max`, landing on 1/2/5 x a power of ten. The last tick is the
 * chart's domain top, so the line always fits under the top gridline instead of touching the frame.
 */
export function niceTicks(max: number, count = 3): number[] {
  if (!Number.isFinite(max) || max <= 0) return [0, 1];
  const magnitude = Math.pow(10, Math.floor(Math.log10(max / count)));
  const normalised = max / count / magnitude;
  const step = (normalised <= 1 ? 1 : normalised <= 2 ? 2 : normalised <= 5 ? 5 : 10) * magnitude;
  const ticks: number[] = [];
  // The 1e-9 slack keeps a max that is exactly a multiple of the step from spilling one tick over.
  for (let v = 0; v <= max + step * 1e-9; v += step) ticks.push(Number(v.toPrecision(12)));
  if (ticks[ticks.length - 1] < max)
    ticks.push(Number((ticks[ticks.length - 1] + step).toPrecision(12)));
  return ticks;
}

/** Axis ticks and direct labels: short enough not to collide, exact enough to read. */
export function compact(n: number): string {
  if (!Number.isFinite(n)) return '—';
  const abs = Math.abs(n);
  if (abs >= 1e6) return trimZero((n / 1e6).toFixed(1)) + 'M';
  if (abs >= 1e4) return trimZero((n / 1e3).toFixed(1)) + 'K';
  if (Number.isInteger(n)) return n.toLocaleString();
  return abs < 10 ? n.toFixed(2) : n.toFixed(1);
}

function trimZero(s: string): string {
  return s.endsWith('.0') ? s.slice(0, -2) : s;
}

/** The sample nearest a pointer at `fraction` (0..1) across the plot. */
export function nearestIndex(fraction: number, count: number): number {
  if (count <= 1) return 0;
  return Math.max(0, Math.min(count - 1, Math.round(fraction * (count - 1))));
}
