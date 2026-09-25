import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { type MoiraiEngine, WasmApi } from './wasm-api';
import { type Message, MessageType } from './types';

/**
 * A stand-in for the compiled engine. What is worth testing on this side is not the simulation — that is
 * covered in C# and by `yarn wasm:smoke` — but the host's own behaviour: that a long pass is broken into
 * chunks with a yield between them, that records arrive while it runs rather than after, that no feed
 * message is lost before something subscribes, and that a world survives a page load.
 */
class FakeEngine implements MoiraiEngine {
  year = 764;
  seed = 42;
  loadedWith: { story: string; seed: string } | null = null;
  readonly calls: string[] = [];
  readonly chunks: number[] = [];
  /** Batches to hand out, one per StreamTick, on top of the year heartbeat. */
  feed: Message[][] = [];
  passYearsThrows = false;
  /** Cursors the host has asked the feed for. A story change has to send it back to zero. */
  readonly cursors: number[] = [];
  /** Whether SetStory reports the story as having parsed. */
  storyApplies = true;
  /** Stories ValidateStory reports an error for. */
  readonly broken = new Set<string>();
  /** Stories whose @start events throw, so building the world fails. */
  readonly throwsOnLoad = new Set<string>();
  /** Stories whose world throws when it is first passed. */
  readonly throwsOnPass = new Set<string>();

  Load(story: string, seed: string) {
    if (this.throwsOnLoad.has(story)) throw new Error('path link is not a ref');
    this.loadedWith = { story, seed };
    this.seed = Number(seed);
  }

  Invoke(method: string, argsJson: string): string {
    const args = JSON.parse(argsJson) as unknown[];
    this.calls.push(`${method}(${args.join(',')})`);
    if (method === 'GetSeed') return JSON.stringify(this.seed);
    if (method === 'GetStory') return JSON.stringify('event a {}');
    if (method === 'ValidateStory')
      return JSON.stringify(
        this.broken.has(String(args[0]))
          ? [
              {
                severity: 'Error',
                code: 'Parser',
                line: 3,
                col: 0,
                lineEnd: 3,
                colEnd: 1,
                message: 'oops',
              },
            ]
          : [],
      );
    if (method === 'SetStory')
      return JSON.stringify({ applied: this.storyApplies, year: 764, diagnostics: [] });
    if (method === 'Reset') {
      this.year = 764;
      return JSON.stringify(this.year);
    }
    if (method === 'Reseed') {
      this.seed = Number(args[0]);
      this.year = 764;
      return JSON.stringify(this.year);
    }
    return JSON.stringify({ method, args });
  }

  PassYears(years: number): string {
    if (this.passYearsThrows) throw new Error('engine threw');
    if (this.loadedWith && this.throwsOnPass.has(this.loadedWith.story))
      throw new Error('missing Time entity');
    this.chunks.push(years);
    this.year += years;
    return JSON.stringify(this.year);
  }

  StreamTick(cursor: number): string {
    this.cursors.push(cursor);
    const queued = this.feed.shift() ?? [];
    // The real DrainFeed always closes a tick with the year heartbeat.
    const messages: Message[] = [
      ...queued,
      { type: MessageType.Year, record: null, year: this.year },
    ];
    return JSON.stringify({ cursor: cursor + 1, messages });
  }
}

const record = (text: string): Message => ({
  type: MessageType.Record,
  record: {
    text,
    changesetId: 1,
    actionId: 1,
    year: 800,
    participants: [],
    tags: null,
    weight: 1,
    firing: 0,
    rule: null,
  },
  year: 0,
});

