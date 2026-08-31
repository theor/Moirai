export type GetSetProperty<T> = [T, (t: T) => void];

export interface Record {
  text: string;
  changesetId: number;
  actionId: number;
  year: number;
  participants: number[];
  tags: string[] | null;
}

export interface Message {
  type: MessageType;
  record: Record | null;
  year: number;
}

export enum MessageType {
  Reset = 'Reset',
  Record = 'Record',
  Year = 'Year',
}

export type EntityPropertyDisplay = { label: string; value: string };

export interface ActionData {
  id: number;
  name: string;
  hidden: boolean;
}
export interface TypeData {
  id: number;
  name: string;
}
export interface ClientData {
  actions: ActionData[];
  types: TypeData[];
  /** Base RNG seed the current world was built from. */
  seed: number;
}

export interface Changeset {
  id: number;
  actionName: string;
  year: number;
  // cats: CategoryId[];
  changes: Changed[];
}

export type PropertyValue = unknown;
export interface Property {
  id: number;
  value: { type: number; value: PropertyValue };
}
export interface Entity {
  id: number;
  type: number;
  properties: Property[];
}
export interface Changed {
  prev: Entity;
  new: Entity;
}

/** One row of the rule-coverage report: how often a rule has fired over the life of the world. */
export interface RuleCoverage {
  id: number;
  name: string;
  kind: 'event' | 'trigger';
  /** For an event its schedule ("@start", "call only", "~1x per 15y"); for a trigger its `when` clause. */
  schedule: string;
  attempts: number;
  successes: number;
  tags: string[];
}

export interface RuleCoverageReport {
  year: number;
  rules: RuleCoverage[];
}

/** A labelled series of samples over simulated years, replayed from the changeset log. */
export interface TimeSeries {
  label: string;
  years: number[];
  values: number[];
}

/** A (type, property) pair the dashboard can plot: bools as a count of true, numbers as a mean. */
export interface ChartableProperty {
  typeId: number;
  typeName: string;
  propertyName: string;
  kind: 'bool' | 'number';
}

export interface WorldOverview {
  year: number;
  entities: number;
  records: number;
  changesets: number;
  series: TimeSeries[];
  properties: ChartableProperty[];
}

/** One moment in an entity's life: a record it appears in, or a changeset that touched it. */
export interface BiographyEntry {
  year: number;
  /** The changeset that produced this entry — orders records and changes against each other. */
  changesetId: number;
  kind: 'record' | 'change';
  text: string;
  actionName: string;
  changes: EntityPropertyDisplay[];
  tags: string[];
}

export interface Biography {
  id: number;
  name: string;
  typeName: string;
  /** The entity's type declares parent1/parent2, so a family tree can be drawn for it. */
  hasFamily: boolean;
  details: EntityPropertyDisplay[];
  timeline: BiographyEntry[];
}

// --- query, genealogy and changeset shapes -------------------------------------------------------
// These mirror Moirai.Api's QueryResult / Result / FamilyTreeNode / EntityChangeDisplay.

export interface Result {
  eid: number;
  properties: EntityPropertyDisplay[];
}

export interface QueryResult {
  sql: string;
  query: string;
  results: Result[];
  errors: string[];
}

/** A node in an entity's genealogy. `p1`/`p2` are parent ids, 0 meaning "none or beyond max depth". */
export interface FamilyTreeNode {
  id: number;
  name: string;
  p1: number;
  p2: number;
}

export interface EntityChangeDisplay {
  id: number;
  year: number;
  actionName: string;
  changes: EntityPropertyDisplay[];
}
