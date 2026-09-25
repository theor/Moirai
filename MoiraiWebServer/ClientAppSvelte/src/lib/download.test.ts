import { describe, expect, it } from 'vitest';
import { chronicleFileName } from './download';

describe('chronicleFileName', () => {
  it('names the world it came from', () => {
    expect(chronicleFileName(42, 964)).toBe('moirai-seed-42-year-964.md');
    expect(chronicleFileName('18446744073709551615', 800)).toBe(
      'moirai-seed-18446744073709551615-year-800.md',
    );
  });

  it('still gives a name before the seed is known', () => {
    expect(chronicleFileName(undefined, 764)).toBe('moirai-seed-unknown-year-764.md');
  });
});
