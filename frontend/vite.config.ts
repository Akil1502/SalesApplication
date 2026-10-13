/// <reference types="vitest/config" />
import { fileURLToPath, URL } from 'node:url'
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    port: 5173,
    strictPort: true,
    // The API runs on :5080 in development. Proxying keeps the browser on one origin,
    // so there is no CORS configuration and the JWT never crosses origins.
    proxy: {
      '/api': { target: 'http://localhost:5080', changeOrigin: false },
      '/health': { target: 'http://localhost:5080', changeOrigin: false },
    },
  },
  build: {
    // Production build is served by the ASP.NET Core API (one IIS site).
    outDir: '../backend/src/SalesApp.Api/wwwroot',
    emptyOutDir: true,
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    css: false,
  },
})
