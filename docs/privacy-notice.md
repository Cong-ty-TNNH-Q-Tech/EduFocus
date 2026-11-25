# Thông báo quyền riêng tư — EduFocus

Phiên bản 2.1 · Ngày hiệu lực: 2026-11-25 · Áp dụng cho EduFocus 2.1.0 (Phase 8) trở đi

EduFocus là ứng dụng học tập chạy trên máy tính Windows, do phụ huynh thiết lập và quản lý.
Thông báo này giải thích EduFocus xử lý dữ liệu gì, vì sao, lưu ở đâu, lưu bao lâu, ai có thể
truy cập và phụ huynh/học sinh có những quyền gì. Thông báo được hiển thị trước khi bật giám sát
trong lần thiết lập đầu tiên và luôn mở lại được từ Parent Mode.

Từ phiên bản 2.1.0, EduFocus có thêm **cloud relay tùy chọn** để gửi thông báo đẩy tới điện thoại
phụ huynh. Cloud **mặc định tắt**; nếu phụ huynh không ghép nối điện thoại, ứng dụng hoạt động hoàn
toàn cục bộ như bản 2.0.0. Khi bật, chỉ năm loại sự kiện tổng hợp rời khỏi máy (mục 6.1).

## 1. Tóm tắt

- Mặc định, EduFocus xử lý dữ liệu **trên chính máy tính đã cài đặt** và không gửi dữ liệu học tập ra
  ngoài. Phụ huynh có thể **tự bật** cloud relay để nhận thông báo trên điện thoại; khi bật, chỉ năm
  loại sự kiện tổng hợp được gửi tới backend (mục 6.1).
- **Không lưu ảnh, video, âm thanh hoặc khung hình webcam.** Khung hình camera chỉ tồn tại trong
  bộ nhớ trong thời gian ngắn để ước lượng trạng thái tập trung rồi bị xóa.
- **Không nhận diện danh tính, không phân tích cảm xúc, không tạo dữ liệu sinh trắc học.**
- **Không telemetry, không quảng cáo, không bán dữ liệu.** Khi cloud bật, distributor UnifiedPush
  (ví dụ ntfy) chỉ nhận endpoint, metadata giao nhận và payload đã mã hóa (mục 7.1).
- Kiểm tra cập nhật mặc định **tắt**; khi phụ huynh bật, ứng dụng chỉ gọi GitHub Releases API và
  GitHub có thể thấy địa chỉ IP của máy.
- Để hiển thị trang chặn trên cả trang HTTPS, khi bảo vệ đang bật EduFocus cài một **chứng chỉ gốc
  cục bộ** (chỉ trên máy này) và tự phát hành chứng chỉ ngắn hạn cho đúng tên miền đang bị chặn;
  chứng chỉ gốc bị gỡ khi tắt bảo vệ hoặc gỡ cài đặt và khóa riêng không rời khỏi máy.
- Dữ liệu được giữ theo thời hạn (mặc định 30/90/365 ngày; dữ liệu cloud 24 giờ–365 ngày tùy loại)
  và có thể xóa toàn bộ trong Parent Mode bằng PIN; dữ liệu cloud xóa trên app mobile bằng xác nhận
  `XÓA`.

## 2. Ai chịu trách nhiệm

- **Nhà phát triển:** ThanhTrunggDEV (bên cung cấp phần mềm EduFocus).
- **Phụ huynh/người giám hộ:** người trực tiếp cài đặt, cấu hình và quyết định bật giám sát trên
  thiết bị của gia đình; giữ vai trò kiểm soát dữ liệu trên thiết bị.
- Nhà phát triển không có quyền truy cập dữ liệu học tập trên máy của gia đình. Khi phụ huynh bật
  cloud relay, nhà phát triển (hoặc tổ chức tự host backend) xử lý dữ liệu cloud mô tả ở mục 6.1 với
  vai trò bên xử lý/bên kiểm soát tùy mô hình triển khai; vai trò pháp lý chi tiết được nêu trong hồ
  sơ DPIA và cần được tư vấn pháp lý xác nhận trước khi phát hành thương mại.

Kênh liên hệ được liệt kê ở mục 12.

## 3. Dữ liệu được xử lý

