# Change log

## Achievement assets and per-trainee history — 2026-08-28 (build unchanged: 123)

- Thay toàn bộ 21 asset biểu trưng bằng các file PNG riêng do người dùng cung
  cấp. Nền đen và phần chữ chú thích được tách thành trong suốt bằng script
  tái lập được; ảnh biểu trưng bên trong được giữ nguyên và catalog/điểm đã
  duyệt không thay đổi.
- Trang **Thành tích** của Founder, Đồng Sáng Lập và Coach không còn hiển thị
  “Tổng điểm trong phạm vi”. Trang này trước tiên liệt kê theo tên những Cầu
  thủ học viên đã có thành tích; chạm vào tên mới mở lịch sử riêng của học
  viên. Tổng điểm cá nhân vẫn được lấy từ snapshot từng thành tích và hiển
  thị trong hồ sơ Trainee (cả hồ sơ do Founder mở và **Thông tin cá nhân** của
  chính Trainee).
- Giữ biểu trưng theo từng Trainee trong danh sách thành viên và danh sách
  lớp học; không gộp điểm giữa các học viên. Các bản ghi đã hết hạn/gỡ vẫn
  giữ điểm và lịch sử, còn Trainee chưa có thành tích không xuất hiện trong
  chỉ mục Thành tích.
- Sửa Picker **Lớp học** ở màn hình **Thêm thành tích**: binding dùng
  `ClassRow.DisplayName` nên tên lớp được hiển thị đúng cho Founder/Coach.
- Đã kiểm tra 62 test backend/UI, Worker typecheck/build và MAUI Android Debug
  build; không thay đổi schema hay migration backend trong checkpoint này.

## Achievement UI hotfix — 2026-08-27 (build 122)

- Sửa lỗi trang **Thành tích** hiển thị “Không thể tải dữ liệu” khi trang
  được render lại (đổi hạng mục, đổi chế độ danh sách gọn hoặc tải lại sau
  khi thao tác). Nguyên nhân là cùng một `Switch` bị gắn lại vào nhiều `Grid`
  trong MAUI; trang hiện tạo control mới cho mỗi lần render và chỉ giữ lại
  trạng thái lựa chọn.
- Đã xác minh backend online không phải nguyên nhân: Worker production trả
  HTTP 200 với 21 biểu trưng và feed rỗng hợp lệ cho Founder, Coach và Trainee;
  DTO JSON của Android deserialize thành công.
- Tăng Android build từ 121 lên 122, giữ version hiển thị 3.4; artifact được
  đặt tên riêng, không ghi đè các bản trước.
- Kiểm tra đạt: 56 test Worker, typecheck, Worker build và MAUI Android Debug
  build không cảnh báo/lỗi.

## Production rollout — 2026-08-27 (build 121)

- Đã áp dụng migration Supabase production `achievements` (remote version
  `20260827155057`), tạo 21 biểu trưng và 10 mức điểm; đã xác minh RLS bật
  cho các bảng thành tích.
- Đã deploy Worker production bằng `--keep-vars`, giữ nguyên D1, R2, secrets
  và biến production. Worker version: `191fb70d-a144-4b8f-b377-527c2d0d9aac`.
  `/health` và `/health/supabase` đều trả HTTP 200.
- Tăng Android build từ 120 lên 121, giữ version hiển thị 3.4. Đã tạo
  Release APK, Release AAB và Debug APK; chỉ Release APK được publish lên
  GitHub Release.
- Kiểm tra đạt: backend check/typecheck/build, Worker dry-run/smoke và cả ba
  MAUI Android publish.

## Hệ thống thành tích — 2026-08-27 (build unchanged: 120)

- Thêm tab **Thành tích** cho Founder, Đồng Sáng lập, Coach và Cầu thủ học
  viên; Manager không có tab vì không được cấp nghiệp vụ thành tích.
- Thêm hai hạng mục: **Xếp hạng giao hữu / giải đấu** và **Xếp hạng lớp học
  theo tuần**. Founder/Đồng Sáng lập được ghi nhận và duyệt; Coach chỉ gửi
  đề xuất kèm lý do; chỉ Founder được gỡ biểu trưng.
- Thêm catalog 21 biểu trưng dùng chung, với đúng tập điểm đã duyệt trong
  ảnh: `500, 150, 100, 60, 30, 20, 15, 10, -10, -30`. Điểm được chụp tại
  thời điểm ghi nhận và giữ vĩnh viễn; biểu trưng được hiển thị 30 ngày rồi
  chuyển hết hạn mà không làm mất điểm.
- Thêm migration additive cho Cloudflare D1 và Supabase, RLS/index, API
  `achievement-badges`, `achievements`, duyệt và gỡ; các mutation có
  idempotency, kiểm tra tenant/quyền, audit và thông báo trong ứng dụng.
- Thêm client/API model và data access online tách khỏi operational snapshot,
  tránh làm chậm đăng nhập; Android có bộ lọc, chế độ danh sách gọn, chi tiết,
  tạo đề xuất và duyệt/gỡ theo quyền.
- Đã kiểm tra cục bộ: 55 test Worker, typegen, typecheck, build, Worker
  dry-run và MAUI compile đều đạt. Chưa áp dụng migration Supabase production
  hoặc deploy Worker trong checkpoint này.

## Production hotfix — 2026-08-26 (build unchanged: 120)

- Sửa lỗi không thể lưu khi sửa lớp hoặc thêm Cầu Thủ Học Viên online: Supabase
  production còn thiếu cột `classes.manager_user_id`, khiến RPC trả lỗi 400 và
  ứng dụng hiển thị “Không thể ghi dữ liệu online”.
- Đã áp dụng migration additive `class_manager_20260822` trên Supabase
  (cột nullable và index tenant/lớp) và migration `0015_class_manager.sql`
  trên Cloudflare D1; không thay đổi dữ liệu hiện có.
- Worker production đã triển khai lại (version
  `6e0d6870-8c5b-4f7f-a2ac-a430231bf920`). `/health/supabase` nay kiểm tra
  trước cột bắt buộc cho luồng ghi lớp để phát hiện lệch schema sớm.
- Đã kiểm tra lưu snapshot lớp gồm Coach và học viên trả HTTP 200; toàn bộ 42
  test backend, typecheck, build và dry-run Worker đều đạt. Không tăng số build
  Android vì thay đổi chỉ ở backend/schema.

## Release 3.4 — build 120 — 2026-08-26

- Sửa lỗi sau khi Founder xóa account thành viên: danh sách thành viên và các
  trang hồ sơ cha được đánh dấu cần tải lại ngay khi thao tác xóa thành công,
  không còn giữ account vừa xóa trong cửa sổ cache 20 giây.
- Giữ nguyên database, Cloudflare Worker, Supabase, D1, R2, OAuth và
  ApplicationId; chỉ tăng số build từ 119 lên 120.
- Build đủ Release APK, Release AAB và Debug APK; chỉ Release APK được đưa
  lên GitHub Release.

## Release 3.4 — build 119 — 2026-08-26

- Founder có nút **Xóa account vĩnh viễn** trong hồ sơ thành viên và màn hình
  sửa hồ sơ; thao tác yêu cầu xác nhận lại trước khi thực hiện.
- Worker bổ sung `DELETE /v1/users/:id`, kiểm tra tenant và ma trận quyền:
  Founder được xóa Coach, Trainee, Manager và Co-Founder; Co-Founder không
  được xóa Co-Founder khác; Manager và các account khác không được xóa.
- Xóa account dọn dữ liệu liên quan của thành viên (phân công lớp, điểm danh,
  học phí, bill, hóa đơn, lương, đánh giá, thông báo, liên kết OAuth và media),
  xử lý attendance có ràng buộc RESTRICT, xóa object R2 và ghi audit.
- Giữ nguyên ApplicationId, database, Cloudflare, Supabase, R2, OAuth và
  version hiển thị 3.4; chỉ tăng số build từ 118 lên 119.
- Build đủ Release APK, Release AAB và Debug APK; chỉ Release APK được đưa
  lên GitHub Release.

## Release 3.4 — build 118 — 2026-08-22

- Manager không còn quyền tạo hoặc chỉnh sửa cấu trúc lớp học; vẫn giữ các
  nghiệp vụ quản lý được phân quyền trên lớp đã được gán.
- Founder và Co-Founder có thể gán một Manager đang hoạt động vào lớp; thông
  tin Manager được hiển thị trong thẻ và chi tiết lớp học, theo đúng tenant.
- Khi tạo lớp học bắt buộc phải thêm ít nhất một Huấn Luyện Viên (Coach), với
  kiểm tra đồng nhất ở Worker, snapshot sync và Android UI/database.
- Thêm migration additive `manager_user_id` cho Cloudflare D1 và Supabase;
  client cũ không làm mất assignment Manager khi đồng bộ thiếu trường mới.
- Đã chạy pass các test authorization/management/schema, Supabase migration,
  Worker typecheck/build/dry-run và Android build trước khi phát hành.
- Build đủ Release APK, Release AAB và Debug APK; chỉ Release APK được upload
  lên GitHub Release. Giữ nguyên ApplicationId, Cloudflare, Supabase, R2,
  OAuth và dữ liệu hiện tại.

## Release 3.3 — build 117 — 2026-08-22

- Phát hành bản build gồm toàn bộ hardening A → D → B → C: bảo vệ backend,
  backup/release workflow, tối ưu snapshot online và UX/error handling.
- Tạo đủ Release APK, Release AAB và Debug APK; chỉ Release APK được publish
  lên GitHub Release.
- Giữ nguyên ApplicationId, version hiển thị 3.3, database, Cloudflare,
  Supabase, R2, OAuth và dữ liệu nghiệp vụ.

## Engineering hardening — 2026-08-22 (build unchanged: 116)

- **A — Backend correctness:** thêm các guard compare-and-swap cho refresh
  session, duyệt bill/check-in, snapshot idempotency; giới hạn audit client
  theo action/entity/role; bảo vệ xoá account khi R2 chưa sẵn sàng; chuẩn hoá
  truy vấn sort tương thích Supabase/PostgreSQL.
- **D — Vận hành:** bổ sung workflow backup online Supabase (pg_dump), D1
  export, inventory R2 và checksum; workflow Android luôn tạo Release APK,
  Release AAB và Debug APK nhưng chỉ publish Release APK lên GitHub Release.
- **B — Hiệu năng online:** refresh snapshot truyền `afterSyncVersion`, nhận
  phản hồi `unchanged` nhỏ khi dữ liệu không đổi; giữ projection online
  memory-only, không khởi tạo SQLite khi online; thêm giới hạn tải avatar song
  song.
- **C — UX/an toàn:** lỗi khởi động và lỗi mạng dùng thông báo an toàn; bỏ dòng
  footer offline cũ khỏi hoá đơn PDF; giữ loading overlay và semantic icon cho
  đăng nhập/tạo tài khoản. Chưa triển khai đa ngôn ngữ theo yêu cầu.
- Checkpoint: 35/35 backend tests pass, Worker typecheck/build/dry-run pass,
  production smoke health pass, Android Debug build pass.


## Release 3.3 — build 116 — 2026-08-20

- Sắp xếp nhóm thành viên Founder theo thứ tự Đồng Sáng Lập, Quản Lý,
  Huấn Luyện Viên, Cầu Thủ Học Viên.
- Bỏ các ký tự tiền tố trùng với icon tự động của nút trên trang Tổng Quan:
  Thêm account, Tạo lớp học, Gửi thông báo, Điểm danh thay và Lớp mở yêu cầu
  đánh giá.

- Chỉ tăng số build từ 115 lên 116; giữ nguyên database, Cloudflare,
  Supabase, R2, OAuth và dữ liệu nghiệp vụ.

