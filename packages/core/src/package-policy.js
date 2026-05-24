export function buildPackageEntries({ skillDir, files, maxFiles, maxBytes }) {
  const normalizedSkillDir = normalizePath(skillDir);
  const scopedFiles = files
    .map((file) => ({
      ...file,
      path: normalizePath(file.path)
    }))
    .filter((file) => isInsideSkillDir(file.path, normalizedSkillDir));

  if (scopedFiles.length > maxFiles) {
    throw new Error(`skill package has too many files: ${scopedFiles.length} > ${maxFiles}`);
  }

  const totalBytes = scopedFiles.reduce((sum, file) => sum + (file.size ?? 0), 0);
  if (totalBytes > maxBytes) {
    throw new Error(`skill package is too large: ${totalBytes} > ${maxBytes}`);
  }

  return scopedFiles.map((file) => {
    const archivePath = normalizedSkillDir === "."
      ? file.path
      : file.path.slice(normalizedSkillDir.length + 1);
    assertSafeArchivePath(archivePath);
    return {
      sourcePath: file.path,
      archivePath
    };
  });
}

function normalizePath(path) {
  return path.replaceAll("\\", "/").replace(/^\/+|\/+$/g, "") || ".";
}

function isInsideSkillDir(path, skillDir) {
  if (skillDir === ".") return true;
  return path === skillDir || path.startsWith(`${skillDir}/`);
}

function assertSafeArchivePath(path) {
  if (!path || path.startsWith("/") || path.includes("../") || path === ".." || path.includes("\0")) {
    throw new Error(`unsafe path in skill package: ${path}`);
  }
}