EduFocus xử lý các nhóm dữ liệu sau; mặc định tất cả lưu cục bộ, riêng nhóm cloud (mục 6.1) chỉ tồn
tại khi phụ huynh chủ động bật:

- **Cấu hình:** bật/tắt bảo vệ và giám sát, lịch bảo vệ, ngưỡng xao nhãng/vắng mặt, chu kỳ lấy
  mẫu, thời hạn lưu, giao diện/ngôn ngữ, khởi động cùng Windows, lựa chọn kiểm tra cập nhật,
  thiết bị camera đã chọn.
- **Xác thực phụ huynh:** PIN và mã khôi phục chỉ được lưu dưới dạng **hash PBKDF2-SHA256** kèm
  salt; không lưu bản rõ. Số lần nhập sai và thời điểm khóa tạm thời được lưu để chống dò PIN.
- **Phiên học:** thời điểm bắt đầu/kết thúc, nhịp tim ứng dụng (heartbeat), thời lượng hoạt động,
  thời lượng ước lượng tập trung/xao nhãng/vắng mặt/buồn ngủ, thời lượng camera không khả dụng,
  thời gian idle, thời gian lớp trực tuyến, độ tin cậy trung bình, số lần cố truy cập bị chặn và
  ghi chú do phụ huynh tự nhập.
- **Mẫu hoạt động:** tên ứng dụng, tên tiến trình, **tiêu đề cửa sổ** (đã làm sạch và cắt ngắn),
  tên miền và URL đã loại bỏ query/fragment/thông tin đăng nhập, nguồn URL, phân loại
  học/xao nhãng/trung tính/không rõ, trạng thái foreground, cờ lớp trực tuyến và số giây idle.
  Tiêu đề cửa sổ có thể chứa tên tài liệu; dữ liệu này chỉ dùng để phân loại hoạt động, không xuất
  ra báo cáo, CSV hay file log.
- **Sự kiện tập trung:** trạng thái (Focused/Distracted/Absent/Drowsy/CameraUnavailable), thời
  điểm bắt đầu/kết thúc, thời lượng, độ tin cậy, tên ứng dụng/tên miền nếu có và ghi chú đã làm
  sạch. **Không có khung hình, ảnh, landmark hay embedding khuôn mặt.**
- **Lần truy cập bị chặn:** loại mục tiêu, tên miền/tên tiến trình/URL đã làm sạch, thời điểm và
  cách quan sát (trang chặn, theo dõi tiến trình hoặc browser extension). Không lưu nội dung
  trang, tin nhắn hay User-Agent.
- **Audit:** hành động quan trọng (bật/tắt bảo vệ, đổi lịch, đổi PIN, xóa dữ liệu, áp dụng lưu
  trữ...), thời điểm và mã tương quan; không chứa PIN, mã khôi phục hay token.
- **Nhật ký kỹ thuật:** tệp log cục bộ phục vụ chẩn đoán, đã che PIN/mã khôi phục/token/URL đầy
  đủ; tối đa 10 MB mỗi tệp, giữ 14 ngày. UI ghi vào `%LocalAppData%\EduFocus\logs`; service bảo vệ
  ghi vào `%ProgramData%\EduFocus\logs` với cùng quy tắc che dữ liệu.
- **Tệp hỗ trợ hệ thống:** bản sao `hosts` trước khi chặn (để khôi phục), tệp trạng thái watchdog
  hoặc service trong `%ProgramData%\EduFocus\runtime`, và bản xuất CSV do phụ huynh chủ động tạo.
- **Chứng chỉ cục bộ cho trang chặn HTTPS:** một chứng chỉ gốc do EduFocus sinh trên máy, lưu tại
  `%ProgramData%\EduFocus\certificates` và được Windows DPAPI bảo vệ; chỉ phần công khai được cài
  vào kho tin cậy của máy trong thời gian bảo vệ ON, và chỉ chứng chỉ ngắn hạn cho đúng tên miền bị
  chặn mới được phát hành. Không có tên miền hay dữ liệu duyệt web nào được gửi tới bên thứ ba để
  cấp chứng chỉ.
- **Khóa mã hóa DB:** 32 byte ngẫu nhiên dùng để mã hóa cơ sở dữ liệu, được Windows DPAPI bảo vệ
  trong `%ProgramData%\EduFocus\edufocus.key`. Khóa không phải PIN/mật khẩu, không nằm dạng văn bản
  thường và không dùng được trên máy khác.