## Release 3.3 — build 115 — 2026-08-20

- Thêm vai trò **Đồng Sáng Lập (Co-Founder)**. Co-Founder dùng giao diện và
  quyền Founder, nhưng không được xóa account Co-Founder khác.
- Thêm vai trò **Quản Lý (Manager)** với phạm vi giới hạn: tạo Coach/Trainee,
  tạo lớp mới, duyệt check-in/check-out Coach, duyệt bill học phí, đóng học phí
  thay phụ huynh và duyệt lương Coach. Manager không sửa hồ sơ, thông tin đội,
  trạng thái account, lịch sử/audit hoặc xóa dữ liệu.
- Cập nhật bảng quyền ở Android, Worker authorization, snapshot sync và D1;
  Manager chỉ được đồng bộ lớp mới, không được sửa lớp đã tồn tại.
- Thêm migration Supabase cho role mới và helper RLS; snapshot Manager được
  thu gọn theo nguyên tắc least-privilege, không trả Founder/Co-Founder hoặc
  audit history vào cache vận hành.
- Bổ sung màn hình Tổng quan/Tài chính/Xử lý riêng cho Manager; Founder và
  Co-Founder tiếp tục dùng đầy đủ màn hình Founder.
- Bổ sung test role/schema/route authorization; Worker typecheck/build và
  Android compile đều đạt.
- Migration D1 compatibility được đánh dấu an toàn, không rebuild bảng `users`
  đang có khóa ngoại; Supabase là source of truth cho role mới.
- Giữ nguyên ApplicationId, database, Cloudflare, Supabase, R2, OAuth và dữ
  liệu nghiệp vụ hiện có. Chỉ tăng số build từ 114 lên 115.

## Release 3.3 — build 114 — 2026-08-20

- Thay logo splash/loading bằng `AWAKEN-COMMUNITY-FCM-VERTICAL-LOGO` do người
  dùng cung cấp; bỏ dòng chữ tên ứng dụng riêng trên `BootstrapPage`.
- Đổi nền splash/loading sang `#F8FAFC`, màu nền lấy từ logo AWAKEN Enterprise
  Technology.
- Đổi app icon sang `AWAKEN-ENTERPRISE-TECHNOLOGY-LOGO` và cập nhật Android
  manifest để dùng đúng icon mới.
- Giữ nguyên ApplicationId, database, Cloudflare, Supabase, R2, OAuth và toàn
  bộ dữ liệu nghiệp vụ hiện có.

## Release 3.2 — build 113 — 2026-08-20

- APK Release mới gồm toàn bộ sửa lỗi lịch cũ/lương idempotent, xử lý lỗi
  Supabase có mã theo dõi và hệ thống UI/icon lavender–navy–teal.
- Giữ nguyên ApplicationId, database, Cloudflare/Supabase/R2 và OAuth để có
  thể cài đè an toàn lên bản trước.
- APK được lưu bằng tên có phiên bản/build riêng và publish ở GitHub Release;
  không ghi đè asset Release 3.1.

## Attendance reliability & UI refresh — 2026-08-20

- Chuẩn hoá trạng thái buổi học cũ trên Worker: **Coach không dạy
  (Founder điểm danh thay)** không tạo lương Coach; **Đã dạy (ghi nhận thủ
  công)** tạo đúng một lượt lương; **Coach không dạy (Founder không điểm danh
  dạy)** xoá điểm danh và không tính lương.
- Tính lương lịch cũ theo dữ liệu check-in đã hoàn tất một cách idempotent;
  lưu lại nhiều lần không cộng trùng và đổi trạng thái về không dạy sẽ tính
  lại kỳ lương đang chờ thanh toán.
- Chuẩn hoá lý do lịch cũ, loại bỏ hậu tố bị lặp từ các bản Android cũ.
- Lỗi ghi Supabase được phân loại an toàn (502/503) và trả mã theo dõi
  request; không đưa chi tiết SQL/PostgreSQL ra thiết bị.
- Refresh UI dùng chung theo bộ tham chiếu: nền lavender, hero navy, card
  trắng bo góc, teal action và icon SVG nét mảnh cho các thao tác thêm, sửa,
  xoá, gửi, trợ giúp, đăng nhập, thông báo, đánh giá và trạng thái rỗng.
- Worker production đã triển khai với `--keep-vars`; giữ nguyên D1, R2,
  Supabase và toàn bộ secrets/biến production. Health, Supabase health và
  smoke test chỉ đọc đều đạt.
- Xác minh `npm run typecheck`, `npm run build` và `dotnet build -t:Compile
  -f net10.0-android --no-restore` đều thành công.

## Supabase/Cloudflare database audit — 2026-08-18

- Kiểm tra lại Worker production và xác nhận `DATA_BACKEND=supabase`; D1 vẫn
  được giữ làm phương án rollback, R2 vẫn là kho media private. `/health` và
  `/health/supabase` đều trả `ok`.
- Smoke test các luồng đọc chính của Admin, Founder, Coach và Trainee: đăng
  nhập, khôi phục phiên, hồ sơ đội, thành viên, lớp, điểm danh, học phí,
  đánh giá, thông báo, OAuth links, snapshot và media R2 đều hoạt động đúng
  theo quyền. Không có lỗi Worker unhandled hoặc lỗi API/Storage/Auth mới.
- Kiểm tra 31/31 bảng public đã bật RLS; `public.d1_batch` chỉ cho
  `service_role` thực thi, `anon` và `authenticated` bị từ chối; Security
  Advisor không còn cảnh báo.
- Kiểm tra quan hệ tenant và khóa ngoại trên users/profiles/venues/classes,
  phân công, buổi học, điểm danh, học phí, bill, hóa đơn, lương, đánh giá,
  thông báo và upload: không có bản ghi mồ côi hoặc liên kết chéo đội.
- Thêm migration `20260818151219_add_foreign_key_indexes` và
  `20260818151255_add_payment_proof_invoice_fk_index` để xử lý 24 cảnh báo
  khóa ngoại chưa có index riêng. Performance Advisor hiện không còn cảnh
  báo `unindexed_foreign_keys`; các mục `unused_index` chỉ là INFO do dữ liệu
  hiện tại còn ít và không ảnh hưởng tính đúng đắn.
- Build/typecheck Worker và compile Android đều thành công; không thay đổi
  secrets, D1/R2 bindings hay dữ liệu nghiệp vụ.

## Supabase production cutover — 2026-08-18

- Đã thêm adapter D1-compatible gọi RPC `public.d1_batch` của Supabase; toàn bộ
  route Worker hiện giữ nguyên contract `/v1` nhưng có thể chạy trên PostgreSQL
  mà không đưa service key xuống Android.
- Đã bật RLS cho các bảng public, khóa Data API trực tiếp với `anon`/
  `authenticated`, chuyển helper `SECURITY DEFINER` vào schema private và giới
  hạn RPC batch chỉ cho `service_role`. Security advisor sau migration không còn
  cảnh báo.
- Đã thêm bảng liên kết `auth_user_links` ở D1/Supabase và endpoint
  `POST /v1/auth/supabase/exchange` để đổi Supabase Auth access token thành
  phiên Worker hiện tại; token không được ghi log.
- Đã tạo backup D1 ngay trước cutover tại
  `backups/cloudflare-d1-pre-supabase-cutover-20260818-213206/` (SHA-256
  `71B5136962D979601037BC713D0A25B0B25E9CFD66AEAC771E10A39A8D1BC078`).
- Worker production đã chuyển `DATA_BACKEND=supabase` (version
  `45c465fe-e415-485e-a246-6e764e70e1e8`). Smoke test `/health`, login,
  `/v1/auth/me`, snapshot, club/classes/users/notifications/tuition/evaluations
  đều trả thành công; logout và token Supabase không hợp lệ cũng được kiểm tra.
- D1 và R2, cùng toàn bộ secrets/biến production, vẫn được giữ nguyên để
  rollback; R2 tiếp tục là kho private cho media lớn.

## Supabase cutover preflight — 2026-08-18

- Đã cấu hình `SUPABASE_URL` cho Worker production và xác nhận secret
  server-side `SUPABASE_SECRET_KEY` đã được nhận đúng tên; không ghi giá trị
  secret vào source code, APK hoặc log.
- Thêm endpoint chỉ đọc `/health/supabase` để kiểm tra kết nối Supabase;
  preflight production trả về `status: ok`.
- D1 và R2 vẫn là nguồn dữ liệu production trong giai đoạn dual-read chuẩn bị
  cutover; chưa thay đổi dữ liệu, binding hoặc cơ chế đăng nhập hiện tại.

## Release 3.1 — build 112 — 2026-08-17

### Tổng hợp thay đổi từ toàn bộ build Release 3.0

- Hoàn thiện kiến trúc online Cloudflare Worker/D1/R2 theo tenant, giữ nguyên
  dữ liệu production, OAuth Google, đồng bộ snapshot và kiểm tra quyền.
- Bổ sung quản lý Founder, Coach và Cầu thủ học viên: phân quyền, hồ sơ,
  thông tin đội, sân, lớp, lịch tháng, lịch sử lớp và lịch sử dạy học.
- Bổ sung check-in/check-out selfie Coach, bộ đếm thời gian, duyệt check-out,
  tính lương theo buổi, tự khóa ca bỏ quên và lịch sử trạng thái.
- Bổ sung điểm danh học viên theo ngày, lịch sử chi tiết, Founder điểm danh
  thay Coach và các trạng thái lớp học cũ.
- Bổ sung học phí theo chu kỳ số buổi, trả trước nhiều chu kỳ, học thử,
  miễn học phí, upload bill/R2, xác nhận Founder, QR và hóa đơn PDF.
- Bổ sung đánh giá học viên theo yêu cầu Founder, thông báo theo vai trò và
  lịch sử đánh giá không thể sửa sau khi Founder xác nhận.
- Bổ sung quản trị account, khóa/kích hoạt/xóa, Bind Google OAuth, thông báo,
  lưu phiên và các bản APK/AAB/Debug có số build riêng không ghi đè.

### Thay đổi mới của build 112

- Trong hồ sơ Cầu thủ học viên của Founder, thêm nút **Tạo PNG**. Ảnh được
  xuất đúng kích thước **590 × 1004 px**, gồm ảnh học viên, họ tên, ngày sinh,
  chiều cao, cân nặng và tên đội; dữ liệu tài khoản/phụ huynh không xuất vào ảnh.
- Chỉ mở ba trạng thái bổ sung lịch cũ cho những buổi có ngày trước ngày tạo
  lớp trong app; lịch học hiện tại dùng luồng điểm danh bình thường.
- Sửa lỗi chuyển từ **Đã dạy (ghi nhận thủ công)** sang **Coach không dạy**:
  lương Coach đang chờ được tính lại, không giữ số tiền cũ; trạng thái Founder
  điểm danh thay không tạo lương Coach.
- Đổi phiên bản Android sang **Release 3.1 build 112**, không thay đổi package
  ID, D1, R2, secrets hoặc biến production.

## Release 3.0 — build 111 — 2026-08-17

- Giữ nguyên toàn bộ sửa lỗi trạng thái lịch cũ và chu kỳ học phí của build 110;
  bổ sung hiển thị trực tiếp **Coach không dạy** trên màn hình chỉnh sửa buổi
  học để không nhầm với trạng thái đang điểm danh.
- Android build tăng lên 111; artifact build 110 và các bản cũ không bị ghi đè.

## Release 3.0 — build 110 — 2026-08-17

