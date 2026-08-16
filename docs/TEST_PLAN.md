# Kịch bản kiểm thử MVP

## Khởi tạo và account

- Cài mới: username/password phải trống; nút Hiện/Ẩn không thay đổi nội dung password.
- Đăng nhập `admin`; xác nhận vào thẳng trang Tổng quan và không xuất hiện màn hình bắt buộc đổi mật khẩu lần đầu.
- Với account Coach/Trainee mới tạo, ở màn hình bắt buộc đổi mật khẩu lần đầu, thử nút **Hiện/Ẩn** trên cả ba ô và xác nhận nội dung không đổi.
- Đăng nhập, đóng hoàn toàn rồi mở lại app; xác nhận trở về đúng account mà không yêu cầu nhập lại username/password.
- Bấm **Đăng xuất**, đóng/mở lại; xác nhận màn hình đăng nhập xuất hiện với hai ô trống.
- Từ hồ sơ Founder/Coach/Trainee mở **Bind Account**, xác nhận account Google trên thiết bị được tự chọn và liên kết/hủy liên kết được mà không nhập email thủ công.
- Xác nhận account đã Bind đăng nhập được bằng đúng provider; account chưa Bind hoặc sai provider bị từ chối.
- Thử sai password 5 lần và xác nhận khóa tạm.
- Tạo một Coach và một Trainee.
- Xác nhận form tạo account không có ô mật khẩu tạm, hiển thị mật khẩu mặc định `12345678` và cảnh báo account sẽ được yêu cầu đổi password.
- Xác nhận username trùng bị chặn.
- Đăng xuất, đăng nhập Coach/Trainee.
- Reset password Coach/Trainee bằng username + email; xác nhận màn hình đăng nhập tự điền đúng username vừa reset và mật khẩu mới đăng nhập được.
- Xác nhận mọi ô password ở đăng nhập, đổi mật khẩu, quên mật khẩu và tạo account đều có nút **Hiện/Ẩn**.
- Founder khóa account và xác nhận account không đăng nhập được.

## Sân/lớp

- Tạo sân tại **Khác > Sân dạy**; xác nhận danh sách Lớp học và form Lớp học không còn nút quản lý/tạo sân.
- Tạo lớp với nhiều ngày trong tuần; xác nhận lịch cố định hiển thị thành hai dòng.
- Nhập học phí mặc định, phân Coach kèm lương/buổi và chọn Trainee.
- Xác nhận danh sách Trainee không còn ô học phí riêng và tất cả dùng học phí mặc định.
- Coach/Trainee chỉ thấy lớp được phân.
- Founder chạm lớp chỉ thấy trang thông tin; bấm **Sửa lớp học** mới mở form sửa.
- Trainee mở chi tiết lớp và thấy danh sách các học viên cùng lớp.
- Ngừng hoạt động lớp/sân và xác nhận lịch sử không bị xóa.

## Check-in và điểm danh

- Coach chụp selfie check-in; xác nhận trạng thái chuyển thành **Chờ duyệt check-in** và lương chưa tăng.
- Founder mở hồ sơ Coach hoặc trang Điểm danh Huấn Luyện Viên, bấm **Chờ duyệt check-in**, xem ảnh selfie toàn màn hình rồi xác nhận.
- Xác nhận check-in đã duyệt được cộng vào số buổi và tiền lương; check-in bị từ chối không được tính và Coach nhận thông báo để chụp lại.
- Mở roster; chọn tất cả Có mặt; đổi một Trainee thành Đi trễ; hoàn tất.
- Mở lại và sửa một Trainee.
- Trainee xem lịch sử read-only.
- Founder điểm danh thay; xác nhận không thể lưu nếu thiếu lý do.
- Founder mở hồ sơ Trainee và xác nhận số Có mặt/Vắng mặt khớp các buổi đã hoàn tất; Đi trễ được tính là đã tham gia. Bên dưới ô tổng hợp phải có tối đa 5 dòng gần nhất theo `dd/MM · lớp · trạng thái`, lịch sử dài có nút xem đầy đủ.
- Founder mở hồ sơ Coach và xác nhận số buổi Đã check-in/Vắng check-in cùng số check-in Chờ duyệt được hiển thị. Bên dưới ô tổng hợp phải có tối đa 5 dòng gần nhất theo `dd/MM · lớp · giờ · trạng thái duyệt`, lịch sử dài có nút xem đầy đủ.
- Trong hồ sơ Trainee, chọn Năm/Tháng và xác nhận danh sách chỉ hiển thị đúng kỳ đã chọn; trong hồ sơ Coach, chọn Lớp dạy/Năm/Tháng và xác nhận bộ lọc kết hợp hoạt động đúng.
- Coach chưa check-in không thấy roster; sau khi chụp selfie check-in mới mở được Điểm danh học viên; chụp selfie check-out xong roster bị đóng và không thể mở lại buổi đó.

