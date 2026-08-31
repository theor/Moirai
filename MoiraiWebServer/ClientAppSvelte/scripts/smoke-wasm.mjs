// Boots the staged WebAssembly engine under Node and exercises every export against a real story.
//
// Why this exists: the WASM host's failure modes are quiet ones. Trimming can strip the members the
// reflective AST dump walks, leaving an empty JSON object instead of a build error; a wrong
// JsonSerializerOptions renames every property, so the client reads undefined everywhere; and a deferred
// IProgress delivers its reports after the pass instead of during it. None of that surfaces in a C# unit
// test, because none of it involves the browser marshalling layer — and all of it looks like a blank page.
//
// Runs against static/_framework, so it checks exactly what ships. Run `yarn wasm:build` first.
//
//   yarn wasm:smoke
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { boot } from '../static/_framework/main.js';

const here = dirname(fileURLToPath(import.meta.url));
const story = readFileSync(process.argv[2] ?? join(here, '..', 'static', 'w.sg'), 'utf8');
const interop = await boot();

const call = (m, ...args) => JSON.parse(interop.Invoke(m, JSON.stringify(args)));
let failures = 0;
const check = (name, ok, detail = '') => {
  if (!ok) failures++;
  console.log(`${ok ? 'ok  ' : 'FAIL'} ${name}${detail ? ' — ' + detail : ''}`);
};

interop.Load(story, '42');

const cd = call('GetClientData');
check(
  'GetClientData: camelCase fields survive',
  Array.isArray(cd.actions) && Array.isArray(cd.types),
);
check('GetClientData: seed round-trips', cd.seed === 42, `seed=${cd.seed}`);
check('GetClientData: story has events', cd.actions.length > 0, `${cd.actions.length} events`);

// The host simulates in chunks and yields between them, so pin that chunking is equivalent to one long
// call — that equivalence is what lets the main-thread host stay responsive without changing the world it
// produces. (WorldSessionTests asserts the same thing against the engine directly.)
const firstChunk = JSON.parse(interop.PassYears(25));
check('PassYears: returns the year reached', firstChunk === 789, `year=${firstChunk}`);
for (let i = 0; i < 3; i++) interop.PassYears(25);
const afterChunks = JSON.parse(interop.PassYears(20));
check(
  'PassYears: 25+25+25+25+20 lands on the same year as one 120y pass',
  afterChunks === 884,
  `year=${afterChunks}`,
);

const tick = JSON.parse(interop.StreamTick(0));
check(
  'StreamTick: string enums, not numbers',
  tick.messages[0].type === 'Reset',
  `first=${tick.messages[0].type}`,
);
check('StreamTick: year heartbeat closes the batch', tick.messages.at(-1).type === 'Year');
const records = tick.messages.filter((m) => m.type === 'Record');
check('StreamTick: records delivered', records.length > 0, `${records.length} records`);
check(
  'StreamTick: record shape intact',
  typeof records[0].record.text === 'string' && Array.isArray(records[0].record.participants),
);

const overview = call('GetWorldOverview');
check(
  'GetWorldOverview: counts present',
  overview.entities > 0 && overview.records > 0,
  `${overview.entities} entities, year ${overview.year}`,
);
check(
  'GetWorldOverview: series present',
  overview.series.length > 0 && overview.properties.length > 0,
);

const series = call(
  'GetPropertySeries',
  overview.properties[0].typeId,
  overview.properties[0].propertyName,
);
check(
  'GetPropertySeries: aligned arrays',
  series.years.length === series.values.length,
  `${series.years.length} points`,
);

const rules = call('GetRuleCoverage');
check(
  'GetRuleCoverage: every rule listed',
  rules.rules.length > 0 && rules.rules.every((r) => r.schedule),
);

// The reflective AST dump is the trim-sensitive one: partial trimming must have left our types whole.
const q = call('Query', 'pick Person $p: ($p.alive)');
check('Query: no errors', !q.errors || q.errors.length === 0, JSON.stringify(q.errors));
check('Query: results returned', q.results?.length > 0, `${q.results?.length} rows`);
check(
  'Query: AST dump is not empty (trimming intact)',
  q.query && q.query.length > 20 && q.query !== '{}',
  `${(q.query || '').length} chars`,
);
check('Query: row properties lead with Type', q.results?.[0]?.properties?.[0]?.label === 'Type');

const eid = q.results[0].eid;
check('EntityId collapses to a number', typeof eid === 'number', `eid=${eid}`);

const details = call('GetEntityDetails', eid);
check('GetEntityDetails: rows returned', details.length > 0, `${details.length} rows`);

const bio = call('GetBiography', eid);
check('GetBiography: timeline built', bio.timeline.length > 0, `${bio.timeline.length} entries`);
check(
  'GetBiography: timeline ordered',
  bio.timeline.every((e, i) => i === 0 || e.year >= bio.timeline[i - 1].year),
);
check(
  'GetBiography: kinds are record/change',
  bio.timeline.every((e) => e.kind === 'record' || e.kind === 'change'),
);

const tree = call('GetFamilyTree', eid, 3);
check('GetFamilyTree: returns nodes', Array.isArray(tree), `${tree.length} nodes`);

const count = call('GetChangesetsCount');
const changes = call('GetChangesets', 0, 5);
check('GetChangesets: window returned', count > 0 && changes.length > 0, `${count} changesets`);
const ec = call('GetEntityChangesets', eid);
check(
  'GetEntityChangesets: only that entity',
  ec.every((c) => c.id === eid),
  `${ec.length} changes`,
);

call('Save');
check('Save: no-op does not throw', true);

// Determinism across a rebuild from the same seed.
const before = JSON.parse(interop.StreamTick(0));
call('Reset');
interop.PassYears(120);
const afterReset = JSON.parse(interop.StreamTick(0));
const texts = (t) => t.messages.filter((m) => m.type === 'Record').map((m) => m.record.text);
check(
  'Reset + same seed reproduces the world',
  JSON.stringify(texts(afterReset)) === JSON.stringify(texts(before)),
  `${texts(before).length} vs ${texts(afterReset).length} records`,
);

const newYear = call('Reseed', 1234);
check('Reseed: returns the fresh year', typeof newYear === 'number', `year=${newYear}`);
check('Reseed: seed reported back', call('GetSeed') === 1234);
interop.PassYears(120);
const afterReseed = JSON.parse(interop.StreamTick(0));
check(
  'Reseed: different seed, different world',
  JSON.stringify(texts(afterReseed)) !== JSON.stringify(texts(before)),
);

console.log(failures === 0 ? '\nAll checks passed.' : `\n${failures} CHECK(S) FAILED.`);
process.exit(failures === 0 ? 0 : 1);