- Bổ sung trạng thái lịch cũ **Coach không dạy (Founder không điểm danh dạy)**.
  Trạng thái này không tạo điểm danh học viên và không tính lương Coach; trạng
  thái **Coach không dạy (Founder điểm danh thay)** vẫn ghi nhận điểm danh học
  viên nhưng không tính lương Coach.
- Lưu lại lựa chọn trạng thái lịch cũ khi mở lại, không tự trả về
  **Đã dạy (ghi nhận thủ công)**. Khi chuyển giữa các trạng thái, dữ liệu điểm
  danh/check-in tổng hợp và lương đang chờ được dọn hoặc tính lại idempotent.
- Khóa chuyển sang chu kỳ học phí mới cho đến khi chu kỳ trước hoàn tất đủ số
  buổi (vắng mặt cũng được tính là buổi đã diễn ra); danh sách Founder chỉ hiện
  thao tác đóng chu kỳ mới khi invoice chu kỳ mới đã được tạo.
- Worker production cập nhật tenant-safe, giữ nguyên D1, R2, secrets và biến
  production hiện có. Android build tăng lên 110; artifact cũ không bị ghi đè.

## Release 3.0 — build 109 — 2026-08-17

- Sửa lỗi 500 khi Founder cập nhật trạng thái lớp học cũ thành **Đã dạy
  (ghi nhận thủ công)**. Phần điểm danh/lương được ghi trước; maintenance
  học phí là tác vụ có thể retry nên không còn làm thất bại thao tác đã lưu.
- Làm idempotent việc tạo hóa đơn chu kỳ và ghi nhận Coach/lương để thao tác
  lặp lại hoặc chạy đồng thời không tạo bản ghi trùng.
- Trang **Đóng học phí thay Phụ huynh** có chọn 1–12 chu kỳ, tính lại số tiền
  và số buổi như trang học phí Trainee. Backend chỉ cho Founder cập nhật invoice
  chưa thanh toán của phân công chính thức đang hoạt động; học thử và học viên
  được hỗ trợ bị chặn ở cả UI lẫn API.
- Ẩn nút đóng thay phụ huynh khi chưa vào lớp chính thức, đang học thử, đã gửi
  bill hoặc đã đóng chu kỳ hiện tại; sau khi xác nhận sẽ quay lại hồ sơ và danh
  sách **Học viên đã đóng** hiển thị nhãn **Đã đóng học phí thay Phụ huynh**.
- Tăng Android build lên 109; artifact cũ không bị ghi đè.

## Release 3.0 — build 107 — 2026-08-17
## Release 3.0 — build 107 — 2026-08-17

- Founder có thể mở buổi học cũ và chọn **Đã dạy (Ghi nhận thủ công)**. Hệ thống tạo
  bản ghi Coach đã hoàn tất, tính một buổi lương và ghi rõ nguồn xác nhận; thao tác
  lặp lại không tạo trùng lương. Ngày trong tương lai chỉ mở chi tiết lớp, không mở
  màn hình điểm danh.
- Chi tiết lớp học cố định thu gọn nút **Lịch sử đánh giá** đặt cùng dòng học viên;
  trạng thái học phí hiển thị thêm số chu kỳ đã hoàn tất. Chu kỳ chỉ được tính khi
  đủ toàn bộ số buổi đã ghi nhận (kể cả vắng mặt) trong chu kỳ đó.
- Founder có trang **Đóng học phí thay Phụ huynh** ngay trong hồ sơ Cầu thủ học
  viên. Trang hiển thị QR, ngân hàng, số tiền và tiến độ; Founder xác nhận chuyển
  khoản trực tiếp, không cần upload bill, sau đó có thể tạo/in hóa đơn PDF.
- Bổ sung endpoint Worker/D1 tenant-scoped `POST /v1/tuition/invoices/:id/parent-confirm`
  với kiểm tra quyền, audit, notification và thao tác idempotent; giữ nguyên D1,
  R2, secrets và biến production hiện có.
- Tăng Android build lên 107; artifact cũ không bị ghi đè.

## Release 3.0 — build 106 — 2026-08-16

- Danh sách Cầu thủ học viên của Founder hiển thị ngay dưới tên account:
  Cầu thủ được hỗ trợ miễn phí, hoặc số chu kỳ đã học và tổng số buổi đã học.
- Trang Lương Huấn Luyện Viên chỉ còn tổng lương và danh sách Coach; chạm vào
  từng Coach để mở trang chi tiết các kỳ lương, lớp học, trạng thái và ghi chú.
- Khi Founder đánh dấu một kỳ lương đã thanh toán, Worker/D1 tự tạo box kỳ
  lương kế tiếp cho Coach; màn hình chi tiết tải lại ngay để hiển thị.
- Giữ nguyên dữ liệu và binding D1/R2 production.
- Tăng Android build lên 106; artifact cũ không bị ghi đè.

## Release 3.0 — build 105 — 2026-08-16

- Chuyển **Lịch sử đánh giá học viên** của Cầu thủ học viên từ trang Hôm nay
  sang trang Hồ sơ, đặt ngay dưới Lịch sử điểm danh.
- Bổ sung thông báo online theo tenant: Founder mở yêu cầu đánh giá sẽ báo
  Coach; mỗi đánh giá Coach gửi sẽ báo Founder; khi Coach hoàn tất toàn bộ lớp
  sẽ báo Founder cần xác nhận; Founder xác nhận sẽ báo Cầu thủ học viên.
- Khi Founder từ chối đánh giá, Coach nhận thông báo yêu cầu chỉnh sửa; các
  thông báo được lưu trong D1 và tải mới khi mở trang Thông báo.
- Thêm endpoint Worker có kiểm tra quyền cho Founder mở/đóng yêu cầu đánh giá;
  giữ nguyên D1, R2, secrets và biến production hiện có.
- Các trang Hôm nay, Lịch học trong tháng, Học phí, Thông báo và Hồ sơ của
  account Trainee không còn tiêu đề tab phía trên, nội dung được đẩy lên trên.
- Tăng Android build lên `105`; artifact cũ không bị ghi đè.

## Release 3.0 — build 104 — 2026-08-16

- Thay app icon bằng `AWAKEN_AppIcon_Light_1024.png` do người dùng cung cấp;
  giữ nguyên ApplicationId, database và toàn bộ kết nối Cloudflare.
- Tên AWAKEN Community FCM trên màn hình đăng nhập được bố trí trên một dòng
  với cỡ chữ phù hợp màn hình nhỏ.
- Founder có nút mở danh sách các lớp đang mở yêu cầu Coach đánh giá; chạm vào
  lớp để xem chi tiết và lịch sử đánh giá.
- Lịch sử đánh giá của Cầu thủ học viên được đưa ra ngoài trang hồ sơ, mở từ
  trang Hôm nay của account Trainee.
- Avatar hồ sơ bỏ nền/khung vuông bên ngoài hình tròn.
- Android build tăng lên `104`; artifact cũ không bị ghi đè.

## Release 3.0 — build 103 — 2026-08-16

- Thu gọn toàn bộ pill trạng thái theo đúng kích thước nội dung; pill không
  còn bị kéo đầy chiều cao của card.
- Ngày trong card lịch sử/lịch học được đặt ở góc trên bên trái, dành toàn bộ
  phần còn lại cho tên lớp, Coach, sân và trạng thái.
- Ngày trong ô lịch tháng cũng căn góc trên bên trái để lịch gọn và dễ quét.
- Giữ nguyên database, Cloudflare Worker/D1/R2, OAuth và logo AWAKEN Community FCM.
- Android build tăng lên `103`; artifact cũ không bị ghi đè.

## Release 3.0 - 2026-08-16

- Đổi tên nhận diện phát hành thành **AWAKEN Community FCM** và giữ nguyên
  `ApplicationId`, OAuth callback, SQLite compatibility, Cloudflare Worker, D1,
  R2, secrets và toàn bộ dữ liệu nghiệp vụ hiện có.
- Dùng logo AWAKEN Community FCM do người dùng cung cấp cho icon, splash và
  nhận diện trong ứng dụng; không thay đổi schema hoặc binding online.
- Bump Android version lên `3.0` / build `102`.
- Artifact phát hành sử dụng prefix `AWAKENCommunityFCM`; chỉ APK Release được
  đưa lên GitHub Release của repository private `AWAKENCommunityFCM`. AAB và
  Debug vẫn được tạo và giữ lại cục bộ, không upload GitHub.

## Release 2.80 - 2026-08-16

- Đổi tên hiển thị ứng dụng thành **AWAKEN Community FCM** và thay app icon/splash bằng logo AWAKEN Community FCM do người dùng cung cấp.
- Giữ nguyên `ApplicationId`, OAuth callback, database SQLite tương thích, Cloudflare Worker, D1, R2, secrets và toàn bộ dữ liệu nghiệp vụ; không có migration hay thay đổi backend.
- Đổi prefix artifact phát hành sang `AWAKENCommunityFCM`; source backup dùng repository GitHub riêng `AWAKENCommunityFCM`.
- Android app version là `2.80` / build `101`.

## Release 2.79 - 2026-08-16

- **Sửa đồng bộ ảnh online:** cập nhật Logo đội và ảnh hồ sơ Founder/Coach/Cầu thủ học viên nay upload lên R2, Worker lưu `logo_object_key`/`photo_object_key` vào D1; các màn hình Tổng quan, Thông tin đội, Thành viên, Lớp học, Điểm danh và Hồ sơ tải ảnh private về thiết bị để hiển thị đúng sau khi cập nhật.
- Thêm endpoint Worker có kiểm tra tenant/quyền: `GET /v1/club/logo` và `GET /v1/users/:id/avatar`; không mở ảnh R2 công khai hoặc cho phép xem chéo đội.
- Bổ sung materialize ảnh học viên trong lịch sử điểm danh, bảo đảm ảnh R2 vẫn hiển thị sau khi mở app lại hoặc tải trang lạnh.
- **Bổ sung buổi học cũ:** Founder chạm ngày đã tô màu trên lịch để mở điểm danh; chọn “Đã dạy (ghi nhận thủ công)” hoặc “Coach không dạy”, ghi nhận trạng thái từng Cầu thủ học viên và lưu lịch sử. Buổi “Coach không dạy” vẫn ghi nhận Founder điểm danh thay; buổi “Đã dạy” không tự tạo check-in/tiền lương Coach.
- Worker production đã triển khai deployment `bb7c7421355742bb89b6c3abe1a5ded3`; giữ nguyên binding D1/R2, secrets và biến production hiện có.
- Sửa nhận diện ảnh logo mới: thư mục ảnh `clublogo` do MediaService chuẩn hóa nay được upload lên R2, nên logo mới hiển thị đúng trên Tổng quan và các trang đội sau khi lưu.
- Android app version là `2.79` / build `100`.

## Release 2.76 - 2026-08-16

- **Đánh giá học viên Coach:** màn hình đầu chỉ hiển thị các lớp học; chạm vào một lớp mới mở danh sách Cầu thủ học viên của lớp đó.
- Danh sách roster đánh giá chỉ hiển thị họ tên, ngày sinh, chiều cao và cân nặng; không còn trộn roster học viên vào thẻ lớp.
- Thêm endpoint Worker `GET /v1/evaluations/roster`, chỉ trả dữ liệu tối thiểu khi Founder đã mở yêu cầu đánh giá và Coach được phân công lớp.
- Worker production triển khai version `a7ee338a-25ff-4733-aae4-14cd8e0d4fc7`; giữ nguyên D1, R2, secrets và biến production.
- Android app version là `2.76` / build `97`.

## Release 2.75 - 2026-08-16

