# AWAKEN Community FCM

## Cloudflare online (version 3.0, build 103)

The app is connected to the production Worker and D1 documented in
`backend/DEPLOYMENT.md`. Cloud login/session, Founder registration, Admin
Founder management, member/profile/club updates and the tenant-scoped snapshot
cache use the HTTPS API. R2 is enabled and bound as `FILES`; private media
routes are tenant-scoped.

The online Worker/D1/R2 stack is the authority. The MAUI facade retains only
legacy SQLite compatibility; its online startup path does not create or read a
SQLite database. Google OAuth uses Authorization Code + PKCE and Worker-side
subject verification.

Ứng dụng Android quản lý đội bóng đá cộng đồng nội bộ, xây dựng bằng .NET MAUI 10 để mở và phát triển trong Visual Studio Community.

## Bản hiện tại

Đây là bản online-first. Mọi account và dữ liệu nghiệp vụ được tenant-scope trên Cloudflare:

- D1 là source of truth; snapshot Coach/Trainee có batch read và marker không đổi.
- R2 lưu logo/avatar/selfie/bill/PDF riêng tư; DB chỉ lưu object key và metadata.
- Google Bind/đăng nhập đi qua OAuth PKCE, không đọc danh sách account Android và không nhập email thủ công.
- Cron Worker xử lý check-in, học phí, lương và dọn security/media rác.

Kiến trúc dữ liệu đã dùng ID do client tạo, timestamp UTC và tách file media khỏi SQLite để thuận lợi bổ sung Cloudflare Workers, D1 và R2 sau này.



## Chức năng theo vai trò

### Founder & Head of Community Football

- Tạo, sửa, khóa account Coach/Trainee và reset password tạm.
- Xem hồ sơ thành viên kèm tổng số buổi điểm danh/có mặt và vắng mặt.
- Tạo sân, lớp, chọn nhiều ngày cố định, giờ học, Coach và Trainee.
- Chạm lớp để xem thông tin; chỉ vào form chỉnh sửa sau khi bấm **Sửa lớp học**.
- Nhập số buổi và học phí của một chu kỳ; khoản học phí đầu tiên được tạo ngay khi phân công học viên vào lớp và có thể đóng trước nhiều chu kỳ.
- Điểm danh thay Coach; bắt buộc ghi lý do và lưu audit log.
- Gửi thông báo riêng hoặc broadcast tất cả Trainee.
- Kiểm tra bill, xác nhận/từ chối học phí và phát hành quyền xuất PDF.
- Chạm bill để xem ảnh lớn và lưu bản sao vào thư viện Ảnh.
- Duyệt đủ ảnh selfie check-in và check-out của Coach; chỉ ca đã check-out và được Founder xác nhận mới được tính vào số buổi dạy và lương.
- Mở từng loại lịch sử điểm danh để xem ngày học/check-in, lớp, thành viên và trạng thái có mặt, đi trễ, vắng mặt hoặc vắng có phép.
- Khi tạo hoặc sửa Trainee có thể bật/tắt **Cầu Thủ Học Viên Được Hỗ Trợ**. Học viên này được miễn toàn bộ học phí, không có QR hay luồng upload bill.
- Xem lương Coach tự tính từ số buổi check-in đã xác nhận × lương/buổi, đánh dấu đã thanh toán và nhắc sau ngày 10.
- Cập nhật tên đội, logo và ngân hàng/VietQR; thông tin Founder nằm trong hồ sơ cá nhân riêng.
- Logo trên trang Tổng Quan lấp đầy khung hiển thị, không còn khoảng đệm làm ảnh bị thu nhỏ.
- Hồ sơ Founder hiển thị chi tiết điểm danh/check-in theo ngày, lớp và trạng thái trong một danh sách gọn; có nút xem đầy đủ khi lịch sử dài.
- Hồ sơ Trainee lọc lịch sử theo năm/tháng; hồ sơ Coach lọc theo lớp dạy, năm/tháng.
- Bind Account mở OAuth Google trực tiếp; không còn form nhập email thủ công hoặc đọc danh sách account trên thiết bị.
- Coach phải check-in selfie trước khi mở roster học viên; check-out selfie sẽ đóng roster và hoàn tất buổi học.
- Nếu Coach quên check-out, ca sẽ tự khóa sau 8 giờ để dừng bộ đếm và ẩn roster; trạng thái này không tính lương cho tới khi Coach gửi selfie check-out thật.
- Khi nhập hai trường cuối của biểu mẫu, nội dung tự cuộn vừa đủ lên trên bàn phím; các trường phía trên giữ nguyên vị trí.
- Tên và chức vụ trong hồ sơ Founder, Coach và Trainee được căn giữa nhất quán.
- Không còn banner lưu dữ liệu trên thiết bị và thẻ “Phạm vi bản Offline” ở trang Tổng Quan.