## Học phí

- Founder cấu hình Bank BIN/số tài khoản.
- Trainee thấy đúng số tiền, hạn ngày 05, nội dung và QR.
- Trainee bấm **Lưu QR Code** và xác nhận ảnh xuất hiện trong thư viện Ảnh.
- Upload bill → Founder nhận thông báo.
- Founder/Trainee chạm thumbnail bill, xem ảnh toàn màn hình, pinch zoom và lưu ảnh.
- Founder từ chối → Trainee nhận thông báo và upload lại.
- Founder xác nhận → Trainee thấy Đã đóng.
- Xuất PDF; kiểm tra tên đội, học viên, lớp, kỳ, số tiền, mã hóa đơn.

## Lương và thông báo

- Coach check-in buổi dạy; Founder xác nhận selfie rồi mở từng mục Tài chính và xác nhận lương tự tính theo số buổi đã duyệt × lương/buổi.
- Xác nhận số lương là read-only; Founder chỉ tick Đã thanh toán và nhập ghi chú.
- Dùng ngày sau 10 để kiểm tra reminder khi Pending.
- Founder gửi riêng một Trainee.
- Founder broadcast tất cả Trainee.
- Account nhận mở thông báo và chuyển sang đã đọc.

## Giao diện

- Xác nhận giao diện dùng chủ đạo xanh lá/trắng, thẻ nội dung bo tròn, nền xám nhạt và điểm nhấn vàng theo mẫu thiết kế bóng đá.
- Màn hình đăng nhập không còn dòng giải thích giữ phiên; cuối màn hình hiển thị đúng `Phiên bản Demo: 1.8` và `Designed by AWAKEN POST Production`, căn giữa.
- Thành viên mở thành hai nhóm Coach/Trainee rồi mới hiển thị danh sách.
- Mở hồ sơ rồi back về danh sách nhiều lần; không được xuất hiện khung trắng.
- Hồ sơ mặc định ở trạng thái xem; Founder/chính chủ có nút Sửa, account không có quyền không thấy nút.
- Hồ sơ Trainee có ngày sinh nhưng không có nút **Xóa ngày sinh** và không có ô số điện thoại cá nhân; Founder vẫn thấy/nhập được SĐT phụ huynh.
- Tài chính hiển thị hai ô Năm và Tháng trên cùng một dòng; danh sách năm bắt đầu từ 2026 và không có năm cũ hơn.
- Tổng quan Founder hiển thị logo lấp đầy khung, `Chào [Tên đội]`, tên Founder ở dòng riêng và ngày `dd/MM/yyyy`, không hiển thị thứ.
- Tên Founder trong hero lớn hơn 2pt so với bản 1.1 trước và ngày `dd/MM/yyyy` nằm ở một dòng riêng bên dưới.
- Dòng `Quản lý đội bóng cộng đồng` trên trang đăng nhập lớn hơn 2pt so với bản 1.1 trước.
- Trang Hôm Nay của Coach/Trainee hiển thị logo, tên đội, tên Founder và ngày `dd/MM/yyyy`, không hiển thị thứ.
- Rời một tab khi đang ở trang con; quay lại phải thấy trang đầu của tab.
- Focus hai ô nhập cuối form; xác nhận form chỉ tự cuộn vừa đủ để hai ô này không bị bàn phím che. Focus các ô phía trên không làm trang tự cuộn.
- Mở hồ sơ Founder, Coach và Trainee; tên và badge chức vụ phải căn giữa.
- Không hiển thị banner `Dữ liệu đang được lưu trên thiết bị này` hoặc thẻ `Phạm vi bản Offline`.
- Với Founder, mở từng mục trong lịch sử điểm danh để kiểm tra ngày học/check-in, lớp, thành viên và trạng thái tương ứng.
- Tạo Trainee có bật `Cầu Thủ Học Viên Được Hỗ Trợ`, sau đó sửa hồ sơ để tắt/bật lại; thêm vào lớp có học phí và xác nhận học viên không có hóa đơn, QR, yêu cầu thanh toán hay nhắc học phí khi đang được hỗ trợ.
- Tiền hiển thị theo mẫu `1,000,000 VNĐ`.
- Khi tạo lớp, nhập số buổi chu kỳ và tổng học phí; hoàn tất điểm danh 4/4 buổi phải tính đủ tổng tiền, còn 5 buổi thực tế phải tính theo 5 buổi, không dùng mức tháng cố định.
- Màn hình nhỏ và lớn.
- Font scale 200%.
- TalkBack đọc được button/status chính.
- Quyền camera bị từ chối.
- Bank chưa cấu hình không hiển thị QR giả.
- Danh sách rỗng và lỗi database hiển thị hướng dẫn phù hợp.
