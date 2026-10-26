# .NET 10, EF Core 10 và DPIA sơ bộ

**Issue:** #4 — Spike, không phải quyết định pháp lý hoặc nâng cấp sản phẩm  
**Đối chiếu:** 26-10-2026  
**Kết luận:** Ưu tiên nâng .NET 9 ngay trong Phase 1. Bản spike chạy trên máy này có SDK 10.0.102 và runtime .NET/Windows Desktop 10.0.2; đây chỉ xác nhận toolchain cục bộ, chưa xác nhận tương thích đầy đủ của toàn bộ thư viện và bộ cài.

## 1. Hiện trạng đã kiểm tra

- WPF đang target `net9.0-windows`; Domain và SqliteDataAccess target `net9.0`. Tests target `net10.0`.
- CI chạy `windows-latest` nhưng chỉ cài SDK `9.0.x`, trong khi test target net10. CI cần chuyển sang SDK 10 trước khi có thể kiểm tra đúng cấu hình test hiện tại.
- EF package versions đang lệch: WPF tham chiếu `Microsoft.EntityFrameworkCore` và `.Design` 9.0.4; SqliteDataAccess tham chiếu `.Design` và `.Sqlite` 9.0.2.
- Các package UI/runtime hiện dùng: CommunityToolkit.Mvvm 8.4.2, MaterialDesignColors/Themes 5.3.1, MdXaml 1.27.0 và Microsoft.Extensions.DependencyInjection 10.0.6. Việc restore không tự chứng minh khả năng tương thích runtime của WPF; cần kiểm tra build và smoke test trên Windows.
- Installer chỉ có `installer/Product.wxs`, khai báo WiX v4 và yêu cầu lệnh `wix build`; repo không khai báo phiên bản WiX/tool extension hoặc pipeline build MSI. `Product.wxs` đóng gói executable và icon, nên cần kiểm tra publish output, kiến trúc, runtime deployment và cài/nâng cấp/gỡ MSI trên máy Windows sạch.
- Mã SQLite hiện có `FocusTask`, `Profile`, `SessionHistory`, `SystemSettings`, `Quote` và snapshot phiên. `docs/database_schema.md` mô tả schema EduFocus đích với `ParentCredentials`, `ActivitySamples`, `AttentionEvents`, `BlockedAttempts`, `AuditLogs`… và ghi rõ đây là schema thiết kế, EF Core 10 mục tiêu. Không được xem toàn bộ bảng đích là dữ liệu đang được thu thập bởi code hiện tại.
- Mã `SessionHistory` hiện lưu tên tác vụ/profile dạng snapshot, thời gian, thời lượng, trạng thái phiên, số lần bị chặn và Strict Mode. Thiết kế đích còn đề xuất camera device ID, điểm/sự kiện chú ý và hoạt động foreground/domain. Các trường camera/behavior cần được rà soát quyền riêng tư trước khi triển khai.

## 2. Ma trận tương thích và lệnh xác minh