- **Hotfix roster đánh giá Coach:** khi Founder mở yêu cầu đánh giá, Worker cấp đúng danh sách Cầu thủ học viên của lớp cho Coach; trước đây snapshot Coach chỉ cấp roster khi đang check-in nên trang đánh giá báo lớp chưa có học viên.
- Trang đánh giá Coach làm mới snapshot online khi mở để nhận ngay thay đổi yêu cầu và roster mới.
- Worker production triển khai version `ae4994a6-03be-43c8-9ed2-d7dadf9951bb`; giữ nguyên D1, R2, secrets và biến production.
- Android app version là `2.75` / build `96`.

## Release 2.74 - 2026-08-16

- **Trang đánh giá cho Coach:** thêm mục “Đánh giá học viên” trong Hồ sơ Coach. Coach có thể chọn lớp và từng Cầu thủ học viên để xem lịch sử hoặc nhập đánh giá mới.
- Danh sách học viên và nút nhập đánh giá chỉ xuất hiện khi Founder đã mở yêu cầu đánh giá cho lớp; Worker vẫn kiểm tra quyền ở phía server.
- Android app version là `2.74` / build `95`.

## Release 2.73 - 2026-08-16

- **Đánh giá học viên:** Founder phải mở yêu cầu đánh giá cho từng lớp trước khi Coach có thể tạo hoặc gửi lại đánh giá. Khi yêu cầu đóng, Coach không còn thấy nút nhập/sửa; lịch sử các đánh giá đã gửi vẫn được giữ để Founder, Coach và học viên theo dõi.
- Thêm cờ yêu cầu đánh giá theo lớp ở SQLite/D1, kiểm tra quyền tại Worker và ghi nhận trạng thái trong snapshot online.
- Đã áp dụng migration D1 `0012_evaluation_request_gate.sql` và triển khai Worker production version `1f01f607-342b-46c1-8144-c326bc15502e`; giữ nguyên D1, R2, secrets và biến production.
- Android app version là `2.73` / build `94`.

## Release 2.72 - 2026-08-16

- **Coach position:** selecting a teaching position is now optional when creating a Coach account; Founder or the Coach can add it later from the profile.
- **Trainee evaluations:** added a role-scoped online/D1 evaluation history for each class. Coach can submit periodic or tournament evaluations with scores, strengths and improvement notes; Founder can approve or request edits; approved evaluations are immutable and remain available to Coach, Founder and the trainee for comparison with the next review.
- Added additive D1 migration `0011_trainee_evaluations.sql` and dedicated evaluation API routes with tenant/class authorization and audit events.
- Worker production updated to version `08caad6a66394d9dbf710b47ffc33c47` (deployment `b29b67c0670e42e8af6f9d0307cd7576`); existing D1, R2, secrets and production variables were preserved.
- Android app version is now `2.72` / build `93`; previous artifacts remain untouched.

## Release 2.71 - 2026-08-15

- **Coach lists:** Coach positions are also shown on the individual Coach cards inside fixed-class and historical-class details.
- Android app version is now `2.71` / build `91`; the Worker, D1 migration `0010_coach_positions.sql`, R2 bucket, secrets and production variables remain unchanged.

## Release 2.70 - 2026-08-15

- **Coach position:** added a stable teaching-position field with seven options: Head Coach / Manager, Goalkeeping Coach, Fitness Coach, Technical Coach, Tactical Coach, Rehabilitation / Conditioning Coach and Performance Coach.
- Creating a Coach account from Founder now requires a Coach position, while Founder/Coach profile editing keeps the position synchronized with the online profile.
- Coach positions are shown in member cards, class Coach labels, Coach check-in/history/review cards and salary rows.
- Added the additive D1 migration `0010_coach_positions.sql` and deployed Worker version `78566c78-02b8-49f8-8ff0-ea811b8a4d16`; existing D1, R2, secrets and production variables were preserved.
- Android app version is now `2.70` / build `90`.

## Release 2.69 - 2026-08-15

- **Trainee Hôm nay:** removed the duplicate page heading and the recent attendance block so the next class and tuition content start at the top.
- **Trainee Lịch học:** removed the duplicate page heading, and changed calendar labels to “Lịch học hôm nay”, “Đã học” and “Sắp tới”. Founder and Coach calendar labels are unchanged.
- **Trainee Học phí / Lịch sử điểm danh:** removed duplicate page headings while keeping the existing tab labels and data flows.
- Android app version is now `2.69` / build `89`; the online Worker, D1, R2 and OAuth configuration is unchanged.

## Release 2.67 - 2026-08-14

- **Coach calendar:** a persisted training session is now treated as a calendar occurrence even when its date differs from the class's recurring weekday. Taught and make-up sessions therefore receive the correct filled status color instead of disappearing from the Coach month calendar.
- The same occurrence rule is used when opening a selected day, so the calendar and its daily class list stay consistent for Coach, Founder and Trainee.
- Android app version is now `2.67` / build `87`; the online Worker, D1, R2 and OAuth configuration is unchanged.

## Release 2.66 - 2026-08-14

- **Hotfix — Snapshot marker:** replaced the 18-term D1 `UNION ALL` compound query with one D1 batch of small aggregate statements. This removes the `too many terms in compound SELECT` error that made Founder, Coach and Trainee snapshots appear empty.
- Production Worker deployed as version `277ed6b5-32dc-46cb-95ea-8ce16f478e48`; read-only snapshot checks passed for Founder, Coach and Trainee accounts.
- Android app version is now `2.66` / build `86`; the release continues to use the same production Worker, D1, R2 and OAuth configuration.

## Release 2.65 - 2026-08-14

- **A — Production hardening:** removed the local Google OAuth client-secret JSON, added a source secret scanner and an isolated staging Worker/D1/R2 template, and kept production D1/R2/secrets/variables unchanged.
- **A — OAuth/privacy:** Google OAuth now requires an exact mobile redirect URI and a matching S256 PKCE verifier; private upload reads are tenant-scoped in SQL.
- **A — Maintenance:** hourly cron cleanup now removes expired OAuth/session/idempotency/registration/reset rows and bounded, unreferenced R2 uploads before tenant maintenance.
- **B — Performance:** Coach/Trainee snapshots use one D1 batch, unchanged snapshots return only a sync marker, and Android keeps the last tenant projection visible while a fresh snapshot is requested.
- **C — Online-only runtime:** Android online sessions do not initialize SQLite, the device-account reader and manual email Bind page were removed, and RememberedLoginService no longer stores passwords.
- **D — Delivery:** added CI, secret scan, D1 migration gate, weekly D1 backup, and manual versioned APK/AAB workflows. App version is now `2.65` / Android build `85`.
- Production Worker `2.4.0` was deployed as version `c35fff76-75de-4acb-9edb-25e98905ce51`; migration `0009_security_indexes_and_reset_tokens.sql` was applied additively.

## Release 2.64 - 2026-08-14

- **A — Online runtime:** production-configured Android sessions now use the Cloudflare snapshot as the source of truth; the SQLite projection is retained only for legacy/offline compatibility and is not initialized or used by the online path. Snapshot refreshes are coalesced and no longer launch an unbounded background SQLite projection refresh.
- **A — Performance:** Founder snapshot collections use D1 batch reads, while identity/club/profile/notification reads share one batch; successful writes invalidate the in-memory snapshot so the next screen gets server data without blocking the current UI.
- **C — Safety:** added a D1-backed per-IP public Founder registration limit (five attempts/hour) with idempotent retry reservation and migration `0008_registration_rate_limit.sql`.
- **C — Privacy:** Coach/Trainee snapshots now field-scope member users and profiles. Peer trainee email, guardian/body details, password state and tuition-support flags are not sent; the signed-in user's complete profile remains available through `currentProfile`.
- **C — Verification:** production Worker health remained HTTP 200 across repeated checks; D1 migration tables were verified remotely; online snapshot checks passed for Founder `awk001` and Trainee `trainee002`.
- Production Worker API `2.4.0` is deployed as version `bddfdd72-8d19-4b94-b6db-5b082326fb91`; existing D1, R2, secrets and production variables were preserved.
- **D — Release hygiene:** aligned app version to `2.64` / Android build `84`, Worker API version to `2.4.0`, and generated a new signed APK, signed AAB and Debug APK without overwriting older artifacts. Only the Release APK is uploaded to the private GitHub Release; AAB and Debug remain local.

## Release 2.63 - 2026-08-14

- Fixed public Founder registration reliability: Android retries one transient Cloudflare failure with the same idempotency key, while D1 safely replays the completed response instead of creating a duplicate account.
- Added D1 migration `0007_public_registration_idempotency.sql`, bounded expired retry records, and verified the production Worker/D1 health endpoint after deployment.
- Gave Coach and Trainee calendars the Founder interaction model: month navigation, filled status days, tap-to-view daily classes, and tap-to-open full class details.
- Removed the duplicate Coach root headings from Today, Classes, and Attendance so each tab begins at the top of its content.
- Added Year and Month filters to the Coach's own teaching history while preserving the live elapsed-time display.
- Reduced login persistence from multiple sequential Android Keystore writes to one secure session bundle; the tenant snapshot remains lazy so successful authentication can enter the app without a full data download.
- Made logout local-first: credentials are detached and the login screen is shown immediately, while the captured refresh session is revoked safely in the background.
- Revised Coach salary scheduling: a Founder confirmation on/before day 10 is due on day 10 of that month; a later confirmation is due on day 10 of the following month. Reminders start only at the due date, with a stronger warning after five unpaid days.
- Renamed the Founder registration page to `Tạo tài khoản Sáng lập & Điều hành`, removed its duplicate content heading, and removed `Tạo mật khẩu riêng` from the first-login password screen.
- Google login now opens the trusted Google OAuth selector directly from the login screen, without an intermediate app page; an unbound identity reports `Tài khoản Google của bạn chưa liên kết với tài khoản`.
- Deployed Worker backend `2.3.0`, version `c3100145-e428-42b0-967d-a044e5d164c0`, with existing D1, R2, secrets, and production variables preserved.
- Built signed/versioned Android artifacts `v2.63-build83`; previous artifacts remain untouched.

## Release 2.62 - 2026-08-14

- Accelerated Coach teaching history: the Founder directory now loads summary data immediately and downloads private check-in/check-out selfies only after a Coach is selected.
- Added persistent, object-key-aware media reuse for Coach selfies and tuition proofs so reopening the app does not download unchanged R2 images again.
- Accelerated Founder tuition by calculating all cycle progress in one indexed pass and by loading proof images only for the category the Founder opens.
- Stabilized navigation from the five Founder root tabs: child headers and content now appear in one layout pass without first pushing the page content downward.
- Built versioned Android artifacts `v2.62-build82`; previous artifacts remain untouched.

## Release 2.61 - 2026-08-14

- Founder root tabs no longer show the duplicated navigation headings; their content starts higher on the screen.
- Removed the Founder Classes toolbar plus button and placed a compact `Tạo lớp học` action beside `Lịch dạy trong tháng`.
- Renamed the Founder bottom tab `Khác` to `Quản lý`.

## Release 2.60 - 2026-08-14

- Historical class details now keep the class header focused on the selected session, without the overall Coach-taught count or tuition line.
- Historical trainee cards place the attendance status at the far right and show cycle/trial progress; supported trainees show `Miễn phí` instead of progress.
- Fixed-class details now place the total Coach-taught count directly below the Coach section.

## Release 2.59 - 2026-08-14

- Fixed-class details now omit the redundant cycle explanation and show the total number of Coach-taught sessions (completed check-outs, excluding Founder substitutions).
- Supported/free trainees now show only the tuition exemption badge; cycle progress and payment warnings are hidden for them.
- Historical class details now reuse the fixed-class detail layout in read-only mode, including Coach cards, the exact historical trainee roster, and each trainee's attendance status.
- Historical rosters continue to come from the selected session's attendance records, so trainees enrolled later cannot appear in an older class session.

