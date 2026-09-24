import type {
  Biography,
  Chronicle,
  ClientData,
  EntityChangeDisplay,
  EntityPropertyDisplay,
  FamilyTreeNode,
  Message,
  QueryResult,
  NotableGroup,
  RuleCoverageReport,
  StoryApplyResult,
  StoryDiagnostic,
  TimeSeries,
  WorldOverview,
} from './types';

/**
 * A stream of values pushed from the engine, and its subscriber.
 *
 * These mirror SignalR's `IStreamResult` / `IStreamSubscriber` on purpose: they are the only SignalR
 * types that reached into the API surface, and declaring our own means the interface below names no
 * transport at all. SignalR's versions satisfy these structurally, so the SignalR implementation needs
 * no adapter.
 */
export interface MoiraiStreamSubscriber<T> {
  next(value: T): void;
  error(err: unknown): void;
  complete(): void;
}

export interface MoiraiSubscription {
  dispose(): void;
}

export interface MoiraiStream<T> {
  subscribe(subscriber: MoiraiStreamSubscriber<T>): MoiraiSubscription;
}

/**
 * Reading and rewriting the story a world is built from.
 *
 * A capability rather than four more methods on {@link MoiraiApi}, because only one backend has it. The
 * in-browser engine holds its story as a string in the page, so editing it is just handing it another
 * one; the server's story is a file on disk that its watcher owns, and a viewer writing to it would be
 * fighting whatever editor is already open on it. Saying that in the type means the UI asks
 * `conn.story !== null` instead of asking which transport it got.
 */
export interface StoryEditor {
  /** The story the current world was built from. */
  get(): Promise<string>;
  /** The story the build shipped, for reverting to. */
  original(): Promise<string>;
  /** What the parser makes of `text`, without touching the world. */
  validate(text: string): Promise<StoryDiagnostic[]>;
  /** Rebuild the world from `text`. A story that does not parse changes nothing. */
  apply(text: string): Promise<StoryApplyResult>;
}

/**
 * Everything the viewer can ask of a world, independent of where that world lives.
 *
 * Two implementations exist: {@link SignalRApi}, which talks to the .NET host over a hub, and
 * {@link WasmApi}, which drives the same engine compiled to WebAssembly in a Web Worker. The methods
 * are named after the server's hub methods and both backends return the same JSON shapes, so a page
 * calling `conn.getBiography(id)` never learns which one it got.
 */
export interface MoiraiApi {
  /** Editing the story, where the backend can offer it. Null on the server, whose story is a file. */
  readonly story: StoryEditor | null;

  /**
   * Whether the world lives in this page, and so belongs in the URL.
   *
   * True for the in-browser engine, where seed and year are the whole identity of a world and a link
   * carrying them rebuilds it exactly. False for the server, which has one world of its own: a link
   * naming a seed and a year would describe nothing the recipient could see.
   */
  readonly worldInPage: boolean;

  /**
   * Why the world on screen is not the one that was asked for, or null. The in-browser engine sets it
   * when a stored or linked story could not be built and it fell back to the shipped one; the server
   * builds its one world from its own file and never does.
   */
  readonly bootNotice: string | null;

  /** Rebuild the world from the story. Returns the year of the fresh world. */
  reset(): Promise<number>;
  /** Rebuild from a different seed. The simulation is deterministic per seed. */
  reseed(seed: number): Promise<number>;
  /** Simulate forward, streaming percentage progress. */
  passYears(years: number): MoiraiStream<number>;
  /** Run one event by id, out of schedule. */
  runAction(actionId: number): Promise<void>;
  save(): Promise<void>;

  /** The record feed: new records, a year heartbeat, and reset notices. */
  streamRecords(): MoiraiStream<Message>;

  /** The startup snapshot: the story's events and types, and the seed. Re-read after a story change. */
  getClientData(): Promise<ClientData>;

  query(q: string): Promise<QueryResult>;
  getBiography(entityId: number): Promise<Biography>;
  getWorldOverview(): Promise<WorldOverview>;
  getPropertySeries(typeId: number, propertyName: string): Promise<TimeSeries>;
  getRuleCoverage(): Promise<RuleCoverageReport>;
  /** Every entity's type id, indexed by entity id (0 = no entity). Colours the entity chips. */
  getEntityTypes(): Promise<number[]>;
  getEntityDetails(entityId: number): Promise<EntityPropertyDisplay[]>;
  /** The entities mentioned in the most records, `perType` of each type, most-mentioned type first. */
  getNotable(perType: number): Promise<NotableGroup[]>;
  /** The world on one card: its span, population, eras and up to `turningPoints` weightiest records. */
  getChronicle(turningPoints: number): Promise<Chronicle | null>;
  /** An entity's properties at the end of `year`: empty before it existed, live details from now on. */
  getEntityAt(entityId: number, year: number): Promise<EntityPropertyDisplay[]>;
  getFamilyTree(entityId: number, maxDepth: number): Promise<FamilyTreeNode[]>;
  getChangesets(start: number, count: number): Promise<EntityChangeDisplay[]>;
  getEntityChangesets(entityId: number): Promise<EntityChangeDisplay[]>;

  /**
   * Called when the connection's liveness changes — SignalR reconnects, or the worker dies. The WASM
   * backend never disconnects, so it simply never calls back.
   */
  onConnectedChanged(handler: (connected: boolean) => void): void;
}

/** What a backend hands back once it is ready to answer. */
export type MoiraiApiHandle = {
  api: MoiraiApi;
  clientData: ClientData;
  connected: boolean;
};