| Thành phần | Hiện tại | Đích/điều kiện Phase 1 | Đánh giá và việc cần làm |
|---|---|---|---|
| SDK | Máy spike có .NET SDK 10.0.102; chưa có `global.json` | SDK 10.x được pin/kiểm soát trong CI | Cài SDK 10 trên CI; ghi rõ SDK trong `global.json` nếu muốn cố định reproducible builds. |
| WPF/Windows Forms | `net9.0-windows`, WPF + WinForms | `net10.0-windows` | Chỉ target/build trên Windows; kiểm tra manifest, icon, startup, camera, native dependencies và DPI/runtime. |
| Class libraries | `net9.0` | Cùng target `net10.0` hoặc có lý do multi-target được ghi lại | Nâng đồng bộ để tránh build graph và runtime target lẫn lộn. |
| Tests | `net10.0` | SDK 10 và test runtime tương ứng | Hiện lệch SDK cài trên CI; thêm test vào CI sau khi SDK được nâng. |
| EF Core | 9.0.2 và 9.0.4 trong hai project | Tất cả gói Microsoft EF Core cùng phiên bản 10.0.x | Chọn cùng patch được hỗ trợ; rà breaking changes, provider, migrations, design-time tooling và test trên SQLite. EF providers thường không chạy qua major version; SQLite provider liệt kê hỗ trợ EF 8/9/10. |
| EF CLI | Không khai báo tool manifest trong repo | `dotnet-ef` cùng major/minor với EF Core dùng trong migration | Pin tool manifest; chạy `dotnet ef migrations list` và kiểm tra script SQL trước áp dụng. Nếu multi-target, chỉ rõ framework. |
| Dependency Injection | 10.0.6 trong app net9 hiện tại | Package version được xác minh hỗ trợ target/net10 | Hiện package 10.x đi cùng app 9.x; xác minh dependency graph và smoke test sau khi đồng bộ TFM. |
| UI packages | Toolkit 8.4.2, MaterialDesign 5.3.1, MdXaml 1.27.0 | Giữ nguyên nếu restore/build/runtime test đạt trên net10 | Kiểm tra TFM/dependency/native support và luồng UI thực; không tuyên bố đã được nhà cung cấp chứng nhận chỉ từ việc restore thành công. |
| WiX | WiX v4 XML; toolset/extension chưa pin trong repo | Pin WiX v4 tool và `WixToolset.UI.wixext` | Build MSI trên Windows; thử cài mới, upgrade, downgrade guard và uninstall; xác định rõ dữ liệu người dùng không bị xóa cùng app. |
| SQLite/data | EF9 code hiện tại; schema EF10 trong tài liệu là thiết kế đích | Không thay schema hoặc chuyển dữ liệu ngầm trong spike | Chỉ đưa migration cần thiết vào thay đổi riêng; backup/restore và kiểm thử trên bản sao DB trước release. |

Các lệnh xác minh mẫu sau khi checkout nhánh nâng cấp, chạy từ thư mục gốc trên Windows có SDK 10:

```powershell
dotnet --list-sdks
dotnet --info
dotnet restore DontBeLazy.slnx
dotnet build DontBeLazy.slnx --no-restore --configuration Release
dotnet test DontBeLazy.slnx --no-build --configuration Release --verbosity minimal
dotnet tool restore
dotnet ef migrations list --project src/DontBeLazy.SqliteDataAccess --startup-project src/DontBeLazy.WPF
```

Lệnh EF CLI cuối cần được điều chỉnh theo tool manifest/startup project thực tế và chạy trên Windows; không chạy migration lên dữ liệu người dùng trong bước kiểm tra này. Với installer, trước hết publish app theo kiến trúc/runtime đã chọn, sau đó build `installer/Product.wxs` bằng phiên bản WiX v4 đã pin và chạy kiểm thử MSI trên VM sạch.

## 3. Tình trạng hỗ trợ và mức ưu tiên

Trang chính sách hỗ trợ Microsoft, cập nhật 25-10-2026, xác nhận .NET 9 kết thúc hỗ trợ **10-11-2026**; .NET 10 LTS đang được hỗ trợ đến 14-11-2028. Tính từ ngày đối chiếu 26-10-2026, .NET 9 chỉ còn 15 ngày hỗ trợ. Vì vậy nâng cấp là ưu tiên P0 của Phase 1; không nên phát hành bản mới dựa trên .NET 9 mà không có phương án hỗ trợ rõ ràng.

Nguồn: [Microsoft .NET and .NET Core Support Policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).

## 4. Thay đổi Phase 1, thứ tự, rủi ro và rollback

