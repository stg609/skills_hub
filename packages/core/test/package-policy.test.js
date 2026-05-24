import assert from "node:assert/strict";
import { test } from "node:test";
import { buildPackageEntries } from "../src/package-policy.js";

test("maps files under skill directory into zip-root relative paths", () => {
  const entries = buildPackageEntries({
    skillDir: "frontend/design",
    files: [
      { path: "frontend/design/SKILL.md", size: 100 },
      { path: "frontend/design/references/palette.md", size: 200 }
    ],
    maxFiles: 10,
    maxBytes: 1_000
  });

  assert.deepEqual(entries, [
    { sourcePath: "frontend/design/SKILL.md", archivePath: "SKILL.md" },
    { sourcePath: "frontend/design/references/palette.md", archivePath: "references/palette.md" }
  ]);
});

test("rejects packages that exceed file count or byte limits", () => {
  assert.throws(
    () => buildPackageEntries({
      skillDir: "skill",
      files: [
        { path: "skill/SKILL.md", size: 10 },
        { path: "skill/large.bin", size: 100 }
      ],
      maxFiles: 1,
      maxBytes: 1_000
    }),
    /too many files/
  );

  assert.throws(
    () => buildPackageEntries({
      skillDir: "skill",
      files: [{ path: "skill/SKILL.md", size: 2_000 }],
      maxFiles: 10,
      maxBytes: 1_000
    }),
    /too large/
  );
});

test("rejects unsafe archive paths", () => {
  assert.throws(
    () => buildPackageEntries({
      skillDir: "skill",
      files: [{ path: "skill/../SKILL.md", size: 10 }],
      maxFiles: 10,
      maxBytes: 1_000
    }),
    /unsafe path/
  );
});
