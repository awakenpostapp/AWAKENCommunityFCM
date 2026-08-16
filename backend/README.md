# AWAKEN Community FCM API 3.0

Production status (2026-08-12): R2 is enabled. Bucket `community-football-club-manager-files` (APAC, Standard) is bound to Worker `community-football-club-manager-api` as `FILES`. Founder registration approval, OAuth PKCE routes and hourly missed-check-in processing are deployed.

Coach check-in policy: check-in opens 60 minutes before the scheduled class start. At two hours after the scheduled class end, any assigned Coach without a check-in is recorded as `Vắng check-in`, locked from checking in, and never contributes salary. A Worker Cron runs hourly; snapshots and the check-in endpoint also run the same repair idempotently.

Backend online cho ứng dụng .NET MAUI, chạy trên Cloudflare Workers với:

- **D1**: account, tenant, lớp, điểm danh, học phí, lương, thông báo và audit log.
- **R2**: logo, avatar, selfie check-in/check-out, bill và PDF hóa đơn. Bucket luôn private; file chỉ được đọc qua API có xác thực.
- **Workers**: REST API, JWT access token ngắn hạn và refresh session có rotation.

## Kiến trúc tenant và phân quyền

- `admin`: tài khoản quản trị toàn hệ thống, không thuộc tenant và không nhận snapshot vận hành đội.
- `founder`: mỗi account tạo một tenant/đội riêng, có toàn quyền trong tenant đó.
- `coach`: chỉ thấy lớp được phân công. Danh sách học viên chỉ xuất hiện khi Coach đã check-in và chưa check-out.
- `trainee`: chỉ thấy lớp của mình, Coach và các học viên học cùng lớp; chỉ thấy điểm danh/học phí của chính mình.

Mọi câu lệnh tenant-scoped lấy `tenant_id` từ JWT/session phía server. API không tin `tenantId`, `clubId` hoặc role do client gửi.

## Cấu trúc

```text
backend/
  migrations/0001_initial.sql     D1 schema multi-tenant
  src/                             TypeScript source
  dist/worker.mjs                  Worker ES module single-file để deploy không cần npm
  dist/worker.mjs.map              Source map phục vụ debug
  wrangler.jsonc                   D1/R2 binding template
  worker-configuration.d.ts        Bootstrap type file; được thay bởi wrangler types
  .dev.vars.example                Tên secret mẫu, không chứa secret thật
```

## Founder approval và OAuth

- Public Founder registration is stored with a suspended tenant and inactive user. The account receives no session until an Admin confirms it from the Admin Founder screen.
- Google login/linking uses the Worker OAuth start/callback/exchange endpoints with Authorization Code + PKCE. Google credentials are configured as private Worker secrets; Apple login/linking is not supported.

## Trạng thái Cloudflare hiện tại

- D1 production: `community-football-club-manager`
- D1 database ID: `8bcd4ffb-d801-4d51-b607-5f0031b6cf6e`
- R2 production bucket `community-football-club-manager-files` is enabled and bound as `FILES`.

Khi không có binding `FILES`, các API D1 vẫn hoạt động; `/v1/uploads` trả `503 storage_unavailable`. Khi R2 đã được bật, tạo bucket và thêm binding `FILES` để kích hoạt media.

## Thiết lập bằng Wrangler

Yêu cầu Node.js 20+ và Wrangler 4.x.

```powershell
cd backend
npm install
Copy-Item .dev.vars.example .dev.vars
npm run typegen
npm run db:migrate:local
npm run dev
```

Không commit `.dev.vars`. Tạo hai secret độc lập, ngẫu nhiên và dài tối thiểu 32 bytes:

```powershell
npx wrangler secret put JWT_SECRET
npx wrangler secret put ADMIN_BOOTSTRAP_SECRET
```

Áp migration và deploy production:

```powershell
npx wrangler d1 migrations apply community-football-club-manager --remote
npx wrangler deploy --dry-run
npx wrangler deploy
```

Khi R2 đã được kích hoạt:

```powershell
npx wrangler r2 bucket create community-football-club-manager-files
```

Nếu R2 chưa được kích hoạt nhưng cần deploy API D1 trước, bỏ tạm block `r2_buckets` khỏi bản config dùng để deploy. `dist/worker.mjs` đã xử lý trường hợp không có `FILES`.

## Deploy không cần Node/npm

`dist/worker.mjs` là ES module đã bundle, không có import nội bộ hay dependency runtime. Có thể upload bằng Cloudflare Dashboard hoặc Workers API multipart với:

- main module: `worker.mjs`
- content type: `application/javascript+module`
- binding D1: `DB` → database ID ở trên
- text vars: `APP_ENV`, `ACCESS_TOKEN_TTL_SECONDS`, `REFRESH_TOKEN_TTL_DAYS`, `MAX_UPLOAD_BYTES`, `ALLOWED_ORIGINS`, `ALLOW_PUBLIC_FOUNDER_REGISTRATION`
- secrets: `JWT_SECRET`, `ADMIN_BOOTSTRAP_SECRET`
- binding R2 tùy chọn: `FILES`

