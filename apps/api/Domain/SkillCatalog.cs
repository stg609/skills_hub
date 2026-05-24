namespace SkillsHub.Api.Domain;

public static class SkillCatalog
{
    public const string DefaultCategory = "默认分类";

    public static SkillRecord BuildSkillRecord(
        long projectId,
        string repoUrl,
        string defaultBranch,
        string skillPath,
        string commitSha,
        DateTimeOffset updatedAt,
        string name,
        string description)
    {
        var skillDir = skillPath.EndsWith("/SKILL.md", StringComparison.OrdinalIgnoreCase)
            ? skillPath[..^"/SKILL.md".Length]
            : ".";
        var slug = Slugify(name);

        return new SkillRecord(
            $"gitlab:{projectId}:{skillDir}",
            slug,
            name,
            description,
            DefaultCategory,
            "skill",
            new SkillSource("gitlab", projectId, repoUrl, defaultBranch, skillDir, skillPath, commitSha),
            updatedAt,
            $"npx skills add {repoUrl} --skill {name}",
            new Downloads(0, 0, 0),
            0);
    }

    public static IReadOnlyList<SkillRecord> Sort(IEnumerable<SkillRecord> skills, string sort) =>
        sort switch
        {
            "downloads_1d" => skills.OrderByDescending(skill => skill.Downloads.D1).ToList(),
            "downloads_7d" => skills.OrderByDescending(skill => skill.Downloads.D7).ToList(),
            "downloads_all" => skills.OrderByDescending(skill => skill.Downloads.All).ToList(),
            _ => skills.OrderByDescending(skill => skill.UpdatedAt).ToList()
        };

    private static string Slugify(string value)
    {
        var chars = value.Trim().ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
}
