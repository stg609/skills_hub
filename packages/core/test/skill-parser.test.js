import assert from "node:assert/strict";
import { test } from "node:test";
import { parseSkillMarkdown } from "../src/skill-parser.js";

test("parses required skill metadata from frontmatter", () => {
  const markdown = `---
name: frontend-taste-lab
description: Opinionated UI critique prompts.
---

# Frontend Taste Lab
`;

  assert.deepEqual(parseSkillMarkdown(markdown), {
    name: "frontend-taste-lab",
    description: "Opinionated UI critique prompts."
  });
});

test("rejects a skill without name or description", () => {
  assert.throws(
    () => parseSkillMarkdown("---\nname: only-name\n---\n"),
    /description is required/
  );
});