## Release 2.58 - 2026-08-14

- Removed duplicate in-page headings from class creation, trainee/Coach lists, fixed classes, class history, Coach teaching history, club information, profile editing, notifications, and checkout review pages.
- Renamed the class creation page to `Tạo lớp học` while removing its repeated content heading; the Founder dashboard action now also says `Tạo lớp học`.
- Renamed `Sân dạy` to `Quản lý sân` in the Founder menu and removed the venue page heading.
- Added Huấn Luyện Viên as an announcement recipient, including all Coaches and individual Coach accounts. The Worker now scopes announcement delivery by role and keeps the existing D1/R2/secrets.
- Renamed tuition category labels to `Học viên chưa đóng` and `Học viên đã đóng` to keep the two states distinct.
- Deployed Worker version `376410cb-722b-47a5-b556-75953c487fd9` with production resources and variables preserved.
- Built versioned Android artifacts `v2.58-build78`; previous artifacts remain untouched.

## Release 2.57 - 2026-08-14

- Simplified page layouts by removing duplicate in-page headings and helper subtitles from Classes, Dashboard, Members, Finance, account creation, tuition and salary screens.
- Renamed the Founder tuition detail page to `Học phí Cầu Thủ Học Viên` while keeping the category cards as the entry points.
- Renamed the supported-trainee count labels to `Cầu thủ học viên` and removed the repeated supported-trainee page heading.
- Restored the Founder quick action label to `Thêm account` and removed the `Thao tác nhanh` heading.
- Built versioned Android artifacts `v2.57-build77`; previous artifacts remain untouched.

## Release 2.56 - 2026-08-13

- Class history now opens a read-only per-session detail page that uses only the attendance records captured on that date, so trainees added later no longer appear in older history.
- Historical attendance is shown in one compact table with each trainee and that day's status (Có mặt, Đi trễ, Vắng mặt or Vắng có phép); Founder edit/delete class actions are not available from history.
- Removed recent sessions and the day-by-day attendance history block from Founder Điểm danh. Coach teaching history now lives under Lớp học, below Lịch sử lớp học.
- Moved member creation to the Thành viên page as “Thêm Huấn Luyện Viên/Cầu Thủ Học Viên”; role list pages no longer duplicate create buttons. Founder dashboard quick action is now “Thêm thành viên”.
- Built versioned Android artifacts `v2.56-build76`; previous artifacts remain untouched.

## Release 2.55 - 2026-08-13

- Founder class history now keeps the date on one compact line, shows the assigned Coach full name, and marks Founder-substituted sessions with a concise note.
- Founder class details now show each trainee's submitted/locked attendance summary: present, late, absent, excused and recorded-session counts.
- Founder dashboard metric cards now open the corresponding trainee, pending bill and unpaid tuition lists; the Cần xử lý panel also shows and opens Coach check-outs waiting for confirmation.
- Fixed the Founder calendar month navigation so its helper message is reset once per render instead of duplicating after repeated month changes.
- Built versioned Android artifacts `v2.55-build75`; previous artifacts remain untouched.

## Release 2.54 - 2026-08-13

- Redesigned the class-editor trainee cards: the “Học thử” control now sits below the trainee name and every card keeps an aligned height.
- Trial enrollments now show `Học thử` instead of `Chưa có bill`, with progress based on the selected number of trial lessons in trainee dashboard, tuition, and Founder class details.
- Founder tuition status cards now open dedicated detail pages for paid, unpaid, and pending-proof trainees instead of expanding long lists on the overview.
- Replaced month navigation controls with compact arrow buttons. Founder’s calendar now shows class details only after tapping a filled date.
- When Founder submits attendance as a Coach substitute, the class remains completed, the Coach history records `Coach không dạy · Founder điểm danh`, and the synthetic history row is never counted for salary or roster access.
- Deployed Worker version `a12518c0-3f8e-4ef0-acf7-614be70db85f` with existing D1, R2, secrets, and production variables unchanged.
- Built versioned Android artifacts `v2.54-build74`; previous artifacts remain untouched.

## Release 2.53 - 2026-08-13

- Trial-to-official conversion now starts the paid cycle after the trial lessons; trial attendance remains in history but is not counted toward the first tuition cycle.
- Rebuilt versioned Android artifacts `v2.53-build73`; previous artifacts remain untouched.

## Release 2.52 - 2026-08-13

- Added per-trainee trial enrollment: Founder can select 1–5 trial lessons while creating or editing a class; supported trainees cannot be placed on trial and trainees with delivered attendance cannot be newly switched to trial.
- Trial enrollment remains tuition-free, automatically converts to official enrollment after the configured delivered lessons, and then creates the first cycle invoice and reminder.
- Added Coach names to class summaries and schedule cards, removed the Founder audit-log shortcut from the Other tab, and made tuition sections expandable for compact browsing.
- Added “Mark all as read” and “Delete all” notification actions with online Worker endpoints scoped to the signed-in account.
- Added automatic cleanup of completed paid-cycle payment-proof rows and their private R2/local images while retaining paid invoice history.
- Added D1 migration `0006_trial_enrollments.sql` and deployed Worker version `081bf2b7-49f5-4f97-b47c-6d10e759a780`.

## Release 2.51 - 2026-08-13

- Added `DELETE /v1/auth/oauth/links/google` to let the signed-in user remove their own Google link directly from the app.
- Enforced authenticated user ownership in the Worker query; a client cannot unlink another account or team.
- Recorded `oauth.account_unlinked` in the same D1 batch as the deletion for an auditable, atomic mutation.
- Built versioned Android artifacts `v2.51-build71`; previous artifacts remain untouched.

## Release 2.50 - 2026-08-13

- Fixed the online Google Bind Account status: Android now reads the signed-in account's Google links from the Cloudflare Worker instead of always returning an empty local list.
- Added the tenant/account-scoped `GET /v1/auth/oauth/links` Worker endpoint; it never exposes links belonging to another account or team.
- Verified the production Worker can return the existing Google link for `awk001`; OAuth secrets, D1, R2 and production variables remain unchanged.
- Built versioned Android artifacts `v2.50-build70`; previous artifacts remain untouched.

## Release 2.49 - 2026-08-13

- Built signed Android artifacts with the production keystore at `F:\AWAKEN\CODING\KEYSTORE\AWPAppKEY.jks`; signing credentials remain outside the repository.
- Kept password visibility controls as eye/eye-off icons across all password fields.
- Built versioned Android artifacts `v2.49-build69`; previous artifacts remain untouched.

## Unreleased - 2026-08-13

- Replaced every password visibility text control with accessible eye/eye-off icons, including login, registration, reset, forced-change and profile/admin password screens.
- Configured Android Release signing to use `F:\AWAKEN\CODING\KEYSTORE\AWPAppKEY.jks` without storing signing secrets in the repository. The keystore alias and passwords are supplied through process environment variables when packaging.

## Release 2.48 - 2026-08-13

- Moved trainee tuition status, cycle progress and the second-lesson unpaid warning into the existing Founder class detail page, directly below each trainee profile card.
- Kept the `Lớp học cố định` screen as a compact class selector; it no longer duplicates tuition information outside the class detail page.
- Built versioned Android artifacts `v2.48-build68`; previous artifacts remain untouched.

## Release 2.47 - 2026-08-13

- Grouped Trainee tuition into `Đã đóng`, `Chưa đóng` and `Bill chờ xác nhận` sections on both Founder finance and Trainee tuition pages.
- Added a total paid amount summary and compact cycle progress (`đã học / tổng số buổi`) for each invoice.
- Added a warning after the trainee has completed the second delivered lesson while the cycle is still unpaid.
- Paid invoices whose cycle is fully delivered are automatically removed from the active billing list while their receipt/audit history remains preserved.
- Added cycle/payment status and progress for every trainee inside Founder’s fixed-class page, including supported-trainee exemption and unpaid-after-second-lesson warning.
- Updated the Cloudflare Worker to recompute progress after submitted attendance, create the next cycle reminder, and keep D1 progress synchronized; deployed version `d975eb7e-5e47-446b-a864-550bf99cc86d` with existing D1, R2, secrets and variables unchanged.
- Built versioned Android artifacts `v2.47-build67`; previous artifacts remain untouched.

## Release 2.46 - 2026-08-13

- Added a Founder-only `Lớp học cố định` page. The dashboard `Lớp đang hoạt động` metric and the Class tab button now open the same active recurring-class list.
- Kept active recurring classes available as compact tappable cards with schedule, time and venue; tapping a card opens the existing full class details.
- Updated the monthly class calendar so every scheduled future day is filled with the `Chưa dạy` color instead of appearing empty.
- Built versioned Android artifacts `v2.46-build66`; previous artifacts remain untouched.

## Release 2.45 - 2026-08-12

- Founder Class now includes a compact `Lớp học cố định trong tháng` section showing each active recurring class, its fixed weekdays, time and venue.
- Added `Lớp học ngừng hoạt động` for Founder; inactive classes remain accessible with their history and can be activated again.
- Added explicit `Xóa lớp học` actions in Founder class details and editor. Deletion is confirmed in context and removes the class-scoped schedule, attendance, check-in, enrollment and tuition records while retaining monthly Coach salary history.
- Added a tenant-scoped `DELETE /v1/classes/{classId}` Worker route with Founder authorization, D1 cascade cleanup and an audit record.
- Built versioned Android artifacts `v2.45-build65`; previous artifacts remain untouched.

## Release 2.44 - 2026-08-12

- Switched configured production builds to direct-online execution: Cloudflare Worker/D1 is the only authoritative data path, while the legacy SQLite database is not created, opened, migrated, seeded, or synchronized during online startup.
- Added a volatile in-memory online projection scoped to the authenticated user and tenant. It is populated lazily from the Worker snapshot and is cleared on account/session changes, so the app avoids SQLite I/O and cross-account cache reuse.
- Changed login/session restore and common Founder, Coach, Trainee, attendance, check-in/out, tuition, salary, notification, audit, venue and class mutations to use direct Worker endpoints without a blocking SQLite write or full-snapshot pull after each operation.
- Kept SQLite code only as an explicit offline compatibility fallback when the Cloudflare Worker URL is not configured; it is no longer part of the configured online execution path.
- Built versioned Android artifacts `v2.44-build64`; previous artifacts remain untouched.

## Release 2.43 - 2026-08-12

- Founder calendar no longer presents future or not-yet-taught occurrences on the active Class page; only today's classes and past status records remain visible there. Future and older completed sessions stay available through the schedule/history flow.

## Release 2.42 - 2026-08-12

- Added a Founder-only current-class view: only classes scheduled for today remain on the main Class page, with today's status shown as scheduled, taught, or Coach did not teach.
- Added a separate Class History page for completed and missed past sessions so the active page stays compact.
- Added a class Start Date field; recurring sessions and calendar cells are not created or displayed before this date.
- Persisted Start Date through the local projection, Cloudflare D1 migration `0005_class_start_date.sql`, snapshot sync, and the production Worker (version `44ed519a-96d5-4c29-9c35-e6d8c82b3647`).

## Release 2.41 - 2026-08-12

- Added a Founder Finance category for supported Trainee accounts. Tapping the category opens a compact list of the Trainees who are exempt from tuition.
- Founder Class now shows only the selected month's scheduled class occurrences. Each occurrence is color-coded as Not taught, Today, Coach did not teach, or Taught; tapping an occurrence opens the full class details.
- Today now transitions to Taught after a completed Coach checkout/session, and transitions to Coach did not teach after the check-in lock when no valid Coach check-in exists.

