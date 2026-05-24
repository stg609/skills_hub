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
- `INTERNAL_SYNC_TOKEN`
- `PG_POOL_MAX`
- `MAX_PACKAGE_FILES`
- `MAX_PACKAGE_BYTES`
- `MAX_CONCURRENT_PACKAGE_BUILDS`
- `HUB_PUBLIC_URL`
- `WRAPPER_PACKAGE_NAME`

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
