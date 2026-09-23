/**
 * A world's address: the three things that determine it, carried in the URL.
 *
 * A Moirai world is a pure function of its story, its seed and its year — the simulation is
 * deterministic per seed, which is why the browser can throw a world away and rebuild the identical one.
 * That fact used to be spent on surviving a tab switch, with seed and year hidden in `sessionStorage`.
 * Putting them in the URL instead costs nothing and buys the thing that was missing: a world you can
 * send to someone.
 *
 * **Seed and year live in the query string** (`?seed=42&year=964`), where they are short, readable, and
 * carried across the app's navigations for free. **The story does not**, in the address bar: it is 37 KB
 * for `w.sg`, and a URL that long is unreadable and unpasteable. It travels only in a link built on
 * demand by {@link shareLink}, compressed into the fragment — so an unedited world shares as a tidy
 * `?seed=&year=`, and an edited one still shares exactly, at the cost of a long link.
 */

/** The default story is the one the build ships; a link with no story means that one. */
export interface WorldAddress {
  /** A string, because a seed is a `ulong` and can exceed what a `number` holds exactly. */
  seed: string | null;
  /** Null when the link says nothing about when — see the caller's default. */
  year: number | null;
}

const SEED = 'seed';
const YEAR = 'year';
/** The fragment key for a compressed story. Short, because it sits in front of several kilobytes. */
const STORY = 's';

/** What a URL says about which world to build. Absent or malformed parts read as "unspecified". */
export function readAddress(url: URL): WorldAddress {
  const seed = url.searchParams.get(SEED);
  const year = Number(url.searchParams.get(YEAR));
  return {
    seed: seed !== null && /^\d+$/.test(seed) ? seed : null,
    // Year 0 is not a world anyone links to — w.sg starts at 764 — so it reads as unspecified rather
    // than as an instruction to show an empty world.
    year: Number.isFinite(year) && year > 0 ? Math.floor(year) : null,
  };
}

/** The same URL, saying this instead. Other parameters — the selected entity, the filters — survive. */
export function withAddress(url: URL, address: WorldAddress): URL {
  const next = new URL(url.href);
  if (address.seed === null) next.searchParams.delete(SEED);
  else next.searchParams.set(SEED, address.seed);
  if (address.year === null) next.searchParams.delete(YEAR);
  else next.searchParams.set(YEAR, String(address.year));
  return next;
}

/** The story a link carries, or null if it carries none. */
export function readStoryParam(hash: string): string | null {
  const params = new URLSearchParams(hash.replace(/^#/, ''));
  return params.get(STORY);
}

/**
 * A link that rebuilds exactly this world somewhere else.
 *
 * The story rides along only when it differs from the one the build ships, which keeps the common link
 * short. Compressed with `deflate-raw` and base64url — a 37 KB story comes out around 8 KB, long but
 * within what every browser accepts, and a fragment is never sent to the server.
 */
export async function shareLink(
  url: URL,
  address: WorldAddress,
  story: { current: string; shipped: string },
): Promise<string> {
  const link = withAddress(url, address);
  link.hash = '';
  if (story.current !== story.shipped) link.hash = `${STORY}=${await compress(story.current)}`;
  return link.href;
}

/** Deflate to base64url. Async because `CompressionStream` is a stream. */
export async function compress(text: string): Promise<string> {
  const stream = streamOf(utf8(text)).pipeThrough(new CompressionStream('deflate-raw'));
  return toBase64Url(new Uint8Array(await new Response(stream).arrayBuffer()));
}

/** The inverse of {@link compress}. Throws on anything that is not one of its outputs. */
export async function decompress(encoded: string): Promise<string> {
  const stream = streamOf(fromBase64Url(encoded)).pipeThrough(
    new DecompressionStream('deflate-raw'),
  );
  return new Response(stream).text();
}

// Base64url rather than base64: a fragment is not the place for '+', '/' and '=', which get percent-
// encoded by some clients and mangled by others.
function toBase64Url(bytes: Uint8Array): string {
  let binary = '';
  for (const b of bytes) binary += String.fromCharCode(b);
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

function fromBase64Url(encoded: string): Uint8Array<ArrayBuffer> {
  const binary = atob(encoded.replace(/-/g, '+').replace(/_/g, '/'));
  return Uint8Array.from(binary, (c) => c.charCodeAt(0));
}

/**
 * UTF-8 bytes in a buffer of our own. `TextEncoder.encode` returns a view over `ArrayBufferLike`, which
 * could in principle be shared memory, and the stream types rightly refuse that.
 */
function utf8(text: string): Uint8Array<ArrayBuffer> {
  const encoder = new TextEncoder();
  // Worst case for UTF-8 is three bytes per UTF-16 code unit.
  const buffer = new Uint8Array(new ArrayBuffer(text.length * 3));
  const { written } = encoder.encodeInto(text, buffer);
  return buffer.subarray(0, written) as Uint8Array<ArrayBuffer>;
}

/**
 * A stream of one chunk of bytes. `new Blob([bytes])` would do, but `TextEncoder` hands back a
 * `Uint8Array<ArrayBufferLike>` and `BlobPart` insists on `ArrayBuffer`; going through a stream keeps
 * the types honest without copying or casting.
 */
function streamOf(bytes: Uint8Array<ArrayBuffer>): ReadableStream<BufferSource> {
  return new ReadableStream({
    start(controller) {
      controller.enqueue(bytes);
      controller.close();
    },
  });
}
