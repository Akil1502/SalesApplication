/// <reference types="vitest/config" />
import { fileURLToPath, URL } from 'node:url'
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// The API the dev server forwards to: :5080 normally; SALESAPP_API_URL points it elsewhere (a second API on a test
// database, for example).
const api = process.env.SALESAPP_API_URL ?? 'http://localhost:5080'

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
    // Proxying keeps the browser on one origin, so there is no CORS configuration and the JWT never crosses origins.
    proxy: {
      '/api': { target: api, changeOrigin: false },
      '/health': { target: api, changeOrigin: false },
    },
  },
  build: {
    // Production build is served by the ASP.NET Core API (one IIS site).
    outDir: '../backend/src/SalesApp.Api/wwwroot',
    emptyOutDir: true,
    // The 3D library (three.js) is one ~700 kB module; it downloads only when a 3D scene is first shown.
    chunkSizeWarningLimit: 1000,
    rolldownOptions: {
      output: {
        // Framework code changes rarely, so give it its own long-cached chunks instead of
        // re-downloading it inside the app bundle after every release.
        codeSplitting: {
          groups: [
            { name: 'vendor-react', test: /node_modules[\\/](react|react-dom|react-router|scheduler)[\\/]/, priority: 30 },
            // Only the 3D scenes use these, so this chunk loads lazily with the first one.
            { name: 'vendor-3d', test: /node_modules[\\/](three|@react-three|react-reconciler|its-fine|suspend-react|zustand|react-use-measure)[\\/]/, priority: 25 },
            { name: 'vendor-motion', test: /node_modules[\\/](motion|framer-motion|motion-dom|motion-utils)[\\/]/, priority: 18 },
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
