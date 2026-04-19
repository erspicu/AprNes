# Release 打包發佈流程（EnigmaBenchmark）

以日期為版本號（例：`enigma-v2026.04.19c`）的雙平台（Windows x64 + macOS arm64）GitHub Release 發布 SOP。

---

## 0. 一次性設定（已完成，不用重做）

- **gh CLI** 已安裝在 `C:\Program Files\GitHub CLI\gh.exe`
- 首次授權指令（新機器才需要）：
  ```powershell
  gh auth login
  # GitHub.com → HTTPS → Login with a web browser
  # 記下 8 碼 code，瀏覽器 Authorize
  ```
- 驗證：`gh auth status`

---

## 1. 版本號規則

基本格式：`enigma-vYYYY.MM.DD`

同一天多次發版用後綴字母：
- `enigma-v2026.04.19` — 當天第一次
- `enigma-v2026.04.19b` — 第二次
- `enigma-v2026.04.19c` — 第三次

`date +%Y.%m.%d` 取當下系統日期（不要靠 context 日期）。

---

## 2. Publish 指令（兩個平台都打）

### Windows x64（self-contained, single-file, compressed）

```bash
dotnet publish EnigmaBenchmarkAvalonia/EnigmaBenchmarkAvalonia.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:EnableCompressionInSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o publish/EnigmaBench-vYYYY.MM.DDx-win-x64-single
```

輸出：`EnigmaBenchmarkAvalonia.exe`（~47 MB 自解包）+ `Shaders/` + `docs/`

### macOS arm64（Apple Silicon）

```bash
dotnet publish EnigmaBenchmarkAvalonia/EnigmaBenchmarkAvalonia.csproj \
  -c Release -r osx-arm64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:EnableCompressionInSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o publish/EnigmaBench-vYYYY.MM.DDx-osx-arm64
```

輸出：`EnigmaBenchmarkAvalonia`（Mach-O 單檔，**無 .exe**）+ `Shaders/` + `docs/`

---

## 3. 清除 pdb（不能放進 release）

```bash
rm -f publish/EnigmaBench-vYYYY.MM.DDx-*/*.pdb
```

**注意**：SkiaSharp 的 `libSkiaSharp.pdb` 在 Windows 平台 publish 會跑出 ~84 MB 巨無霸，一定要清。macOS 平台不會產生。

---

## 4. 壓縮

### Windows → zip（PowerShell Compress-Archive）

```bash
cd publish
powershell -NoProfile -Command \
  "Compress-Archive -Path 'EnigmaBench-vYYYY.MM.DDx-win-x64-single' \
   -DestinationPath 'EnigmaBench-vYYYY.MM.DDx-win-x64-single.zip' \
   -CompressionLevel Optimal -Force"
```

### macOS → tar.gz（保留 Unix 執行位元）

**不能用 zip**（PowerShell Compress-Archive 不保留 exec bit，Mac 用戶收到會無法執行）。

```bash
cd publish
tar -czf EnigmaBench-vYYYY.MM.DDx-osx-arm64.tar.gz \
    EnigmaBench-vYYYY.MM.DDx-osx-arm64/
```

最終各約 45-46 MB。

---

## 5. 寫 RELEASE_NOTES.md

放在 `publish/RELEASE_NOTES.md`。必備段落：

1. 一句話描述本次變更（vs 前一版）
2. **Downloads 表格**（兩個下載連結）
3. **🍎 First run on macOS**（Gatekeeper 解除 quarantine 指令）
4. **🪟 First run on Windows**（self-extract 首次啟動說明 + AV false positive）
5. What's in it（六個 cipher、四個 backend、SIMD dispatch）
6. System requirements
7. Known limitations
8. Acknowledgements
9. Parent project link

macOS Gatekeeper 段落必備，否則新用戶會卡住：
```bash
chmod +x EnigmaBenchmarkAvalonia
xattr -d com.apple.quarantine EnigmaBenchmarkAvalonia
./EnigmaBenchmarkAvalonia
```

