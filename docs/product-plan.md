# Skills Hub V1 Product Plan

## Summary

Skills Hub is an internal catalog for coding-agent skills. V1 uses company GitLab as the only source of truth. The hub scans configured GitLab Groups, discovers `SKILL.md` files, indexes public metadata, and exposes a no-login web catalog for browsing, downloading, liking, and copying install commands.

V1 intentionally excludes upload flows, login, admin pages, semantic versions, plugin bundles, and required object storage. Categories are fixed to the built-in default category. Future versions can add LLM-based category organization, admin tooling, OIDC login, MinIO cache/snapshots, and plugin bundle support.

## Product Decisions

- GitLab Group indexing is the only V1 ingestion path.
- Repositories may contain multiple skills; each `SKILL.md` directory is one skill.
- Skill identity is based on GitLab project plus `SKILL.md` directory path.
- Metadata comes from `SKILL.md` frontmatter:
  - `name`
  - `description`
- All skills use `默认分类` in V1.
- V1 shows last update time instead of semantic versions.
- MinIO is not required in V1; downloads can be generated from GitLab on demand.
- Anonymous users can browse, download, install, and like.
- Likes are de-duplicated by long-lived anonymous visitor ID.
- Metrics distinguish zip downloads, install command copies, future Hub-started installs, and future wrapper-CLI installs.
- Missing GitLab skills are only deactivated after repeated successful sync runs do not see them.

## User Experience

- The list page is a dense internal index: left category/sync rail, central skill list, right detail drawer.
- Users can search by name, description, or GitLab path.
- Users can sort by last update, 1-day downloads, 7-day downloads, or all-time downloads.
- Selecting a skill updates the detail drawer.
- The detail drawer shows metadata, GitLab source, download stats, likes, and a copyable install command.
- Install command format assumes users have internal GitLab access:

```bash
npx skills add <company-gitlab-repo-url> --skill <skill-name>
```

## Architecture

- Frontend: React + Vite.
- Backend: ASP.NET Core (.NET 8) + PostgreSQL.
- Shared core package: pure parsing, indexing, sorting, and stats rules.
- GitLab adapter: lists projects under configured groups, reads repository trees/files, and maps discovered `SKILL.md` files into skill records.
- Storage: no required object storage in V1. MinIO remains an optional future cache layer.

## Backend Runtime Decision

Next.js is not the preferred backend runtime for this project. The backend workload is API-heavy and job-heavy: GitLab group scanning, metadata parsing, download packaging, stats writes, and scheduled synchronization. In an internal Kubernetes cluster with limited CPU and memory, a dedicated ASP.NET Core API is a better fit because it can run as a small long-lived service, keep background jobs and dependency injection explicit, and avoid coupling backend resource use to frontend rendering concerns.

For at least 50 concurrent users, the likely bottlenecks are not HTTP request handling; they are GitLab API fan-out, zip packaging memory, database connection count, and download bandwidth. The V1 service should run with small resource requests, a bounded PostgreSQL connection pool, paginated GitLab API access, and no repository cloning inside the Pod. If frontend SSR is later needed, Next.js can be introduced as a separate web tier or BFF, not as the source indexing backend.

Initial Kubernetes posture:

- API Pod: 1 replica minimum, 2 replicas if manual sync is rare and DB pool is capped.
- Sync: keep in the API for V1; split to a CronJob/worker when GitLab group size grows.
- Web: static Vite build served by Nginx or another small static server.
- Downloads: generate from GitLab API on demand; add MinIO cache only if repeated large downloads become expensive.
- DB: PostgreSQL for persistent skills, stats counters, daily download buckets, likes, download events, and sync runs.

## Public API

- `GET /api/skills`
  - Query: `q`, `sort`, `page`, `pageSize`
  - Sort values: `updated`, `downloads_1d`, `downloads_7d`, `downloads_all`
  - Returns a page object: `items`, `page`, `pageSize`, `total`, `totalRelation`.
- `GET /api/skills/:slug`
- `POST /api/skills/:slug/like`
- `DELETE /api/skills/:slug/like`
- `GET /api/skills/:slug/download`
- `GET /api/skills/:slug/install`
  - Returns the native install command and the Hub-tracked wrapper command without changing metrics.
- `POST /api/skills/:slug/install`
  - Records `install_command_copied` after the browser confirms clipboard write.
- `POST /api/skills/:slug/install/wrapper`
  - Records `wrapper_cli_installed` for the company npm wrapper CLI before it delegates to native `npx skills add`.

Note: direct `npx skills add <gitlab-url> --skill <name>` does not naturally call the Hub, so the Hub cannot count those installs. Exact CLI install counting requires publishing and using the company wrapper package from `packages/skills-hub-cli`; native commands remain available for compatibility but only clipboard-copy actions are counted.

## Internal API

- `POST /api/internal/sync/gitlab`
  - Protected by internal token in V1.
  - Triggers GitLab group indexing.
- `POST /api/internal/stats/rebuild`
  - Protected by internal token.
  - Rebuilds derived daily/stat tables from source events and likes.

## Future Extension Points

- Add `artifactType` values beyond `skill`, especially `plugin`.
- Add LLM category suggestions from frontmatter and description.
- Add admin review and manual category management.
- Add account/password auth first, then OIDC.
- Add MinIO cache/snapshots when downloads need acceleration or auditability.
