import assert from "node:assert/strict";
import { test } from "node:test";
import { discoverSkillFiles } from "../src/gitlab-discovery.js";

test("discovers every SKILL.md file in a multi-skill repository tree", () => {
  const tree = [
    { type: "tree", path: "frontend" },
    { type: "blob", path: "frontend/design/SKILL.md" },
    { type: "blob", path: "frontend/design/reference.md" },
    { type: "blob", path: "workflow/meeting/SKILL.md" },
    { type: "blob", path: ".agents/installed/SKILL.md" },
    { type: "blob", path: "src/.claude/copied/SKILL.md" },
    { type: "blob", path: "README.md" }
  ];

  assert.deepEqual(discoverSkillFiles(tree), [
    { skillDir: "frontend/design", skillFilePath: "frontend/design/SKILL.md" },
    { skillDir: "workflow/meeting", skillFilePath: "workflow/meeting/SKILL.md" }
  ]);
});
