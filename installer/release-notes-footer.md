### Yêu cầu hệ thống

- Windows 10 22H2 (build 19045) hoặc Windows 11, kiến trúc x64.
- .NET 10 runtime được đóng gói sẵn trong `EduFocus.exe`; không cần cài .NET riêng.
- Khoảng 250 MB dung lượng trống cho bộ cài và ứng dụng.
- Bản MVP yêu cầu quyền Administrator khi cài đặt và chạy ứng dụng.

### Cài đặt

1. Tải `EduFocus-<version>-win-x64.msi` (bộ cài) hoặc `EduFocus-<version>-win-x64-portable.zip`.
2. Đối chiếu SHA-256 với `SHA256SUMS.txt` và kiểm tra chữ ký số trước khi chạy.
3. Chạy MSI và làm theo hướng dẫn. Dữ liệu trong `%ProgramData%\EduFocus` được giữ khi nâng cấp
   hoặc gỡ cài đặt, trừ khi bạn chủ động chọn xóa.

Hướng dẫn đầy đủ nằm trong `EduFocus-<version>-install-guide.md` đính kèm và
[docs/installer.md](https://github.com/Cong-ty-TNNH-Q-Tech/EduFocus/blob/master/docs/installer.md).

### Xác minh bản phát hành

```powershell
Get-FileHash .\EduFocus-<version>-win-x64.msi -Algorithm SHA256
Get-AuthenticodeSignature .\EduFocus-<version>-win-x64.msi | Format-List Status, SignerCertificate
```

Đối chiếu hash với `SHA256SUMS.txt`. Nếu bản phát hành có chữ ký số (`SignerCertificate` khác rỗng),
`Status` phải là `Valid`; nếu bản phát hành chưa được ký, chỉ cài khi hash khớp và tải từ release
chính thức. Khi nghi ngờ, không cài đặt và báo cáo theo
[SECURITY.md](https://github.com/Cong-ty-TNNH-Q-Tech/EduFocus/blob/master/SECURITY.md).
