import assert from "node:assert/strict";
import { test } from "node:test";
import {
  DEFAULT_CATEGORY,
  buildSkillRecord,
  sortSkillCards,
  toggleAnonymousLike
} from "../src/catalog.js";

test("builds a skill record with default category and GitLab identity", () => {
  const record = buildSkillRecord({
    projectId: 42,
    repoUrl: "git@gitlab.company.local:agent-skills/product.git",
    defaultBranch: "main",
    skillPath: "frontend/taste/SKILL.md",
    commitSha: "abc123",
    updatedAt: "2026-05-23T01:18:00.000Z",
    metadata: {
      name: "frontend-taste-lab",
      description: "Opinionated UI critique prompts."
    }
  });

  assert.equal(record.slug, "frontend-taste-lab");
  assert.equal(record.category, DEFAULT_CATEGORY);
  assert.equal(record.identity, "gitlab:42:frontend/taste");
  assert.equal(record.installCommand, "npx skills add git@gitlab.company.local:agent-skills/product.git --skill frontend-taste-lab");
});

test("sorts skill cards by download windows", () => {
  const cards = [
    { slug: "a", downloads: { d1: 2, d7: 12, all: 30 }, updatedAt: "2026-05-20T00:00:00.000Z" },
    { slug: "b", downloads: { d1: 8, d7: 9, all: 80 }, updatedAt: "2026-05-21T00:00:00.000Z" },
    { slug: "c", downloads: { d1: 1, d7: 20, all: 50 }, updatedAt: "2026-05-22T00:00:00.000Z" }
  ];

  assert.deepEqual(sortSkillCards(cards, "downloads_1d").map((card) => card.slug), ["b", "a", "c"]);
  assert.deepEqual(sortSkillCards(cards, "downloads_7d").map((card) => card.slug), ["c", "a", "b"]);
  assert.deepEqual(sortSkillCards(cards, "downloads_all").map((card) => card.slug), ["b", "c", "a"]);
  assert.deepEqual(sortSkillCards(cards, "updated").map((card) => card.slug), ["c", "b", "a"]);
});

test("deduplicates anonymous likes by visitor id", () => {
  const first = toggleAnonymousLike([], "frontend-taste-lab", "visitor-1");
  const second = toggleAnonymousLike(first.likes, "frontend-taste-lab", "visitor-1");

  assert.equal(first.liked, true);
  assert.equal(first.likes.length, 1);
  assert.equal(second.liked, false);
  assert.equal(second.likes.length, 0);
});
