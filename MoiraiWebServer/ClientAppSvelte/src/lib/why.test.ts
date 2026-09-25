import { describe, expect, it } from 'vitest';
import { readWhy, withWhy } from './why';

describe('readWhy', () => {
  it('reads the firing a query names', () => {
    expect(readWhy('?backend=wasm&seed=42&why=2381')).toBe(2381);
  });

  it('is null for none, and for anything that is not a firing', () => {
    expect(readWhy('?seed=42')).toBeNull();
    expect(readWhy('?why=')).toBeNull();
    expect(readWhy('?why=0')).toBeNull();
    expect(readWhy('?why=-3')).toBeNull();
    expect(readWhy('?why=12abc')).toBeNull();
  });
});

describe('withWhy', () => {
  const url = new URL('http://localhost/records?backend=wasm&seed=42&year=964&e=28');

  it('sets it and keeps the world and the selection', () => {
    const next = withWhy(url, 77);
    expect(next.searchParams.get('why')).toBe('77');
    expect(next.searchParams.get('seed')).toBe('42');
    expect(next.searchParams.get('year')).toBe('964');
    expect(next.searchParams.get('e')).toBe('28');
    expect(next.pathname).toBe('/records');
  });

  it('removes it on close, leaving the rest as it was', () => {
    expect(withWhy(withWhy(url, 77), null).href).toBe(url.href);
  });
});
