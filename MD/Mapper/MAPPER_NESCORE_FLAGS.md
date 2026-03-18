# Mapper 特例旗標設計筆記

## 現狀

NesCore 各子系統（MEM、APU、IO、CPU、PPU）目前**不含任何 `mapper ==` 數字判斷**。
所有 mapper 行為透過 `IMapper` 介面呼叫，特殊需求由 mapper 自行宣告 flag，NesCore 在 init 階段讀取一次，執行期只判斷 flag。

已實施的範例：
- `mapperNeedsA12` / `mapperA12IsMmc3` — PPU A12 通知路由（2026-03-19）

---

## 潛在未來特例

新增 mapper 時，若遇到以下情境，應比照 A12 flag 的模式處理，**不直接在 NesCore 加 mapper 編號判斷**。

| 情境 | 目前狀況 | 將來可能需要的 flag |
|------|----------|---------------------|
| **CPU cycle 計數需求** | `CpuCycle()` 介面已有，全 mapper 都被呼叫（即使是空的） | 效能考量可加 `bool NeedsCpuCycle` |
| **PRG-RAM 尋址差異** | 目前 `$6000-$7FFF` 直接讀 `NES_MEM`，部分 mapper 有特殊 bank | Mapper005 等複雜 mapper 可能需要 flag |
| **Bus conflict** | 目前由各 mapper `MapperW_PRG` 自行處理 | 無需 NesCore 特例 |
| **擴充音效** | VRC6/VRC7/Namco 163 等需要混音 | APU 可能需要 `bool HasExpansionAudio` flag + mixer hook |
| **4-screen VRAM** | 目前 `*Vertical == 4` 控制 | 已是通則，不需特例 |

---

## 設計原則

1. **mapper 宣告，NesCore 讀取**：flag 從 mapper class 的屬性宣告，`Main.cs` init 時讀一次存入靜態欄位。
2. **init 階段讀取，執行期只看 flag**：避免執行期反覆存取介面屬性。
3. **通則邏輯不入 flag**：可用程式碼通則解決的問題（如 CHR bank clamp、PRG wrapping）不需要 flag。
4. **擴充音效優先度最高**：VRC6 / Namco 163 是下一批高優先 mapper，`HasExpansionAudio` + APU mixer hook 屆時一併設計。

---

*最後更新：2026-03-19*