### Coach

- Xem thông tin đội, sân, lớp và học viên được phân công.
- Xem logo, tên đội và thông tin Founder trong trang đội/trang Hôm Nay.
- Chụp selfie check-in theo buổi học và theo dõi trạng thái chờ duyệt/đã xác nhận/bị từ chối.
- Điểm danh Có mặt/Đi trễ/Vắng/Có phép, lưu nháp và hoàn tất.
- Mở lại buổi đã hoàn tất để sửa học viên đi trễ.
- Xem hồ sơ trước; chỉ chuyển sang trạng thái sửa khi bấm **Sửa hồ sơ**.

### Football Trainee / Phụ huynh

- Xem thông tin đội, Founder, Coach, sân, lớp và các học viên cùng lớp.
- Xem hồ sơ trước; tự sửa ảnh, ngày sinh, chiều cao, cân nặng, email và password khi cần. Số điện thoại học viên đã được bỏ; hồ sơ vẫn giữ số điện thoại phụ huynh/người giám hộ.
- Xem lịch sử điểm danh read-only.
- Xem học phí theo chu kỳ, tiến độ số buổi, chọn đóng trước 1–12 chu kỳ và nhận QR với tổng tiền tương ứng.
- Nhận VietQR khi Founder đã cấu hình BIN ngân hàng + số tài khoản.
- Lưu QR Code vào thư viện Ảnh để mở trong ứng dụng ngân hàng.
- Upload bill, nhận trạng thái xác nhận/từ chối.
- Sau khi đã đóng: tạo, lưu hoặc chia sẻ hóa đơn PDF.

## Thiết lập ngân hàng/VietQR

Đăng nhập Founder → **Khác** → **Thông tin đội & ngân hàng** rồi nhập:

- Tên ngân hàng.
- Bank BIN (mã 6 số, ví dụ `970436`).
- Số tài khoản.
- Tên chủ tài khoản.

QR không hiển thị khi chưa đủ BIN và số tài khoản. Payload dùng chuẩn EMVCo VietQR với số tiền và nội dung `[TenCauThuHocVien] dong hoc phi`.

## Dữ liệu và bảo mật

- Online DB: Cloudflare D1; media/PDF: R2 private bucket.
- Access token chỉ ở RAM; refresh token xoay vòng trong Android SecureStorage.
- RememberedLoginService chỉ nhớ username và xóa password plaintext từ bản cũ.
- Google OAuth subject chỉ được liên kết với một account; mọi liên kết/hủy liên kết ghi audit.
- Mọi form nhập/đổi/reset password đều có nút **Hiện/Ẩn**; reset thành công tự đưa đúng username vừa reset về màn hình đăng nhập.
- Đăng nhập sai 5 lần: khóa tạm 10 phút.
- Quyền được kiểm tra tại tầng database/service, không chỉ ẩn nút giao diện.
- Account/lớp/sân được vô hiệu hóa thay vì xóa lịch sử.
- Thao tác account, điểm danh, học phí, lương và thông tin đội được ghi audit log.

SQLite bản MVP chưa mã hóa toàn database. Trước khi dùng thật với dữ liệu trẻ em và bill thanh toán, nên bổ sung SQLCipher hoặc mã hóa field/file bằng khóa Android Keystore.

## Vận hành online

- Cloudflare Workers: REST API, JWT/refresh-session và authorization theo vai trò.
- Cloudflare D1: dữ liệu nghiệp vụ, migration 0001–0009.
- Cloudflare R2: selfie, ảnh hồ sơ, bill và PDF riêng tư.
- Cloudflare Cron: tạo chu kỳ học phí, nhắc hạn thanh toán, nhắc lương và cleanup.
- CI/release/backup gates: `.github/workflows/` và `docs/CI_RELEASE.md`.

Chi tiết mô hình và quy tắc chuyển đổi nằm trong `docs/ARCHITECTURE.md`.

## Quy tắc check-in Coach (online)

- Coach chỉ được check-in từ 60 phút trước giờ bắt đầu lớp.
- Sau 2 giờ kể từ giờ kết thúc, nếu Coach chưa check-in, Worker Cron sẽ tự ghi nhận **Vắng check-in**, khóa ca và không tính lương.
- Lịch sử dạy học của Founder hiển thị danh sách Coach trước; chạm vào từng Coach mới mở lịch sử chi tiết check-in/check-out.