/** A session-storage stub, since the host remembers the world it is on. */
const store = new Map<string, string>();
/** Local storage is where an edited story lives — a longer life than the world's, on purpose. */
const local = new Map<string, string>();
beforeEach(() => {
  store.clear();
  local.clear();
  vi.stubGlobal('window', {
    location: { origin: 'http://localhost', search: '' },
    sessionStorage: {
      getItem: (k: string) => store.get(k) ?? null,
      setItem: (k: string, v: string) => void store.set(k, v),
    },
    localStorage: {
      getItem: (k: string) => local.get(k) ?? null,
      setItem: (k: string, v: string) => void local.set(k, v),
      removeItem: (k: string) => void local.delete(k),
    },
  });
});
afterEach(() => vi.unstubAllGlobals());

const make = () => {
  const engine = new FakeEngine();
  return { engine, start: () => WasmApi.fromEngine(engine) };
};

/** Subscribe, keeping only the messages a viewer renders — the year heartbeat is noise here. */
const collect = (api: WasmApi) => {
  const seen: Message[] = [];
  const sub = api.streamRecords().subscribe({
    next: (m) => {
      if (m.type !== MessageType.Year) seen.push(m);
    },
    error: () => {},
    complete: () => {},
  });
  return { seen, sub };
};

const runToCompletion = async (api: WasmApi, years: number) => {
  const complete = vi.fn();
  api.passYears(years).subscribe({ next: () => {}, error: () => {}, complete });
  await vi.waitFor(() => expect(complete).toHaveBeenCalled());
};

describe('WasmApi method dispatch', () => {
  it('sends the method name and positional args the C# dispatcher expects', async () => {
    const { engine, start } = make();
    const api = start();

    await api.getPropertySeries(3, 'prosperity');
    await api.getFamilyTree(7, 2);
    await api.getBiography(9);

    expect(engine.calls).toEqual([
      'GetPropertySeries(3,prosperity)',
      'GetFamilyTree(7,2)',
      'GetBiography(9)',
    ]);
  });

  it('parses the engine’s JSON reply', async () => {
    const { start } = make();
    const result = (await start().query('pick Person $p: (true)')) as unknown as {
      method: string;
      args: unknown[];
    };
    expect(result.method).toBe('Query');
    expect(result.args).toEqual(['pick Person $p: (true)']);
  });
});

