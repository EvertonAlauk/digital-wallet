import type { NextConfig } from "next";

/** Address of the BFF. Read on the server only, so it never reaches the browser bundle. */
const bffUrl = process.env.BFF_URL ?? "http://localhost:8082";

/**
 * Static export (production): the BFF serves the files built into out/, so the browser already uses one origin.
 * Rewrites do not exist in a static export, so they are only used in development and in the server build.
 */
const staticExport = process.env.STATIC_EXPORT === "true";

const nextConfig: NextConfig = staticExport
  ? { output: "export" }
  : {
      async rewrites() {
        // In development, the browser talks only to this origin. Everything under /bff and /api goes to the BFF.
        return [
          { source: "/bff/:path*", destination: `${bffUrl}/bff/:path*` },
          { source: "/api/:path*", destination: `${bffUrl}/api/:path*` },
        ];
      },
    };

export default nextConfig;
