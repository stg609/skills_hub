# Skills Hub

Company-internal catalog for coding-agent skills. GitLab is the source of truth: the backend indexes configured GitLab Groups, discovers `SKILL.md` files, and exposes a public no-login catalog for browsing, liking, downloading, and copying install commands.

## Architecture

- Frontend: React + Vite, served as static files.
- Backend: ASP.NET Core (.NET 8) API, scheduled GitLab indexing, zip package generation.
- Database: PostgreSQL.
- Source: Company GitLab Groups.
- Storage: No object storage required for V1.
- Scale posture: paginated database-side listing, `skill_stats` counters, daily download buckets, and trigram search for hundreds of thousands of skills.

Next.js is intentionally not used as the backend runtime. This project is API/job-heavy rather than SSR-heavy, and a small ASP.NET Core service is easier to operate in resource-limited Kubernetes for a C#-capable team.

## Local Development

Install dependencies:

```bash
npm install
```

Run tests:

```bash
npm test
```

Run backend:

```bash
dotnet run --project apps/api/SkillsHub.Api.csproj --urls http://127.0.0.1:5000
```

Run frontend:

```bash
npm run dev:frontend
```

The Vite dev server proxies `/api` to `http://127.0.0.1:5000`.

## Local GitLab Index Test

Use this flow when testing against a real company GitLab group from your machine.

1. If PostgreSQL is inside Kubernetes, forward it to localhost:

```powershell
kubectl port-forward -n postgres-dev svc/postgres 5432:5432
```

2. In a new PowerShell window, set environment variables:

```powershell
$env:DATABASE_URL="Host=127.0.0.1;Port=5432;Database=skills_hub_module;Username=postgres;Password=<password>"
$env:GITLAB_BASE_URL="https://gitlab.company.local"
$env:GITLAB_TOKEN="<gitlab-read-token>"
$env:GITLAB_GROUPS="your-group-path"
$env:GITLAB_SCAN_ALL_PROJECTS="false"
$env:GITLAB_RECURSIVE_SKILL_DISCOVERY="false"
$env:INTERNAL_SYNC_TOKEN="dev-sync-token"
$env:WEB_ORIGIN="http://localhost:5173"
```

`GITLAB_GROUPS` must be the GitLab group path, not the display name. For a subgroup, use a slash path such as `platform/agent-skills`.

Keep `GITLAB_RECURSIVE_SKILL_DISCOVERY=false` when GitLab resources are tight. In this mode the sync only checks each project's repository root for `SKILL.md`; it does not clone repositories and does not recursively scan all files. Set it to `true` only when skills intentionally live in subdirectories such as `some-skill/SKILL.md`.

3. Apply migrations:

```powershell
npm run migrate
```

4. Start the API:

```powershell
dotnet run --project apps/api/SkillsHub.Api.csproj --urls http://127.0.0.1:5000
```

5. Trigger GitLab indexing manually from another PowerShell window:

```powershell
Invoke-RestMethod `
  -Uri "http://127.0.0.1:5000/api/internal/sync/gitlab" `
  -Method Post `
  -Headers @{ "x-internal-sync-token" = "dev-sync-token" } |
  ConvertTo-Json -Depth 5
```

6. Check sync status:

```powershell
Invoke-RestMethod `
  -Uri "http://127.0.0.1:5000/api/internal/sync/status" `
  -Headers @{ "x-internal-sync-token" = "dev-sync-token" } |
  ConvertTo-Json -Depth 5
```

7. Check indexed skills:

```powershell
Invoke-RestMethod `
  -Uri "http://127.0.0.1:5000/api/skills?page=1&pageSize=30" |
  ConvertTo-Json -Depth 8
```

8. Start the frontend:

```powershell
npm run dev:frontend
```

Open `http://localhost:5173`.

## Environment

Copy `.env.example` and fill company values:

```bash
copy .env.example .env
```

Important variables:

- `DATABASE_URL`
- `GITLAB_BASE_URL`
- `GITLAB_TOKEN`
- `GITLAB_GROUPS`
- `GITLAB_SCAN_ALL_PROJECTS` - set to `true` to scan all projects visible to the token instead of configured groups.
- `GITLAB_RECURSIVE_SKILL_DISCOVERY` - default `false`; set to `true` only if skills live in subdirectories and GitLab can handle recursive tree scans.
- `INTERNAL_SYNC_TOKEN`
- `PG_POOL_MAX`
- `MAX_PACKAGE_FILES`
- `MAX_PACKAGE_BYTES`
- `MAX_CONCURRENT_PACKAGE_BUILDS`
- `HUB_PUBLIC_URL`
- `WRAPPER_PACKAGE_NAME`

`GITLAB_TOKEN` and `INTERNAL_SYNC_TOKEN` are different secrets:

- `GITLAB_TOKEN` is sent to GitLab. It should belong to a dedicated bot user with the minimum GitLab role/scope required to read configured projects, typically `Reporter` plus `read_api` and `read_repository`.
- `INTERNAL_SYNC_TOKEN` is only checked by this Hub API. It protects internal Hub endpoints such as `/api/internal/sync/gitlab`, `/api/internal/sync/status`, and `/api/internal/stats/rebuild`. It is not sent to GitLab. For local testing, any strong random string works; `dev-sync-token` is just an example.

## Database

Apply the initial migration:

```bash
dotnet build apps/api/SkillsHub.Api.csproj
npm run migrate
```

SQL lives in `apps/api/migrations/001_initial.sql`.

## Production Build

```bash
npm run build
```

## Kubernetes Deploy

1. Build and push images:

```bash
docker build -f apps/api/Dockerfile -t registry.company.local/skills-hub-api:latest .
docker build -f apps/web/Dockerfile -t registry.company.local/skills-hub-web:latest .
docker push registry.company.local/skills-hub-api:latest
docker push registry.company.local/skills-hub-web:latest
```

2. Update image names in `deploy/k8s/api.yaml` and `deploy/k8s/web.yaml`.

3. Copy and fill the secret:

```bash
copy deploy/k8s/secret.example.yaml deploy/k8s/secret.yaml
```

Add `secret.yaml` to `deploy/k8s/kustomization.yaml`.

4. Deploy:

```bash
kubectl apply -k deploy/k8s
```

5. Trigger first sync from inside the cluster:

```bash
kubectl run skills-hub-sync --rm -it --restart=Never --image=curlimages/curl -- \
  curl -X POST http://skills-hub-api:8080/api/internal/sync/gitlab \
  -H "x-internal-sync-token: <INTERNAL_SYNC_TOKEN>"
```

The public Ingress intentionally routes only public API paths. `/api/internal/*` is for in-cluster operations.

## API

- `GET /api/skills?q=&sort=updated&page=1&pageSize=30`
- `GET /api/skills/:slug`
- `POST /api/skills/:slug/like`
- `DELETE /api/skills/:slug/like`
- `GET /api/skills/:slug/download`
- `GET /api/skills/:slug/install` - returns native and tracked install commands without changing metrics.
- `POST /api/skills/:slug/install` - records `install_command_copied` after clipboard copy succeeds.
- `POST /api/skills/:slug/install/wrapper` - records `wrapper_cli_installed` for the company wrapper CLI.
- `POST /api/internal/sync/gitlab`
- `GET /api/internal/sync/status`
- `POST /api/internal/stats/rebuild`
- `GET /healthz`
- `GET /readyz`

Native `npx skills add <gitlab-url> --skill <name>` talks directly to GitLab and cannot report actual installs to the Hub. Publish `packages/skills-hub-cli` to the company npm registry and show `trackedInstallCommand` when exact CLI install counting is required.