describe('WasmApi passYears chunking', () => {
  it('splits a long pass into chunks rather than one blocking call', async () => {
    // The page cannot repaint inside a chunk, so a single 200-year call would freeze the UI for its whole
    // duration. This is the property that keeps the main-thread host usable.
    const { engine, start } = make();
    await runToCompletion(start(), 200);

    expect(engine.chunks.length).toBeGreaterThan(1);
    expect(engine.chunks.reduce((a, b) => a + b, 0)).toBe(200);
  });

  it('simulates exactly the years asked for, never overshooting', async () => {
    const { engine, start } = make();
    await runToCompletion(start(), 60);
    expect(engine.chunks[0]).toBe(5); // the starting size, before anything has been measured
    expect(engine.chunks.reduce((a, b) => a + b, 0)).toBe(60);
  });

  it('shrinks the chunk when years get expensive', async () => {
    // The cost of a year grows with the population, so a size that was fine in an empty world stalls the
    // page once there are thousands of entities. The chunk has to follow the measured cost.
    const { engine, start } = make();
    let now = 0;
    // 4ms per simulated year: a 25-year chunk would block for 100ms, twice the target.
    vi.spyOn(performance, 'now').mockImplementation(() => now);
    const realPass = engine.PassYears.bind(engine);
    engine.PassYears = (y: number) => {
      now += y * 4;
      return realPass(y);
    };

    await runToCompletion(start(), 400);

    expect(engine.chunks[0]).toBe(5);
    // Settles near TARGET_CHUNK_MS / 4ms = 12 years.
    expect(engine.chunks.at(-2)).toBeLessThan(20);
    expect(engine.chunks.reduce((a, b) => a + b, 0)).toBe(400);
    vi.mocked(performance.now).mockRestore();
  });

  it('never exceeds the ceiling, however cheap the years are', async () => {
    const { engine, start } = make();
    await runToCompletion(start(), 20000);
    expect(Math.max(...engine.chunks)).toBeLessThanOrEqual(200);
    expect(engine.chunks.reduce((a, b) => a + b, 0)).toBe(20000);
  });

  it('grows gradually but shrinks at once', async () => {
    // Years get dearer as the population grows, so every measurement describes a cheaper world than the
    // next chunk will run in. Rising slowly keeps the estimate on the safe side of that; falling
    // immediately means one expensive chunk is not followed by another.
    const { engine, start } = make();
    let now = 0;
    let msPerYear = 0.1; // cheap to begin with, so the size climbs
    vi.spyOn(performance, 'now').mockImplementation(() => now);
    const realPass = engine.PassYears.bind(engine);
    engine.PassYears = (y: number) => {
      now += y * msPerYear;
      if (engine.chunks.length === 8) msPerYear = 25; // the world suddenly gets expensive
      return realPass(y);
    };

    await runToCompletion(start(), 600);

    const growth = engine.chunks.slice(1, 8).map((c, i) => c / engine.chunks[i]);
    expect(Math.max(...growth)).toBeLessThanOrEqual(1.5); // never leaps
    // Once a year costs 25ms, the budget only affords two of them.
    expect(engine.chunks.at(-2)).toBeLessThanOrEqual(3);
    vi.mocked(performance.now).mockRestore();
  });

  it('reports rising progress that ends at 100', async () => {
    const { start } = make();
    const seen: number[] = [];
    const complete = vi.fn();

    start()
      .passYears(100)
      .subscribe({ next: (v) => seen.push(v), error: () => {}, complete });
    await vi.waitFor(() => expect(complete).toHaveBeenCalled());

    expect(seen.length).toBeGreaterThan(1);
    expect(seen).toEqual([...seen].sort((a, b) => a - b));
    expect(seen.at(-1)).toBe(100);
  });

  it('stops at the next chunk boundary when disposed', async () => {
    const { engine, start } = make();
    const sub = start()
      .passYears(1000)
      .subscribe({ next: () => {}, error: () => {}, complete: () => {} });

    await vi.waitFor(() => expect(engine.chunks.length).toBeGreaterThan(0));
    sub.dispose();
    const atCancel = engine.chunks.length;
    await new Promise((r) => setTimeout(r, 50));

    expect(engine.chunks.length).toBeLessThanOrEqual(atCancel + 1);
    expect(engine.chunks.reduce((a, b) => a + b, 0)).toBeLessThan(1000);
  });

  it('surfaces an engine failure as a stream error', async () => {
    const { engine, start } = make();
    engine.passYearsThrows = true;
    const error = vi.fn();

    start()
      .passYears(50)
      .subscribe({ next: () => {}, error, complete: () => {} });

    await vi.waitFor(() => expect(error).toHaveBeenCalled());
    expect(error.mock.calls[0][0]).toBeInstanceOf(Error);
  });

  it('delivers records while the pass runs, not only at the end', async () => {
    // Without a feed drain per chunk, a long pass would sit silent and then dump every record at once,
    // which is the opposite of watching a history unfold.
    const { engine, start } = make();
    const api = start();
    const { seen } = collect(api);

    engine.feed = [[record('during 1')], [record('during 2')]];
    await runToCompletion(api, 60);

    expect(seen.map((m) => m.record?.text)).toEqual(['during 1', 'during 2']);
  });
});

