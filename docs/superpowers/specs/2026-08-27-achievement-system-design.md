# Hệ thống thành tích — Thiết kế v1

## Mục tiêu

Thêm một phân hệ thành tích online cho Community Football Club Manager mà không thay đổi tenant, tài khoản, dữ liệu lớp học, điểm danh, học phí hoặc luồng Cloudflare/Supabase đang hoạt động.

## Phạm vi đã duyệt

- Có tab **Thành tích** riêng cho Founder/Co-Founder, Coach và Trainee.
- Có hai nhóm thành tích: **Xếp hạng giao hữu/giải đấu** và **Xếp hạng lớp học theo tuần**.
- Founder/Co-Founder và Coach được tạo yêu cầu; Coach bắt buộc nhập lý do. Yêu cầu của Coach ở trạng thái chờ duyệt. Founder/Co-Founder duyệt hoặc từ chối; Founder có quyền gỡ thành tích đã duyệt.
- Thành tích đã duyệt hiển thị cho Trainee/Coach/Founder trong 30 ngày. Sau đó bản ghi chuyển sang trạng thái hết hạn/đã gỡ khỏi danh sách hiển thị, nhưng điểm trong ledger vẫn tồn tại vĩnh viễn.
- Một Trainee không bị giới hạn số huy hiệu hoặc tổng điểm. Tính năng đổi quà giữ ở trạng thái Coming soon.
- Catalog huy hiệu là nguồn dùng chung cho mọi màn hình. UI có hai chế độ hiển thị: thẻ huy hiệu lớn và danh sách gọn; hai chế độ chỉ khác cách trình bày, không tạo hai nguồn dữ liệu.

## Quyết định kiến trúc

Chọn phương án A: API chuyên dụng và bảng riêng. Worker tiếp tục là lớp API duy nhất; Supabase production là nguồn dữ liệu chính, D1 migration được giữ đồng bộ cho môi trường dự phòng. Thành tích không được nhồi vào snapshot đăng nhập: mobile gọi endpoint role-scoped khi mở tab, cập nhật `OnlineDataState` trong bộ nhớ và chỉ tải lại khi có thao tác hoặc pull-to-refresh.

### Bảng Supabase/D1

`achievement_badges` là catalog chung:

- `id`, `key` (unique), `name`, `category`, `asset_key`, `display_size`, `points`, `sort_order`, `is_active`, `created_at`, `updated_at`.
- `category` là `match_ranking` hoặc `weekly_class_ranking`.
- `points` là số nguyên; catalog khởi tạo chỉ dùng đúng các mức đã xác nhận: 500, 150, 100, 60, 30, 20, 15, 10, -10, -30. Các huy hiệu trong ảnh chưa có mức riêng sẽ dùng một trong các mức này theo cấp huy hiệu (không dùng giá trị ngoài danh sách xác nhận). UI không tự tính lại điểm.

`trainee_achievements` là bản ghi yêu cầu/thành tích:

- `id`, `tenant_id`, `trainee_user_id`, `badge_id`, `class_id` (nullable), `category`, `title`, `event_name`, `reason`, `awarded_for_date`.
- `points_snapshot` chụp điểm tại thời điểm tạo để catalog đổi sau này không làm thay đổi lịch sử.
- `status`: `pending`, `approved`, `rejected`, `removed`, `expired`.
- `created_by_user_id`, `reviewed_by_user_id`, `reviewed_at`, `review_note`, `visible_until`, `removed_at`, `created_at`, `updated_at`.

Không xóa vật lý bản ghi thành tích khi hết 30 ngày hoặc Founder gỡ: chỉ đổi trạng thái. Tổng điểm của Trainee là tổng `points_snapshot` của bản ghi `approved`, `removed` hoặc `expired`; bản ghi `rejected` không cộng điểm. Điều này bảo toàn điểm vĩnh viễn và vẫn cho audit đầy đủ.

Index bắt buộc: tenant/status, tenant/trainee/status/visible_until, tenant/category/awarded_for_date, và unique idempotency key cho thao tác tạo/duyệt/gỡ ở API.

### Phân quyền và dữ liệu trả về