Không nhúng secret trong metadata hoặc source bundle.

## Khởi tạo Admin một lần

Sau khi migration và deploy, gọi endpoint bootstrap. Endpoint chỉ hoạt động nếu chưa có bất kỳ Admin nào và yêu cầu secret riêng:

```http
POST /v1/setup/admin
X-Bootstrap-Secret: <ADMIN_BOOTSTRAP_SECRET>
Content-Type: application/json

{
  "username": "admin",
  "fullName": "Quản trị hệ thống",
  "email": "admin@example.com",
  "password": "StrongPassword123"
}
```

Sau khi Admin tồn tại, endpoint trả `409 admin_exists`. Có thể thay `ADMIN_BOOTSTRAP_SECRET` sau bootstrap.

## Contract xác thực

### Đăng nhập

```http
POST /v1/auth/login
Content-Type: application/json

{
  "username": "founder01",
  "password": "StrongPassword123",
  "deviceName": "Android"
}
```

Response:

```json
{
  "accessToken": "...",
  "refreshToken": "...",
  "tokenType": "Bearer",
  "expiresIn": 900,
  "accessTokenExpiresAtUtc": "2026-08-09T10:15:00.000Z",
  "refreshTokenExpiresAtUtc": "2026-09-08T10:00:00.000Z",
  "sessionId": "...",
  "user": { "id": "...", "tenantId": "...", "role": "founder" },
  "profile": { "userId": "...", "fullName": "..." },
  "activeClub": { "teamName": "..." },
  "club": { "teamName": "..." }
}
```

Role và status đều là chuỗi, không phải số. Role: `admin`, `founder`, `coach`, `trainee`.

Các endpoint xác thực:

- `POST /v1/auth/register-founder`
- `POST /v1/auth/login`
- `POST /v1/auth/refresh` body `{ "refreshToken": "..." }`
- `POST /v1/auth/logout` với Bearer access token
- `GET /v1/auth/me`
- `PATCH /v1/auth/password`

Refresh token được hash trong D1 và rotate sau mỗi lần dùng. Access token mặc định hết hạn sau 15 phút; session refresh mặc định 30 ngày. Logout và đổi/reset password sẽ revoke session.

## Contract snapshot cho app MAUI

### Tải snapshot

```http
GET /v1/sync/snapshot
Authorization: Bearer <accessToken>
```

Response có các alias tương thích client:

```json
{
  "syncVersion": 1,
  "serverTime": "2026-08-09T10:00:00.000Z",
  "role": "founder",
  "currentUser": {},
  "currentProfile": {},
  "activeClub": {},
  "club": {},
  "users": [],
  "profiles": [],
  "venues": [],
  "classes": [],
  "classCoaches": [],
  "classEnrollments": [],
  "trainingSessions": [],
  "sessionCoaches": [],
  "coachCheckIns": [],
  "attendanceRecords": [],
  "tuitionInvoices": [],
  "paymentProofs": [],
  "receipts": [],
  "coachSalaries": [],
  "notifications": []
}
```

Coach chỉ nhận roster/profile học viên trong thời gian có check-in mở. Sau check-out, response snapshot không còn `classEnrollments`, profile học viên hoặc attendance roster của lớp đó. Client phải xóa cache roster cũ sau mỗi snapshot.

### Gửi batch thay đổi

```http
PUT /v1/sync/snapshot
Authorization: Bearer <accessToken>
Idempotency-Key: <UUID-do-client-tao>
Content-Type: application/json

{
  "deviceId": "android-installation-id",
  "clientMutationId": "local-sequence-42",
  "changes": {
    "currentProfile": {},
    "activeClub": {},
    "users": [],
    "profiles": [],
    "venues": [],
    "classes": [],
    "classCoaches": [],
    "classEnrollments": [],
    "trainingSessions": [],
    "sessionCoaches": [],
    "attendanceRecords": [],
    "tuitionInvoices": [],
    "coachSalaries": [],
    "notifications": []
  }
}
```

Response:

```json
{
  "applied": true,
  "appliedCount": 18,
  "serverTime": "2026-08-09T10:00:00.000Z",
  "syncVersion": 1
}
```

`profile` là alias của `currentProfile`; `club` là alias của `activeClub`. Server lưu `sync_cursors` theo user/device và cache response theo `Idempotency-Key` trong 24 giờ.

Mỗi request tối đa 100 thay đổi/câu lệnh. Client phải chia snapshot lớn thành nhiều batch,
mỗi batch dùng một `Idempotency-Key` khác nhau. Batch Founder được sắp theo dependency
`users → profiles → venues → classes → assignments/enrollments → sessions → attendance/finance`,
vì vậy ID offline được giữ nguyên khi các bản ghi cha và con cùng được gửi trong một request.

