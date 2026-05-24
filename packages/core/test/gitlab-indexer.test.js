import assert from "node:assert/strict";
import { test } from "node:test";
import { indexGitLabProjects } from "../src/gitlab-indexer.js";

test("indexes skills from GitLab projects by reading each discovered SKILL.md", async () => {
  const projects = [
    {
      id: 7,
      ssh_url_to_repo: "git@gitlab.company.local:agent-skills/product.git",
      default_branch: "main",
      last_activity_at: "2026-05-23T01:00:00.000Z"
    }
  ];

  const client = {
    async listGroupProjects() {
      return projects;
    },
    async listRepositoryTree(projectId) {
      assert.equal(projectId, 7);
      return [
        { type: "blob", path: "frontend/SKILL.md" },
        { type: "blob", path: "README.md" }
      ];
    },
    async getRawFile(projectId, filePath) {
      assert.equal(projectId, 7);
      assert.equal(filePath, "frontend/SKILL.md");
      return "---\nname: frontend-taste-lab\ndescription: UI critique prompts.\n---\n";
    }
  };

  const skills = await indexGitLabProjects(client, ["agent-skills"]);

  assert.equal(skills.length, 1);
  assert.equal(skills[0].identity, "gitlab:7:frontend");
  assert.equal(skills[0].name, "frontend-taste-lab");
  assert.equal(skills[0].category, "默认分类");
});
