# EduFocus — Quy trình phát hành

Tài liệu mô tả cách tạo bản phát hành từ tag: build, test, ký số, đóng gói MSI,
tạo checksum và công bố GitHub Release. Workflow nằm ở
[`.github/workflows/release.yml`](../.github/workflows/release.yml).

Tham chiếu: [installer.md](./installer.md), [SECURITY.md](../SECURITY.md),
[CHANGELOG.md](../CHANGELOG.md), [CONTRIBUTING.md](../CONTRIBUTING.md).

---

## 1. Versioning

EduFocus dùng [Semantic Versioning](https://semver.org/spec/v2.0.0.html). **Tag là nguồn
sự thật duy nhất** cho version của bản phát hành:

| Hạng mục | Giá trị | Ví dụ |
|---|---|---|
| Tag | `v<major>.<minor>.<patch>[-prerelease]` | `v2.0.0`, `v2.0.0-rc.1` |
| Assembly/ProductVersion | full SemVer từ tag | `2.0.0-rc.1` |
| FileVersion (Win32) | phần số `major.minor.patch` | `2.0.0` |
| MSI `ProductVersion` | phần số `major.minor.patch` | `2.0.0` |
| GitHub Release | prerelease khi version có dấu `-` | `v2.0.0-rc.1` → prerelease |

`release.yml` truyền version vào `dotnet publish` và `installer/Build-Installer.ps1`,
sau đó kiểm tra lại `ProductVersion`/`FileVersion` của `EduFocus.exe` và `ProductVersion`
của MSI. Nếu một giá trị lệch với tag, job fail trước khi phát hành.

**CHANGELOG là bắt buộc.** Section của version phải tồn tại và có mục
`### Breaking changes` (ghi "Không có" nếu không phá vỡ tương thích):

```markdown
## [2.0.0] - 2026-11-01
...
### Breaking changes
- ...
```

Workflow chấp nhận cả heading `## [Unreleased] — EduFocus 2.0.0 (ngày)` để hỗ trợ
giai đoạn chưa chốt ngày phát hành. Release notes lấy nguyên section này, thêm yêu cầu
hệ thống và hướng dẫn xác minh từ `installer/release-notes-footer.md`.

---

## 2. Kích hoạt workflow

### Phát hành thật bằng tag

```bash
git tag v2.0.0
git push origin v2.0.0
```

Tag khớp `v*.*.*` sẽ chạy job **Build, test and package**, sau đó job
**Publish GitHub Release** tạo release (prerelease nếu tag có hậu tố).

### Dry run trên branch thử nghiệm

Vào **Actions → Release → Run workflow**, chọn branch, nhập `version` và giữ
`dry_run = true`. Hoặc dùng `gh`:

```bash
gh workflow run release.yml --ref <branch> -f version=2.0.0-rc.1 -f dry_run=true
gh run watch
```

Dry run chạy đầy đủ build/test/ký/đóng gói/checksum và upload artifact, nhưng **không**
tạo release và **không** tạo tag. Tải artifact trong trang run để kiểm tra.

> `dry_run = false` từ `workflow_dispatch` sẽ tạo tag `v<version>` tại commit hiện tại.
> Chỉ dùng khi thật sự muốn phát hành; quy trình chuẩn vẫn là push tag.

---

## 3. Workflow làm gì

### Job `build` — quyền `contents: read`

1. Checkout với `persist-credentials: false` (không lưu token vào git config).
2. Cài .NET 10 và cache NuGet theo `*.csproj` + `.config/dotnet-tools.json`.
3. Xác thực SemVer và tính `version`/`numeric`/`tag`/`dry_run`.
4. Tạo release notes từ CHANGELOG; fail sớm nếu thiếu section hoặc `### Breaking changes`.
5. `dotnet tool restore`, restore, build Release, chạy toàn bộ test (TRX được upload).
6. Publish self-contained single-file `win-x64` cho `EduFocus.exe` và
   `publish/native-host/EduFocus.NativeHost.exe`; xóa `*.pdb`/`*.lib`.
7. Kiểm tra version trong cả hai executable.
8. Ký `EduFocus.exe` và `EduFocus.NativeHost.exe` nếu có chứng chỉ (xem §4).
9. Build MSI bằng WiX 4.0.6 + `WixToolset.UI.wixext`/`WixToolset.Util.wixext`;
   `Build-Installer.ps1` quét payload để chặn secret/runtime data. Kiểm tra
   `ProductVersion` của MSI bằng Windows Installer COM.
10. Ký MSI nếu exe đã được ký.
11. Tạo portable ZIP, đóng gói browser extension bằng `scripts/pack-browser-extension.ps1`
    (`EduFocus-<version>-browser-extension.zip`) và sao chép hai hướng dẫn
    `install-guide.md`/`browser-extension.md`.
12. Tạo `SHA256SUMS.txt` (ASCII, LF) cho MSI/portable ZIP/extension ZIP/hai install guide và đọc
    lại để xác minh hash.
13. Quét toàn workspace: fail nếu còn `.pfx/.p12/.snk/.key/.pem/.cer/.crt`, file secret
    hoặc dữ liệu runtime `.db/.log/.bak/.tmp`.
14. Upload artifact `edufocus-release-<version>` gồm MSI, hai ZIP, hai install guide,
    `SHA256SUMS.txt` và release notes; ghi GitHub Actions summary.

### Job `release` — quyền `contents: write`

Chỉ chạy khi `dry_run = false`. Job không checkout source, chỉ tải artifact của job
`build`, rồi tạo GitHub Release với:

- tag và tên `EduFocus v<version>`;
- body lấy từ release notes đã tạo;
- đính kèm MSI, portable ZIP, browser extension ZIP, hai hướng dẫn cài đặt và
  `SHA256SUMS.txt`.

Quyền `contents: write` chỉ tồn tại ở job này; mọi bước build/test/ký chạy với
`contents: read`. Không step nào echo secret hay token.

---

## 4. Code signing

### 4.1. Cấu hình một lần

Cần chứng chỉ **code signing** (code-signing EKU) dạng PFX exportable. Với EV token
phần cứng, cần dịch vụ ký cloud — ngoài phạm vi hiện tại (mua chứng chỉ cũng ngoài phạm vi).

Thêm hai repository secret:

| Secret | Nội dung |
|---|---|
| `CODESIGN_PFX_BASE64` | nội dung PFX mã hóa base64 |
| `CODESIGN_PFX_PASSWORD` | mật khẩu PFX |

```powershell
# Chỉ chạy trên máy tin cậy; không commit file PFX.
[Convert]::ToBase64String([IO.File]::ReadAllBytes("EduFocus-codesign.pfx")) |
  Set-Clipboard

gh secret set CODESIGN_PFX_BASE64 --repo Cong-ty-TNNH-Q-Tech/EduFocus
gh secret set CODESIGN_PFX_PASSWORD --repo Cong-ty-TNNH-Q-Tech/EduFocus
```

Sau khi secret đã có, đặt repository variable `REQUIRE_CODE_SIGNING = true`
(Settings → Secrets and variables → Actions → Variables). Khi đó release fail nếu
không có chứng chỉ, thay vì âm thầm phát hành bản unsigned.

> **Trạng thái hiện tại:** repo chưa mua/cấu hình chứng chỉ, nên các bản phát hành trước
> khi hoàn tất §4.1 sẽ là unsigned và chỉ có SHA-256. Bật `REQUIRE_CODE_SIGNING` ngay khi
> chứng chỉ sẵn sàng để không thể phát hành unsigned ngoài ý muốn.

### 4.2. Cách workflow ký

`installer/Sign-Artifacts.ps1`:

1. Giải mã PFX vào file tạm trong `%TEMP%` của runner.
2. Import vào `Cert:\CurrentUser\My`, chọn certificate có private key.
3. Gọi `signtool sign /sha1 <thumbprint> /fd SHA256 /td SHA256 /tr <timestamp>`
   — mật khẩu PFX **không** xuất hiện trên command line.
4. Gọi `signtool verify /pa /v` cho từng file; sai chữ ký làm job fail.
5. Trong `finally`: xóa file PFX tạm và certificate vừa import (không đụng certificate
   có sẵn trên runner).

Timestamp server mặc định là `http://timestamp.digicert.com` để chữ ký còn hiệu lực sau
khi certificate hết hạn. Khi chưa cấu hình chứng chỉ, script cảnh báo và trả `false`;
workflow vẫn phát hành nếu `REQUIRE_CODE_SIGNING` không bật, và summary ghi rõ
`Code signed: false`. Bản unsigned vẫn có checksum để xác minh.

### 4.3. Xác minh

```powershell
Get-AuthenticodeSignature .\EduFocus-2.0.0-win-x64.msi |
  Format-List Status, StatusMessage, SignerCertificate
```

`Status` phải là `Valid` khi bản phát hành được ký. Có thể dùng `signtool verify /pa /v <file>`
nếu có Windows SDK. Nếu bản phát hành unsigned (chưa cấu hình chứng chỉ), chỉ cài khi hash khớp
và tải từ release chính thức. Nếu chữ ký có nhưng `Status` khác `Valid`, không cài đặt và báo cáo
theo [SECURITY.md](../SECURITY.md).

Khi xoay vòng certificate: cập nhật hai secret, giữ `REQUIRE_CODE_SIGNING = true`;
không cần sửa workflow.

---

## 5. Checksum SHA-256

`SHA256SUMS.txt` có dạng `hash  filename`, mã hóa ASCII, xuống dòng LF để dùng được với
`sha256sum`:

```text
9f2c...  EduFocus-2.0.0-win-x64.msi
4a71...  EduFocus-2.0.0-win-x64-portable.zip
5b82...  EduFocus-2.0.0-browser-extension.zip
d1e4...  EduFocus-2.0.0-install-guide.md
e6f5...  EduFocus-2.0.0-browser-extension.md
```

```powershell
Get-FileHash .\EduFocus-2.0.0-win-x64.msi -Algorithm SHA256
certutil -hashfile .\EduFocus-2.0.0-win-x64.msi SHA256
```

```bash
sha256sum -c SHA256SUMS.txt
```

Hash khớp chưa đủ nếu bản phát hành được ký: kiểm tra thêm chữ ký theo §4.3.

---

## 6. Dry run và release checklist

### Dry run

1. Push branch thử nghiệm (không cần tag).
2. Chạy `workflow_dispatch` với `dry_run = true` và version dự kiến.
3. Kiểm tra: job xanh, test pass, version trong tên file/exe/MSI đúng, `SHA256SUMS.txt`
   khớp, chữ ký `Valid` (nếu đã cấu hình chứng chỉ), artifact đầy đủ.
4. Chỉ khi dry run đạt mới push tag thật.

### Checklist trước khi push tag

- [ ] Section CHANGELOG cho version có `### Breaking changes`.
- [ ] `dotnet test EduFocus.slnx --configuration Release` pass ở máy local/CI.
- [ ] Dry run trên branch thử nghiệm thành công.
- [ ] Secret ký số đã cấu hình và `REQUIRE_CODE_SIGNING = true` (nếu chính thức).
- [ ] Tag đúng SemVer và khớp section CHANGELOG.
- [ ] Đã chạy checklist máy sạch trong [installer.md](./installer.md) §7.
- [ ] Đã chạy checklist E2E bảo mật/quyền riêng tư trong
      [security-e2e-checklist.md](./security-e2e-checklist.md) §4; không còn P0/P1 mở.
- [ ] Đã chạy bộ đo hiệu năng tự động
      (`PerformanceBudgetTests`, `BlockPagePerformanceTests`) và soak 8 giờ cho
      Idle/Protection/Attention theo [performance-report.md](./performance-report.md);
      mọi ngưỡng PASS hoặc finding đã được ghi thành issue P1/P2.
- [ ] [release-checklist.md](./release-checklist.md) đã điền đủ, ký duyệt và được lưu
      cùng artifact `edufocus-release-<version>` (kèm TRX, số đo hiệu năng, soak và bằng
      chứng đã khử dữ liệu nhạy cảm).

### Sau khi release

- [ ] Tải MSI từ GitHub Release trên VM sạch Windows 10 22H2 và Windows 11.
- [ ] Kiểm tra SHA-256 + chữ ký trước khi cài.
- [ ] Cài đặt thành công, bật Protection được, hosts/task hoạt động theo chính sách máy.
- [ ] Nâng cấp từ bản trước giữ DB/settings và không tạo hai bản cài.
- [ ] Gỡ cài đặt không để lại hosts marker, scheduled task hay thư mục runtime.
- [ ] Cập nhật bảng phiên bản hỗ trợ trong [SECURITY.md](../SECURITY.md) khi cần.

> Kiểm thử cài đặt trên VM sạch **không chạy tự động** trong GitHub Actions; đây là
> cổng thủ công bắt buộc trước khi công bố bản chính thức.

---

## 7. Xử lý sự cố

| Triệu chứng | Nguyên nhân thường gặp | Cách xử lý |
|---|---|---|
| `CHANGELOG.md has no section for version` | Chưa thêm/sai tên section | Thêm `## [<version>]` hoặc đặt version vào heading Unreleased |
| `must contain a '### Breaking changes' subsection` | Thiếu mục bắt buộc | Thêm mục, ghi rõ "Không có" nếu không phá vỡ |
| `EduFocus.exe ProductVersion ... does not match` | Tag/`-p:Version` sai | Kiểm tra tag và input `version` |
| `MSI ProductVersion ... does not match` | `-Version` truyền vào installer sai | Giữ `numeric` từ workflow, không tự truyền prerelease |
| `signtool.exe was not found` | Runner thiếu Windows SDK | Dùng `windows-latest` hoặc cài SDK signing tools |
| `Signature verification failed` | Certificate hết hạn/không đúng EKU/timestamp lỗi | Kiểm tra certificate và secret; dry run lại |
| Release job bị skip | `dry_run = true` | Push tag hoặc dispatch với `dry_run = false` |
| `Forbidden certificate, secret or runtime files found` | Có file nhạy cảm trong workspace | Xóa khỏi repo/publish; build lại từ đầu |

---

## 8. Phạm vi

Trong phạm vi issue #41: workflow tag/dry run, versioning, ký số qua GitHub Secrets,
checksum, release notes, quyền tối thiểu và tài liệu quy trình.

Issue #43 bổ sung cổng hiệu năng/độ bền (performance test tự động, soak script, báo cáo
đo trên máy đại diện) và [release-checklist.md](./release-checklist.md) — bản ghi bắt buộc
được ký và lưu cùng artifact trước khi push tag.

Issue #45 bổ sung native messaging host và browser extension ZIP vào artifact, ký native host
cùng `EduFocus.exe` và checklist cài/gỡ đăng ký native messaging.

Ngoài phạm vi: mua chứng chỉ, tích hợp EV token/cloud signing, auto-update trong app
(post-MVP).
