import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// En desarrollo, /api se redirige a la API .NET (dotnet run en el puerto 5080).
// base './': los recursos quedan relativos a <base href>, que la API ajusta a la subruta (P-007).
export default defineConfig({
  base: './',
  plugins: [react()],
  server: {
    port: 5173,
    proxy: { '/api': 'http://localhost:5080' },
  },
})
