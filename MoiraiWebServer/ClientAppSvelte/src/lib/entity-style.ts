/**
 * Which colour an entity chip wears.
 *
 * A record links entities by id and name only, and before this every chip was the same pale green, so a
 * person, a god and a country looked alike in a sentence. Colour by type fixes that at a glance.
 *
 * The slot a type gets is the order in which its **first entity** was created. That is fixed once the
 * world has built its first entity of the type, so colours never shuffle as the world grows — ranking by
 * count would repaint the page whenever two types swapped places. It is also deterministic per seed, so a
 * shared link looks the same on the other end. Types past the last slot share the neutral one.
 */

/** Hued slots; see `.ent-*` in app.css. Slot `NEUTRAL` is grey. */
export const ENTITY_SLOTS = 8;
export const NEUTRAL = ENTITY_SLOTS;

/**
 * Map each type id to a slot, from the id → type table (`types[entityId]` is the entity's type id, with
 * 0 meaning no entity).
 */
export function typeSlots(types: readonly number[]): Map<number, number> {
  const slots = new Map<number, number>();
  for (const t of types) {
    if (t === 0 || slots.has(t)) continue;
    slots.set(t, Math.min(slots.size, NEUTRAL));
  }
  return slots;
}

/** The slot for one entity, or `NEUTRAL` if its type is not known yet (it was born after the last fetch). */
export function slotOf(entityId: number, types: readonly number[], slots: Map<number, number>) {
  return slots.get(types[entityId] ?? 0) ?? NEUTRAL;
}
