// The Record name shadows TypeScript's own Record<K, V> utility type inside this module, so it is
// aliased rather than imported bare.
import type { Record as StoryRecord } from './types';

/**
 * Reading the record feed.
 *
 * The chronicle is one long list that four controls narrow at once — a selected entity, a tag, and the
 * per-event visibility toggles — so the filtering is worth stating once, in one place, rather than as a
 * clause chain inside a template.
 */

/** Every tag seen across the loaded records, sorted, for the filter bar. */
export function allTags(records: readonly StoryRecord[]): string[] {
  return Array.from(new Set(records.flatMap((r) => r.tags ?? []))).sort();
}

/**
 * Whether a record is about an entity.
 *
 * Participants come from the record's `{$var}` interpolation slots, so a record emitted before a rule
 * bound any variable has none. The fallback looks for the entity marker the printer writes into the
 * text, which is the same fallback the engine uses server-side (see `WorldSession.MentionsEntity`) —
 * the two need to agree, or filtering the feed disagrees with the biography built from it.
 */
export function mentionsEntity(record: StoryRecord, entity: number): boolean {
  if (record.participants?.length) return record.participants.includes(entity);
  return record.text.includes(`#${entity}>`);
}

export interface RecordFilter {
  /** Only records about this entity; negative means no entity filter. */
  entity: number;
  /** Only records carrying this tag; empty means no tag filter. */
  tag: string;
  /** Events the user has switched off in the sidebar. */
  hiddenActionIds: ReadonlySet<number>;
}

/** The records left after every active filter. Order is preserved: the chronicle reads front to back. */
export function visibleRecords(
  records: readonly StoryRecord[],
  { entity, tag, hiddenActionIds }: RecordFilter,
): StoryRecord[] {
  return records.filter(
    (r) =>
      (entity < 0 || mentionsEntity(r, entity)) &&
      (tag === '' || (r.tags?.includes(tag) ?? false)) &&
      !hiddenActionIds.has(r.actionId),
  );
}
