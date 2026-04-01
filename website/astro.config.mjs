import { defineConfig } from "astro/config";
import tailwindcss from "@tailwindcss/vite";

const owner = process.env.GITHUB_REPOSITORY_OWNER;
const repo = process.env.GITHUB_REPOSITORY?.split("/")[1];
const isProjectPage =
  Boolean(owner) &&
  Boolean(repo) &&
  repo.toLowerCase() !== `${owner.toLowerCase()}.github.io`;

export default defineConfig({
  site: process.env.SITE_URL ?? (owner ? `https://${owner}.github.io` : "https://example.com"),
  base: process.env.BASE_PATH ?? (isProjectPage && repo ? `/${repo}` : "/"),
  vite: {
    plugins: [tailwindcss()]
  }
});
