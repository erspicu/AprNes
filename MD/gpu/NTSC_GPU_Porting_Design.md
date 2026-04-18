# NTSC GPU 化移植設計

**日期**：2026-04-18
**狀態**：**最終結論 — 不實作**（Phase 3A+3B 收尾；本檔作為未來若重啟的完整技術藍圖保留）
**背景**：Phase 3A 把 CRT 搬到 GPU 後 emu thread 得到 1.7-2.0× 加速，Phase 3B phosphor 優化讓 10x presented 達 58 FPS（接近 vsync）。NTSC 階段仍在 CPU；本檔評估繼續 GPU 化的可行性，**結論是放棄**。關鍵理由見 §12。

---

## 0. 使用者設計原則（本文件根本準則）

1. **時序依賴的部分留 CPU**：slew-rate IIR、phase 相位狀態、PPU-driven ingest — 這些不適合 GPU 平行處理
2. **可平行部分才 GPU**：per-pixel / per-sample 無狀態運算
3. **避免 CPU↔GPU 反覆搬遷**：一幀只 upload 一次、readback 一次
4. **Shader 一次跑完**：不做「shader A → readback → shader B → readback」的鏈式設計，避免進出 shader 的 overhead
5. **AprNes .NET 4.8.1 Debug 版不動**：繼續純 scalar，不受任何 GPU 規劃影響

---

## 1. NTSC Pipeline 分類回顧

