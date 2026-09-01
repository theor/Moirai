import type {
  Biography,
  ClientData,
  EntityChangeDisplay,
  EntityPropertyDisplay,
  FamilyTreeNode,
  Message,
  QueryResult,
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
  getEntityDetails(entityId: number): Promise<EntityPropertyDisplay[]>;
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
