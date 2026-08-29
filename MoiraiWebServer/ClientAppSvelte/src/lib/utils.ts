import { goto } from '$app/navigation';
import type { Page } from '@sveltejs/kit';

export function parseEntityLink(
  str: string,
): ({ type: 'text'; text: string } | { type: 'entity'; id: number; link: string })[] {
  const rx = /(?:(?:<#(?<id>\d+)>(?<link>[^<]+)<\/>)|(?<text>[^<\n]+))/gi;
  return [...str.matchAll(rx)].map((match: RegExpMatchArray) => {
    if (!match?.groups) return { type: 'text', text: '???' };
    if (match.groups['text']) {
      return { type: 'text', text: match.groups['text'] };
    } else {
      const id: number = Number(match.groups['id']);
      return { type: 'entity', id, link: match.groups['link'] };
    }
  });
}

export function urlParam(page: Page, name: string) {
  const set = (value: string) => {
    // Mutating the current URL keeps the base path (and any hash) intact, which
    // hand-building the string from window.location.pathname did not.
    const url = new URL(window.location.href);
    url.searchParams.set(name, value);
    // resolve() turns an app-relative route into a base-prefixed path; `url` is
    // already the current absolute URL, so resolving it would double the base.
    // The rule can only recognise a literal resolve() call without type-aware
    // linting, hence the narrow exemption.
    // eslint-disable-next-line svelte/no-navigation-without-resolve
    goto(url, { invalidateAll: true });
  };
  return {
    getNumber: (def?: number) => {
      const s = page.url.searchParams.get(name) as string;
      const defNumber = def ?? -1;
      if (s === '' || !s) return defNumber;
      // `Number('abc')` is NaN, not null, so the old `?? defNumber` never fired
      // and callers could get NaN back for a malformed search param.
      const n = Number(s);
      return Number.isNaN(n) ? defNumber : n;
    }, // new URLSearchParams(window.location.search).get(name),
    get: () => page.url.searchParams.get(name) as string, // new URLSearchParams(window.location.search).get(name),
    set,
    setNumber(number: number) {
      set(number.toString());
    },
  };
}

export const selectedEntity = (page: Page) => urlParam(page, 'e');
export const filteredEntity = (page: Page) => urlParam(page, 'f');
export const filteredTag = (page: Page) => urlParam(page, 't');

/**
 * A @display field (e.g. "Members", "Settlements") yields one details row per item, all sharing a
 * label. Collapsing the run to one group per label is what lets a panel show "Children: 4" instead of
 * four rows that each repeat the word.
 */
export type DetailGroup = { label: string; values: string[] };

export function groupByLabel(items: { label: string; value: string }[]): DetailGroup[] {
  const out: DetailGroup[] = [];
  for (const d of items) {
    const last = out[out.length - 1];
    if (last && last.label === d.label) last.values.push(d.value);
    else out.push({ label: d.label, values: [d.value] });
  }
  return out;
}