- **Dữ liệu cloud (tùy chọn, chỉ khi bật):** năm loại sự kiện tổng hợp (`daily_summary` gồm phút học,
  phút tập trung ước lượng, số lần xao nhãng, số lần bị chặn, độ phủ camera, phút lớp trực tuyến;
  `attention_alert` gồm loại và thời lượng; `protection_off` gồm thời điểm/lý do; `security_alert` gồm
  loại và thời điểm; `blocked_attempt` chỉ gồm ngày địa phương), tên thiết bị, nền tảng, UnifiedPush
  endpoint và khóa công khai Web Push, token truy cập cloud (server chỉ lưu hash), preference thông báo
  và log thông báo đã phát. Không gửi
  URL, tên miền, tiêu đề cửa sổ, tên ứng dụng, mẫu hoạt động thô, dữ liệu camera, PIN, mã khôi phục hay
  ghi chú phụ huynh.

EduFocus **không** thu thập: vị trí, danh bạ, lịch, email, nội dung clipboard, nội dung trang web,
lịch sử duyệt web đầy đủ, âm thanh micro, dữ liệu sinh trắc học, hay thông tin nhận dạng khuôn mặt.

### Browser extension

Khi phụ huynh cài extension Chrome/Edge của EduFocus, URL điều hướng cấp tab được so khớp với
danh sách chặn **ngay trong bộ nhớ trình duyệt**; extension không ghi lịch sử duyệt web, không đọc
nội dung trang, cookie hay biểu mẫu. Chỉ khi URL khớp một rule chặn, URL đã làm sạch (bỏ
query/fragment/thông tin đăng nhập) mới được gửi qua native messaging tới tiến trình EduFocus trên
cùng máy để ghi nhận lần truy cập bị chặn. URL này không được gửi tới máy chủ hay bên thứ ba.
Khi EduFocus không chạy, extension không chặn và không gửi dữ liệu. Chi tiết:
[browser-extension.md](./browser-extension.md).

## 4. Camera và ước lượng tập trung

- Camera chỉ được mở khi phụ huynh bật giám sát và phiên học đang hoạt động.
- Mỗi khung hình được xử lý trong RAM để phát hiện khuôn mặt và ước lượng trạng thái; khung hình
  bị hủy ngay sau khi xử lý, **trước khi** bất kỳ sự kiện nào được ghi.
- Không ghi ảnh/video/âm thanh, không lưu landmark thô, không tạo embedding, không gửi dữ liệu
  camera qua mạng. Kể cả khi cloud bật, chỉ cảnh báo tổng hợp `distracted`/`absent` kèm thời lượng
  được gửi, không có khung hình hay dữ liệu attention thô.
- Kết quả chỉ là ước lượng (Focused/Distracted/Absent/Drowsy/CameraUnavailable) kèm độ tin cậy và
  có thể sai. EduFocus không phải thiết bị y tế, không phải eye tracker và không dùng để chống
  gian lận thi cử.
- Khi camera bị từ chối quyền, đang bận hoặc không có, EduFocus ghi nhận `CameraUnavailable` và
  giảm độ phủ dữ liệu thay vì suy đoán trạng thái.

## 5. Mục đích và cơ sở pháp lý

Dữ liệu được xử lý để:

- hiển thị cho phụ huynh thời gian học, mức độ tập trung ước lượng và hoạt động trên máy;
- thực thi danh sách chặn web/ứng dụng và lịch bảo vệ do phụ huynh cấu hình;
- tạo báo cáo ngày/tuần/tháng và bản xuất CSV do phụ huynh yêu cầu;
- gửi thông báo đẩy và báo cáo tóm tắt tới điện thoại phụ huynh đã ghép nối (chỉ khi bật cloud);
- vận hành an toàn: xác thực PIN, chống dò mã, phục hồi sau crash, lưu trữ và xóa dữ liệu.

