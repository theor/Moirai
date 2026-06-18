export const prerender = false;
export const ssr = false;

export function load() {
    var p = new URLSearchParams(window.location.search);
    return {
        selected: Number(p.get("e")) || -1,
    };
}
