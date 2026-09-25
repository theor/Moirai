/**
 * Handing the reader a file the page made, with no server involved: the static site has none, and the
 * in-browser engine already holds everything the file needs.
 */

/** `moirai-seed-42-year-964.md`: what identifies the world, so two downloads never collide. */
export function chronicleFileName(seed: number | string | undefined, year: number): string {
  return `moirai-seed-${seed ?? 'unknown'}-year-${year}.md`;
}

/** Save `text` as a file named `name`, through a throwaway link to an in-memory blob. */
export function downloadText(name: string, text: string, type = 'text/markdown') {
  const url = URL.createObjectURL(new Blob([text], { type: `${type};charset=utf-8` }));
  const a = document.createElement('a');
  a.href = url;
  a.download = name;
  document.body.appendChild(a);
  a.click();
  a.remove();
  // Revoked on the next tick: some browsers start the download asynchronously from the click.
  setTimeout(() => URL.revokeObjectURL(url), 0);
}
