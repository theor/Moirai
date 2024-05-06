import {defineConfig} from 'vite'
import react from '@vitejs/plugin-react-swc'
import mkcert from 'vite-plugin-mkcert';

// https://vitejs.dev/config/
export default defineConfig({
    plugins: [react(), mkcert()
    ],
    server: {
        https: false,
        strictPort: true,
        port: 3000,
        // proxy: {
        //     '/api': {
        //         target: 'https://localhost:7148',
        //         secure: false,
        //     },
        //     '/signalr': {
        //         target: 'wss://localhost:7148',
        //         ws: true,
        //         secure: false
        //     },
        //     '/hub': {
        //         target: 'https://localhost:7148',
        //         ws: true,
        //         changeOrigin: true,
        //         secure: false
        //     },
        // }
    }
})