Cơ sở pháp lý: sự đồng ý của phụ huynh/người giám hộ và việc thực hiện trách nhiệm giáo dục,
giám sát con trong gia đình, tuân thủ **Luật Bảo vệ dữ liệu cá nhân 91/2025/QH15** và
**Nghị định 356/2025/NĐ-CP** (hiệu lực 01/01/2026) cùng văn bản hướng dẫn hiện hành. Dữ liệu
attention/camera của trẻ em được xem là dữ liệu cá nhân nhạy cảm; hồ sơ đánh giá tác động xử lý
dữ liệu cá nhân (DPIA) được lập riêng và phải được tư vấn pháp lý rà soát trước khi phát hành
thương mại.

Cloud relay là **tùy chọn và dựa trên sự đồng ý riêng** của phụ huynh tại thời điểm ghép nối; phụ
huynh có thể rút lại bất kỳ lúc nào (mục 9). Vì distributor UnifiedPush/ntfy có thể do bên thứ ba
vận hành, việc bật thông báo đẩy có thể liên quan đến xử lý dữ liệu ngoài thiết bị; đánh giá chi tiết
và biện pháp bảo vệ nằm trong [dpia.md](./dpia.md).

## 6. Lưu trữ và bảo vệ

- Cơ sở dữ liệu: `%ProgramData%\EduFocus\edufocus.db` (kèm WAL/SHM và tối đa 3 bản backup).
  Thư mục được cấp quyền chỉ cho Administrators và SYSTEM; tài khoản Windows chuẩn không đọc được.
- Chứng chỉ cục bộ: `%ProgramData%\EduFocus\certificates\block-root-ca.edufocus` chứa khóa riêng
  của CA dưới dạng DPAPI bảo vệ, cùng ACL Administrators/SYSTEM. Thư mục này bị xóa khi gỡ cài đặt.
  Chứng chỉ gốc công khai nằm trong kho `LocalMachine\Root` chỉ khi bảo vệ đang bật; tắt bảo vệ hoặc
  gỡ cài đặt sẽ gỡ bỏ. Khóa riêng của chứng chỉ ngắn hạn chỉ tồn tại trong tiến trình đang phục vụ
  trang chặn và bị xóa khi dừng.
- Cơ sở dữ liệu, WAL/SHM và backup được **mã hóa bằng SQLCipher**. Khóa mã hóa lưu trong
  `edufocus.key` dưới dạng được Windows DPAPI bảo vệ (không phải văn bản thường), cùng thư mục được
  giới hạn cho Administrators/SYSTEM. Nếu file khóa bị mất hoặc dữ liệu được chuyển sang máy khác,
  EduFocus không thể giải mã; phụ huynh cần giữ file khóa cùng bản sao lưu và chỉ khôi phục trên
  cùng máy.
- Log kỹ thuật: `%LocalAppData%\EduFocus\logs` (UI) và `%ProgramData%\EduFocus\logs` (service).
- Bản xuất CSV: `%LocalAppData%\EduFocus\exports` — do phụ huynh chủ động tạo nên nằm ngoài ACL
  thư mục dữ liệu; phụ huynh chịu trách nhiệm bảo vệ và xóa tệp này.
- Trong bản cài MSI, phần bảo vệ (hosts, block page, theo dõi tiến trình, heartbeat) chạy trong
  Windows Service `EduFocusProtection` dưới tài khoản LocalSystem. Service chỉ đọc/ghi cùng cơ sở
  dữ liệu và log mô tả ở trên; camera và attention vẫn chạy trong phiên người dùng, không chuyển
  sang service và không tạo thêm loại dữ liệu.
- PIN/recovery code chỉ lưu hash; khóa Parent Mode tự hết hạn sau 5 phút idle và không được khôi
  phục sau khi khởi động lại.
- Khi cloud tắt (mặc định), toàn bộ dữ liệu nằm trên máy, không có máy chủ của EduFocus.

### 6.1. Cloud relay tùy chọn (Phase 8)

- Khi phụ huynh ghép nối điện thoại, desktop gửi sự kiện tổng hợp qua HTTPS tới backend EduFocus
  (do nhà phát triển vận hành hoặc do phụ huynh/tổ chức tự host). Dữ liệu cloud nằm trong SQLite
  của backend, không nằm trên máy tính học sinh.
- Token truy cập cloud (`efd_` cho desktop, `efm_` cho mobile) là chuỗi ngẫu nhiên 32 byte; server chỉ
  lưu SHA-256, có scope tối thiểu và có thể thu hồi/xoay vòng. Token lưu trên desktop được DPAPI bảo
  vệ; trên Android lưu trong secure storage của hệ điều hành. Token không bao giờ được ghi log.
