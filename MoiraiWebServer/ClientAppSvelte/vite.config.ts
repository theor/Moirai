import { purgeCss } from 'vite-plugin-tailwind-purgecss';
import { sveltekit } from '@sveltejs/kit/vite';
import { defineConfig } from 'vite';
import mkcert from 'vite-plugin-mkcert';
import Icons from 'unplugin-icons/vite';

export default defineConfig({
  plugins: [
    sveltekit(),
    purgeCss(),
    mkcert(),
    Icons({
      compiler: 'svelte',
    }),
  ],
  server: {
    strictPort: true,
    port: 3000,

    https: false,
    // proxy: {
    // 	"/hub": "localhost:5028/hub"
    // }
  },
});
