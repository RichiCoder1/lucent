import { defineConfig } from "blume";
import { z } from "zod";

export default defineConfig({
  title: "Lucent",
  description: "Documentation for the experimental Lucent language and UI framework.",
  content: { root: "docs" },
  frontmatter: { extend: { status: z.string().optional() } },
  github: { owner: "RichiCoder1", repo: "lucent" },
  ai: { llmsTxt: true },
  deployment: { output: "static" },
});