1. **Chụp baseline:** lưu kết quả restore/build/test, danh sách package, migration hiện có và bản sao DB dùng thử. Không dùng dữ liệu cá nhân thật.
2. **Nâng toolchain/CI:** dùng SDK 10, sửa pipeline test target net10, đồng nhất target framework của solution. Khóa SDK/tool versions để build lặp lại được.
3. **Đồng bộ EF:** nâng mọi Microsoft EF Core package và `dotnet-ef` cùng major/minor/patch; xác minh provider SQLite được hỗ trợ. Rà EF10 breaking changes và generated migrations.
4. **Sửa và kiểm tra ứng dụng:** xử lý compile/analyzer/runtime regressions; kiểm tra DI, WPF screens, persistence, timezone, app startup/shutdown và SQLite trên Windows.
5. **Xác minh phát hành:** pin WiX tool/extension, publish theo runtime/architecture đã định, kiểm tra MSI cài mới/nâng cấp/gỡ và khởi chạy trên Windows sạch.
6. **Chỉ sau khi các bước trước đạt:** đề xuất migration DB riêng, có backup và phục hồi đã diễn tập. Không ghép nâng framework với thay đổi schema không liên quan.

| Rủi ro | Giảm thiểu |
|---|---|
| EF provider hoặc package bên thứ ba không tương thích major mới | Đồng bộ versions; kiểm tra dependency graph, build và runtime smoke test trên Windows; thay package nếu không có hỗ trợ phù hợp. |
| Migration EF tạo SQL/behavior khác hoặc làm hỏng DB cũ | So sánh migration/script với schema; dùng DB copy; backup trước upgrade; kiểm tra restore backup. |
| MSI hỏng do publish/runtime/WiX khác | Pin WiX; test MSI trên VM sạch, cả đường upgrade và uninstall; giữ lại MSI phát hành trước. |
| Build local được nhưng CI hoặc máy người dùng thất bại | Chạy pipeline trên Windows với SDK pin; kiểm tra publish artifact và Windows Desktop runtime trên môi trường sạch. |
| Downgrade app sau khi DB đã đổi schema không an toàn | Tách deploy code và migration; không tự hạ version DB. Nếu migration đã áp dụng, chỉ rollback bằng restore backup đã xác minh hoặc migration phục hồi được thiết kế/test riêng. |

**Rollback code:** trước migration, revert commit nâng cấp và cài lại MSI cũ. **Rollback dữ liệu:** không giả định downgrade tự đảo schema; khôi phục backup trước migration và xác minh dữ liệu. DB EduFocus mới không import DB DontBeLazy 1.x; giữ quy tắc này cho đến khi có công cụ import được duyệt/test riêng.

## 5. Data map sơ bộ

Bảng dưới tách **đang có trong code** khỏi **thiết kế đích trong tài liệu**. “Thời hạn” của các dòng thiết kế là đề xuất hiện có trong `SECURITY.md`/`docs/database_schema.md`; cần xác minh retention job có thật trước khi mô tả là được thực thi. Đường dẫn `%ProgramData%\EduFocus\edufocus.db` cũng là đường dẫn schema đích, chưa được xác nhận là nơi code hiện tại lưu DB.

