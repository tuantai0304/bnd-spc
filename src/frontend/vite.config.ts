import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import { tanstackRouter } from '@tanstack/router-plugin/vite';

// The router plugin must run before @vitejs/plugin-react so the generated route
// tree is in place before React Fast Refresh transforms the route modules.
export default defineConfig({
  plugins: [
    tanstackRouter({ target: 'react', autoCodeSplitting: true }),
    react(),
    tailwindcss(),
  ],
  server: {
    // The backend registers neither AddCors() nor UseCors(), so a direct
    // cross-origin call from :5173 to :5095 is blocked with an opaque network
    // error. Proxying keeps every request same-origin — which is why every
    // fetch in src/api uses a relative path and there is no VITE_API_BASE_URL.
    proxy: {
      '/api': { target: 'http://localhost:5095', changeOrigin: true },
      '/openapi': { target: 'http://localhost:5095', changeOrigin: true },
    },
  },
});
