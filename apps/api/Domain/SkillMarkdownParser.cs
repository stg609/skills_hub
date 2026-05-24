using System.Text.RegularExpressions;

namespace SkillsHub.Api.Domain;

public static partial class SkillMarkdownParser
{
    public static (string Name, string Description) Parse(string markdown)
    {
        var match = FrontmatterRegex().Match(markdown);
        if (!match.Success) throw new InvalidOperationException("frontmatter is required");

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in match.Groups["body"].Value.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var separator = line.IndexOf(':');
            if (separator < 0) continue;
            values[line[..separator].Trim()] = line[(separator + 1)..].Trim().Trim('"', '\'');
        }

        if (!values.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("name is required");
        if (!values.TryGetValue("description", out var description) || string.IsNullOrWhiteSpace(description))
            throw new InvalidOperationException("description is required");

        return (name, description);
    }

    [GeneratedRegex("^---\\r?\\n(?<body>[\\s\\S]*?)\\r?\\n---", RegexOptions.Compiled)]
    private static partial Regex FrontmatterRegex();
}
