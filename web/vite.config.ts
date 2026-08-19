import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

const SERVICE = 'http://127.0.0.1:8722'

export default defineConfig({
  plugins: [react(), tailwindcss()],

  // Built straight into the service's wwwroot, so one executable serves both the API and the
  // app it talks to. The overlay's expanded panel loads this same build in a WebView2 — there is
  // one management UI, not two that drift apart.
  build: {
    outDir: '../src/RatNav.Service/wwwroot',
    emptyOutDir: true,
  },

  server: {
    proxy: {
      '/api': { target: SERVICE, changeOrigin: true },
    },
  },
})