## Release 2.40 - 2026-08-12

- Switched the production data path to online-first: Cloudflare D1/Worker remains the source of truth whenever Cloud backend is configured; SQLite is retained only as a short-lived compatibility projection.
- Removed full snapshot downloads from normal venue/class, attendance, Coach check-in/out, tuition-proof, salary and notification mutations. A coalesced background reconciliation keeps the local projection current without blocking the user-facing operation.
- Reused a same-user/same-tenant projection after online login/session restore and reconciled it in the background, reducing repeat login/startup latency while preventing cross-team cache reuse.
- Moved schedule repair, stale check-in closure, pending salary recomputation and initial invoice creation out of every snapshot GET and into the hourly Worker Cron.
- Batched attendance notifications/audit writes inside the Worker and parallelized snapshot ID/tenant-boundary checks, reducing D1 round-trips for classes with many trainees.
- Added server-side audit/notification records for Coach check-in, check-out and review flows; rejected tuition proofs now update the local projection without a blocking full snapshot.
- Deployed Worker version `dd2c20da-da28-48bc-b9e1-0d1348e63f62` with the existing D1, R2, secrets and production variables unchanged.

## Release 2.39 - 2026-08-12

## Release 2.39 - 2026-08-12

- Coach class details no longer show tuition amounts or tuition explanations.
- Renamed the Coach/Founder schedule heading to `Lịch dạy trong tháng` and removed the extra calendar-detail caption; Trainee keeps the `Lịch học trong tháng` wording.
- Replaced date dots with full-cell status colors and a compact legend: Chưa dạy, Lịch dạy hôm nay, Bỏ dạy and Đã dạy. Past scheduled days without a submitted session are shown as Bỏ dạy.
- Moved the Coach teaching-history entry point from Hôm nay to Hồ sơ.
- Built versioned Android artifacts `v2.39-build59`; previous artifacts remain untouched.

## Release 2.38 - 2026-08-12

- Changed the Coach Today and teaching-history check-in state to a high-contrast green `Check-in thành công` badge while a check-in is active.
- Added a live teaching-duration timer to the Founder teaching-history directory and each Coach history timeline for open check-ins.
- Fixed month navigation crashes by creating a fresh calendar toolbar for every month render instead of re-parenting the old arrow controls.
- Built versioned Android artifacts `v2.38-build58`; previous artifacts remain untouched.

## Release 2.37 - 2026-08-12

- Reduced the Coach, Trainee and Founder class calendar to a compact month grid that only marks dates with a class.
- Moved class information below the calendar; summary cards now show only class name, recurring day/time and venue, while tapping a card opens the full class details.
- Changed the Coach Today status from `Chờ duyệt check-in` to `Check-in thành công` until the checkout selfie is submitted for Founder review.
- Built versioned Android artifacts `v2.37-build57`; previous artifacts remain untouched.

## Release 2.36 - 2026-08-12

- Replaced the Coach and Trainee weekly timetable with a monthly calendar: seven day columns, date cells containing the recurring class details, and previous/next month controls.
- Removed the leading time-axis rows; class times remain inside each class card so the existing information is still available without the extra table axis.
- Built versioned Android artifacts `v2.36-build56`; previous artifacts remain untouched.

## Release 2.35 - 2026-08-12

- Added a strict Coach check-in window: opens 60 minutes before class start and locks two hours after class end.
- Added idempotent offline/Cloudflare processing that records `Vắng check-in` for assigned Coaches who never checked in; the locked absence never creates salary.
- Added an hourly Cloudflare Worker Cron plus snapshot/check-in repair so missed classes are handled even when the Coach does not reopen the app.
- Founder teaching history now opens with a compact Coach directory; tapping a Coach opens that Coach's detailed check-in/check-out timeline.
- Deployed Worker version `4fa6dec8-4b8b-4b50-9965-681d69896cd3` with the production D1/R2 bindings unchanged.
- Built versioned Android artifacts `v2.35-build55`; previous artifacts remain untouched.

## Release 2.34 - 2026-08-12

- Hardened salary projection migration: pending salary totals are recomputed from approved sessions that have both a real checkout timestamp and checkout selfie, so legacy pre-checkout rows cannot be paid by mistake.
- Rebuilt all Android artifacts as `v2.34-build54` after the final online/local parity fix; previous v2.33 artifacts remain untouched.

## Release 2.33 - 2026-08-12

- Changed Founder review to a checkout-based approval flow: a Coach check-in is not eligible for approval or salary until a checkout selfie has been uploaded.
- Founder review cards now download and display both private R2 images (check-in and check-out), with approval disabled when either image is unavailable.
- Added a safety close after eight hours for abandoned Coach sessions. It stops the timer and hides the trainee roster without creating salary; the Coach can still submit a real checkout selfie to complete the session.
- Added a private Worker endpoint for checkout-selfie previews and aligned local SQLite projection and Cloudflare D1 behavior.
- Deployed Worker version `b3481bfb-d8cb-40bb-96e5-69b5edc3efc1` while preserving the existing D1, R2, secrets and production variables.
- Built versioned Android artifacts `v2.33-build53` without overwriting previous releases. Only the Release APK is uploaded to GitHub; the Debug APK and Release AAB remain local/drive artifacts.

## Release 2.32 - 2026-08-12

- Redesigned the app UI from the supplied mobile reference with a sea-green/teal palette, rounded Apple HIG cards, clear touch spacing and coral destructive actions.
- Added semantic icons to shared action buttons for attendance, finance, classes, members, notifications, profiles and account actions using the existing native icon assets.
- Refined login/bootstrap heroes, form fields, badges, cards, logo frames and timetable colors while preserving all existing online/database flows.
- Built versioned Android artifacts `v2.32-build52` without overwriting previous releases. Only the Release APK is uploaded to GitHub; the Debug APK and Release AAB remain local/drive artifacts.

## Release 2.31 - 2026-08-12

- Reverted the full-app Trophy visual redesign after review; restored the previous Community Football Club Manager interface while keeping all online/database behavior and the weekly Coach/Trainee timetable.
- Built versioned Android artifacts `v2.31-build51` without overwriting previous releases. Only the Release APK is uploaded to GitHub; the Debug APK and Release AAB remain local/drive artifacts.

## Release 2.29 - 2026-08-12

- Redesigned the Coach and Trainee class tabs with a compact seven-day weekly timetable inspired by the supplied Trophy junior-soccer reference.
- Added a horizontally scrollable Monday-to-Sunday grid with time rows, class name, Coach, venue and session time in each scheduled cell; the current day is highlighted in green.
- Kept the existing class detail cards below the timetable so Coach and Trainee can still open the full class roster and details without changing the online data flow.
- Built versioned Android artifacts `v2.29-build49` without overwriting previous releases. Only the Release APK is uploaded to GitHub; the Debug APK and Release AAB remain local/drive artifacts.

## Release 2.28 - 2026-08-12

- Added a live Coach teaching timer that starts after selfie check-in and updates every second until check-out.
- Check-out now stores the authoritative elapsed teaching duration in seconds in both the local cache and Cloudflare D1; old completed rows are backfilled from their check-in/check-out timestamps.
- Founder and Coach teaching history now shows check-in time, check-out time (or “đang dạy”) and the total duration for each class session. Coaches can open their own teaching history from the Coach dashboard.
- Deployed Worker version `deb2038d-252e-4c6b-be1f-efa879837e1b` and applied the additive D1 migration `0004_coach_teaching_duration.sql` while preserving existing data, D1, R2, secrets and production variables.
- Built versioned Android artifacts `v2.28-build48` without overwriting previous releases. Only the Release APK is uploaded to GitHub; the Debug APK and Release AAB remain local/drive artifacts.

## Release 2.27 - 2026-08-11

- Fixed Founder check-in review images when the online snapshot contains a private R2 object key instead of a device file path. The app now downloads the authorized selfie to the local media cache before rendering the approval queue and check-in history.
- Removed the checkout prerequisite from Founder approval. A Coach check-in can now be approved and added to salary immediately after the selfie check-in; check-out only closes the class and hides the trainee roster.
- Preserved an open/pending Coach check-in when restoring an online session, so the Coach does not have to take a new selfie after restarting the app before check-out.
- Deployed Worker version `accdf71e-51c2-4ef8-84e3-e1bf8fc4e3f8` while preserving the existing D1, R2, secrets and production variables.
- Built versioned Android artifacts `v2.27-build47` without overwriting previous releases. Only the Release APK is uploaded to GitHub; the Debug APK and Release AAB remain local/drive artifacts.

## Release 2.26 - 2026-08-11

- Fixed online Founder activation, approval and reactivation from the Admin pending and disabled lists.
- Online Founder status changes are now handled by the Cloudflare Worker as the authoritative operation; the Android client no longer sends a duplicate Admin audit request that fails because an Admin has no tenant.
- The local SQLite projection is updated only when the Founder is cached, so a missing/stale local row cannot make a successful online status change appear as an error.
- Built versioned Android artifacts `v2.26-build46` without overwriting previous releases. Only the Release APK is uploaded to GitHub; the Debug APK and Release AAB remain local/drive artifacts.

## Release 2.25 - 2026-08-11

- Applied the compact right-side action layout to the Admin `Đang chờ đợi xác nhận` and `Đang bị vô hiệu hóa` pages as well as `Đang hoạt động`.
- Approval, reactivation, password reset and delete actions now stay aligned beside each Founder’s information with the same small controls, keeping all three Admin lists concise.
- Built all three versioned Android packages `v2.25-build45` without overwriting previous releases. Only the Release APK is uploaded to GitHub; the Debug APK and Release AAB remain local/drive artifacts.

## Release 2.24 - 2026-08-11

- Redesigned Founder cards in the Admin `Đang hoạt động` list with a compact two-column layout: account and team details remain on the left, while the three management actions sit together on the right.
- Reduced `Đổi mật khẩu`, `Vô hiệu hóa` and `Xóa account` to fixed 112 x 34 buttons with tighter spacing so cards stay concise without hiding an action.
- Kept the Pending and Disabled Founder layouts unchanged, preserving their existing approval and reactivation flows.
- Built all three versioned Android packages `v2.24-build44` without overwriting previous releases. Only the Release APK is uploaded to GitHub; the Debug APK and Release AAB remain local/drive artifacts.

## Release 2.23 - 2026-08-11

- Fixed online payment-proof previews. Founder and Trainee finance pages now download the private R2 object into the device cache before rendering the bill image.
- Added `GET /v1/tuition/proofs/{proofId}/image` with tenant and owner/Founder authorization; the endpoint streams the private R2 object inline without exposing its object key as a public URL.
- Verified production upload and preview flow with QA tenant F01: a new PNG bill uploaded successfully to R2, both the owning Trainee and Founder downloaded 92,532 bytes, and a Founder from another tenant received 404.
- Deployed Worker version `5c2103bb-dfef-4663-a6ab-22b9c42ba5c5` while preserving the existing D1, R2, secrets and production variables.
- Built versioned Android artifacts `v2.23-build43` without overwriting previous releases.

## Release 2.22 - 2026-08-11

- Attendance submission now persists the `submitted` session state online when Coach presses “Điểm danh hoàn tất”; it no longer waits for check-out to publish the completed attendance.
- The Worker validates that every enrolled Trainee has a final status before accepting a submitted roster, while draft saves remain available before completion.
- Trainee snapshots and the Trainee attendance history now show `Có mặt`, `Đi trễ`, `Vắng mặt` and `Vắng có phép` immediately after attendance submission and keep them after Coach check-out.
- Coach roster and Coach attendance remain hidden after check-out as required by the privacy rule.
- Deployed Worker version `48435404-de88-47c8-96cb-0ee93dc41074` with existing D1, R2, secrets and production variables preserved.
- Built versioned Android artifacts `v2.22-build42` without overwriting previous releases.

