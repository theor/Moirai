import { goto } from '$app/navigation';
import type { GetSetProperty } from './types';
import { page } from '$app/stores';
import type { Page } from '@sveltejs/kit';
import { writable } from 'svelte/store';
    
export function parseEntityLink(str: string): ({ type:'text', text: string } | { type:'entity', id: number; link: string })[] {
  const rx = /(?:(?:<#(?<id>\d+)>(?<link>[^<]+)<\/>)|(?<text>[^<\n]+))/gi;
  return [...str.matchAll(rx)].map((match: RegExpMatchArray, i) => {
    if (!match?.groups) return { type: 'text', text: '???' };
    if (match.groups['text']) {
      return { type: 'text',text: match.groups['text'] };
    } else {
      let id: number = Number(match.groups['id']);
      return { type: 'entity',id, link: match.groups['link'] };
    }
  });
}

export function urlParam(page: Page<any>, name: string) {
    const set = (value: string) => {
      const p = new URLSearchParams(window.location.search);
      p.set(name, value);
      goto(window.location.pathname + '?' + p.toString(), {invalidateAll: true});
  };
  return {
    getNumber: (def?: number) => {
      const s = page.url.searchParams.get(name) as string;
      const defNumber = def ?? -1;
      if(s === "" || !s) return defNumber;
      return Number(s) ?? defNumber;
    }, // new URLSearchParams(window.location.search).get(name),
    get: () => page.url.searchParams.get(name) as string, // new URLSearchParams(window.location.search).get(name),
    set,
    setNumber(number:number) { set(number.toString()) }
  };
}

export const selectedEntity = (page: any) => urlParam(page, 'e');
export const filteredEntity = (page: any) => urlParam(page, 'f');
export const filteredTag = (page: any) => urlParam(page, 't');
