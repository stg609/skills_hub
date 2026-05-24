import assert from "node:assert/strict";
import { test } from "node:test";
import { applyIndexedSkills } from "../src/sync.js";

test("upserts indexed skills and waits for repeated missing runs before deactivation", () => {
  const existing = [
    {
      identity: "gitlab:1:old-skill",
      slug: "old-skill",
      name: "old-skill",
      description: "Old skill",
      category: "默认分类",
      artifactType: "skill",
      source: { provider: "gitlab", projectId: 1, skillDir: "old-skill" },
      updatedAt: "2026-05-20T00:00:00.000Z",
      installCommand: "npx skills add repo --skill old-skill",
      downloads: { d1: 1, d7: 2, all: 3 },
      likes: 4,
      active: true
    }
  ];

  const incoming = [
    {
      identity: "gitlab:1:new-skill",
      slug: "new-skill",
      name: "new-skill",
      description: "New skill",
      category: "默认分类",
      artifactType: "skill",
      source: { provider: "gitlab", projectId: 1, skillDir: "new-skill" },
      updatedAt: "2026-05-23T00:00:00.000Z",
      installCommand: "npx skills add repo --skill new-skill",
      downloads: { d1: 0, d7: 0, all: 0 },
      likes: 0
    }
  ];

  const result = applyIndexedSkills(existing, incoming, "2026-05-23T01:00:00.000Z");

  assert.equal(result.summary.created, 1);
  assert.equal(result.summary.updated, 0);
  assert.equal(result.summary.deactivated, 0);
  assert.equal(result.skills.find((skill) => skill.slug === "old-skill").active, true);
  assert.equal(result.skills.find((skill) => skill.slug === "old-skill").missingSyncCount, 1);
  assert.equal(result.skills.find((skill) => skill.slug === "old-skill").downloads.all, 3);
  assert.equal(result.skills.find((skill) => skill.slug === "old-skill").likes, 4);
  assert.equal(result.skills.find((skill) => skill.slug === "new-skill").active, true);
});

test("deactivates a missing skill after the grace window", () => {
  const existing = [
    {
      identity: "gitlab:1:old-skill",
      slug: "old-skill",
      name: "old-skill",
      description: "Old skill",
      category: "默认分类",
      artifactType: "skill",
      source: { provider: "gitlab", projectId: 1, skillDir: "old-skill" },
      updatedAt: "2026-05-20T00:00:00.000Z",
      installCommand: "npx skills add repo --skill old-skill",
      downloads: { d1: 1, d7: 2, all: 3 },
      likes: 4,
      active: true,
      missingSyncCount: 2
    }
  ];

  const result = applyIndexedSkills(existing, [], "2026-05-23T01:00:00.000Z");
  const skill = result.skills[0];

  assert.equal(result.summary.deactivated, 1);
  assert.equal(skill.active, false);
  assert.equal(skill.missingSyncCount, 3);
});

test("updates metadata while preserving stats for an existing skill", () => {
  const existing = [
    {
      identity: "gitlab:1:skill-a",
      slug: "skill-a",
      name: "skill-a",
      description: "Old description",
      category: "默认分类",
      artifactType: "skill",
      source: { provider: "gitlab", projectId: 1, skillDir: "skill-a" },
      updatedAt: "2026-05-20T00:00:00.000Z",
      installCommand: "npx skills add repo --skill skill-a",
      downloads: { d1: 7, d7: 8, all: 9 },
      likes: 10,
      active: true
    }
  ];

  const incoming = [
    {
      ...existing[0],
      description: "New description",
      updatedAt: "2026-05-23T00:00:00.000Z",
      downloads: { d1: 0, d7: 0, all: 0 },
      likes: 0
    }
  ];

  const result = applyIndexedSkills(existing, incoming, "2026-05-23T01:00:00.000Z");
  const skill = result.skills[0];

  assert.equal(result.summary.created, 0);
  assert.equal(result.summary.updated, 1);
  assert.equal(skill.description, "New description");
  assert.deepEqual(skill.downloads, { d1: 7, d7: 8, all: 9 });
  assert.equal(skill.likes, 10);
});

test("keeps existing slugs and disambiguates new slug collisions", () => {
  const existing = [
    {
      identity: "gitlab:1:frontend",
      slug: "frontend",
      name: "frontend",
      description: "Existing skill",
      category: "默认分类",
      artifactType: "skill",
      source: { provider: "gitlab", projectId: 1, skillDir: "frontend" },
      updatedAt: "2026-05-20T00:00:00.000Z",
      installCommand: "npx skills add repo --skill frontend",
      downloads: { d1: 0, d7: 0, all: 0 },
      likes: 0,
      active: true
    }
  ];

  const incoming = [
    {
      ...existing[0],
      description: "Renamed metadata"
    },
    {
      identity: "gitlab:2:frontend",
      slug: "frontend",
      name: "frontend",
      description: "Another frontend skill",
      category: "默认分类",
      artifactType: "skill",
      source: { provider: "gitlab", projectId: 2, skillDir: "frontend" },
      updatedAt: "2026-05-23T00:00:00.000Z",
      installCommand: "npx skills add repo2 --skill frontend",
      downloads: { d1: 0, d7: 0, all: 0 },
      likes: 0
    }
  ];

  const result = applyIndexedSkills(existing, incoming, "2026-05-23T01:00:00.000Z");
  const slugs = result.skills.map((skill) => skill.slug);

  assert.equal(slugs[0], "frontend");
  assert.match(slugs[1], /^frontend-[0-9a-f]{8}$/);
});