- Founder/Co-Founder: xem toàn bộ thành tích trong tenant, tạo trực tiếp ở trạng thái đã duyệt, duyệt/từ chối yêu cầu Coach, gỡ thành tích; chỉ Founder-like mới được đọc thông tin review đầy đủ.
- Coach: xem thành tích của học viên thuộc lớp được phân công và yêu cầu do mình tạo; được tạo yêu cầu khi học viên thuộc lớp mình phụ trách; `reason` bắt buộc tối thiểu một ký tự sau trim.
- Trainee: chỉ xem thành tích của chính mình; chỉ bản ghi `approved` còn `visible_until >= now` được hiển thị, cùng tổng điểm vĩnh viễn.
- Manager không có quyền tạo, duyệt hoặc gỡ thành tích; không đưa dữ liệu thành tích vào projection của Manager.
- Mọi route đều xác nhận `tenant_id`, quyền trên lớp/học viên và ghi `audit_logs`. Không dùng claim user-editable cho quyết định quyền.

## API Worker

- `GET /v1/achievement-badges`: trả catalog huy hiệu đang hoạt động.
- `GET /v1/achievements?traineeUserId=&classId=&category=&status=`: lọc theo quyền; với Trainee tự ép `traineeUserId` là chính mình và loại bản ghi quá hạn.
- `POST /v1/achievements`: nhận `traineeUserId`, `badgeId`, `classId`, `category`, `title`, `eventName`, `reason`, `awardedForDate`; kiểm tra Coach assignment/enrollment và bắt buộc lý do Coach. Founder-like được tạo bản ghi approved.
- `PATCH /v1/achievements/:id/review`: Founder-like nhận `approved` và `note`; chuyển pending → approved/rejected, gửi thông báo cho Coach/Trainee.
- `DELETE /v1/achievements/:id`: Founder-only soft-remove; không xóa điểm/ledger.
- Scheduled Worker mở rộng maintenance để đổi `approved` quá `visible_until` thành `expired`; thao tác idempotent.

Thông báo dùng bảng `notifications`: yêu cầu mới gửi Founder/Co-Founder; duyệt gửi Coach và Trainee; từ chối gửi Coach; hoàn tất nhóm yêu cầu có thể hiển thị thông báo tổng hợp. Không yêu cầu FCM mới; app đồng bộ notification hiện có.

## Android/MAUI

- Thêm model `AchievementBadge` và `TraineeAchievement`, enum category/status và bộ chuyển đổi JSON.
- Thêm DTO/API methods trong `CloudApiClient`, các phương thức role-scoped trong `AppDatabase`, cùng list volatile trong `OnlineDataState`.
- Thêm `AchievementPages.cs`: danh sách theo nhóm, tổng điểm, bộ lọc, chuyển đổi thẻ lớn/danh sách gọn; Founder có vùng “Chờ duyệt” và nút duyệt/từ chối/gỡ; Coach có form tạo với lý do bắt buộc; Trainee chỉ xem.
- Tab dùng icon riêng `tab_achievements.svg`, giữ layout Apple HIG hiện tại, card compact, trạng thái màu rõ ràng và nhãn “Coming soon” cho đổi quà.
- Badge image dùng `asset_key` chung và mapping icon trong client cho đến khi có bộ ảnh từng huy hiệu riêng. Không cắt ảnh reference tự động và không đưa ảnh composite vào dữ liệu cá nhân.

## Xử lý lỗi và nhất quán

- Lỗi quyền, tenant hoặc trạng thái trả mã 403/404/409 hiện có; UI hiển thị thông báo tiếng Việt ngắn và giữ form để sửa.
- Tạo/duyệt/gỡ dùng idempotency key; retry không tạo bản ghi/notification trùng.
- Khi catalog không tồn tại hoặc bị vô hiệu hóa, API từ chối tạo mới; lịch sử vẫn đọc được qua `points_snapshot`.
- Mở tab khi mất mạng không tự chuyển sang SQLite; UI giữ dữ liệu memory cuối cùng và báo trạng thái online không khả dụng.

## Kiểm thử và vận hành

- Unit test ma trận quyền, lý do Coach, tenant isolation, trạng thái chuyển tiếp, tính tổng điểm, hết hạn 30 ngày và soft-delete.
- Route test tạo → duyệt/từ chối → hiển thị Trainee → hết hạn; kiểm tra notification/audit và idempotency.
- Migration test trên D1 và Supabase; query kiểm chứng sau apply migration.
- Typecheck/build Worker, test MAUI compile, smoke endpoint production. Chỉ sau khi tất cả kiểm tra đạt mới deploy Worker và push source/changelog lên GitHub.
