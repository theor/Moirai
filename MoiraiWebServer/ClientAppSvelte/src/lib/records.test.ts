import { describe, expect, it } from 'vitest';
import { allTags, mentionsEntity, visibleRecords } from './records';
import type { Record as StoryRecord } from './types';

const rec = (over: Partial<StoryRecord> = {}): StoryRecord => ({
  text: 'something happened',
  changesetId: 1,
  actionId: 1,
  year: 800,
  participants: [],
  tags: null,
  ...over,
});

const none = new Set<number>();

describe('allTags', () => {
  it('collects the union, sorted, with duplicates collapsed', () => {
    const records = [
      rec({ tags: ['war', 'faith'] }),
      rec({ tags: ['faith'] }),
      rec({ tags: ['era'] }),
    ];
    expect(allTags(records)).toEqual(['era', 'faith', 'war']);
  });

  it('tolerates records with no tags at all', () => {
    // Tags arrive as null, not [], when a rule declared none.
    expect(allTags([rec(), rec({ tags: null })])).toEqual([]);
  });
});

describe('mentionsEntity', () => {
  it('uses the participants when a rule bound any', () => {
    expect(mentionsEntity(rec({ participants: [7, 9] }), 7)).toBe(true);
    expect(mentionsEntity(rec({ participants: [7, 9] }), 8)).toBe(false);
  });

  it('falls back to the entity marker in the text when there are none', () => {
    // Records emitted before a rule bound a variable carry no participants, so the printer's
    // <#id>name</> marker is the only evidence. The engine does the same server-side.
    const r = rec({ participants: [], text: 'the <#42>Kingdom of Arthur</> is created' });
    expect(mentionsEntity(r, 42)).toBe(true);
    expect(mentionsEntity(r, 4)).toBe(false);
  });

  it('does not match an id that is merely a prefix of another', () => {
    const r = rec({ participants: [], text: 'the <#421>Kingdom</> is created' });
    expect(mentionsEntity(r, 42)).toBe(false);
  });

  it('prefers participants over the text when both exist', () => {
    const r = rec({ participants: [1], text: 'mentions <#42>someone else</>' });
    expect(mentionsEntity(r, 42)).toBe(false);
  });
});

describe('visibleRecords', () => {
  const records = [
    rec({ actionId: 1, participants: [7], tags: ['war'], text: 'a' }),
    rec({ actionId: 2, participants: [8], tags: ['faith'], text: 'b' }),
    rec({ actionId: 1, participants: [7, 8], tags: null, text: 'c' }),
  ];

  it('returns everything when nothing is filtered', () => {
    expect(visibleRecords(records, { entity: -1, tag: '', hiddenActionIds: none })).toHaveLength(3);
  });

  it('filters by entity', () => {
    const out = visibleRecords(records, { entity: 8, tag: '', hiddenActionIds: none });
    expect(out.map((r) => r.text)).toEqual(['b', 'c']);
  });

  it('filters by tag, and an untagged record never matches one', () => {
    const out = visibleRecords(records, { entity: -1, tag: 'war', hiddenActionIds: none });
    expect(out.map((r) => r.text)).toEqual(['a']);
  });

  it('drops records from switched-off events', () => {
    const out = visibleRecords(records, { entity: -1, tag: '', hiddenActionIds: new Set([1]) });
    expect(out.map((r) => r.text)).toEqual(['b']);
  });

  it('applies every filter at once', () => {
    const out = visibleRecords(records, { entity: 8, tag: 'faith', hiddenActionIds: new Set([1]) });
    expect(out.map((r) => r.text)).toEqual(['b']);
  });

  it('keeps the feed in order', () => {
    const out = visibleRecords(records, { entity: 7, tag: '', hiddenActionIds: none });
    expect(out.map((r) => r.text)).toEqual(['a', 'c']);
  });
});
