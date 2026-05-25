using SkillsHub.Api.Domain;
using SkillsHub.Api.GitLab;
using System.Runtime.CompilerServices;

namespace SkillsHub.Api.Sync;

public sealed class GitLabIndexer(GitLabClient gitlab, AppConfig config)
{
    public async Task<IReadOnlyList<SkillRecord>> IndexAsync(CancellationToken cancellationToken)
    {
        var indexed = new List<SkillRecord>();
        await foreach (var project in EnumerateProjectsAsync(cancellationToken))
        {
            var tree = await gitlab.ListRepositoryTreeAsync(project.Id, project.DefaultBranch, "", config.GitLabRecursiveSkillDiscovery, cancellationToken);
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
        return indexed;
    }

    public async IAsyncEnumerable<IReadOnlyList<SkillRecord>> IndexBatchesAsync(int batchSize, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var batch = new List<SkillRecord>(batchSize);
        await foreach (var project in EnumerateProjectsAsync(cancellationToken))
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

        if (batch.Count > 0) yield return batch;
    }

    private async IAsyncEnumerable<GitLabProject> EnumerateProjectsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (config.GitLabScanAllProjects)
        {
            // 中文注释：全量扫描只枚举当前 token 有成员关系的项目，避免管理员 token 意外扫入整个 GitLab 实例。
            await foreach (var project in gitlab.EnumerateAccessibleProjectsAsync(cancellationToken))
            {
                yield return project;
            }

            yield break;
        }

        foreach (var group in config.GitLabGroups)
        {
            await foreach (var project in gitlab.EnumerateGroupProjectsAsync(group, cancellationToken))
            {
                yield return project;
            }
        }
    }

    private async Task<IReadOnlyList<SkillRecord>> IndexProjectAsync(GitLabProject project, CancellationToken cancellationToken)
    {
        var skills = new List<SkillRecord>();
        await foreach (var skillFile in gitlab.EnumerateRepositoryTreeAsync(project.Id, project.DefaultBranch, "", config.GitLabRecursiveSkillDiscovery, cancellationToken))
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