Quyền ghi snapshot:

- Founder: nhập Coach/Trainee + profile, club/venue/class/phân công/ghi danh/session/attendance/tuition/salary/notification trong tenant. User mới giữ ID offline, nhận password `12345678` và `mustChangePassword=true`; Founder/Admin trong mảng `users` bị bỏ qua.
- Coach: profile của mình và attendance, nhưng chỉ khi đã check-in và chưa check-out.
- Trainee: profile của mình.
- `coachCheckIns`, `paymentProofs`, `receipts`, `auditLogs` không được batch upsert; API trả `422 route_required`. Các collection này phải qua endpoint nghiệp vụ để kiểm tra quyền và R2, tránh silent data loss.

## Endpoint nghiệp vụ

| Nhóm | Endpoint |
|---|---|
| Admin-Founder | `GET/POST /v1/admin/founders`, `PATCH /v1/admin/founders/{id}/password`, `DELETE /v1/admin/founders/{id}` |
| Thành viên | `GET/POST /v1/users`, `PATCH /v1/users/{id}/profile`, `PATCH /v1/users/{id}/password`, `PATCH /v1/users/{id}/status` |
| Đội/lớp | `GET/PATCH /v1/club`, `GET/POST /v1/classes` |
| Điểm danh | `GET /v1/attendance?sessionId=...`, `PUT /v1/attendance/{sessionId}` |
| Coach | `POST /v1/check-ins`, `POST /v1/check-outs`, `PATCH /v1/check-ins/{id}/review` |
| Học phí | `GET/POST /v1/tuition/invoices`, `POST /v1/tuition/invoices/{id}/proofs`, `PATCH /v1/tuition/proofs/{id}/review` |
| Thông báo | `GET /v1/notifications`, `PATCH /v1/notifications/{id}/read` |
| Media | `POST /v1/uploads`, `GET /v1/uploads/{id}` |

Founder review is checkout-based: the Worker rejects approval until the row
has a checkout timestamp and a non-empty checkout selfie object key. Private
check-in and checkout previews are streamed through
`GET /v1/check-ins/{id}/selfie` and
`GET /v1/check-ins/{id}/checkout-selfie`. An abandoned open check-in is safely
closed after eight hours with an empty checkout key; this hides the roster and
stops the timer but never creates salary.

`POST /v1/users` nhận `username`, `fullName`, `email?`, `phone?`, `guardianName?`,
`guardianPhone?`, `role` (`coach` hoặc `trainee`), `isTuitionSupported?` và `password?`.
Nếu không truyền password, backend dùng mật khẩu khởi tạo `12345678` và luôn đặt
`mustChangePassword=true`.

## Upload private qua R2

Upload dùng raw body có giới hạn kích thước, không dùng base64:

```http
POST /v1/uploads?purpose=checkin_selfie
Authorization: Bearer <accessToken>
Content-Type: image/jpeg
Content-Length: 123456

<binary>
```

Purpose hợp lệ: `avatar`, `club_logo`, `checkin_selfie`, `checkout_selfie`, `payment_proof`, `receipt`.

File chỉ tải qua `GET /v1/uploads/{id}` với Bearer token và tenant check. Không cấu hình bucket public.

## Quy tắc vận hành và bảo mật

- Đặt `ALLOWED_ORIGINS` thành danh sách origin chính xác, phân cách bằng dấu phẩy; không dùng `*` cho production.
- Cấu hình Cloudflare Rate Limiting/WAF cho `/v1/auth/*`, `/v1/setup/admin` và `/v1/uploads`.
- Chỉ log request ID/path/status; không log password, JWT, refresh token hoặc nội dung file.
- D1 migration được version-control. Backup trước migration production bằng `wrangler d1 export`.
- Xóa Founder là soft delete: khóa user, revoke session và đánh dấu tenant `deleted`; dữ liệu nghiệp vụ không bị xóa ngay.
- Dọn `idempotency_keys` quá hạn bằng cron/maintenance khi lượng dữ liệu tăng.

## Chức năng chưa bật trong scaffold

- Quên mật khẩu qua email chưa được bật vì chưa có nhà cung cấp email/OTP. Không nên reset
  password chỉ dựa vào email do client gửi. Admin có thể reset Founder và Founder có thể reset
  Coach/Trainee; self-service reset nên được bổ sung cùng email verification trước production.
- R2 media chỉ hoạt động sau khi account Cloudflare bật R2 và binding `FILES` được cấu hình.

## Kiểm tra trước deploy

```powershell
npm run typegen
npm run typecheck
npx wrangler d1 migrations apply community-football-club-manager --local
npx wrangler deploy --dry-run
```

Bundle không cần Node được tạo bằng:

```powershell
esbuild src/index.ts --bundle --format=esm --platform=browser --target=es2022 --outfile=dist/worker.mjs --sourcemap
```
