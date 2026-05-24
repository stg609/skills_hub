using SkillsHub.Api.Domain;

namespace SkillsHub.Api;

public static class DemoSkills
{
    public static IReadOnlyList<SkillRecord> Create() =>
    [
        SkillCatalog.BuildSkillRecord(
            101,
            "git@gitlab.company.local:agent-skills/dev-quality.git",
            "main",
            "codex-review-pack/SKILL.md",
            "abc123",
            DateTimeOffset.Parse("2026-05-23T01:18:00Z"),
            "codex-review-pack",
            "Pull request review, CI failure triage, and risky diff inspection for coding agents."),
        SkillCatalog.BuildSkillRecord(
            102,
            "git@gitlab.company.local:agent-skills/product.git",
            "main",
            "frontend-taste-lab/SKILL.md",
            "def456",
            DateTimeOffset.Parse("2026-05-22T09:42:00Z"),
            "frontend-taste-lab",
            "Opinionated UI critique prompts, layout heuristics, and visual QA checklists.")
    ];
}
