# 后续任务

## 下载包缓存

- 背景：当前用户每次下载 skill zip 时，API 都会访问 GitLab repository tree 并读取文件内容，然后重新生成 zip。即使同一个 skill 的 commit 没有变化，第二次下载也会重复访问 GitLab。
- 目标：减少 GitLab 压力，提升下载响应稳定性。
- 建议方案：
  - 使用 MinIO 作为生产缓存存储。
  - 缓存 key 使用 `skill.Identity + source.CommitSha + skill.Source.SkillDir`。
  - 第一次下载时从 GitLab 读取文件、生成 zip、写入 MinIO，并返回给用户。
  - 后续下载时如果 key 命中，直接从 MinIO 返回 zip，只记录下载事件，不访问 GitLab。
  - GitLab 同步发现 `commitSha` 变化后自然生成新的缓存 key；旧对象通过 MinIO lifecycle 策略清理。
- 注意事项：
  - 写缓存前仍需保留 `MAX_PACKAGE_FILES`、`MAX_PACKAGE_BYTES`、安全路径校验。
  - 缓存写入失败不应影响用户下载成功；可以先返回 zip，并记录告警。
  - 多 Pod 并发首次下载同一 skill 时，需要对象级锁或“先查后写、允许重复构建一次”的简单策略。
