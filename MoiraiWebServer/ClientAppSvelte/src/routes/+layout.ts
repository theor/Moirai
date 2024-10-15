export const prerender = false;
export const ssr = false;

import {page } from '$app/stores';
import { get } from 'svelte/store';
export function load() {
    console.log("load", window.location, get(page))
    var p = new URLSearchParams(window.location.search);
    return {
        selected: Number(p.get("e")) || -1,
    };
}