---

## 6. 建立 GitHub Release（gh CLI 一行）

```bash
"/c/Program Files/GitHub CLI/gh.exe" release create enigma-vYYYY.MM.DDx \
  publish/EnigmaBench-vYYYY.MM.DDx-win-x64-single.zip \
  publish/EnigmaBench-vYYYY.MM.DDx-osx-arm64.tar.gz \
  --title "EnigmaBenchmark vYYYY.MM.DDx (<標題副說明>)" \
  --notes-file publish/RELEASE_NOTES.md
```

執行後回傳 release URL，如：
`https://github.com/erspicu/AprNes/releases/tag/enigma-v2026.04.19c`

**Tag 會自動建立**（release create 同時建 tag）。

---

## 7. 刪除舊 release + tag（如果要）

只刪舊包、保留當前：

```bash
"/c/Program Files/GitHub CLI/gh.exe" release delete <舊 tag> --cleanup-tag --yes
```

`--cleanup-tag` 同時刪 git tag，不加只會把 release 物件刪掉、tag 還在。

多個一起刪：
```bash
for tag in enigma-v2026.04.19 enigma-v2026.04.19b; do
  "/c/Program Files/GitHub CLI/gh.exe" release delete $tag --cleanup-tag --yes
done
```

驗證：`gh release list` 應該只剩目前要的那一個。

---

## 8. 常見陷阱

| 陷阱 | 症狀 | 解法 |
|------|------|------|
| **編譯 Release 沒重打包** | 使用者回報「改了沒效果」 | `dotnet publish` 會自己 build，但先確認對應 config；`feedback_release_build_parity.md` 記有類似問題 |
| **libSkiaSharp.pdb 80MB** | zip 突然暴肥到 130+ MB | publish 後 `rm -f */*.pdb` |
| **Mac 下載解不開** | Windows 打 zip 不保留執行位 | macOS 一律用 `tar -czf` |
| **Gatekeeper 卡住** | Mac 用戶報「打不開，未識別的開發者」 | 寫進 release notes 教 `chmod +x` + `xattr -d` |
| **日期寫死** | Code 裡 `"Your GPU (2025)"` | 改用 `DateTime.Now.Year` |
| **sub-project 沒 bilingual readme** | Traditional Chinese 用戶看英文 | L10n.cs 派發，About 按鈕打對應 `readme.html` / `readme_en.html` |

---

## 9. 一鍵完整流程（未來可寫成 script）

將來可整理成 `scripts/publish_enigma.sh` 但目前手工流程如下，大概 5-10 分鐘：

```bash
VER="2026.04.19c"

# 1. publish × 2
dotnet publish ... -r win-x64 ... -o publish/EnigmaBench-v$VER-win-x64-single
dotnet publish ... -r osx-arm64 ... -o publish/EnigmaBench-v$VER-osx-arm64

# 2. strip pdb
rm -f publish/EnigmaBench-v$VER-*/*.pdb

# 3. compress
cd publish
powershell -Command "Compress-Archive ..."
tar -czf EnigmaBench-v$VER-osx-arm64.tar.gz ...

# 4. release create
"/c/Program Files/GitHub CLI/gh.exe" release create enigma-v$VER \
  EnigmaBench-v$VER-win-x64-single.zip \
  EnigmaBench-v$VER-osx-arm64.tar.gz \
  --title "..." --notes-file RELEASE_NOTES.md

# 5. 可選：刪舊 release
"/c/Program Files/GitHub CLI/gh.exe" release delete <舊 tag> --cleanup-tag --yes
```

---

## 版本歷史提醒

目前 (2026-04-19) 只保留一個 release。舊版 `enigma-v2026.04.19` 和 `enigma-v2026.04.19b` 連同 git tag 都已清除。若要找舊版：`git log --oneline` 找回對應 commit 手動 checkout + build。
