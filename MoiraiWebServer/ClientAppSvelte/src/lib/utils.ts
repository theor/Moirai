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
    // console.log(page)
    const set = (value: string) => {
      const p = new URLSearchParams(window.location.search);
      p.set(name, value);
      console.log('setting', name, value, 'goto', p.toString());
      goto(window.location.pathname + '?' + p.toString(), {invalidateAll: true});
      // window.location.search = p.toString();
  };
  return {
    getNumber: () => {
      const s = page.url.searchParams.get(name) as string;
      if(s === "" || !s) return -1;
      return Number(s) ?? -1;
    }, // new URLSearchParams(window.location.search).get(name),
    get: () => page.url.searchParams.get(name) as string, // new URLSearchParams(window.location.search).get(name),
    set,
    setNumber(number:number) { set(number.toString()) }
  };
}

export const selectedEntity = (page: any) => urlParam(page, 'e');
export const filteredEntity = (page: any) => urlParam(page, 'f');
