export type GetSetProperty<T> = [T, (t: T) => void];

export interface Record {
  text: string;
  changesetId: number;
  actionId: number;
  year: number;
  participants: number[];
  tags: string[] | null;
  /** How much the record matters, from `record('…', weight)`: 1 by default, 0 for noise, more for a turning point. */
  weight: number;
  /** The run of the rule that wrote it, for asking why (`getCause`). 0 when no rule did. */
  firing: number;
  /** The rule that wrote it: the trigger itself, where `actionId` is the event it belongs to. */
  rule: string | null;
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
  /** For a record, the run of the rule that wrote it (see `getCause`); 0 for a change. */
  firing: number;
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
  /** Year of birth (`birthdate`, else the year the entity was created); 0 when unknown. */
  born: number;
  /** Year of death (`deathdate`); 0 when unknown or alive. */
  died: number;
  /** Dead, even when the story did not record the year. */
  dead: boolean;
  /** The `partner` reference, or 0. */
  partner: number;
}

export interface EntityChangeDisplay {
  id: number;
  year: number;
  actionName: string;
  changes: EntityPropertyDisplay[];
}

// --- editing the story ---------------------------------------------------------------------------
// Mirrors Moirai.Api's StoryDiagnostic / StoryApplyResult.

/**
 * One thing the parser has to say about a story.
 *
 * The coordinates are the engine's own and are **1-based line, 0-based column** — the convention
 * `StoryParser.Error` inherited from ANTLR. `$lib/diagnostics` is the one place that converts them.
 */
export interface StoryDiagnostic {
  severity: 'Error' | 'Warning' | 'Information';
  code: string;
  line: number;
  col: number;
  lineEnd: number;
  colEnd: number;
  message: string;
}

/** What came of applying a story. When `applied` is false the world is untouched and `year` is where it still stands. */
export interface StoryApplyResult {
  applied: boolean;
  year: number;
  diagnostics: StoryDiagnostic[];
}

/** Mirrors Moirai.Api's NotableEntity: one entity and how many records mention it. */
export interface NotableEntity {
  id: number;
  name: string;
  mentions: number;
  firstYear: number;
  lastYear: number;
}

/** Mirrors Moirai.Api's NotableGroup: the most mentioned entities of one type. */
export interface NotableGroup {
  typeId: number;
  typeName: string;
  /** The type declares parent1/parent2, so its entities have a family tree. */
  hasFamily: boolean;
  top: NotableEntity[];
}

// --- the chronicle: the world on one card --------------------------------------------------------
// These mirror Moirai.Api's Chronicle / ChronicleEra / ChronicleEntry / ChronicleTag.

/** A named period: an entity whose type declares start_year and end_year. The open one is the present. */
export interface ChronicleEra {
  id: number;
  name: string;
  start: number;
  end: number;
  open: boolean;
}

export interface ChronicleEntry {
  year: number;
  changesetId: number;
  text: string;
  weight: number;
  tags: string[];
}

export interface ChronicleTag {
  tag: string;
  records: number;
}

export interface Chronicle {
  startYear: number;
  year: number;
  records: number;
  population: TimeSeries;
  eras: ChronicleEra[];
  turningPoints: ChronicleEntry[];
  tags: ChronicleTag[];
  /** The story weighs its records. When false, the turning points are only an even sample. */
  weighted: boolean;
}

// --- why a record happened -----------------------------------------------------------------------
// These mirror Moirai.Api's Cause / CauseStep.

/** One rule that ran, on the way from a record back to the event the schedule started. */
export interface CauseStep {
  firing: number;
  rule: string;
  kind: 'event' | 'call' | 'trigger' | 'scheduled';
  /** 1-based line of the rule in the story; 0 when unknown. */
  line: number;
  year: number;
  /** What set a trigger off, in record markup: `<#12>Aldric</>: alive true -> false`. Empty otherwise. */
  because: string;
  /** What that rule wrote. */
  records: string[];
}

/** Nearest first: the rule that wrote the record, then what it ran inside, back to the root. */
export interface Cause {
  steps: CauseStep[];
}
