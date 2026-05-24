using System.IO.Compression;
using SkillsHub.Api.Domain;
using SkillsHub.Api.GitLab;

namespace SkillsHub.Api.Packaging;

public sealed class SkillPackageBuilder
{
    public IReadOnlyList<(string SourcePath, string ArchivePath)> BuildEntries(SkillRecord skill, IReadOnlyList<GitLabTreeItem> tree, AppConfig config)
    {
        var files = tree
            .Where(item => item.Type == "blob")
            .Where(item => IsInsideSkillDir(NormalizePath(item.Path), skill.Source.SkillDir))
            .ToList();

        if (files.Count > config.MaxPackageFiles)
            throw new InvalidOperationException($"skill package has too many files: {files.Count} > {config.MaxPackageFiles}");

        var totalBytes = files.Sum(file => file.Size ?? 0);
        if (totalBytes > config.MaxPackageBytes)
            throw new InvalidOperationException($"skill package is too large: {totalBytes} > {config.MaxPackageBytes}");

        return files.Select(file =>
        {
            var sourcePath = NormalizePath(file.Path);
            var archivePath = skill.Source.SkillDir == "."
                ? sourcePath
                : sourcePath[(skill.Source.SkillDir.Length + 1)..];
            AssertSafeArchivePath(archivePath);
            return (sourcePath, archivePath);
        }).ToList();
    }

    public static byte[] Zip(IReadOnlyDictionary<string, byte[]> files)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, bytes) in files)
            {
                var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
                using var stream = entry.Open();
                stream.Write(bytes);
            }
        }
        return output.ToArray();
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/').Trim('/');

    private static bool IsInsideSkillDir(string path, string skillDir) =>
        skillDir == "." || path == skillDir || path.StartsWith(skillDir + "/", StringComparison.Ordinal);

    private static void AssertSafeArchivePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.Contains("../", StringComparison.Ordinal) || path == ".." || path.Contains('\0'))
            throw new InvalidOperationException($"unsafe path in skill package: {path}");
    }
}