| Dữ liệu | Trạng thái, mục đích | Nơi lưu dự kiến/hiện biết | Thời hạn và xóa | Người truy cập / mức rủi ro |
|---|---|---|---|---|
| Tác vụ, profile, settings, quote | Có entity/repository trong app; cấu hình học tập | SQLite của app hiện tại; vị trí runtime cần xác minh. Schema đích dự kiến `%ProgramData%\EduFocus\edufocus.db` | Đến khi người dùng xóa/reset app; cơ chế xóa/export cần xác minh | Người dùng Windows và người quản lý thiết bị; có thể liên kết với học sinh. |
| Lịch sử phiên và snapshot tên tác vụ/profile, thời lượng, trạng thái, blocked count | Có trong `SessionHistory`; báo cáo quá trình học | SQLite cục bộ | Schema đích đề xuất session 365 ngày; code hiện tại không chứng minh retention tự xóa | Người dùng thiết bị/người quản lý; hành vi và lịch học có thể là dữ liệu cá nhân. |
| PIN/recovery hash, lockout | Chỉ là thiết kế trong schema đích; không thấy bảng tương ứng trong `AppDbContext` hiện tại | SQLite đích | Tới khi đổi/reset hoặc gỡ tài khoản; cần xóa hash/salt cùng bản ghi | Người quản lý thiết bị; bí mật xác thực cần bảo vệ cao, tuyệt đối không lưu plaintext. |
| Activity sample: ứng dụng/cửa sổ/domain, mốc thời gian | Mô tả trong tài liệu yêu cầu/schema đích; xác minh code/thu thập trước khi phát hành | SQLite đích | Đề xuất 30 ngày; cần job và kiểm thử xóa thực tế | Người học/người quản lý thiết bị; dữ liệu hành vi, nguy cơ tăng nếu lưu title/URL chi tiết. Thu thập tối thiểu, không lưu nội dung trang. |
| Attention events/scores từ webcam | Đề xuất trong tài liệu tính năng/schema; xác minh implementation và model output | Nếu triển khai: xử lý local, chỉ lưu event/tổng hợp; không lưu frame theo yêu cầu thiết kế | Đề xuất 90 ngày cho event; không thu/lưu frame/audio/video | Có thể liên quan sinh trắc học/sức khỏe/hành vi và trẻ em; coi là high risk/potentially sensitive cho đến khi chuyên gia xác định phân loại. |
| Blocked attempts, domain/rule | Một phần có `BlockedCount` trong session; log chi tiết là thiết kế đích | SQLite đích | Đề xuất 90 ngày | Người dùng/người quản lý; có thể suy ra thói quen truy cập. Giới hạn domain, không ghi URL/query/content nếu không cần. |
| Audit logs | Thiết kế đích, chưa thấy bảng trong context hiện tại | SQLite đích | Đề xuất 365 ngày; phải xác định sự kiện cần lưu, quyền xóa và ngoại lệ | Người quản lý/đội hỗ trợ có thẩm quyền; tránh ghi credential, nội dung trang hoặc dữ liệu camera. |
| Telemetry, sao lưu, hỗ trợ từ xa | Chưa được xác nhận là có; nguyên tắc tài liệu tuyên bố local-first/no cloud | Chưa xác định; mặc định không gửi ra ngoài theo thiết kế | Nếu bổ sung, cần thời hạn, thông báo, lựa chọn và quy trình xóa riêng | Không có bên nhận ngoài theo thiết kế hiện tại; kiểm tra package/service và crash reporting trước release. |

**Luồng dự kiến:** thiết bị/camera và Windows foreground context → xử lý trên máy → chỉ lưu cấu hình, summary/event cần thiết vào SQLite → giao diện cục bộ cho người dùng/người quản lý. Chưa có bằng chứng trong spike rằng mọi luồng này đã được triển khai hoặc không có network egress; phải xác minh từ code, dependency/runtime và packet test trước release.

## 6. Checklist DPIA sơ bộ trước phát hành

Đây là checklist sàng lọc và chuẩn bị, không phải kết luận rằng mọi yêu cầu pháp lý đều áp dụng hoặc đã hoàn tất.

