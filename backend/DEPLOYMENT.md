# Cloud deployment

Deployment date: 2026-08-09

- Worker: `community-football-club-manager-api`
- Base URL: `https://community-football-club-manager-api.old-mud-b712.workers.dev/`
- D1: `community-football-club-manager` (`8bcd4ffb-d801-4d51-b607-5f0031b6cf6e`, APAC)
- Health route: `/health`
- Mobile API prefix: `/v1/`

The Worker is deployed with D1, JWT/refresh-session authentication, tenant-scoped
RBAC, admin Founder management, member/profile/club APIs, attendance/check-in/out,
tuition, notification and snapshot routes.

R2 is enabled on the Cloudflare account. The APAC Standard bucket
`community-football-club-manager-files` is bound to the Worker as `FILES`, so
private logo/avatar/selfie/bill/PDF media routes are ready for online storage.

Secrets are managed as Worker secrets (`JWT_SECRET` and
`ADMIN_BOOTSTRAP_SECRET`) and are intentionally not stored in this repository.
