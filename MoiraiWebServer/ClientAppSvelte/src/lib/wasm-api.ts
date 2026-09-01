import type {
  MoiraiApi,
  MoiraiApiHandle,
  MoiraiStream,
  MoiraiStreamSubscriber,
  MoiraiSubscription,
  StoryEditor,
} from './api';
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
import { storedStory, storeStory } from './story-storage';
import { base } from '$app/paths';

/** How often the engine is asked for new records. Matches the server's feed cadence. */
const FEED_INTERVAL_MS = 500;

/**
 * How long a single chunk should aim to take. The page cannot repaint mid-chunk, so this is the worst
 * stall a pass can impose; three frames is short enough to feel like work in progress rather than a
 * freeze, and long enough that the per-chunk marshalling cost stays negligible.
 */
const TARGET_CHUNK_MS = 50;

/**
 * Where the chunk size starts, before anything has been measured. Deliberately small: the first chunk of
 * a pass is the one guess we make blind, and guessing high is what produces a visible hitch at the start
 * of a pass over a large world. The measurement ramps it up within two or three chunks, and the learned
 * size then persists for the session.
 */
const INITIAL_CHUNK_YEARS = 5;

/** Bounds on the chunk size. One year is the floor; the ceiling stops a cheap world overshooting. */
const MIN_CHUNK_YEARS = 1;
const MAX_CHUNK_YEARS = 200;

/** The exports of Moirai.Wasm's MoiraiInterop, all crossing as JSON strings. */
export interface MoiraiEngine {
  Load(storyText: string, seed: string): void;
  Invoke(method: string, argsJson: string): string;
  PassYears(years: number): string;
  StreamTick(cursor: number): string;
}

/** Yield to the browser so it can paint and handle input before the next chunk. */
const yieldToBrowser = () => new Promise<void>((resolve) => setTimeout(resolve, 0));

/**
 * Where the identity of the current world is kept so it survives a page load.
 *
 * The nav links do real navigation, so switching tabs is a fresh document — and with the engine living in
 * the page rather than on a server, that would otherwise throw the world away and drop you back at year
 * one. Nothing needs to be serialized to avoid that: a Moirai world is entirely determined by its story,
 * its seed and its year, so remembering seed and year is enough to rebuild the identical world. That is
 * the same trick the server uses to restore a world after the story file changes on disk.
 */
const WORLD_KEY = 'moirai.wasm.world';

type RememberedWorld = { seed: string; year: number };

function rememberWorld(world: RememberedWorld) {
  try {
    window.sessionStorage.setItem(WORLD_KEY, JSON.stringify(world));
  } catch {
    // Private browsing can refuse storage. The world simply will not survive a navigation.
  }
}