依 [CRT_GPU_Design.md §15](CRT_GPU_Design.md#15-ntsc-gpu-適配性分析) 分析：

### CPU-ONLY（時序相依，絕不能搬 GPU）

| Method | 行 | 為何 CPU-only |
|--------|:-:|--------------|
| `Ntsc_CaptureScanline` | 380 | PPU cx==260 per scanline 呼叫；寫 `scanPhase6` / `scanPhaseBase` 序列狀態 |
| `RunWaveformLoop` | 601 | 4-sample slew-rate IIR：`vPrev`、`vVel` 前後依賴 |
| `RunWaveformLoop_SVideo` | 682 | 同上，S-Video 變體 |

**這三個是 UltraAnalog Physical 模式的靈魂**（RF 雜訊、slew limiting、buzz），搬不動。

### GPU-MAYBE（refactor IIR→FIR 後可搬）

| Method | 行 | Refactor |
|--------|:-:|----------|
| `RunDecodeLoop` | 491 | IIR chroma filter → 2-3 tap FIR，畫質差 <1% |
| `DecodeAV_SVideo` | 514 | 同上 |

### GPU-OK（無狀態，直接搬）

- `GenerateSignal`（palette LUT 查表）
- `DemodulateRow_Core`（6-tap Hann + 矩陣）
- `YiqToRgb`（矩陣 + gamma）
- `ResampleH_Bilinear`、`VerticalFillRows`

---

## 2. 可能的 CPU↔GPU 切點

```
PPU ─► Ntsc_CaptureScanline ─► [scanPhase state + palette index buf]
        (CPU-only)
        │
        ├─► Path A (fast, non-UltraAnalog):
        │     GenerateSignal ─► RunDecodeLoop ─► DemodulateRow ─► YiqToRgb ─► linearBuffer(RGB)
        │     (palette LUT)    (IIR → FIR)     (parallel)       (matrix)
        │
        └─► Path B (UltraAnalog Physical):
              RunWaveformLoop ─► DemodulateRow ─► YiqToRgb ─► linearBuffer(RGB)
              (CPU-ONLY slew)   (parallel)       (matrix)

              linearBuffer ─► GPU CRT shader ─► 螢幕
              （目前 Phase 3A 邊界）
```

---

## 3. 四種切法對照

### Option A（目前 Phase 3A）：CPU 做完 NTSC，GPU 只做 CRT

**切點**：`linearBuffer` (RGB float 1024×240)

| 項目 | 情況 |
|------|------|
| CPU 工作 | 全部 NTSC（PPU capture + demod + YIQ→RGB） |
| GPU 工作 | CRT shader |
| 每幀 upload | 1.05 MB（`linearBuffer` quantized 到 Bgra8888）|
| 每幀 readback | 0（render thread 直接畫到視窗）|
| 新 shader | 不需要（沿用 `crt_core_v1.sksl`） |
| UltraPhysical 相容 | ✅ |
| 複雜度 | ★ |
| **邊際加速** | **目前實測** |

### Option B：把 Fast Path NTSC 全搬 GPU（單 shader pass）

**切點**：palette index buffer (256×240 bytes) + phase 陣列 (240 × int)

單一 shader 裡做：palette LUT → FIR chroma demod → YIQ → RGB → CRT → 螢幕

| 項目 | 情況 |
|------|------|
| CPU 工作 | 只剩 `Ntsc_CaptureScanline`（寫 palette buf + phase0） |
| GPU 工作 | **全部**（LUT + demod + CRT） |
| 每幀 upload | **62 KB**（palette 8-bit + phase int）— 比 Option A 省 17× |
| 每幀 readback | 0 |
| 新 shader | 需要 `ntsc_fast.sksl`（大幅擴充 CRT shader） |
| UltraPhysical 相容 | ❌（要另一條路徑） |
| 複雜度 | ★★★ |
| **預期加速** | +10-25% emu FPS（fast-path only） |

### Option C：Path-dependent 雙 shader 選擇

- UltraAnalog OFF（fast）：Option B 單 shader
- UltraAnalog ON（physical）：Option A 路徑

| 項目 | 情況 |
|------|------|
| CPU 工作 | Fast：最小 / Physical：完整 NTSC |
| GPU 工作 | Fast：全部 / Physical：只 CRT |
| 每幀 upload | Fast：62 KB / Physical：1.05 MB |
| 每幀 readback | 0 |
| 新 shader | `ntsc_fast.sksl` + 現有 `crt_core_v1.sksl` |
| UltraPhysical 相容 | ✅ |
| 複雜度 | ★★★★ |
| **預期加速** | fast +10-25% / physical 不變 |

### Option D：Physical Path 也搬部分 GPU

`RunWaveformLoop` 留 CPU（slew IIR），`DemodulateRow` + `YiqToRgb` 搬 GPU。

中間產物 YIQ（3 × 1024 × 240 float = 2.95 MB）要 upload，反而**比 Option A 多搬資料**。不划算。**不推薦**。

---

## 4. 推薦方案

### 短期（下一步）：**保持 Option A（不動 NTSC）+ 做 Phosphor 優化**

理由：
- Phosphor writeback 目前每幀跑 2 次完整 shader pass（main + prev surface）。改用 `SKSurface.Snapshot` 直接複製輸出到 prev surface（GPU→GPU copy，不重跑 shader），**可省一半 shader 成本** — 預期比 Option B 的 10-25% emu 加速更直接、風險更低。
- Option B 的 IIR→FIR 會改畫質，要做 A/B 比對，工作量大。
- 目前 emu thread 已 100+ FPS（8x 時），`ApplyHorizontalBlur` 後的 NTSC 已非瓶頸。

### 中期（選擇性）：**Option C（只 fast path 走單 shader）**

當下列條件成立時才做：
- 用戶實測 fast-mode（非 UltraAnalog）效能不夠
- 願意接受 IIR→FIR 的微小畫質改變（~1% pixel diff）

不做的話也沒差 — Option A 已經夠用。

### 長期（不做）：**Option D 跳過**

Physical path 搬一部分沒效益。

---

## 5. 如果做 Option B/C，shader 一次跑完的詳細設計

依使用者原則 #4「shader 一次跑完」，Option B 的單 shader 負責：

```glsl
// ntsc_fast.sksl (virtual)
uniform shader uPalette;        // 256×240 Bgra8888，用 B 當 palette index (0-63)
uniform shader uPrev;           // phosphor prev
uniform float2 uSrcSize;        // 256, 240
uniform float2 uDstSize;        // Crt_DstW, Crt_DstH
uniform float  uPhase0[240];    // per-scanline phase（240 floats）
// ... CRT uniforms same as before

// LUT textures（uploaded once, static）:
//   uPaletteYIQLut: 64 × 8 × 3 float (palette idx × emphasis × YIQ)
//   uGammaLut:      256 × 3 float

half4 main(float2 fragCoord) {
    // 1. Map output to source scanline / position
    float2 srcPx = warpByCurvature(fragCoord);
    int srcY = int(srcPx.y);
    int srcX = int(srcPx.x);

    // 2. FIR chroma demod (3-tap around srcX)
    float phase = uPhase0[srcY] + srcX * 3.0;    // or whatever the phase formula is
    int palIdx = int(uPalette.eval(...).b * 255.5);
    half3 yiq = paletteLut(palIdx, ...) * firWindow(phase, ...);

    // 3. YIQ → RGB
    half3 rgb = yiqToRgbMatrix * yiq;
    rgb = gamma(rgb);

    // 4. CRT effects (reuse crt_core logic)
    rgb *= scanlineWeight(...);
    rgb = mask(rgb, ...);
    rgb = convergence(rgb, ...);
    rgb = phosphorBlend(rgb, uPrev);

    return half4(rgb, 1);
}
```

**一次 DrawRect**，無中間 readback。

---

## 6. 記憶體流動圖

### Option A（目前）
```
CPU: PPU → Ntsc full pipeline → linearBuffer RGB float
                                      │
                                      ▼ quantize Bgra8888 (1.05 MB)
                                      │
                                      ▼ upload per frame
CPU → GPU texture → crt_core_v1.sksl → window
```

### Option B（Fast path GPU）
```
CPU: PPU → Ntsc_CaptureScanline → [palBuf 60 KB + phaseArray 1 KB]
                                       │
                                       ▼ upload per frame (62 KB)
                                       │
CPU → GPU → ntsc_fast.sksl (單 pass) → window
```

每幀資料量減 17 倍，但 shader 複雜度增加。

---

## 7. AprNes .NET 4.8.1 相容性

**完全不受影響**：
- `AprNes.csproj` 不定義 `CRT_SIMD_AVAILABLE` / `CRT_GPU_AVAILABLE`
- `CrtScreen.cs`（scalar）、`Ntsc.cs` 維持純 `Vector<T>` 自動向量化
- Shader code、shader loader、runtime dispatcher 全在 AprNesAvalonia 專案或 `#if CRT_GPU_AVAILABLE` 保護範圍內
- AprNes 執行路徑：永遠走 `CrtScreenScalar.Render()` → 讀 `linearBuffer` → 寫 `crt_analogScreenBuf` → WinForms `Graphics.DrawImageUnscaled`

Option A/B/C 都不破壞這條路。

---

## 8. 風險分析

| Option | 主要風險 | 緩解 |
|--------|---------|------|
| A（保持）| 無新風險 | — |
| B（Fast path GPU）| IIR→FIR 畫質差異 | A/B 截圖比對，跑 gui_benchmark 量 FPS |
| B | Shader 複雜度爆炸 | 單 shader > 300 行可能觸碰 SkSL 限制 — 先小範圍驗證 |
| B | `uPhase0[240]` uniform 陣列傳輸 | SkSL `uniform float phase[240]` 支援；或改用 1D texture |
| C | 雙 path 邏輯維護 | shader 版本機制（filename timestamp）已能 side-by-side |
| D | 看不到邊際效益 | **不做** |

---

## 9. 實作優先序與時間估計

| Phase | 內容 | 預期效益 | 時間 | 推薦 |
|:-----:|------|:--------:|:----:|:----:|
| **B (phosphor optimize)** | Snapshot prev surface 取代 re-render | **Presented FPS +30-50%**（10x 時從 55 提到 70+）| 0.5 天 | ★★★ |
| **C (stretch)** | Fast path NTSC 單 shader | emu +10-25% fast-only | 3-5 天 | ★★ |
| **D (NTSC physical)** | 部分 demod GPU | 接近 0 | — | ✗ |

**強烈建議先做 Phase B**（phosphor 優化），這是 Phase 3A 留下來最直接的優化項目，工作量小收益明顯。NTSC GPU 化（Phase C）等 Phase B 完成後依需求再評估。

---

## 10. 若未來真做 Option C，實作步驟

1. **先做 IIR→FIR refactor（CPU side）**：在現有 `RunDecodeLoop` 旁加 FIR 版本，`UltraAnalog=false` 時選 FIR；跑 blargg 確認沒有回歸。
2. **CPU → GPU palette 上傳機制**：每幀 emu 線程把 palette buf + phase 寫到 shared memory，render thread 讀取
3. **`ntsc_fast.sksl` 第一版**：只做 LUT + demod + YIQ→RGB（不含 CRT）— 輸出到 off-screen texture，方便比對 CPU 結果
4. **合併 CRT**：把 crt_core 的效果融進 ntsc_fast shader → 單 pass
5. **Runtime dispatch**：`CrtGpuRenderThread.Render` 依 `NesCore.UltraAnalog` 選 shader

---

## 11. 結論（原設計完成時的推論）

- 目前最大的 GPU 優化空間在 **phosphor writeback 改成 snapshot-copy**（Phase 3B），不是 NTSC
- NTSC 有時序依賴的部分（slew IIR、phase）永遠在 CPU，這是 NES 物理模擬的本質
- 若真要把 NTSC 部分搬 GPU，**Option C（Fast path only）是唯一合理的切法**，且**務必維持一次 shader pass**（依用戶原則 #4）— 避免 CPU↔GPU 反覆搬遷
- AprNes .NET 4.8.1 版**永遠不受影響**，scalar 路徑完全獨立

---

## 12. 為什麼最終決定不實作 Phase 3C（2026-04-18 最終決議）

深入研讀 `Ntsc.cs` 的 `DecodeAV_Composite` + `RunDecodeLoop` 後，發現原先估計過於樂觀。最終決定**停在 Phase 3A+3B，不實作 Phase 3C**。

### 12.1 技術障礙：不是兩個 IIR，是**三個**

原先以為 fast path 只有 chroma IIR filter，實際上 `RunDecodeLoop` 內每 dst pixel 有：

```csharp
iF += ChromaBlur * (chroma * c - iF);       // IIR #1: Chroma I 低通
qF += ChromaBlur * (-chroma * s - qF);      // IIR #2: Chroma Q 低通
yV = yV * ringDamp + (dotY[d] - yF) * SlewRate;
yF += yV;                                    // IIR #3: Y slew（2 階）
```

**三個都是前值依賴（serial dependence）**，全部要 GPU 化都得 refactor 成 FIR。

### 12.2 Y slew 的 2 階 IIR 難以 FIR 近似

| Filter | IIR→FIR 可行性 | 預期畫質損失 |
|--------|:-------------:|:------------:|
| Chroma I 低通 | ✓ 3-tap FIR OK | < 1%（肉眼不可辨）|
| Chroma Q 低通 | ✓ 3-tap FIR OK | < 1% |
| **Y slew 2 階** | △ FIR 近似勉強 | **2-5%（可見）** |

Y slew 是 `yV` 累積速度項，模擬**類比濾波器的阻尼響應**（軟過渡）。FIR 難以完整還原這個特性 — 近似後硬邊會回來，失去部分 CRT 軟焦感。這正是 UltraAnalog 區別於 fast 的**核心特徵**，破壞它等於失去「好看」的類比味。

### 12.3 Phase 3A+3B 已擷取主要收益

Phase 3A+3B 完成後的狀態（AMD Ryzen 7 3700X，locked freq，D3D11）：

| 尺寸 | Presented | Emu | 相對 SIMD |
|:----:|:---------:|:---:|:--------:|
| 8x | ~59 (vsync) | 121 | emu 1.69× |
| 10x | **58** (近 vsync) | 99 | **presented 1.82× / emu 2.0×** |

**所有實用解析度都達到 vsync 上限或非常接近**。Phase 3C 剩下的只有 emu thread 可能再 +10-15%（100 → 110-115 FPS），但：
- emu thread 已 100+ FPS 遠超 NES 60 Hz 所需
- 多出來的 CPU 預算目前無應用場景
- **邊際報酬遞減**

### 12.4 工作量 vs 風險比不值得

完整 Phase 3C 實作估計：

| 子步驟 | 時間 | 風險 |
|:------:|:----:|:----:|
| 3 個 IIR→FIR refactor（CPU 先做）+ 畫質 A/B 驗證 | 1-2 天 | Y slew 品質 |
| SkSL 實作 palette LUT + FIR demod + 整合 CRT | 2-3 天 | Shader 複雜度超出 SkSL 限制 |
| Runtime dispatch（雙 shader 選擇）+ 整合測試 | 1-2 天 | Regression |
| **合計** | **4-7 天** | **中-高** |

對比 Phase 3B（0.5 天無風險、presented 58 FPS）— Phase 3C 的 cost/benefit 比不划算。

### 12.5 未來若重啟的條件

本檔完整保留技術細節。未來若下列情境成立可重啟：
1. 出現 **> 12x 解析度** 實用需求（例如 8K 顯示）→ emu CPU 可能成為限制
2. 出現 **更重的 audio DSP / mapper** 吃滿 CPU → NTSC 搬走可救場
3. 對 Y slew 畫質損失有**明確的接受度測試**（使用者主觀比對結果 OK）

屆時：
- §4 Option C 架構與 §5 shader 範例可直接施工
- §10 實作步驟為完整 roadmap
- §9 時間估計可能因熟悉度提升而縮短

### 12.6 決議摘要

**停在 Phase 3A+3B。**

- ✅ CRT 完全 GPU 化
- ✅ Phosphor snapshot 優化
- ✅ Runtime dispatch 架構（scalar/simd/gpu）就緒
- ✅ Shader 版本機制（filename 選最新）就緒
- ❌ NTSC GPU 化（本檔延後至未來情境）

這是**合理停損**：收下已到手的 80% 收益，不追風險高報酬遞減的 20%。

---

## 相關文件

- [CRT_GPU_Design.md](CRT_GPU_Design.md) — Phase 0-3 主設計
- [CRT_Dispatch_GUI_Baseline_Phase3A_2026-04-18.md](CRT_Dispatch_GUI_Baseline_Phase3A_2026-04-18.md) — D3D11 實測基線
