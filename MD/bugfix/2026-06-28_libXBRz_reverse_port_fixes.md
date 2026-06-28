# libXBRz.cs 逆向回補修正（來自 AprCSTyrian 的 xBRZ 濾鏡 bug 修正）

**日期**: 2026-06-28
**檔案**: `AprNes/tool/libXBRz.cs`（WinForms + Avalonia 共用，Avalonia 經 `<Compile Include="../AprNes/tool/libXBRz.cs">` 連結）
**來源**: xBRZ 濾鏡被移植到另一專案（AprCSTyrian）後發現並修正了數個 bug，逆向回補本專案。
**參考說明**: `temp2/xbrz_libXBRz_修改說明.txt`

## 背景

`libXBRz.cs`（`XBRz_speed.HS_XBRz`，xBRZ 多倍率 2x–6x C# 版）與 AprCSTyrian 同源（vendored 自本專案）。
對方在使用過程發現幾個從第一版就存在的 bug，本次逐項驗證後回補。

## 套用的修正

### #1 [CRITICAL] `_AlphaBlend` R+B 打包除法 → 假藍點
- **症狀**: 畫面亮部（紅/黃/金）邊角出現零星藍點，倍率越高越明顯。
- **根因**: 原本把 R(bit16-23) 與 B(bit0-7) 打包成一個整數再一起除以 m。角落混合比例常是 `/100` 等非 2 次方，整數除法時 R 通道的餘數會往下溢進 B 通道，加出假藍（線混合用 1/4、3/4，m=4 是 2 次方剛好被遮罩遮掉，所以只有角落混合中招）。
- **數值驗證**: 純紅 col(R=255,B=0)、dst=0、n=21、m=100 → 打包 `(0x00FF0000*21)/100 = 0x358F0C`，`& 0x00FF00FF` 後 B=0x0C=12（應為 0）→ 假藍 12。
- **修正**: 逐通道（R/G/B）各自整數除，對應官方 `gradientRGB`。

### #3 [MEDIUM] 跨幀緩衝未清 → 頂列殘留、逐幀閃爍
- **根因**: `preProcBuffer_local` / `_preProcBuffer` 是 `initTable` 配的常駐非託管記憶體，跨幀重用。`ComputeEdgeFeatures` 沒在開頭清零，第 0 列會讀到上一幀殘留。
- **修正**: `ComputeEdgeFeatures` 開頭加 `Unsafe.InitBlock(preProcBuffer_local, 0, width)` 與 `Unsafe.InitBlock(_preProcBuffer, 0, width*height)`。

### #4 [MEDIUM] 合併迴圈漏最右欄 → 右緣一格漏混合
- **根因**: 第二階段合併迴圈是 `for (x=0; x<width-1; ...)`，最右欄（x=width-1）的 `_preProcBuffer` 從未寫入，被 RenderPipeline 讀到殘留。
- **修正**: 迴圈改 `x < width`，只在寫 `x+1` 時加 `if (x+1 < width)` 邊界守衛（width==1 也安全）。

### #5 [LOW/防禦] `_FillBlock4x/5x/6x` 非對齊 Vector4 寫入
- **根因**: `*(Vector4*)ptr = vCol` 對未對齊位址寫入，某些 JIT 可能生成需 16-byte 對齊的 SIMD 指令 → AccessViolation。本專案 xBRZ 開放 2x–6x，4x/5x/6x 會走到此路徑，故硬化。
- **修正**: 改 `Unsafe.WriteUnaligned`（強制 movups 非對齊 store）。

## 未套用

### #2 [HIGH] `DistYCbCr` 平方距離 vs 線性門檻
- **分析正確**: 本專案 `DistYCbCr` 回傳平方距離（無 sqrt），門檻用 4/2/900。`eqColorThres`（單一距離比較）恰好等價於官方線性 30，但 `steep`（2 ≈ 線性 1.41 vs 官方 2.2，偏鬆）與 `dominant`（多項距離「和的平方 ≠ 平方和」）與官方 xBRZ 不一致。
- **未套用原因**: 這是「對齊官方 xBRZ 保真度」的取捨，非純 bug；改線性需每次 `DistYCbCr` 多一個 sqrt（每像素呼叫約 10 次，是即時模擬器熱路徑），且會改變每幀邊緣外觀。本次決定保留現狀，未來若要對齊官方再評估（可考慮把既有 16MB `lTable_dist` 改存線性距離 + 修正 index 壓縮 bug，達成 sqrt-free 的線性查表）。

### #6 initTable grow-only
- 本專案 xBRZ 輸入固定 256×240（`RenderPipeline.cs` `initTable(256,240)` 一次），尺寸不變，原 once 機制已足夠，無需 grow-only。

## 驗證

- WinForms（`AprNes.csproj` Debug x64）+ Avalonia（`AprNesAvalonia.csproj` Release）雙專案編譯通過。
- xBRZ 2x / 4x / 6x benchmark 各跑 2 秒無 crash（4x/6x 走 `Unsafe.WriteUnaligned` 新路徑），FPS 與修正前同級（122–124，因未動 `DistYCbCr` 熱路徑）。
- blargg / AccuracyCoin 準確度測試不受影響（濾鏡與 CPU/PPU/APU 完全隔離，無任何測試 ROM 會執行 xBRZ）。
