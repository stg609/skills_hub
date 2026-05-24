export const DEFAULT_CATEGORY = "默认分类";

export function buildSkillRecord(source) {
  const skillDir = source.skillPath.replace(/\/?SKILL\.md$/i, "").replace(/^\/+|\/+$/g, "") || ".";
  const slug = slugify(source.metadata.name);

  return {
    identity: `gitlab:${source.projectId}:${skillDir}`,
    slug,
    name: source.metadata.name,
    description: source.metadata.description,
    category: DEFAULT_CATEGORY,
    artifactType: "skill",
    source: {
      provider: "gitlab",
      projectId: source.projectId,
      repoUrl: source.repoUrl,
      defaultBranch: source.defaultBranch,
      skillDir,
      skillPath: source.skillPath,
      commitSha: source.commitSha
    },
    updatedAt: source.updatedAt,
    installCommand: `npx skills add ${source.repoUrl} --skill ${source.metadata.name}`,
    downloads: {
      d1: 0,
      d7: 0,
      all: 0
    },
    likes: 0
  };
}

export function sortSkillCards(cards, sortMode = "updated") {
  const copy = [...cards];
  const byNumberDesc = (reader) => (left, right) => reader(right) - reader(left);

  if (sortMode === "downloads_1d") {
    return copy.sort(byNumberDesc((card) => card.downloads?.d1 ?? 0));
  }

  if (sortMode === "downloads_7d") {
    return copy.sort(byNumberDesc((card) => card.downloads?.d7 ?? 0));
  }

  if (sortMode === "downloads_all") {
    return copy.sort(byNumberDesc((card) => card.downloads?.all ?? 0));
  }

  return copy.sort((left, right) => new Date(right.updatedAt).getTime() - new Date(left.updatedAt).getTime());
}

export function toggleAnonymousLike(existingLikes, skillSlug, visitorId) {
  if (!visitorId) {
    throw new Error("visitor id is required");
  }

  const found = existingLikes.some((like) => like.skillSlug === skillSlug && like.visitorId === visitorId);
  if (found) {
    return {
      liked: false,
      likes: existingLikes.filter((like) => like.skillSlug !== skillSlug || like.visitorId !== visitorId)
    };
  }

  return {
    liked: true,
    likes: [
      ...existingLikes,
      {
        skillSlug,
        visitorId,
        createdAt: new Date().toISOString()
      }
    ]
  };
}

function slugify(value) {
  return value
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9\u4e00-\u9fa5]+/g, "-")
    .replace(/^-+|-+$/g, "");
}
