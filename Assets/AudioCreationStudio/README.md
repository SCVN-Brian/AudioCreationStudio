# Cài Audio Creation Studio (Unity)

Audio Creation Studio là một "mini DAW" chạy ngay trong Unity Editor để làm âm thanh cho game: ghép nhiều track, cắt/trim/fade, mixer và effect, tạo âm thanh procedural hoặc bằng AI (ElevenLabs), loop liền mạch, tạo biến thể, rồi xuất WAV / AIFF / FLAC / MP3 / OGG.

## Cần gì
- **Unity 2022.3 trở lên.**
- **Git** đã cài trên máy, vì Unity tải package qua git. Kiểm tra bằng cách mở Terminal và gõ `git --version`.
  - macOS: nếu chưa có, máy sẽ hỏi cài *Command Line Tools*, bấm **Install**.
  - Windows: tải tại [git-scm.com](https://git-scm.com/download/win), cài xong **khởi động lại Unity Hub**.
- **Quyền đọc repo** `SCVN-Brian/AudioCreationStudio` trên GitHub, nếu repo để private. Máy phải `git clone` được repo này: đăng nhập GitHub qua Git Credential Manager hoặc SSH key.
- Không bắt buộc:
  - **ffmpeg**, chỉ cần khi xuất MP3/OGG (xem bước 4).
  - **API key ElevenLabs**, chỉ cần khi tạo âm thanh bằng AI.

## 1. Cài package
**Cách 1: sửa `manifest.json`.** Mở `Packages/manifest.json` trong project của bạn, thêm dòng này vào `"dependencies"`:

```json
"com.supercent.audiocreationstudio": "https://github.com/SCVN-Brian/AudioCreationStudio.git?path=Assets/AudioCreationStudio#v1.2.1"
```

Ví dụ:

```json
{
  "dependencies": {
    "com.supercent.audiocreationstudio": "https://github.com/SCVN-Brian/AudioCreationStudio.git?path=Assets/AudioCreationStudio#v1.2.1",
    "com.unity.modules.audio": "1.0.0"
  }
}
```

Quay lại Unity, chờ Unity tải và biên dịch xong.

**Cách 2: dùng Package Manager.** Vào **Window ▸ Package Manager**, bấm **+ ▸ Add package from git URL…**, dán:

```
https://github.com/SCVN-Brian/AudioCreationStudio.git?path=Assets/AudioCreationStudio#v1.2.1
```

Lưu ý:
- Phần `?path=Assets/AudioCreationStudio` là **bắt buộc**, vì package nằm trong thư mục con của repo.
- Phần `#v1.2.1` là version, tức là tag trên GitHub. Không ghi thì Unity lấy code mới nhất trên `main`, không nên dùng cho project thật.

## 2. Cập nhật lên bản mới
Đổi tag cuối dòng (ví dụ `#v1.2.1` thành `#v1.3.0`) trong `manifest.json`, rồi quay lại Unity. Xem các version và thay đổi trong [CHANGELOG.md](CHANGELOG.md).

Unity ghi lại commit đã tải trong `Packages/packages-lock.json`. Nếu đổi tag rồi mà Unity vẫn giữ bản cũ, xóa mục `com.supercent.audiocreationstudio` trong file đó rồi quay lại Unity.

## 3. Mở tool
- **Tools ▸ Audio Creation Studio ▸ Open Studio** (phím tắt ⌥⇧⌘A trên Mac, Ctrl+Alt+Shift+A trên Windows), hoặc bấm đúp một asset *Audio Recipe*.
- Xem demo: **Tools ▸ Audio Creation Studio ▸ Open Demo** (hoặc menu **Demos**).
- Tài liệu chi tiết: **Tools ▸ Audio Creation Studio ▸ Documentation** (thư mục `Documentation/`).

## 4. Cài đặt (Preferences ▸ Audio Creation Studio)
Mở bằng **Tools ▸ Audio Creation Studio ▸ Settings…**. Các cài đặt lưu riêng trên máy bạn (EditorPrefs), **không** nằm trong project hay git, nên không lộ key cho người khác.

**API key ElevenLabs (tạo âm thanh bằng AI):**
1. Lấy key tại [elevenlabs.io](https://elevenlabs.io/app/settings/api-keys) (Developers ▸ API Keys).
2. Dán vào ô **API Key**, bấm **Save**.
3. Hoặc đặt biến môi trường `ELEVENLABS_API_KEY`, tool sẽ tự dùng khi chưa lưu key nào.

Mỗi lần tạo âm thanh sẽ trừ credit trong gói ElevenLabs của bạn. Nếu bấm Generate khi chưa có key, Console hiện cảnh báo chỉ chỗ cài.

**ffmpeg (chỉ cần cho MP3 / OGG):**
- macOS: `brew install ffmpeg`
- Windows: `winget install ffmpeg`

Tool tự tìm ffmpeg (PATH, Homebrew, WinGet, Scoop, Chocolatey). Nếu không tìm thấy, bấm **Browse…** và chọn file `ffmpeg` / `ffmpeg.exe`. WAV, AIFF và FLAC không cần ffmpeg.

## 5. Xuất file
Bấm **Export**, chọn định dạng:

| Định dạng | Cần gì | Ghi chú |
|---|---|---|
| WAV | không | 16 / 24-bit, Float 32. Chỉ WAV ghi được điểm loop (`smpl`) |
| AIFF | không | 16 / 24-bit, Float 32 |
| FLAC | không | Nén lossless 16 / 24-bit. Unity không import `.flac` thành AudioClip, nên dùng để lưu trữ hoặc gửi file |
| MP3 | ffmpeg | 64–320 kbps, tối đa 48 kHz. Không loop liền mạch được (MP3 có đệm ở đầu và cuối) |
| OGG | ffmpeg | 64–320 kbps |

File xuất mặc định vào `Assets/AudioCreationStudio/Exports` **trong project của bạn** và được import tự động.

## 6. Công cụ edit trên timeline
Bốn nút cạnh nút Split/Snap trên toolbar, giống DaVinci Resolve:

| Nút | Phím | Kéo chuột làm gì |
|---|---|---|
| Selection | A | Chọn, kéo di chuyển, trim mép, fade |
| Trim Edit | T | Kéo **phần sóng âm**: slip (đổi đoạn audio hiển thị trong clip). Kéo **thanh tên**: slide. Kéo **mép**: ripple trim. Kéo **chỗ cắt giữa 2 clip sát nhau**: roll |
| Blade | B | Click để cắt (Shift-click cắt mọi track) |
| Dynamic Trim | W | Như Trim, và tự phát lại sau mỗi lần sửa |

`,` / `.` dịch clip đang chọn 10 ms (giữ Shift: 100 ms). Chi tiết trong `Documentation/TimelineEditor.md`.

## Dữ liệu tool tạo ra
Package nằm trong thư mục `Packages` và chỉ đọc. Mọi thứ tool tạo ra đều nằm trong `Assets/AudioCreationStudio/` **của project bạn**:

| Thư mục | Nội dung |
|---|---|
| `Exports/` | File xuất |
| `Generated/ElevenLabs/` | Âm thanh tạo bằng AI (kèm file `.txt` ghi prompt) |
| `UserPresets/` | Preset bạn lưu |
| `Imported/` | File audio import từ ngoài project |

Nên commit các thư mục này nếu team cần dùng chung.

## Lỗi thường gặp
- **"No 'git' executable was found"**: máy chưa cài Git, hoặc Unity chưa thấy. Cài Git rồi **khởi động lại Unity Hub và Unity**.
- **Lỗi xác thực (authentication) / "repository not found"**: máy chưa có quyền đọc repo private. Thử `git clone https://github.com/SCVN-Brian/AudioCreationStudio.git` trong Terminal; clone được thì Unity cũng tải được.
- **"Could not find package.json"**: thiếu hoặc sai phần `?path=Assets/AudioCreationStudio`.
- **MP3/OGG báo không có ffmpeg**: xem bước 4. Trên Mac, Unity mở từ Dock không đọc PATH của Terminal, nhưng tool vẫn tự tìm ở `/opt/homebrew/bin` và `/usr/local/bin`.
- **"Rebuild Demo Assets" báo lỗi**: package chỉ đọc nên không ghi đè demo được. Các demo có sẵn vẫn mở bình thường.

## Gỡ cài đặt
Xóa dòng `com.supercent.audiocreationstudio` trong `Packages/manifest.json`. Các file trong `Assets/AudioCreationStudio/` (exports, preset…) vẫn còn; xóa thủ công nếu không cần.
