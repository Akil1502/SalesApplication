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
    rolldownOptions: {
      output: {
        // Framework code changes rarely, so give it its own long-cached chunks instead of
        // re-downloading it inside the app bundle after every release.
        codeSplitting: {
          groups: [
            { name: 'vendor-react', test: /node_modules[\\/](react|react-dom|react-router|scheduler)[\\/]/, priority: 30 },
            { name: 'vendor-ui', test: /node_modules[\\/](radix-ui|@radix-ui|lucide-react|sonner|cn|class-variance-authority)[\\/]/, priority: 20 },
            // Only the dashboard draws charts, so this chunk loads lazily with it.
            { name: 'vendor-charts', test: /node_modules[\\/](recharts|victory-vendor|d3-[^\\/]+|@reduxjs|react-redux|reselect|immer|es-toolkit|decimal\.js-light|eventemitter3)[\\/]/, priority: 15 },
            { name: 'vendor-data', test: /node_modules[\\/](@tanstack|zod|react-hook-form|@hookform|openapi-fetch)[\\/]/, priority: 10 },
          ],
        },
      },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    css: false,
  },
})
