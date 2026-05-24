export function parseSkillMarkdown(markdown) {
  if (typeof markdown !== "string") {
    throw new Error("skill markdown must be a string");
  }

  const match = markdown.match(/^---\r?\n([\s\S]*?)\r?\n---/);
  if (!match) {
    throw new Error("frontmatter is required");
  }

  const metadata = {};
  for (const rawLine of match[1].split(/\r?\n/)) {
    const line = rawLine.trim();
    if (!line || line.startsWith("#")) continue;

    const separatorIndex = line.indexOf(":");
    if (separatorIndex === -1) continue;

    const key = line.slice(0, separatorIndex).trim();
    const value = line.slice(separatorIndex + 1).trim().replace(/^["']|["']$/g, "");
    if (key) metadata[key] = value;
  }

  if (!metadata.name) {
    throw new Error("name is required");
  }

  if (!metadata.description) {
    throw new Error("description is required");
  }

  return {
    name: metadata.name,
    description: metadata.description
  };
}
