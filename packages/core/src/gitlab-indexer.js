import { buildSkillRecord } from "./catalog.js";
import { discoverSkillFiles } from "./gitlab-discovery.js";
import { parseSkillMarkdown } from "./skill-parser.js";

export async function indexGitLabProjects(client, groupPaths) {
  const indexedSkills = [];

  for (const groupPath of groupPaths) {
    const projects = await client.listGroupProjects(groupPath);
    for (const project of projects) {
      const tree = await client.listRepositoryTree(project.id, project.default_branch);
      const skillFiles = discoverSkillFiles(tree);

      for (const skillFile of skillFiles) {
        const markdown = await client.getRawFile(project.id, skillFile.skillFilePath, project.default_branch);
        const metadata = parseSkillMarkdown(markdown);
        indexedSkills.push(buildSkillRecord({
          projectId: project.id,
          repoUrl: project.ssh_url_to_repo ?? project.http_url_to_repo,
          defaultBranch: project.default_branch,
          skillPath: skillFile.skillFilePath,
          commitSha: project.last_activity_at,
          updatedAt: project.last_activity_at,
          metadata
        }));
      }
    }
  }

  return indexedSkills;
}
