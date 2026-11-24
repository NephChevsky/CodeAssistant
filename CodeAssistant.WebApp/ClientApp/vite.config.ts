import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// The ASP.NET Core app (http://localhost:5080) starts this dev server through SpaProxy; hub traffic is proxied back to it.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      '/hubs': {
        target: 'http://localhost:5080',
        ws: true,
      },
    },
  },
});
