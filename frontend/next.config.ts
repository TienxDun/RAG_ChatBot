import type { NextConfig } from "next";
import { join } from "path";
import { readFileSync, existsSync } from "fs";

// Load environment variables from the parent directory's .env file
const envPath = join(process.cwd(), "..", ".env");
const envVariables: Record<string, string> = {};

if (existsSync(envPath)) {
  const envFile = readFileSync(envPath, "utf-8");
  envFile.split("\n").forEach((line) => {
    // Ignore comments and empty lines
    if (line.trim().startsWith("#") || !line.trim()) return;
    
    // Parse key-value pairs
    const match = line.match(/^\s*([\w.-]+)\s*=\s*(.*)?\s*$/);
    if (match) {
      const key = match[1];
      let value = match[2] || "";
      // Remove wrapping quotes if present
      value = value.replace(/^(['"])(.*)\1$/, "$2");
      envVariables[key] = value;
      // Assign to process.env for server-side usage
      process.env[key] = value;
    }
  });
}

const nextConfig: NextConfig = {
  /* config options here */
  env: {
    // Expose only NEXT_PUBLIC_ variables to the browser
    ...Object.entries(envVariables).reduce((acc, [key, value]) => {
      if (key.startsWith("NEXT_PUBLIC_")) {
        acc[key] = value;
      }
      return acc;
    }, {} as Record<string, string>),
  },
};

export default nextConfig;