describe('WasmApi record feed', () => {
  it('replays messages that arrived before anything subscribed', () => {
    // The feed starts as soon as the world exists, and its first tick is the one carrying the reset
    // notice and every @start record. Losing it would leave the records page silently empty.
    const { engine, start } = make();
    engine.feed = [[{ type: MessageType.Reset, record: null, year: 0 }, record('a world begins')]];

    const { seen } = collect(start());

    expect(seen.map((m) => m.type)).toEqual([MessageType.Reset, MessageType.Record]);
    expect(seen[1].record?.text).toBe('a world begins');
  });

  it('delivers the backlog once, not on every subscribe', () => {
    const { engine, start } = make();
    engine.feed = [[record('once')]];
    const api = start();

    const first = collect(api);
    first.sub.dispose();
    const second = collect(api);

    expect(first.seen).toHaveLength(1);
    expect(second.seen).toHaveLength(0);
  });

  it('advances the cursor so a record is never delivered twice', async () => {
    const { engine, start } = make();
    const api = start();
    const { seen } = collect(api);

    engine.feed = [[record('a')], [], [record('b')]];
    await runToCompletion(api, 75);

    expect(seen.map((m) => m.record?.text)).toEqual(['a', 'b']);
  });

  it('stops delivering after the subscription is disposed', async () => {
    const { engine, start } = make();
    const api = start();
    const { seen, sub } = collect(api);
    sub.dispose();

    engine.feed = [[record('ignored')]];
    await runToCompletion(api, 25);

    expect(seen).toHaveLength(0);
  });

  it('keeps running when a feed tick throws', () => {
    const { engine, start } = make();
    engine.StreamTick = () => {
      throw new Error('tick blew up');
    };
    const spy = vi.spyOn(console, 'error').mockImplementation(() => {});

    expect(() => start()).not.toThrow();
    expect(spy).toHaveBeenCalled();
    spy.mockRestore();
  });
});

describe('WasmApi opening year', () => {
  // Where a world's identity lives is the URL's business now (see world-address.ts); what belongs here
  // is what this host does with the year it is handed.

  it('simulates to the year it was asked for', async () => {
    const { engine, start } = make();
    await start().openTo(900);

    expect(engine.year).toBe(900);
  });

  it('gives a link with no year two centuries of history, rather than an empty world', async () => {
    // A world at its start year has no records in it at all. Arriving there asks a visitor to work out
    // that they must press a button before anything exists.
    const { engine, start } = make();
    await start().openTo(null);

    expect(engine.year).toBe(964); // the fake starts at 764
  });

  it('opens in chunks, so a page load does not lock up while it catches up', async () => {
    const { engine, start } = make();
    await start().openTo(1064);

    expect(engine.chunks.length).toBeGreaterThan(1);
    expect(engine.chunks.reduce((a, b) => a + b, 0)).toBe(300);
  });

  it('does nothing when the world is already past that year', async () => {
    // A link can name a year behind where the story starts; the simulation has no reverse gear, and
    // asking for one should be a no-op rather than an error.
    const { engine, start } = make();
    await start().openTo(700);

    expect(engine.year).toBe(764);
    expect(engine.chunks).toEqual([]);
  });
});

describe('editing the story', () => {
  it('dispatches to the session methods by name', async () => {
    const { engine, start } = make();
    const api = start();

    await api.story.get();
    await api.story.validate('event a {}');

    expect(engine.calls).toEqual(['GetStory()', 'ValidateStory(event a {})']);
  });

  it('restarts the feed after a story is applied', async () => {
    // A new story is a new world with no records in it, so the cursor has to go back to zero. Left
    // where it was, every record the fresh world produces before it catches up is skipped — the feed
    // simply stays empty, with nothing to say why.
    const { engine, start } = make();
    const api = start();
    await runToCompletion(api, 100);
    expect(engine.cursors.at(-1)).toBeGreaterThan(0);

    const before = engine.cursors.length;
    await api.story.apply('event a {}');

    // The next tick of the feed, not the apply itself: what matters is that the poll goes back to the
    // start, so the new world's records are read rather than skipped.
    await vi.waitFor(() => expect(engine.cursors.slice(before)).toContain(0), { timeout: 3000 });
  });

  it('keeps an applied story, so a reload rebuilds the world you were looking at', async () => {
    const { start } = make();
    await start().story.apply('event edited {}');

    expect(local.get('moirai.story.applied')).toBe('event edited {}');
  });

  it('keeps nothing when the story did not parse', async () => {
    const { engine, start } = make();
    engine.storyApplies = false;
    const api = start();
    await runToCompletion(api, 100);

    const result = await api.story.apply('event broken {');

    expect(result.applied).toBe(false);
    expect(local.has('moirai.story.applied')).toBe(false);
    // The world is untouched: no records were thrown away, so the feed cursor must not have moved.
    expect(engine.cursors.at(-1)).toBeGreaterThan(0);
  });
});