## Release 2.21 - 2026-08-11

- Fixed the rejected Coach check-in retry flow in the online Worker. D1 stores an empty checkout object key instead of `NULL`, matching the `NOT NULL` schema constraint that previously caused HTTP 500 on the second selfie.
- Founder rejection now reopens the training session as `draft`, clears the submitted marker and allows the same Coach/session check-in row to be updated safely.
- The Android offline projection applies the same session reset after rejection so the roster and attendance screen do not remain stuck in the previous submitted state.
- Deployed Worker version `95aa6872-0772-4fc8-9cc7-e4735b0aafe2` with the existing D1, R2, secrets and production variables preserved.
- Verified production QA flow: check-in -> check-out -> Founder rejection -> new selfie -> `pending` check-in, `draft` session, 10-person roster and attendance endpoint available.
- Built versioned Android artifacts `v2.21-build41` without overwriting previous releases.

## QA data and verification - 2026-08-11 (Release 2.20)

- Created five tenant-isolated Founder QA accounts (`qa-v220-f01` through `qa-v220-f05`) in the production Cloudflare database for regression testing.
- Each Founder has two Coach accounts, twenty Trainee accounts, two venues and two classes. Each class has one assigned Coach and ten assigned Trainees; the first tuition invoice is generated online for every eligible enrollment.
- Verified tenant scoping: every Founder sees only its own members, venues, classes, enrollments, invoices and audit entries.
- Verified the representative online flow on QA tenant F01: Coach check-in unlocks the ten-person roster, attendance is saved, check-out hides the roster, Founder approval creates the salary record, Trainee bill upload is visible to Founder, confirmation creates a receipt, and the invoice becomes paid.
- No functional defect was reproduced in this seeded regression run. The Coach snapshot intentionally hides attendance/roster after check-out as required by the privacy rule.
- Detailed QA account patterns and verification results are documented in `docs/QA_TEST_ACCOUNTS.md`.

## Release 2.20 - 2026-08-10

- Corrected the online audit call to use the Worker no-content response contract; audit events no longer fail after a successful mutation.
- Rebuilt the online Android artifacts as version `v2.20-build40` without overwriting v2.19.

## Release 2.19 - 2026-08-10

- Payment proof submission now uploads the bill image to the private Cloudflare R2 bucket and creates the proof in the tenant-scoped D1 database; Founder snapshots can see it immediately.
- Founder bill confirmation/rejection now uses the Cloudflare review endpoint; accepted bills create the D1 receipt and notify the trainee, while rejected bills remain auditable online.
- Moved prepaid-cycle changes, supported-trainee toggles, coach salary updates and receipt PDF uploads to authenticated Cloudflare endpoints.
- Coach check-in review, attendance-related notifications, announcements, notification read state and audit events now have online persistence instead of SQLite-only writes.
- Preserved local bill and receipt PDF paths when importing their private R2 object keys into the device projection.
- Deployed Worker version `3b6a9180-614b-4371-ad16-1848153771b3` with existing D1, R2, secrets and production variables preserved.
- Built versioned Android artifacts `v2.19-build39` (Release APK, Debug APK and Release AAB) without overwriting previous releases.

## Release 2.18 - 2026-08-10

- Connected Coach check-in selfie upload, check-out selfie upload and attendance saves to the authenticated Cloudflare API; these operations no longer remain only in the device SQLite cache.
- Coach check-in now creates the tenant-scoped online training session when needed, reuses an interrupted open check-in safely, and refreshes the authoritative snapshot so the assigned trainee roster opens immediately after check-in.
- On online session restore, an interrupted local Coach check-in is cleared so the Coach must capture a fresh “Chụp selfie check-in” before the roster is shown again.
- Added the missing Android storage declaration required by MediaPicker on modern Android; the Emulator now opens the camera instead of showing the previous WRITE_EXTERNAL_STORAGE error.
- The Worker now self-heals the first tuition invoice for every active, non-supported enrollment. Trainee and Founder snapshots therefore show the cycle tuition immediately after class assignment, while supported trainees remain exempt.
- Preserved local selfie capture paths when importing R2 object keys into the device projection, so online refreshes do not remove local image previews.
- Deployed Worker version `ed49fab3-6cd7-4c62-97dc-be2e8c26e060` with the existing D1 database, R2 bucket, secrets and production variables preserved.
- Verified production snapshots: `coach.002` has the correct `test.app02` tenant and assigned class; `trainee002` and `test.app02` both receive the same `Mái Ấm` cycle invoice (`500,000 VNĐ`) from D1. Installed the final Release APK on `emulator-5554`; Coach camera opens and restart returns to the fresh check-in action.
- Built versioned Android artifacts `v2.18-build38-Release-01` without overwriting previous releases.

## Release 2.17 - 2026-08-10

- Fixed the Android snapshot decoder to accept legacy D1/SQLite `0/1` boolean values; the production Worker now emits proper JSON `true/false` values.
- Cloud login and session restore now require a valid, tenant-matched snapshot before replacing the on-device projection. Snapshot/network/schema failures can no longer silently open an empty or stale team.
- Removed online fallback to cached SQLite credentials/data and disabled local recurring invoice, reminder and salary generation while Cloudflare is configured. D1 is the sole business-data authority.
- Added post-import integrity checks for Venues, Classes, Coach assignments, Trainee enrollments, sessions and audit history.
- Verified production tenant snapshots for `test.app` and `test.app02`: each independently returns 3 users, 1 Venue, 1 Class, 1 Coach assignment, 1 Trainee enrollment and 5 audit entries.
- Installed Release 2.17 on `emulator-5554` and verified the rendered UI: `test.app` shows Coach 001/Trainee 001, `test.app02` shows Coach 002/Trainee 002, and each tenant shows its own Mái Ấm class, Chín Hải venue and audit history.
- Deployed production Worker version `1faddc28-b609-4cb3-8761-1a6f22c0dc42` while preserving the existing D1 database, R2 bucket, secrets and production variables.
- Built versioned Android artifacts `v2.17-build37` without overwriting previous releases.

## Release 2.16 - 2026-08-10

- Founder cloud snapshots now include the tenant-scoped audit history, so existing Founder activity is restored after app restart or online sign-in.
- Verified that the members, Venue, Class, Coach assignment, Trainee enrollment and audit records for `test.app` and `test.app02` are present under their correct D1 tenants.
- Deployed production Worker version `884dea88-e590-4a0b-82f4-69986924efa7`; restored `APP_ENV=production` and the previous origin setting while preserving the existing D1 database, R2 bucket and all secrets.

- Founder Sân/Lớp write operations now require a valid Cloudflare session whenever online mode is configured; an offline cache can no longer appear to save successfully and then disappear after the next cloud snapshot refresh.
- Added a clear sign-in-again message when the Founder session is missing or expired.
- Confirmed production D1 tenant separation for `test.app` and `test.app02`; the exact username `test.app01` is not present in D1.
- Built versioned Android artifacts `v2.16-build36` without overwriting previous releases.

## Release 2.15 - 2026-08-10

- Founder-created and edited Venues now push a tenant-scoped delta to Cloudflare D1 immediately after saving, including active/inactive changes.
- Founder-created and edited Classes now persist the class, Coach assignments and Trainee enrollments to D1 instead of remaining only in the device SQLite cache.
- Coach salary cards now show the active Class names for each Coach, or `Chưa phân lớp` when no class assignment is present.
- Built versioned Android artifacts `v2.15-build35` without overwriting previous releases.

## Release 2.14 - 2026-08-10

- Admin-created Founder requests now omit the password field so the production Worker applies its default bootstrap password `12345678` without weakening public self-registration validation.
- Kept the first-login password-change requirement and the Cloud-session guard for Google Bind.
- Built versioned Android artifacts `v2.14-build34` without overwriting previous releases.

## Release 2.13 - 2026-08-10

- Admin-created Founder accounts now always use the default password `12345678`; the first-login password-change requirement remains enabled.
- The Worker accepts the explicit Admin bootstrap password while keeping strong-password validation for public Founder self-registration.
- Verified that `testacc` and `test.acc` are stored in the same production D1 database under separate tenant rows; `testacc` has one Google link, while `test.acc` is active/approved but has no Google link until it signs in online and completes Bind Google.
- Cloud Bind now requires a valid Cloudflare refresh session for the current account, preventing stale offline cache data from attempting OAuth linking.
- Built versioned Android artifacts `v2.13-build33` without overwriting previous releases.

## Release 2.12 - 2026-08-10

- Added tenant-aware identity data to the local SQLite cache. Cloud user identities now retain their D1 `tenant_id`, and Founder/member lookups only resolve accounts from the active team's tenant.
- Added a tenant cache boundary: starting a Cloud session clears operational rows and stale singleton club data from the previous team before importing the current tenant snapshot. The current user's Google link is retained locally because OAuth links are not part of the snapshot contract.
- Confirmed the Worker already scopes Founder-created Coach/Trainee accounts and all operational D1 queries by the authenticated tenant; no D1/R2 data or production bindings were changed.
- Built versioned Android artifacts `v2.12-build32` without overwriting previous releases.

## Release 2.11 - 2026-08-10

- Authentication, logout and Founder registration now show a centered modal-style loading notice with a dimmed background; each notice is hidden automatically when the operation finishes.
- Founder self-registration is explicitly Cloudflare-only and no longer contains an unreachable offline SQLite fallback. It clears only local tokens after the pending request is submitted, so an existing session is not revoked accidentally.
- Kept the approval workflow: a Founder created from the login screen remains pending until Admin confirms it; after approval, sign in again online before using Google Bind Account.
- Built versioned Android artifacts `v2.11-build31` without overwriting previous releases.

## Release 2.10 - 2026-08-09

- Removed the OAuth security information panel from Bind Account.
- Founder self-registration now refuses to create a local offline account when the Cloudflare Worker is configured; registration must complete through the online backend.
- Added a compact “Đang đăng nhập” notice while username or Google login is being processed.
- Added a compact “Đang đăng xuất” notice while the session is being cleared.
- Built versioned Android artifacts `v2.10-build30` without overwriting previous releases.

## Release 2.9 - 2026-08-09

- Admin Founder management now shows status groups as compact overview cards; selecting a group opens a separate detail page.
- Founder cards in the Admin detail page now show the team name.
- Admin action buttons are arranged vertically with full width so “Xóa account” remains visible and is not clipped on narrow screens.
- Built versioned Android artifacts `v2.9-build29` without overwriting previous releases.

## Release 2.8 - 2026-08-09

- Admin Founder management now separates accounts into “Đang chờ đợi xác nhận”, “Đang hoạt động” and “Đang bị vô hiệu hóa”.
- Added a dedicated “Vô hiệu hóa tài khoản” action. A disabled Founder cannot sign in until an Admin activates the account again; active sessions are revoked when disabling.
- Changed Admin “Xóa account” to permanent deletion: the Founder, team and all tenant-owned classes, members, attendance, tuition, salary, notification, audit and sync records are deleted from D1; uploaded R2 objects are deleted on a best-effort basis.
- Added D1 migration `0003_founder_status.sql` and deployed Worker `7b6ebdc4a4954793970b6f725ba92f41` with the existing D1, R2, secrets and production variables preserved.
- Built versioned Android artifacts `v2.8-build28` without overwriting previous releases.

## Release 2.7 - 2026-08-09

