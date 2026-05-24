export function discoverSkillFiles(tree) {
  if (!Array.isArray(tree)) {
    throw new Error("repository tree must be an array");
  }

  return tree
    .filter((item) => item?.type === "blob" && /(^|\/)SKILL\.md$/i.test(item.path ?? ""))
    .map((item) => {
      const skillFilePath = normalizePath(item.path);
      return {
        skillDir: dirname(skillFilePath),
        skillFilePath
      };
    })
    .sort((left, right) => left.skillFilePath.localeCompare(right.skillFilePath));
}

function normalizePath(path) {
  return path.replaceAll("\\", "/").replace(/^\/+|\/+$/g, "");
}

function dirname(path) {
  const index = path.lastIndexOf("/");
  return index === -1 ? "." : path.slice(0, index);
}
