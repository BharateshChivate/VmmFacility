import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// The production build is served by the ASP.NET Core backend from its wwwroot folder.
export default defineConfig({
  plugins: [react()],
  build: {
    outDir: "../backend/VmmFacility.Api/wwwroot",
    emptyOutDir: true,
  },
  server: {
    host: "127.0.0.1",
    port: 5173,
    proxy: { "/api": "http://127.0.0.1:5180" },
  },
});