- [ ] Chốt controller, processor, nhà cung cấp/phụ thuộc, người vận hành thiết bị và đầu mối tiếp nhận yêu cầu dữ liệu; lập sơ đồ luồng thực tế, gồm backup, crash logs, update và hỗ trợ.
- [ ] Liệt kê riêng từng mục đích và trường dữ liệu; xác định căn cứ xử lý, tính cần thiết/tương xứng, thông báo dễ hiểu cho phụ huynh và học sinh, cơ chế rút lại/đóng giám sát và liên hệ xử lý yêu cầu quyền dữ liệu. Chuyên gia pháp lý xác nhận cơ chế phù hợp với trẻ em/độ tuổi/đại diện.
- [ ] Phân loại camera-derived attention, dữ liệu hành vi/foreground/domain và lịch học với tư vấn chuyên môn. Không khẳng định score là sự thật khách quan; hiển thị giới hạn và độ tin cậy, tránh dùng cho kỷ luật/tự động quyết định không được đánh giá.
- [ ] Thực hiện data minimization: xử lý camera trong bộ nhớ cục bộ nếu thực sự cần; không lưu/gửi frame, audio, face embedding hoặc nội dung trang; loại bỏ title/URL chi tiết nếu không thiết yếu.
- [ ] Xác minh nơi lưu thật, Windows ACL, phạm vi người dùng trên shared PC, bảo vệ DB/backup, credential hashing, khóa mã hóa nếu có, log redaction và bảo vệ trước truy cập quản trị viên cục bộ.
- [ ] Biến retention đề xuất thành job thực thi có kiểm thử: activity 30 ngày, event/blocked attempt 90 ngày, session summary/audit 365 ngày; kiểm tra cả DB, log, export và backup. Cho phép xóa/reset và xử lý bản sao theo quy tắc rõ ràng.
- [ ] Đánh giá rủi ro cho học sinh, truy cập trái phép, suy diễn hành vi, sai lệch attention detection, shared Windows account, mất máy, backup bị lộ, thay đổi mục đích và dữ liệu còn sót sau uninstall; ghi mức rủi ro, chủ sở hữu, biện pháp và rủi ro còn lại.
- [ ] Xác nhận có truyền dữ liệu ra ngoài hoặc chuyển qua biên giới không; nếu có thì ghi nhà nhận, trường dữ liệu, mục đích, căn cứ, hợp đồng/biện pháp và thông báo thích hợp.
- [ ] Chuẩn bị privacy notice, thiết lập mặc định an toàn, chỉ báo camera/monitoring dễ thấy, bật/tắt và xóa dữ liệu; kiểm tra không âm thầm khởi động thu thập sau crash/update.
- [ ] Tư vấn pháp lý đối chiếu Luật 91/2025/QH15 và Nghị định 356/2025/NĐ-CP, biểu mẫu, phạm vi, thời điểm nộp hồ sơ và nghĩa vụ thông báo/sự cố trước khi có xử lý thật. Điều 19 Nghị định mô tả nội dung hồ sơ đánh giá tác động gồm chủ thể/đầu mối, mục đích và hoạt động/luồng xử lý, dữ liệu, lưu trữ/xóa, biện pháp bảo vệ, đánh giá tuân thủ và rủi ro/giảm thiểu; yêu cầu cụ thể và khả năng áp dụng cần luật sư xác nhận. Không bắt đầu pilot có dữ liệu cá nhân trước khi chốt cổng pháp lý này.

**Biện pháp giảm thiểu đề xuất:** local-first/no cloud mặc định; không ghi raw camera/media; thu ít trường và thời gian ngắn nhất; thông báo minh bạch và có chỉ báo rõ; quyền truy cập giới hạn; không lưu credential plaintext; xóa tự động được kiểm thử; backup có bảo vệ; log không chứa bí mật; không biến ước lượng attention thành đánh giá tuyệt đối; hoàn tất đánh giá với chuyên gia trước pilot.

## 7. Nguồn pháp lý/chính sách chính thức

- [Microsoft: .NET and .NET Core Support Policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) — vòng đời .NET 9/10.
- [Microsoft Learn: Entity Framework Core providers](https://learn.microsoft.com/en-us/ef/core/providers/) — provider cần khớp major EF Core; SQLite liệt kê EF Core 8, 9, 10.
- [Microsoft Learn: EF Core 10 breaking changes](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/breaking-changes) — cần rà soát breaking changes và tooling khi nâng major.
- [Luật 91/2025/QH15 — PDF ký chính thức](https://datafiles.chinhphu.vn/cpp/files/vbpq/2025/7/91qh.signed.pdf) — Luật Bảo vệ dữ liệu cá nhân; hiệu lực 01-01-2026.
- [Nghị định 356/2025/NĐ-CP — PDF ký chính thức](https://datafiles.chinhphu.vn/cpp/files/vbpq/2026/01/356-nd.signed.pdf) — hướng dẫn thi hành; hiệu lực 01-01-2026. Điều 19 cần được đối chiếu cùng tư vấn pháp lý về hồ sơ đánh giá tác động.

### Giới hạn của spike

Chưa nâng code, chưa pin/đổi SDK CI, chưa build WiX MSI, chưa chạy DB migration, chưa kiểm chứng runtime data path/retention/network egress và chưa hoàn tất DPIA pháp lý. Các việc này là tiêu chí kiểm chứng ở Phase 1/7 trước pilot/phát hành.
