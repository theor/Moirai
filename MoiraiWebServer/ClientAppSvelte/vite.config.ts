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

    // Load-bearing, not redundant: vite-plugin-mkcert opts out on exactly this
    // value (`typeof server.https === 'boolean' && server.https === false`).
    // Drop it and the dev server becomes HTTPS, breaking the ASP.NET host's
    // UseProxyToSpaDevelopmentServer("http://localhost:3000"). Vite's types only
    // permit https.ServerOptions, so the plugin's own opt-out doesn't type-check.
    // @ts-expect-error -- see above
    https: false,

    // The ASP.NET host proxies to this dev server, but its SPA launcher shells
    // out to `cmd`, so on macOS/Linux you run vite directly and forward /hub to
    // the backend yourself. Inert when browsing the .NET host on :5028, which
    // serves /hub itself. ws: true is required for SignalR's socket upgrade.
    proxy: {
      '/hub': { target: 'http://localhost:5028', ws: true, changeOrigin: true },
    },
  },
});
