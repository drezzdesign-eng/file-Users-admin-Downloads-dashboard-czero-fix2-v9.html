# C ZERO Dashboard API

ASP.NET Core + PostgreSQL backend for `index.html` (Ops Monitor) and `coaching.html` (Coaching).
It replaces the old JSONBin storage and the passwords/PINs that were hard-coded in the pages.

The HTML pages call it at `/api/...` — on the live site nginx forwards
`https://fleet-managing.czeros.tech/api/` to this container (`127.0.0.1:8097`).

## Endpoints

| Method | Path | Who | Used by |
|---|---|---|---|
| GET | `/api/board` | public | TV board — teams + last 20 checkpoints, no PINs |
| POST | `/api/auth/ops-admin` `{pin}` | — | ⚙ Settings PIN → token |
| GET / PUT | `/api/teams` | ops admin | ⚙ Edit tab (GET includes PINs) |
| POST | `/api/teams/{id}/pin-check` `{pin}` | — | Check-in PIN screen |
| POST | `/api/teams/{id}/checkins` `{pin, stage, lat, lng}` | team PIN | Check-in buttons (also updates KPI) |
| GET / PUT | `/api/staff` | ops admin | ⚙ Staff tab |
| GET | `/api/kpi` | ops admin | ⚙ KPI tab |
| POST | `/api/auth/coaching` `{role, password}` | — | Coaching login (`admin` / `hr`) |
| GET | `/api/coaching/records` | coach admin / HR | Coaching (HR gets its department only) |
| PUT | `/api/coaching/records` | coach admin | Save button |
| DELETE | `/api/coaching/records?name=` | coach admin | Delete button |
| POST | `/api/coaching/records/import` | coach admin | Import button |
| GET | `/api/coaching/ops-jobs?name=` | coach admin | "Import dari Ops Monitor" |
| POST | `/api/admin/import-jsonbin` | ops admin | One-time move of old JSONBin data |
| GET | `/api/health` | public | Health check |

PIN/password endpoints are limited to 10 tries per minute per IP; check-ins to 30.

## Run

```bash
cp .env.example .env   # fill in real values
docker compose up -d --build
```

Database tables are created/updated automatically on startup from `CZero.Api/Migrations/*.sql`
(add a new numbered file for any schema change — never edit an applied one).

## Deploy

Push to `main` with changes under `backend/` → `.github/workflows/deploy-backend.yml` copies this
folder to `/opt/czero-dashboard-api` on the VPS and runs `docker compose up -d --build`.
The `.env` file lives only on the VPS.
