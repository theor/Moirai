import { describe, expect, it } from 'vitest';
import {
  compress,
  decompress,
  readAddress,
  readStoryParam,
  shareLink,
  withAddress,
} from './world-address';

const at = (search: string) => new URL(`https://theor.github.io/Moirai/records${search}`);

describe('readAddress', () => {
  it('reads a seed and a year', () => {
    expect(readAddress(at('?seed=1234&year=964'))).toEqual({ seed: '1234', year: 964 });
  });

  it('says nothing when the link says nothing', () => {
    expect(readAddress(at(''))).toEqual({ seed: null, year: null });
  });

  it('ignores a malformed seed rather than building a world from it', () => {
    expect(readAddress(at('?seed=abc')).seed).toBeNull();
    expect(readAddress(at('?seed=-1')).seed).toBeNull();
  });

  it('keeps a seed as a string, because it can be bigger than a JS number holds', () => {
    const big = '18446744073709551615';
    expect(readAddress(at(`?seed=${big}`)).seed).toBe(big);
  });

  it('treats year 0 as unspecified', () => {
    // No world starts there — w.sg begins at 764 — so a 0 is noise, and reading it literally would
    // show an empty world instead of applying the default.
    expect(readAddress(at('?year=0')).year).toBeNull();
    expect(readAddress(at('?year=nope')).year).toBeNull();
  });
});

describe('withAddress', () => {
  it('writes both parts', () => {
    expect(withAddress(at(''), { seed: '42', year: 964 }).search).toBe('?seed=42&year=964');
  });

  it('leaves the app’s own parameters alone', () => {
    const url = withAddress(at('?e=17&f=born'), { seed: '42', year: 964 });
    expect(url.searchParams.get('e')).toBe('17');
    expect(url.searchParams.get('f')).toBe('born');
  });

  it('removes what is not specified', () => {
    expect(withAddress(at('?seed=42&year=964'), { seed: null, year: null }).search).toBe('');
  });

  it('round-trips', () => {
    const address = { seed: '7', year: 1200 };
    expect(readAddress(withAddress(at(''), address))).toEqual(address);
  });
});

describe('the story in a link', () => {
  it('round-trips through compression', async () => {
    const story = "event a {\n  record('x')\n}\n".repeat(50);
    expect(await decompress(await compress(story))).toBe(story);
  });

  it('survives characters that base64 would mangle in a fragment', async () => {
    const story = "record('a+b/c=d')";
    const encoded = await compress(story);
    expect(encoded).not.toMatch(/[+/=]/);
    expect(await decompress(encoded)).toBe(story);
  });

  it('round-trips text outside ASCII', async () => {
    // The UTF-8 encoding here is hand-rolled to keep the stream types honest, so pin that it encodes.
    const story = "record('Æthelred — 大陸 — naïve 🜚')";
    expect(await decompress(await compress(story))).toBe(story);
  });

  it('is worth compressing', async () => {
    const story = "event a {\n  record('x')\n}\n".repeat(200);
    expect((await compress(story)).length).toBeLessThan(story.length / 4);
  });

  it('is left out when the story is the one the build ships', async () => {
    const link = await shareLink(
      at('?e=3'),
      { seed: '42', year: 964 },
      { current: 'a', shipped: 'a' },
    );
    expect(link).toContain('seed=42');
    expect(link).toContain('year=964');
    expect(link).not.toContain('#');
  });

  it('carries an edited story in the fragment, and comes back out', async () => {
    const current = "event edited {\n  record('mine')\n}";
    const link = await shareLink(at(''), { seed: '42', year: 964 }, { current, shipped: 'other' });
    const param = readStoryParam(new URL(link).hash);
    expect(param).not.toBeNull();
    expect(await decompress(param!)).toBe(current);
  });

  it('reads nothing from a link with no story', () => {
    expect(readStoryParam('')).toBeNull();
    expect(readStoryParam('#other=1')).toBeNull();
  });
});
