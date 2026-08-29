import tailwindcss from '@tailwindcss/vite';
import { sveltekit } from '@sveltejs/kit/vite';
import { defineConfig } from 'vite';
import mkcert from 'vite-plugin-mkcert';
import Icons from 'unplugin-icons/vite';

export default defineConfig({
  plugins: [
    sveltekit(),
    tailwindcss(),
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