- Endpoint UnifiedPush và khóa công khai Web Push (`p256dh`, `auth`) của điện thoại được lưu để gửi
  thông báo; dữ liệu này không chứa nội dung học tập và bị vô hiệu khi phụ huynh xóa thiết bị/gia đình
  hoặc khi distributor báo endpoint không còn hợp lệ.
- Nội dung push được mã hóa Web Push (RFC8291) và chỉ gồm mã loại, ngày và mã thông báo; app mobile
  tự dựng câu tiếng Việt chung. Chi tiết học tập chỉ được tải qua API khi phụ huynh mở app.
- Backend ghi audit thao tác ghép nối/thu hồi/xóa nhưng không lưu PIN, mã khôi phục, token thô hay
  nội dung học tập chi tiết.
- Backend có thể tự host: khi đó chủ máy chủ tự chịu trách nhiệm TLS, backup, retention và nghĩa vụ
  dữ liệu cá nhân; tài liệu vận hành ở [cloud-backend.md](./cloud-backend.md).

## 7. Chia sẻ dữ liệu

- Khi cloud tắt (mặc định), EduFocus **không bán, không chia sẻ, không tải lên** dữ liệu học tập,
  dữ liệu camera hay danh sách hoạt động.
- Không có telemetry, phân tích từ xa hay quảng cáo.
- **Kiểm tra cập nhật là tùy chọn và mặc định tắt.** Khi phụ huynh bật, ứng dụng gửi một yêu cầu
  GET tới GitHub Releases API với `User-Agent: EduFocus/{version}`. Nội dung yêu cầu không chứa
  dữ liệu học tập, tên máy, danh sách tiến trình hay nội dung màn hình, nhưng GitHub vẫn thấy địa
  chỉ IP và thông tin kết nối tiêu chuẩn. Phụ huynh có thể tắt lại bất kỳ lúc nào trong Parent
  Mode.
- Thay đổi `hosts` chỉ định tuyến tên miền trên máy; không gửi dữ liệu ra ngoài.
- Chứng chỉ cho trang chặn HTTPS được ký bởi CA cục bộ trên máy; EduFocus không liên hệ CA/OCSP/CRL
  bên ngoài và không gửi tên miền bị chặn đi đâu để cấp chứng chỉ.
- Browser extension chỉ trao đổi với tiến trình EduFocus qua named pipe/native messaging cục bộ;
  không gửi URL, lịch sử duyệt web hay dữ liệu học tập ra ngoài máy.

### 7.1. Cloud relay và UnifiedPush/ntfy (tùy chọn)

- Khi phụ huynh bật cloud, dữ liệu ở mục 6.1 được gửi qua HTTPS tới backend EduFocus. Nhà phát triển
  không dùng dữ liệu này cho quảng cáo, huấn luyện mô hình hay mục đích khác ngoài vận hành thông báo
  và báo cáo tóm tắt cho chính gia đình.
- Backend dùng **Web Push (RFC8030)** qua distributor UnifiedPush, ví dụ ntfy. Distributor/ntfy xử lý
  endpoint thiết bị, địa chỉ IP và metadata giao nhận; nội dung message được mã hóa RFC8291 bằng khóa
  do app tạo, chỉ gồm mã loại, ngày và mã thông báo, không chứa chi tiết học tập. Tùy distributor, dữ
  liệu có thể được xử lý ngoài Việt Nam; phụ huynh có thể chọn self-host ntfy.
- Không có nhà cung cấp quảng cáo, analytics hay dịch vụ bên thứ ba nào khác tham gia khi cloud bật.
- Nếu phụ huynh tự host backend hoặc ntfy, dữ liệu cloud/push nằm trên hạ tầng do phụ huynh/tổ chức
  chọn; nhà phát triển không có quyền truy cập.