- Production online mode no longer falls back to local SQLite for login or account creation when the Cloudflare Worker is configured.
- Founder, Coach and Trainee account creation now requires an authenticated online Founder/Admin session; Cloudflare outages return an error instead of creating an unsynchronized local account.
- Built versioned online artifacts `v2.7-build27` without overwriting earlier releases.
- Rebuilt the same version as `v2.7-build27-01` after localizing the new online-mode messages; the original artifacts remain untouched.

## Release 2.6 - 2026-08-09

- Prevented a legacy offline local session from being silently restored when the Cloudflare Worker is configured; the app now asks the user to sign in again with the online account before using Google Bind Account.
- Improved the Bind Account error so it clearly explains that Google linking requires an online Cloudflare account.
- Built versioned online artifacts `v2.6-build26` without overwriting earlier releases.

## Release 2.5 - 2026-08-09

- Fixed Android OAuth callback handling by routing `communityfootballclubmanager://oauth/callback` through a dedicated `WebAuthenticatorCallbackActivity`.
- Added Android Custom Tabs package visibility configuration required by WebAuthenticator on Android 11+.
- Worker production OAuth exchange now returns structured validation errors instead of an internal 500 response.
- Deployed Worker content-only update `ca7781e54e4b4a0d9a6becfdc646ce27`; D1, R2, secrets and production variables were preserved.

## Release 2.4 - 2026-08-09

- Fixed Google Bind Account caching: the Google OAuth subject is now stored as an opaque provider identifier instead of being incorrectly validated as an email address.
- Google Bind Account now completes after the Worker confirms the OAuth link.

## Release 2.3 - 2026-08-09

- Removed Apple login and Apple Bind Account; Google is now the only OAuth provider exposed by the app.
- Worker production was updated with a content-only deployment while preserving D1, R2, secrets and production variables.
- Bumped application version from `2.2 (22)` to `2.3 (23)` and created new versioned Android artifacts.

## Release 2.2 - 2026-08-09

- Founder self-registration now creates a suspended account and waits for Admin approval before login/activation.
- Admin Founder management now includes “Xác nhận thành lập”; disabling a Founder also revokes active sessions.
- Login footer now displays “Phiên bản Release”.
- Login and Bind Account now use direct Google/Apple OAuth Authorization Code + PKCE; no manual email/device-account selection is used.
- Added D1 migration `0002_oauth.sql` for OAuth state, ticket and external-account link records.
- Deployed Worker version `0cb2120483154cfcabaa8c9bb9927885` with R2 binding and OAuth/approval routes.
- OAuth production sign-in/linking requires provider credentials; Google is configured below and Apple remains pending.
- Google OAuth Web client is now configured for project `AwakenApp001`; `GOOGLE_OAUTH_CLIENT_ID` and `GOOGLE_OAUTH_CLIENT_SECRET` are stored as private Worker secrets.
- Google OAuth consent screen was published to **In production**; Google test-user allowlisting is no longer required.
- OAuth sign-in remains restricted to app accounts that have already used **Bind Account**: an unbound Google subject is rejected with `oauth_not_linked`.
- Removed Apple login and Apple Bind Account from the app; Google is now the only supported OAuth provider.
- Worker OAuth routes now reject Apple provider requests and only accept Google OAuth.
- Deployed the Google-only Worker content to production using a content-only update (`deployment_id` `6a9d76fbde384094a27b233f95e07b5d`); D1, R2, secrets and production variables were preserved.
- Fixed immutable redirect-response headers so Google OAuth start now returns HTTP 302 correctly.
- Built versioned Android artifacts: APK Release, APK Debug and AAB Release (`v2.2-build22`); previous artifacts remain untouched.

## R2 enabled - 2026-08-09

- Enabled R2 after account subscription confirmation.
- Created APAC Standard bucket `community-football-club-manager-files`.
- Bound the bucket to Worker `community-football-club-manager-api` as `FILES` and deployed version `7d48df72-035c-4091-8831-7a5761316e35`.
- Online media routes for logo, avatar, selfie, payment bill and PDF are now ready.
- Built online Release APK `CommunityFootballClubManager-v2.1-build21-Release.apk` (SHA-256 `CBE5E34CDBB18B4045E89F7C91673EAEE59B096D53868F93FFDC8A00BF4F785F`).

## Online sync follow-up — 2026-08-09

- After online login or session restore, the MAUI client pulls the tenant-scoped D1 snapshot into its local read cache.
- Profile and club edits now use the authenticated Worker API when a Cloud session is active.

## Online 2.1 — 2026-08-09

So với Demo 2.0:

- Thêm Cloudflare Worker API multi-tenant với JWT access token ngắn hạn, refresh token rotation, RBAC Admin/Founder/Coach/Trainee và audit log.
- Tạo và migrate D1 production `community-football-club-manager` (APAC), gồm account, đội, lớp, phân công, điểm danh, học phí, lương, thông báo, upload metadata, idempotency và audit.
- Kết nối client MAUI tới Worker URL `https://community-football-club-manager-api.old-mud-b712.workers.dev/` cho đăng nhập, đăng ký Founder, khôi phục phiên, đổi mật khẩu và quản trị Founder/member.
- Roster Coach chỉ được mở sau check-in và bị ẩn sau check-out; dữ liệu snapshot được tenant-scope ở server.
- Bỏ credential Admin cố định khỏi source/tài liệu; Admin production được provision trực tiếp trên backend.
- Tại thời điểm 2.1, R2 chưa được enable nên media upload còn trả `storage_unavailable`; bản Release 2.2 đã bật bucket và binding R2 production.
- Bump phiên bản ứng dụng từ `2.0 (20)` lên `2.1 (21)`.


## Demo 2.0 — 2026-08-07

So với Demo 1.10:

- Tách role `Admin` khỏi `Founder`. Admin không có các tab vận hành đội bóng, lớp học, thành viên, điểm danh hoặc tài chính.
- Admin có màn hình riêng để đổi mật khẩu, tạo account Sáng lập & Điều hành, đặt lại mật khẩu Founder và xóa account Founder.
- Màn hình đăng nhập có nút tạo tài khoản Sáng lập & Điều hành công khai bằng username, email và mật khẩu riêng.
- Tự động migrate account `admin` cũ từ Founder sang Admin, giữ nguyên thông tin đăng nhập hiện có.
- Bump phiên bản ứng dụng từ `1.10 (11)` lên `2.0 (20)`.
- Đã đóng gói APK Release, APK Debug và AAB Demo 2.0; các artifact phiên bản cũ được giữ nguyên.

## Demo 1.10 — 2026-08-07

So với Demo 1.9:

- Chu kỳ học phí tính theo số buổi đã hoàn tất của lớp; trạng thái Có mặt, Đi trễ, Vắng hoặc Có phép đều được tính khi buổi học đã được hoàn tất.
- Sau khi dùng hết số chu kỳ đã thanh toán, hệ thống tự tạo khoản thu chu kỳ tiếp theo và gửi thông báo nhắc thanh toán cho học viên.
- Học phí của học viên hiển thị số chu kỳ đã đóng thay vì số thứ tự chu kỳ.
- Logo đội trong Thông tin đội được hiển thị tràn khung, tên đội căn giữa; menu hồ sơ hiển thị đúng tên đội.
- Trang Hôm nay của Coach và Trainee tách riêng lời chào, Founder và ngày tháng thành từng dòng.
- Bump phiên bản ứng dụng từ `1.9 (10)` lên `1.10 (11)`.
- Tạo APK Release, APK Debug và AAB có tên phiên bản; các artifact cũ được giữ nguyên.
- Coach chỉ xem được danh sách học viên sau khi gửi selfie check-in; không cần Founder duyệt trước để điểm danh, và danh sách bị ẩn ngay sau check-out.
- Đóng gói lại các artifact `-01` để bao gồm thay đổi bảo mật danh sách học viên.

## Demo 1.8 — 2026-08-07

So với Demo 1.7:

- Đổi toàn bộ nhãn thành **Cầu Thủ Học Viên Được Hỗ Trợ**.
- Công tắc hỗ trợ nằm cùng dòng với nhãn khi tạo account Trainee.
- Thêm công tắc bật/tắt trạng thái được hỗ trợ trong màn hình sửa hồ sơ Trainee của Founder.
- Hồ sơ và học phí hiển thị đúng trạng thái miễn học phí với nhãn mới.
- Bind Account tự đọc account Google/Apple đã có trên thiết bị, cho phép chọn account khi có nhiều account và không còn yêu cầu nhập email thủ công.
- Đăng nhập Google/Apple cũng lấy account từ thiết bị thay vì yêu cầu nhập email.
- Bổ sung `DeviceAccountService`, xin quyền Android đọc account khi cần và ghi chú giới hạn provider theo hệ điều hành.
- Bump phiên bản ứng dụng từ `1.7 (8)` lên `1.8 (9)`.

## Demo 1.9 — 2026-08-07

So với Demo 1.8:

- Cầu Thủ Học Viên có thể mở hồ sơ đầy đủ của Huấn Luyện Viên và các bạn học cùng lớp ngay trong phần Lịch học.
- Chuyển học phí từ kỳ tháng sang chu kỳ số buổi được cấu hình trong lớp.
- Tạo khoản học phí đầu tiên ngay khi học viên được phân công vào lớp.
- Thêm lựa chọn đóng trước 1–12 chu kỳ; QR, nội dung chuyển khoản và tổng tiền tự cập nhật theo công thức học phí một chu kỳ × số chu kỳ.
- Theo dõi tiến độ số buổi của chu kỳ và tự tạo chu kỳ tiếp theo sau khi chu kỳ đã thanh toán được hoàn tất.
- Thêm migration an toàn cho database offline cũ và giữ các kỳ tháng lịch sử để không mất dữ liệu.
- Bump phiên bản ứng dụng từ `1.8 (9)` lên `1.9 (10)`.
- Bổ sung APK Debug và Android App Bundle với tên có phiên bản, không ghi đè các artifact cũ.

## Achievement badges — 2026-08-28

So với bản trước:

- Tách đủ 21 biểu trưng từ ảnh nguồn thành PNG nền trong suốt, giữ nguyên tên và quy ước điểm đã duyệt (`500, 150, 100, 60, 30, 20, 15, 10, -10, -30`).
- Bổ sung ánh xạ asset cho toàn bộ biểu trưng trong trang Thành tích và trang chi tiết.
- Điểm được chiếu theo từng Cầu thủ học viên từ `points_snapshot` của từng lượt trao; không còn hiểu là điểm dùng chung của biểu trưng.
- Hiển thị các biểu trưng đang còn hiệu lực cùng tổng điểm cá nhân ngay dưới tên học viên trong danh sách thành viên, lớp học hiện tại và lịch sử lớp học.
- Không cần migration/deploy backend: dữ liệu đã có khóa `tenant_id`, `trainee_user_id` và snapshot điểm riêng cho từng lượt trao.
- Bump số build Android từ `122` lên `123` (giữ nguyên version `3.4`); tạo đủ Release APK/AAB và Debug APK, chỉ publish Release APK.

## Quy ước

Mỗi thay đổi tiếp theo phải thêm một mục mới vào file này, ghi rõ phiên bản, ngày thực hiện và các khác biệt so với bản trước.

## Quy trình đóng gói APK

- Dùng `scripts/Build-AndroidArtifact.ps1 -Configuration Release` để tạo APK có số phiên bản trong tên.
- Tên mặc định có dạng `CommunityFootballClubManager-v1.10-build11-Release.apk`.
- Nếu tên đã tồn tại, script tự thêm hậu tố `-01`, `-02`, ... và không ghi đè bản cũ.
- Thêm `-Bundle` khi cần tạo AAB theo cùng quy tắc.
