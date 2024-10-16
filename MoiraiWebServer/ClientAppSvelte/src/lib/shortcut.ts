import type { KeyboardEventHandler } from 'svelte/elements';
interface ShortcutParams {
  alt?: boolean;
  shift?: boolean;
  control?: boolean;
  code: string;
  callback: () => void;
}
export const shortcut = (node: HTMLElement, params: ShortcutParams) => {
  let handler: KeyboardEventHandler<any>; // { (this: Window, ev: KeyboardEvent): any; (e: any): void; (this: Window, ev: KeyboardEvent): any; };
  const removeHandler = () => window.removeEventListener('keydown', handler),
    setHandler = () => {
      removeHandler();
      if (!params) return;
      handler = (e) => {
        if (
          !!params.alt != e.altKey ||
          !!params.shift != e.shiftKey ||
          !!params.control != (e.ctrlKey || e.metaKey) ||
          params.code != e.code
        )
          return;
        e.preventDefault();
        params.callback();
      };
      window.addEventListener('keydown', handler);
    };
  setHandler();
  return {
    update: setHandler,
    destroy: removeHandler,
  };
};