function rememberedWorld(): RememberedWorld | null {
  try {
    const raw = window.sessionStorage.getItem(WORLD_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as RememberedWorld;
    return typeof parsed?.seed === 'string' && Number.isFinite(parsed?.year) ? parsed : null;
  } catch {
    return null;
  }
}

/**
 * {@link MoiraiApi} backed by the engine compiled to WebAssembly. No server involved: the world is built
 * in the browser from a story fetched as a static asset.
 *
 * **Runs on the main thread**, which is a constraint rather than a choice — the single-threaded .NET
 * runtime only finishes initialising there. In a dedicated worker it downloads every assembly, reaches
 * `preInit`, and then never completes, with nothing thrown to explain it. So instead of moving the work
 * off the page, `passYears` breaks it into chunks and yields between them; the engine's determinism is
 * unaffected, because the RNG streams live on the execution context and the clock is re-read from the
 * world each call.
 */
export class WasmApi implements MoiraiApi {
  private readonly engine: MoiraiEngine;
  private feedSubscriber: MoiraiStreamSubscriber<Message> | null = null;
  // Feed messages that arrived before anything subscribed. The timer starts as soon as the world exists,
  // but the store only subscribes once make() has returned — and the first tick is the one carrying the
  // reset notice and every @start record, so dropping it would start the records feed silently empty.
  private feedBacklog: Message[] = [];
  private cursor = 0;
  private feedTimer: ReturnType<typeof setInterval> | null = null;
  /**
   * Years per chunk, adjusted from how long the last one actually took.
   *
   * A fixed size does not hold up: the cost of a year grows with the population, so a chunk that was
   * 20 ms in an empty world was over 400 ms once a few thousand entities existed. Carried across passes,
   * so the rate learned from one pass benefits the next.
   */
  private chunkYears = INITIAL_CHUNK_YEARS;

  /**
   * The story the build shipped, kept so "revert" does not need a second fetch — and so it is still
   * available after an edit has replaced the world's story.
   */
  private shippedStory = '';

  private constructor(engine: MoiraiEngine) {
    this.engine = engine;
  }

  /**
   * Editing the story, which this backend can offer because the story is simply a string it holds.
   * `apply` goes through the engine, which refuses anything that does not parse, so a broken edit costs
   * you nothing but the squiggles.
   */
  readonly story: StoryEditor = {
    get: async () => this.invoke<string>('GetStory'),
    original: async () => this.shippedStory,
    validate: async (text: string) => this.invoke<StoryDiagnostic[]>('ValidateStory', text),
    apply: async (text: string) => {
      const result = this.invoke<StoryApplyResult>('SetStory', text);
      if (result.applied) {
        // A fresh world: the records are gone, so the feed has to start from the beginning again, and
        // the remembered year is now the new story's start year rather than wherever we had got to.
        this.cursor = 0;
        storeStory(text);
        this.remember();
      }
      return result;
    },
  };

  /** Build a session around an already-booted engine. The seam tests drive a fake through. */
  static fromEngine(engine: MoiraiEngine): WasmApi {
    const api = new WasmApi(engine);
    api.startFeed();
    return api;
  }

  /**
   * `base` is the deployment's path prefix — empty when served from the root (dev, the .NET host), and
   * `/Moirai` on the GitHub Pages project site. Both URLs here point at files in `static/`, which no
   * bundler rewrites, so they are the two places a prefixed deployment would otherwise 404: the runtime
   * would never load, and the story would never be fetched.
   */
  static async make(storyUrl = `${base}/w.sg`, defaultSeed = '42'): Promise<MoiraiApiHandle> {
    // Hidden from the bundler on purpose. `@vite-ignore` is not enough: in dev, Vite wraps a statically
    // visible dynamic import in `injectQuery(url, 'import')`, which routes the .NET runtime through
    // Vite's JavaScript transform instead of serving it verbatim out of `static/`. Going through
    // `Function` keeps the specifier opaque so the browser fetches the published files untouched.
    const importRuntime = new Function('url', 'return import(url)') as (
      url: string,
    ) => Promise<{ boot(): Promise<MoiraiEngine> }>;

    const { boot } = await importRuntime(
      new URL(`${base}/_framework/main.js`, window.location.origin).href,
    );
    const engine = await boot();

    const shipped = await fetch(storyUrl).then((r) => {
      if (!r.ok) {
        throw new Error(
          `Could not fetch the story from ${storyUrl} (${r.status}). Did you run \`yarn wasm:build\`?`,
        );
      }
      return r.text();
    });
    // An edited story outranks the shipped one: it is what the last world here was built from, and
    // rebuilding from `w.sg` instead would quietly throw the edit away on the next reload.
    const story = storedStory() ?? shipped;
    // Rebuild the world we were on, if there was one, rather than starting a new one.
    const previous = rememberedWorld();
    const seed = previous?.seed ?? defaultSeed;
    engine.Load(story, seed);

    const api = WasmApi.fromEngine(engine);
    api.shippedStory = shipped;
    if (previous) await api.fastForwardTo(previous.year);
    api.remember();

    return { api, clientData: api.invoke<ClientData>('GetClientData'), connected: true };
  }

  /**
   * Simulate up to <paramref name="year"/>, in chunks so a long catch-up does not lock the page. The
   * result is byte-identical to however the world originally reached that year, because the simulation is
   * deterministic per seed.
   */
  private async fastForwardTo(year: number) {
    let current = this.currentYear();
    while (current < year) {
      const chunk = Math.min(this.chunkYears, year - current);
      const started = performance.now();
      current = JSON.parse(this.engine.PassYears(chunk)) as number;
      this.adjustChunkSize(chunk, performance.now() - started);
      await yieldToBrowser();
    }
  }

  private currentYear(): number {
    // DrainFeed always closes with the year heartbeat, and asking for the tick we have already seen costs
    // nothing, so this reads the clock without needing a method of its own.
    const tick = JSON.parse(this.engine.StreamTick(this.cursor)) as {
      cursor: number;
      messages: Message[];
    };
    this.cursor = tick.cursor;
    for (const m of tick.messages) {
      if (this.feedSubscriber === null) this.feedBacklog.push(m);
      else this.feedSubscriber.next(m);
    }
    return tick.messages.at(-1)?.year ?? 0;
  }

  /** Record the world's identity so the next page load can rebuild it. */
  private remember() {
    rememberWorld({ seed: String(this.invoke<number>('GetSeed')), year: this.currentYear() });
  }

  // An in-browser engine is either there or the page is broken, so liveness never changes.
  onConnectedChanged(_handler: (connected: boolean) => void) {}

  /** The WASM twin of SignalR's `invoke`, which is what keeps the two backends this similar. */
  private invoke<T>(method: string, ...args: unknown[]): T {
    return JSON.parse(this.engine.Invoke(method, JSON.stringify(args))) as T;
  }

  // Every read is synchronous here, but the interface is async because the SignalR backend's is. Async
  // also keeps a slow call (a query over a large world) from being mistaken for a cheap one at the call
  // site, and leaves room to move the work if the runtime ever supports a worker.
  private async invokeAsync<T>(method: string, ...args: unknown[]): Promise<T> {
    return this.invoke<T>(method, ...args);
  }

  async reset(): Promise<number> {
    const year = await this.invokeAsync<number>('Reset');
    this.remember();
    return year;
  }

  async reseed(seed: number): Promise<number> {
    const year = await this.invokeAsync<number>('Reseed', seed);
    this.remember();
    return year;
  }

  async runAction(actionId: number): Promise<void> {
    await this.invokeAsync('RunAction', actionId);
  }

  async save(): Promise<void> {
    await this.invokeAsync('Save');
  }

  getClientData(): Promise<ClientData> {
    return this.invokeAsync<ClientData>('GetClientData');
  }

  query(q: string): Promise<QueryResult> {
    return this.invokeAsync<QueryResult>('Query', q);
  }

  getBiography(entityId: number): Promise<Biography> {
    return this.invokeAsync<Biography>('GetBiography', entityId);
  }

  getWorldOverview(): Promise<WorldOverview> {
    return this.invokeAsync<WorldOverview>('GetWorldOverview');
  }

  getPropertySeries(typeId: number, propertyName: string): Promise<TimeSeries> {
    return this.invokeAsync<TimeSeries>('GetPropertySeries', typeId, propertyName);
  }

  getRuleCoverage(): Promise<RuleCoverageReport> {
    return this.invokeAsync<RuleCoverageReport>('GetRuleCoverage');
  }

  getEntityDetails(entityId: number): Promise<EntityPropertyDisplay[]> {
    return this.invokeAsync<EntityPropertyDisplay[]>('GetEntityDetails', entityId);
  }

  getFamilyTree(entityId: number, maxDepth: number): Promise<FamilyTreeNode[]> {
    return this.invokeAsync<FamilyTreeNode[]>('GetFamilyTree', entityId, maxDepth);
  }

  getChangesets(start: number, count: number): Promise<EntityChangeDisplay[]> {
    return this.invokeAsync<EntityChangeDisplay[]>('GetChangesets', start, count);
  }

  getEntityChangesets(entityId: number): Promise<EntityChangeDisplay[]> {
    return this.invokeAsync<EntityChangeDisplay[]>('GetEntityChangesets', entityId);
  }

  /**
   * Simulate forward in chunks, yielding between them so the page keeps painting. Progress is the
   * fraction of years done — the same granularity the server streams, since it reports every ten years.
   */
  passYears(years: number): MoiraiStream<number> {
    return {
      subscribe: (subscriber: MoiraiStreamSubscriber<number>): MoiraiSubscription => {
        let cancelled = false;

        void (async () => {
          try {
            let done = 0;
            let sinceRemembered = 0;
            while (done < years && !cancelled) {
              const chunk = Math.min(this.chunkYears, years - done);
              const started = performance.now();

              this.engine.PassYears(chunk);
              // Drained here too, not just on the timer, so records appear as the pass runs rather than
              // in one dump at the end.
              this.tickFeed();

              // Timed around both: the drain's cost is proportional to the records produced, which is
              // proportional to the years simulated, so it belongs inside the budget. Measuring only the
              // simulation left the drain outside it, and the page blocked for roughly twice the target.
              this.adjustChunkSize(chunk, performance.now() - started);

              done += chunk;
              sinceRemembered += chunk;
              subscriber.next(Math.round((100 * done) / years));
              if (sinceRemembered >= 100) {
                this.remember();
                sinceRemembered = 0;
              }
              await yieldToBrowser();
            }
            // Remembered per chunk, not just at the end: a tab switch mid-pass should land you where the
            // simulation had actually got to.
            this.remember();
            subscriber.complete();
          } catch (err) {
            subscriber.error(err);
          }
        })();

        // Cancellation stops at the next chunk boundary — the engine has no mid-pass cancellation, and
        // a chunk is short enough that waiting for one to finish is not noticeable.
        return {
          dispose: () => {
            cancelled = true;
          },
        };
      },
    };
  }

  streamRecords(): MoiraiStream<Message> {
    return {
      subscribe: (subscriber: MoiraiStreamSubscriber<Message>): MoiraiSubscription => {
        this.feedSubscriber = subscriber;
        const backlog = this.feedBacklog;
        this.feedBacklog = [];
        for (const m of backlog) subscriber.next(m);
        return {
          dispose: () => {
            this.feedSubscriber = null;
          },
        };
      },
    };
  }

  /**
   * Aim the next chunk at {@link TARGET_CHUNK_MS}, based on what this one cost per year.
   *
   * Growth is capped but shrinking is not, and that asymmetry is the point. A year gets more expensive as
   * the population grows, so every measurement is of a cheaper world than the next chunk will run in —
   * scale up freely on an early, cheap measurement and each chunk then overshoots the budget. Rising
   * slowly and falling immediately keeps the estimate on the safe side of a moving cost.
   */
  private adjustChunkSize(years: number, elapsedMs: number) {
    // A chunk too fast to measure says nothing except "try more".
    const perYear = elapsedMs / years;
    const ideal = perYear > 0 ? TARGET_CHUNK_MS / perYear : this.chunkYears * 2;
    const ceiling = Math.min(MAX_CHUNK_YEARS, this.chunkYears * 1.25 + 1);
    this.chunkYears = Math.max(MIN_CHUNK_YEARS, Math.round(Math.min(ideal, ceiling)));
  }

  /** Stop polling the engine. Nothing calls this yet; the store keeps one session for the page's life. */
  dispose() {
    if (this.feedTimer !== null) clearInterval(this.feedTimer);
    this.feedTimer = null;
  }

  private startFeed() {
    this.tickFeed();
    this.feedTimer = setInterval(() => this.tickFeed(), FEED_INTERVAL_MS);
  }

  private tickFeed() {
    try {
      const tick = JSON.parse(this.engine.StreamTick(this.cursor)) as {
        cursor: number;
        messages: Message[];
      };
      this.cursor = tick.cursor;
      if (this.feedSubscriber === null) this.feedBacklog.push(...tick.messages);
      else for (const m of tick.messages) this.feedSubscriber.next(m);
    } catch (err) {
      console.error('Moirai feed tick failed', err);
    }
  }
}
