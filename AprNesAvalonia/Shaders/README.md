# AprNesAvalonia Shaders

這個目錄放 SkSL（SkiaSharp Shading Language）runtime fragment shader 檔案。
執行時 `ShaderLoader.cs` 從 **exe 同目錄的 `Shaders/`** 子資料夾讀取。

## 執行期路徑

| 角色 | 路徑 |
|------|------|
| 原始檔（git 追蹤、IDE 編輯） | `AprNesAvalonia/Shaders/*.sksl` |
| 執行時載入（可即時編輯）| `AprNesAvalonia/bin/{Debug,Release}/net10.0/Shaders/*.sksl` |

csproj 的 `<None CopyToOutputDirectory="PreserveNewest">` 在 build 時把原始檔同步到輸出目錄。

---

## 檔名版本機制（自動選最新）

`ShaderLoader.LoadLatest(prefix, fallback)` 會：
1. 掃描 `{prefix}*.sksl`
2. 過濾符合 `{prefix}YYYYMMDDHHMMSS[_author].sksl` 格式的檔
3. 選 **timestamp 最大**（即時間最新）的那個載入
4. 若都沒 match，fallback 到指定檔（目前是 `crt_core_v1.sksl`）
5. 若連 fallback 也沒，throw

### 命名格式

```
crt_core_YYYYMMDDHHMMSS[_author].sksl
```
- `YYYYMMDDHHMMSS` 14 位純數字，零填充
- `_author` 可選（建議有，方便辨識）
- 範例：
  - `crt_core_20260418144532_baxer.sksl`
  - `crt_core_20260420091500_alice.sksl`
  - `crt_core_20260421133000_bob.sksl` ← 會被選中（最新）

### 基準檔 `crt_core_v1.sksl`

這個**不符合**版本格式，所以不會被 `LoadLatest` 選中。它的角色是 **fallback baseline**：沒人放版本檔時才用它。

### 排序規則

當有多個版本檔：
1. **Timestamp 降序**（新的勝）
2. **同 timestamp → 作者名字母升序**
3. **仍同 → file mtime 降序**（剛改過的勝）

---

## 使用流程

### 場景 1：想試自己的版本
1. 從 `crt_core_v1.sksl` 複製一份
2. 改名 `crt_core_YYYYMMDDHHMMSS_yourname.sksl`（用當下時間）
3. 丟在 `bin/Release/net10.0/Shaders/`（或 source side）
4. 重啟 exe → console 顯示 `[Shader] latest = crt_core_..._yourname.sksl`
5. 直接編輯你的檔，重啟 exe 看效果

### 場景 2：想回到其他版本
- 刪除比較新的檔
- 或複製想要的版本、改名給新 timestamp

### 場景 3：想強制用特定檔（不自動選最新）
CLI 覆寫：
```
AprNesAvalonia.exe --rom X.nes --crt-strategy gpu --crt-shader crt_core_v1.sksl
```
`ShaderLoader.CliOverride` 會被 `LoadLatest` 優先採用，忽略版本選擇。

### 場景 4：A/B 比較兩個版本
```powershell
# 用 A 版
.\AprNesAvalonia.exe --gui-benchmark 20 --crt-strategy gpu --crt-shader crt_core_20260420091500_alice.sksl

# 用 B 版
.\AprNesAvalonia.exe --gui-benchmark 20 --crt-strategy gpu --crt-shader crt_core_20260421133000_bob.sksl
```

---

## Console 訊息

啟動時會看到：

```
[Shader] latest = crt_core_20260421133000_bob.sksl (skipping 2 older)
```
或
```
[Shader] no versioned match for 'crt_core_*.sksl'; using fallback crt_core_v1.sksl
```
或
```
[Shader] CLI override: crt_core_v1.sksl
```

---

## SkSL 版本與限制

目前 SkiaSharp **3.119.3-preview.1.1**（Skia m116+）。寫 shader 時注意：

| 限制 | 建議 |
|------|------|
| `int` 整數取模 `%` 不允許 | 用 `int(mod(x, N))` |
| `uniform int` 不允許 | 用 `uniform float` 再 `int(v + 0.5)` 轉 |
| 子 shader 宣告 | `uniform shader uName;`，呼叫 `uName.eval(coord)` |
| 無 loop 動態邊界 | 常數邊界，或改用 branch |
| 無 structured buffer | 用 uniform / child shader 傳資料 |

---

## 現有 shader

### `crt_core_v1.sksl`（baseline / fallback）
Phase 3A CRT 主 shader，功能對齊 `CrtScreen.Simd.cs`：
- Barrel curvature、3-tap hblur、Position-dependent RGB convergence
- Gaussian beam + interlace jitter
- Scanline × bloom 耦合、Vignette × brightness 融合
- Aperture grille / shadow mask
- Gamma、Phosphor decay（ping-pong prev surface）

---

## 除錯

- Shader 編譯錯誤會 log 到 console 並 throw
- 上游 `CrtScreen.Shared.cs` dispatcher 會 fallback 到 Simd backend
- `ShaderLoader.CliOverride` 可以快速回到 v1 測比對

## 參考資源

- [SkSL 官方規格](https://skia.org/docs/user/sksl/)
- [SkiaSharp SKRuntimeEffect API](https://learn.microsoft.com/en-us/dotnet/api/skiasharp.skruntimeeffect)
- 本專案設計文件：`MD/gpu/CRT_GPU_Design.md`
