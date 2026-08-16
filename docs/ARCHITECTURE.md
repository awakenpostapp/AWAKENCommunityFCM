# Kiến trúc online-first (Cloudflare D1 + R2)

## Thành phần

- `Models`: entity SQLite, enum và DTO hiển thị.
- `Services/AppDatabase`: facade nghiệp vụ tương thích; khi Worker được cấu
  hình, mọi read/write đi thẳng qua D1 và không khởi tạo SQLite.
- `Services/PasswordService`: PBKDF2-SHA256.
- `Services/PersistentSessionService`: khôi phục phiên Cloud bằng refresh token
  xoay vòng trong Android SecureStorage; access token chỉ ở RAM.
- `Services/RememberedLoginService`: chỉ nhớ username và dọn password plaintext
  còn sót từ bản cũ; không bao giờ ghi password mới.
- `Services/MediaService`: camera/thư viện và lưu file vào AppData.
- `Services/QrCodeService`: payload EMVCo VietQR + QR PNG.
- `Platforms/Android/AndroidImageSaveService`: lưu bill/QR vào MediaStore hoặc thư mục Pictures trên Android cũ.
- `Platforms/Android/AndroidReceiptPdfService`: hóa đơn PDF bằng Android Graphics.
- `Views`: giao diện .NET MAUI programmatic, điều hướng theo role Admin và ba vai trò nghiệp vụ.
- `Ui`: design token và component dùng lại theo tinh thần Apple HIG.

## Bảng dữ liệu

| Nhóm | Bảng |
|---|---|
| Account | UserAccounts, PersonProfiles, ExternalAccountLinks |
| Đội | ClubProfile |
| Sân/lớp | Venues, TrainingClasses, ClassCoachAssignments, ClassEnrollments |
| Điểm danh | TrainingSessions, SessionCoachAssignments, CoachCheckIns, AttendanceRecords |
| Học phí | TuitionInvoices, PaymentProofs, Receipts |
| Lương | CoachSalaries |
| Hệ thống | AppNotifications, AuditLogs |

Các quan hệ quan trọng có unique index:

- Username.
- Provider + external email; User + provider.
- Class + Coach.
- Class + Trainee.
- Class + ngày buổi học.
- Session + Trainee attendance.
- Enrollment + kỳ học phí.
- Invoice + receipt.
- Coach + kỳ lương.

## Quy tắc nghiệp vụ

### Điểm danh

- Founder: mọi lớp, bắt buộc lý do khi điểm danh thay.
- Coach: chỉ lớp được phân công.
- Trainee: chỉ xem record của mình.
- `SessionCoachAssignments` chụp danh sách Coach được phân công theo từng buổi để giữ đúng lịch sử khi Coach rời hoặc được thêm lại vào lớp.
- Hồ sơ thành viên Founder tổng hợp các buổi đã hoàn tất: Trainee dùng trạng thái attendance hiện tại; Coach đối chiếu check-in với snapshot phân công theo buổi.
- Hoàn tất yêu cầu mọi học viên có trạng thái; UI cho phép chuyển phần chưa ghi nhận thành Vắng sau xác nhận.
- Sửa record tăng `Revision` và cập nhật người thực hiện/thời gian.

### Hồ sơ cá nhân

- Ngày sinh chỉ áp dụng cho Trainee, lưu dưới dạng nullable để hồ sơ cũ không nhận ngày mặc định giả.
- Ngày sinh được chuẩn hóa về phần ngày và không được nằm trong tương lai.
- Trainee không lưu số điện thoại cá nhân; số điện thoại phụ huynh/người giám hộ vẫn được quản lý bởi Founder.

### Phiên và account ngoài

- Đăng nhập username/password hoặc Google đều tạo phiên bền vững bằng refresh
  token xoay vòng trong SecureStorage.
- Khởi động app khôi phục account đang hoạt động; chỉ thao tác **Đăng xuất** mới xóa phiên.
- `ExternalAccountLinks` đảm bảo mỗi Google subject chỉ thuộc một account và mỗi
  account chỉ có một liên kết Google. Android dùng OAuth Authorization Code +
  PKCE; backend xác minh subject với Google, không đọc danh sách account trên
  thiết bị.

### Học phí

- Mỗi lần app khởi động/đăng nhập, hệ thống đảm bảo invoice tháng hiện tại tồn tại cho enrollment đang hoạt động.
- Lớp có một học phí mặc định; mọi enrollment đang chọn nhận cùng mức phí.
- Invoice Pending/Overdue/Rejected cập nhật theo enrollment; invoice Paid và receipt giữ snapshot bất biến.
- Ngày đến hạn là ngày 05.
- Trainee upload proof → `ProofSubmitted` → thông báo Founder.
- Founder xác nhận → `Paid` + receipt snapshot bất biến + thông báo Trainee.
- Founder từ chối → `Rejected`; Trainee được phép tải lại bill.

### Lương

- Mỗi Coach hoạt động có một record/kỳ tháng, hạn ngày 10.
- `ClassCoachAssignment` lưu mức lương/buổi; check-in lần đầu chụp snapshot mức lương đó.
- Lương Pending được tính lại từ các buổi đã check-in trong tháng; Founder không nhập tay số lương.
- Khi đã Paid, số tiền trở thành snapshot và không chuyển ngược về Pending.
- Sau ngày 10, record Pending tạo một thông báo nhắc Founder, không tạo trùng.

## Phân quyền

Tất cả write use case gọi `RequireUser`, `RequireRole` hoặc `EnsureClassAccess` trong `AppDatabase`. UI role-based chỉ là lớp trải nghiệm bổ sung.

- `Admin` chỉ được quản trị account `Founder` qua các service tạo, đổi mật khẩu và xóa account.
- `Founder` mới được phép vận hành đội bóng, tạo Coach/Trainee, lớp học, điểm danh và tài chính.

## Trạng thái Cloudflare

1. Worker production là source of truth; D1 lưu metadata và R2 lưu media/PDF.
2. Snapshot được tenant-scope; Coach/Trainee dùng một D1 batch và
   `syncVersion` để bỏ qua tải lại khi dữ liệu không đổi.
3. Workers kiểm tra JWT/session và role; không tin role từ client.
4. Payment proof là append-only; attendance dùng revision; xác nhận học phí/lương phải idempotent.
5. Cron xử lý attendance, lương, học phí và dọn security/media rác.
6. SQLite chỉ còn mã tương thích offline cũ, không được khởi tạo trong build
   online; có thể loại bỏ hoàn toàn sau khi ngừng hỗ trợ bản offline.