describe('WasmApi.boot', () => {
  const opts = (story: string, fromLink = false) => ({
    story,
    shipped: 'event shipped {}',
    seed: '42',
    year: 800,
    fromLink,
  });

  it('builds the story it is given when it is sound', async () => {
    const engine = new FakeEngine();
    const api = await WasmApi.boot(engine, opts('event mine {}'));

    expect(engine.loadedWith?.story).toBe('event mine {}');
    expect(api.bootNotice).toBeNull();
  });

  it('falls back to the shipped story when the stored one does not parse, and forgets it', async () => {
    const engine = new FakeEngine();
    engine.broken.add('event half {');
    local.set('moirai.story.applied', 'event half {');
    local.set('moirai.story', 'event newer_edit {');

    const api = await WasmApi.boot(engine, opts('event half {'));

    expect(engine.loadedWith?.story).toBe('event shipped {}');
    expect(api.bootNotice).toMatch(/story you last applied does not parse \(line 3: oops\)/);
    // The next load must not trip over it again, but the edit is still there to fix.
    expect(local.has('moirai.story.applied')).toBe(false);
    // An edit already in progress is newer than what failed, so it stays.
    expect(local.get('moirai.story')).toBe('event newer_edit {');
  });

  it('makes the failed story the draft when there is none, so there is something to fix', async () => {
    const engine = new FakeEngine();
    engine.broken.add('event half {');

    await WasmApi.boot(engine, opts('event half {'));

    expect(local.get('moirai.story')).toBe('event half {');
  });

  it('falls back when the world throws on its way up, and still opens at the year asked for', async () => {
    const engine = new FakeEngine();
    engine.throwsOnPass.add('event no_clock {}');

    const api = await WasmApi.boot(engine, opts('event no_clock {}'));

    expect(engine.loadedWith?.story).toBe('event shipped {}');
    expect(api.bootNotice).toMatch(/stopped the world from starting \(missing Time entity\)/);
    expect(engine.year).toBe(800);
  });

  it('falls back when the story throws while it is built, before anything is passed', async () => {
    const engine = new FakeEngine();
    engine.throwsOnLoad.add('event null_path {}');

    const api = await WasmApi.boot(engine, opts('event null_path {}', true));

    expect(engine.loadedWith?.story).toBe('event shipped {}');
    expect(api.bootNotice).toMatch(
      /story in this link stopped the world from starting \(path link/,
    );
    expect(local.get('moirai.story')).toBe('event null_path {}');
  });

  it('keeps a linked story only once it has built a world', async () => {
    const good = new FakeEngine();
    await WasmApi.boot(good, opts('event linked {}', true));
    expect(local.get('moirai.story.applied')).toBe('event linked {}');

    local.clear();
    const bad = new FakeEngine();
    bad.broken.add('event linked_broken {');
    const api = await WasmApi.boot(bad, opts('event linked_broken {', true));
    expect(api.bootNotice).toMatch(/story in this link/);
    expect(local.has('moirai.story.applied')).toBe(false);
  });

  it('lets a failure of the shipped story itself surface, since there is nothing to fall back to', async () => {
    const engine = new FakeEngine();
    engine.throwsOnPass.add('event shipped {}');
    await expect(WasmApi.boot(engine, opts('event shipped {}'))).rejects.toThrow(
      'missing Time entity',
    );
  });
});
