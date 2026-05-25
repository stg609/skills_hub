# Kubernetes Deployment Notes

## Runtime Choice

Use a small ASP.NET Core (.NET 8) API deployment plus a static Vite web deployment. Do not run Next.js as the backend for V1; the backend needs predictable API/job behavior, not SSR.

## Required Environment

API:

- `DATABASE_URL`
- `GITLAB_BASE_URL`
- `GITLAB_TOKEN`
- `GITLAB_GROUPS`
- `GITLAB_SCAN_ALL_PROJECTS=false`
- `GITLAB_RECURSIVE_SKILL_DISCOVERY=false`
- `INTERNAL_SYNC_TOKEN`
- `PG_POOL_MAX=5`
- `WEB_ORIGIN`

Web:

- Serve the built `apps/web/dist` as static files.
- Route `/api/*` to the API service through ingress or reverse proxy.

GitLab scope:

- Prefer `GITLAB_GROUPS=group-a,group-b` for predictable sync scope.
- Set `GITLAB_SCAN_ALL_PROJECTS=true` only when the Hub should scan every project visible to the token. The implementation uses GitLab `membership=true`, so it does not intentionally scan projects outside the token's memberships.
- Keep `GITLAB_RECURSIVE_SKILL_DISCOVERY=false` when GitLab resources are tight. In this mode sync only checks the repository root for `SKILL.md`; no repository clone is performed. Set it to `true` only when your skill repositories intentionally store skills in subdirectories.

## Resource Starting Point

API:

- requests: `cpu: 100m`, `memory: 192Mi`
- limits: `cpu: 500m`, `memory: 512Mi`
- replicas: 2 for V1. GitLab sync, stats refresh, and migrations use PostgreSQL locks to avoid concurrent execution.

Web:

- requests: `cpu: 25m`, `memory: 64Mi`
- limits: `cpu: 100m`, `memory: 128Mi`

PostgreSQL connection pool:

- Keep `PG_POOL_MAX` small, default `5`.
- If API replicas increase, total pool size is `replicas * PG_POOL_MAX`.

## Migration

Apply SQL in `apps/api/migrations/*.sql` before starting the API with `DATABASE_URL`.

The provided Kubernetes deployment runs the same migration through an API initContainer. The runner uses a PostgreSQL advisory lock, so multiple API pods will not execute DDL concurrently:

```bash
dotnet SkillsHub.Api.dll --migrate
```

## Deploy Steps

1. Build and push images:

```bash
docker build -f apps/api/Dockerfile -t registry.company.local/skills-hub-api:latest .
docker build -f apps/web/Dockerfile -t registry.company.local/skills-hub-web:latest .
docker push registry.company.local/skills-hub-api:latest
docker push registry.company.local/skills-hub-web:latest
```

2. Update image names in `deploy/k8s/api.yaml` and `deploy/k8s/web.yaml`.

3. Create the secret:

```bash
copy deploy/k8s/secret.example.yaml deploy/k8s/secret.yaml
```

Fill real `DATABASE_URL`, `GITLAB_TOKEN`, and `INTERNAL_SYNC_TOKEN`, then add `secret.yaml` to `deploy/k8s/kustomization.yaml`.

4. Apply manifests:

```bash
kubectl apply -k deploy/k8s
```

5. Trigger the first GitLab sync from inside the cluster or through a protected internal route:

```bash
kubectl run skills-hub-sync --rm -i --restart=Never --image=curlimages/curl -- \
  curl -X POST http://skills-hub-api:8080/api/internal/sync/gitlab \
  -H "x-internal-sync-token: <INTERNAL_SYNC_TOKEN>"
```

6. Check sync status:

```bash
kubectl run skills-hub-status --rm -i --restart=Never --image=curlimages/curl -- \
  curl http://skills-hub-api:8080/api/internal/sync/status \
  -H "x-internal-sync-token: <INTERNAL_SYNC_TOKEN>"
```

## Production Guards

- API startup fails outside Development when `DATABASE_URL` is missing; in-memory demo data is development-only.
- `MAX_PACKAGE_FILES` rejects large skill packages before downloading all files.
- `MAX_PACKAGE_BYTES` rejects packages whose GitLab tree reports too many bytes.
- Download archive paths are normalized and checked to prevent unsafe zip paths.
- Manual sync and stats refresh are guarded by database locks. GitLab sync renews the lock while running.
- Internal sync/status/rebuild endpoints require `INTERNAL_SYNC_TOKEN`; the provided public Ingress only routes `/api`, `/api/skills`, `/healthz`, and `/readyz`, so `/api/internal/*` is not exposed through the public host. Use an internal-only route, port-forward, or in-cluster job for operations.
- `PG_POOL_MAX` defaults to `5`; keep it low in small clusters.