- Phụ huynh có thể ngắt kết nối hoặc xóa dữ liệu cloud bất kỳ lúc nào (mục 9–10).
- **Giới hạn đã biết:** push phụ thuộc distributor UnifiedPush (ví dụ ntfy) và kết nối Web Push; độ
  trễ do mạng và chính sách tiết kiệm pin của Android. App mobile chỉ xem dữ liệu desktop đã đồng bộ
  (không có timeline chi tiết) và không điều khiển từ xa (không sửa blocklist, không bật/tắt bảo vệ).
  Gỡ ứng dụng không tự
  xóa dữ liệu cloud; phụ huynh phải dùng "Xóa dữ liệu trên cloud" trên app mobile.

## 8. Thời hạn lưu

- Mẫu hoạt động chi tiết: **30 ngày** (có thể chỉnh trong Parent Mode).
- Sự kiện tập trung và lần truy cập bị chặn: **90 ngày** (có thể chỉnh).
- Tóm tắt phiên học và audit: **365 ngày** (có thể chỉnh cho phiên; audit cố định 365 ngày).
- Log kỹ thuật: **14 ngày**; tối đa 10 MB mỗi tệp.
- Backup DB: tối đa **3 bản**, được mã hóa cùng khóa, tạo trước migration và xoay vòng. File khóa
  không nằm trong bản sao lưu; cần được giữ cùng bộ backup.
- Bản sao `hosts`: chỉ giữ trong thời gian cần khôi phục và bị xóa sau khi khôi phục/self-heal.
- Bản xuất CSV: giữ cho tới khi phụ huynh tự xóa; EduFocus không tự xóa tệp bên ngoài DB.
- Dữ liệu của phiên đang hoạt động không bị retention xóa.
- **Dữ liệu cloud (khi bật):** pairing session **24 giờ**; sync event thô để chống trùng **7 ngày**;
  log thông báo **30 ngày**; tóm tắt ngày **180 ngày**; audit cloud **365 ngày**. Xóa gia đình trên
  app mobile xóa ngay toàn bộ dữ liệu cloud, không chờ hết hạn.

Retention cục bộ chạy tự động mỗi ngày (khoảng 03:00 giờ địa phương) hoặc theo lịch của ứng dụng.
Retention cloud chạy hằng ngày trên backend.

## 9. Quyền của phụ huynh và học sinh

- **Xem dữ liệu:** báo cáo trong Parent Mode; xuất CSV theo khoảng ngày.
- **Chỉnh sửa cấu hình:** phụ huynh sửa mọi thiết lập trong Parent Mode sau khi xác thực PIN.
- **Xóa dữ liệu:** xóa toàn bộ dữ liệu quan sát trong Parent Mode (yêu cầu PIN và gõ đúng chữ
  `XÓA`), hoặc chọn xóa dữ liệu khi gỡ cài đặt. Xóa là không thể hoàn tác. Dữ liệu cloud được xóa
  trên app mobile bằng xác nhận `XÓA` hoặc bằng cách ngắt kết nối và yêu cầu nhà phát triển xóa.
- **Rút lại đồng ý:** tắt giám sát (dừng camera/attention), tắt bảo vệ, hoặc gỡ cài đặt và chọn
  xóa dữ liệu. Với cloud, ngắt kết nối trên desktop (dừng gửi dữ liệu, thu hồi token) hoặc xóa gia
  đình trên app mobile. Việc rút lại không ảnh hưởng tính hợp pháp của xử lý đã thực hiện trước đó.
- **Phản đối/hạn chế xử lý:** tắt giám sát hoặc tắt bảo vệ trong Parent Mode; một số tính năng
  chặn sẽ ngừng hoạt động tương ứng.
- **Khiếu nại:** liên hệ nhà phát triển theo mục 12; nếu chưa được giải quyết, có thể khiếu nại
  tới cơ quan có thẩm quyền về bảo vệ dữ liệu cá nhân theo quy định hiện hành.

Học sinh được phụ huynh thông báo về phạm vi giám sát. Theo quy định hiện hành, trẻ từ đủ 7 tuổi
cần được xem xét ý kiến/sự đồng ý phù hợp độ tuổi khi xử lý dữ liệu cá nhân của trẻ; EduFocus nhắc
phụ huynh xác nhận điều này trong onboarding và không hỗ trợ giám sát che giấu.

## 10. Yêu cầu xóa dữ liệu và thời hạn phản hồi

- **Trong ứng dụng:** xóa ngay khi phụ huynh xác nhận; áp dụng cho mẫu hoạt động, sự kiện tập
  trung, lần truy cập bị chặn và tóm tắt phiên không đang hoạt động. Settings, PIN và audit được
  giữ lại để bảo vệ an toàn; audit ghi nhận thao tác xóa.
