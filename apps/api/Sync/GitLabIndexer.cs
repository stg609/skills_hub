using SkillsHub.Api.Domain;
using SkillsHub.Api.GitLab;
using System.Runtime.CompilerServices;

namespace SkillsHub.Api.Sync;

public sealed class GitLabIndexer(GitLabClient gitlab, AppConfig config)
{
    public async Task<IReadOnlyList<SkillRecord>> IndexAsync(CancellationToken cancellationToken)
    {
        var indexed = new List<SkillRecord>();
        foreach (var group in config.GitLabGroups)
        {
            var projects = await gitlab.ListGroupProjectsAsync(group, cancellationToken);
            foreach (var project in projects)
            {
                var tree = await gitlab.ListRepositoryTreeAsync(project.Id, project.DefaultBranch, "", cancellationToken);
                foreach (var skillFile in tree.Where(item => item.Type == "blob" && item.Path.EndsWith("SKILL.md", StringComparison.OrdinalIgnoreCase)))
                {
                    var markdown = await gitlab.GetRawFileAsync(project.Id, skillFile.Path, project.DefaultBranch, cancellationToken);
                    var metadata = SkillMarkdownParser.Parse(markdown);
                    var repoUrl = project.SshUrlToRepo ?? project.HttpUrlToRepo ?? throw new InvalidOperationException("GitLab project has no clone URL.");
                    indexed.Add(SkillCatalog.BuildSkillRecord(
                        project.Id,
                        repoUrl,
                        project.DefaultBranch,
                        skillFile.Path,
                        project.LastActivityAt.ToString("O"),
                        project.LastActivityAt,
                        metadata.Name,
                        metadata.Description));
                }
            }
        }
        return indexed;
    }

    public async IAsyncEnumerable<IReadOnlyList<SkillRecord>> IndexBatchesAsync(int batchSize, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var batch = new List<SkillRecord>(batchSize);
        foreach (var group in config.GitLabGroups)
        {
            await foreach (var project in gitlab.EnumerateGroupProjectsAsync(group, cancellationToken))
            {
                IReadOnlyList<SkillRecord> projectSkills;
                try
                {
                    projectSkills = await IndexProjectAsync(project, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception error)
                {
                    // 中文注释：单个仓库结构或权限异常不应拖垮整轮索引；失败项目会被跳过，本轮未见到的旧 skill 仍需连续多轮缺失才下架。
                    Console.Error.WriteLine($"GitLab project {project.Id} indexing failed: {error.Message}");
                    continue;
                }

                foreach (var skill in projectSkills)
                {
                    batch.Add(skill);
                    if (batch.Count >= batchSize)
                    {
                        yield return batch.ToList();
                        batch.Clear();
                    }
                }
            }
        }

        if (batch.Count > 0) yield return batch;
    }

    private async Task<IReadOnlyList<SkillRecord>> IndexProjectAsync(GitLabProject project, CancellationToken cancellationToken)
    {
        var skills = new List<SkillRecord>();
        await foreach (var skillFile in gitlab.EnumerateRepositoryTreeAsync(project.Id, project.DefaultBranch, "", cancellationToken))
        {
            if (skillFile.Type != "blob" || !skillFile.Path.EndsWith("SKILL.md", StringComparison.OrdinalIgnoreCase)) continue;

            var markdown = await gitlab.GetRawFileAsync(project.Id, skillFile.Path, project.DefaultBranch, cancellationToken);
            var metadata = SkillMarkdownParser.Parse(markdown);
            var repoUrl = project.SshUrlToRepo ?? project.HttpUrlToRepo ?? throw new InvalidOperationException("GitLab project has no clone URL.");
            skills.Add(SkillCatalog.BuildSkillRecord(
                project.Id,
                repoUrl,
                project.DefaultBranch,
                skillFile.Path,
                project.LastActivityAt.ToString("O"),
                project.LastActivityAt,
                metadata.Name,
                metadata.Description));
        }

        return skills;
    }
}
