import {defineConfig} from 'vite'
import react from '@vitejs/plugin-react-swc'
import mkcert from 'vite-plugin-mkcert';

// https://vitejs.dev/config/
export default defineConfig({
    plugins: [react(), mkcert()
    ],
    server: {
        https: true,
        strictPort: true,
        port: 3000,
        proxy: {
            '/api': {
                target: 'https://localhost:7148',
                // target: 'http://localhost:5028',
                secure: false,
                // ws: true,
                // headers: {
                //     Connection: "Keep-Alive",
                // },
            },
            // '/signalr': {
            //     target: 'wss://localhost:7148',
            //     ws: true,
            //     secure: false
            // },
            '/signalr': {
                target: 'wss://localhost:7148',
                ws: true,
                secure: false
            },
            '/hub': {
                // target: 'http://localhost:5028',
                target: 'https://localhost:7148',
                ws: true,
                // headers: {
                //     Connection: "Keep-Alive",
                // },
                changeOrigin: true,
                secure: false
            },
        }
    }
})