- **Qua nhà phát triển:** gửi yêu cầu qua kênh riêng tư ở mục 12. Nhà phát triển sẽ xác nhận tiếp
  nhận trong **72 giờ làm việc** và hướng dẫn hoàn tất trong **30 ngày** kể từ khi xác minh yêu
  cầu, trừ khi pháp luật yêu cầu thời hạn khác. Vì dữ liệu nằm trên máy gia đình, nhà phát triển
  thường không thể xóa thay; hướng dẫn sẽ tập trung vào thao tác xóa cục bộ và gỡ cài đặt.
- **Khi gỡ cài đặt:** mặc định giữ dữ liệu để cài lại; chọn "xóa toàn bộ dữ liệu" hoặc silent
  uninstall với `DELETEUSERDATA=1` để xóa `%ProgramData%\EduFocus`, gồm DB, backup, file khóa
  `edufocus.key` và log của service. File CSV và log của UI trong `%LocalAppData%\EduFocus` cần được
  xóa thủ công.
- **Dữ liệu cloud:** xóa ngay khi phụ huynh chọn "Xóa dữ liệu cloud" trên app mobile và gõ `XÓA`
  (backend cascade toàn bộ gia đình, thiết bị, token, push registration, tóm tắt và thông báo). Ngắt
  kết nối trên desktop chỉ dừng gửi và thu hồi token desktop; để xóa toàn bộ, phụ huynh dùng app
  mobile hoặc gửi yêu cầu qua kênh ở mục 12. Xóa cloud không thể hoàn tác.
- **Dữ liệu cloud do bên tự host:** chủ máy chủ chịu trách nhiệm thực hiện yêu cầu xóa theo chính
  sách của họ; nhà phát triển không truy cập được backend đó.

## 11. Sự cố dữ liệu

Nếu phát hiện lộ, mất hoặc truy cập trái phép dữ liệu, hãy báo ngay qua kênh riêng tư ở mục 12.
Nhà phát triển sẽ đánh giá phạm vi, hạn chế tác động, khắc phục và thông báo cho các bên liên quan
theo thời hạn luật định (dự kiến trong vòng 72 giờ kể từ khi xác nhận sự cố, theo tư vấn pháp lý).
Sự cố liên quan backend cloud (token, push registration, tóm tắt ngày) được xử lý theo cùng quy
trình, gồm thu hồi token bị ảnh hưởng và thông báo cho phụ huynh. Chi tiết quy trình nằm trong
[dpia.md](./dpia.md) §8.

## 12. Liên hệ

- Báo cáo riêng tư/bảo mật: https://github.com/Cong-ty-TNNH-Q-Tech/EduFocus/security/advisories/new
- Yêu cầu về dữ liệu cá nhân: mở báo cáo riêng tư qua liên kết trên, ghi rõ "Yêu cầu dữ liệu cá
  nhân" và mô tả yêu cầu. Không gửi PIN, mã khôi phục, dữ liệu học tập hoặc tệp DB trong báo cáo.
- Tài liệu kỹ thuật: [SECURITY.md](../SECURITY.md), [dpia.md](./dpia.md),
  [cloud-backend.md](./cloud-backend.md), [database_schema.md](./database_schema.md) §14/§16/§18.

## 13. Thay đổi thông báo

Khi dữ liệu, mục đích hoặc cách chia sẻ thay đổi, EduFocus cập nhật thông báo này, tăng số phiên
bản và hiển thị lại trong onboarding/bản phát hành tương ứng. Bản 2.1 (2026-11-25) thay FCM bằng
UnifiedPush/ntfy; phụ huynh phải xác nhận lại thông báo trước khi bật cloud lần đầu. Bản đang
áp dụng được cài kèm tại `docs/privacy-notice.md` trong thư mục cài đặt và trong ứng dụng tại mục
"Thông báo quyền riêng tư".

---

Tài liệu này là bản diễn giải kỹ thuật về quyền riêng tư của sản phẩm, **không phải tư vấn pháp
lý**. Hồ sơ DPIA và thông báo này cần được luật sư/DPO rà soát trước khi phát hành thương mại.
