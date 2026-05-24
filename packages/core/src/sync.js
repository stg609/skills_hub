import { createHash } from "node:crypto";

export function applyIndexedSkills(existingSkills, incomingSkills, indexedAt) {
  const missingSyncGraceRuns = 3;
  const existingByIdentity = new Map(existingSkills.map((skill) => [skill.identity, skill]));
  const normalizedIncomingSkills = ensureUniqueSlugs(incomingSkills, existingSkills);
  const incomingIdentities = new Set(normalizedIncomingSkills.map((skill) => skill.identity));
  const nextSkills = [];
  const summary = {
    created: 0,
    updated: 0,
    deactivated: 0,
    indexedAt
  };

  for (const incoming of normalizedIncomingSkills) {
    const existing = existingByIdentity.get(incoming.identity);
    if (!existing) {
      summary.created += 1;
      nextSkills.push({ ...incoming, active: true, indexedAt, missingSyncCount: 0 });
      continue;
    }

    summary.updated += 1;
    nextSkills.push({
      ...existing,
      ...incoming,
      downloads: existing.downloads,
      likes: existing.likes,
      active: true,
      indexedAt,
      missingSyncCount: 0
    });
  }

  for (const existing of existingSkills) {
    if (incomingIdentities.has(existing.identity)) continue;
    const missingSyncCount = (existing.missingSyncCount ?? 0) + 1;
    const active = missingSyncCount < missingSyncGraceRuns && existing.active !== false;
    summary.deactivated += existing.active !== false && !active ? 1 : 0;
    nextSkills.push({
      ...existing,
      active,
      indexedAt,
      missingSyncCount
    });
  }

  return {
    skills: nextSkills,
    summary
  };
}

function ensureUniqueSlugs(incomingSkills, existingSkills) {
  const previousSlugs = new Map(existingSkills.map((skill) => [skill.identity, skill.slug]));
  const used = new Map(existingSkills.map((skill) => [skill.slug, skill.identity]));

  return incomingSkills.map((skill) => {
    let slug = previousSlugs.get(skill.identity) ?? skill.slug;
    const owner = used.get(slug);
    if (owner && owner !== skill.identity) {
      slug = `${skill.slug}-${stableSuffix(skill.identity)}`;
    }

    used.set(slug, skill.identity);
    return { ...skill, slug };
  });
}

function stableSuffix(value) {
  return createHash("sha256").update(value).digest("hex").slice(0, 8);
}
