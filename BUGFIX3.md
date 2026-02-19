# AprNes Bug 修復紀錄（第三輯）

本文件接續 `BUGFIX2.md`（Bug 10–14），記錄後續 session 修復的 Bug 15。
所有修復均已 commit 至 master 分支。

---

## 目錄

15. [Sprite Priority Quirk：遮罩精靈無法正確隱藏高 OAM 索引的前景精靈](#15-sprite-priority-quirk遮罩精靈無法正確隱藏高-oam-索引的前景精靈)

---

## 15. Sprite Priority Quirk：遮罩精靈無法正確隱藏高 OAM 索引的前景精靈

**Commit**：`3b86e23`

### 現象

SMB3 中兩處精靈穿透背景磚塊的問題：

- **食人花（Piranha Plant）** 在水管口升起/下降時，身體穿透水管中段磚塊可見
- **蘑菇道具**從「?」磚或磚塊中冒出時，穿透磚塊可見

這是 SMB3 使用「遮罩精靈技巧（mask sprite trick）」的場景，
NES 硬體應讓低 OAM 索引的後景精靈（priority=1）遮蔽同像素位置的高 OAM 索引前景精靈。

### NES 硬體的正確行為（Sprite Priority Quirk）

NES PPU 的精靈合成分為**兩個獨立階段**：

**階段一：精靈間競爭（OAM 索引優先）**

每個螢幕像素只由「OAM 索引最小且有不透明像素」的精靈「勝出」，取得該像素的控制權。

**階段二：與背景合成（由勝出者的 priority 位元決定）**

以「勝出精靈」的 OAM byte 2, bit 5 決定最終輸出：

| 背景像素 | 勝出精靈像素 | Priority bit | 輸出 |
|---|---|---|---|
| 透明（0） | 不透明 | 任意 | 精靈 |
| 不透明 | 不透明 | 0（前景） | 精靈 |
| 不透明 | 不透明 | 1（後景） | 背景 |

**關鍵漏洞**：低 OAM 索引的後景精靈（priority=1）勝出後，
它的 priority=1 會讓不透明背景遮蔽**整個像素**——
包括原本應該顯示的高 OAM 索引前景精靈（priority=0）。

### SMB3 的遮罩精靈技巧

```
OAM index 2 = 遮罩精靈（priority=1, 不透明, 貼在水管/磚塊位置）
OAM index 5 = 食人花精靈（priority=0, 可見）
```

OAM 2 遮罩「勝出」→ 其 priority=1 + 不透明背景 → 整個像素顯示背景 → 食人花也被遮住。

### 根本原因

舊程式碼讓每個精靈**各自獨立**用自己的 priority 位元判斷是否繪製：

```csharp
// 修復前：反向 OAM 迴圈，每個精靈直接畫到螢幕
for (int si = selCount - 1; si >= 0; si--)
{
    // ...
    // 每個精靈各自判斷自己的 priority
    if (pixel != 0 && (!ShowBackGround || Buffer_BG_array[array_loc] == 0 || !priority))
        ScreenBuf1x[array_loc] = NesColors[...];
}
```

執行流程（錯誤）：

1. `si = selCount-1`：食人花（OAM 5, priority=0, front）先被渲染 → 畫到 `ScreenBuf1x`
2. `si = ...`：遮罩精靈（OAM 2, priority=1, behind）後被渲染 → 條件為假（BG 不透明且 priority=1）→ 跳過
3. 結果：食人花已在 `ScreenBuf1x`，繼續顯示 ❌

### 修復方式

`PPU.cs`：`RenderSpritesLine()` 重構為三階段渲染。

```csharp
// 修復後：三階段渲染
// 每個螢幕像素記錄「勝出精靈」的顏色和 priority，最後統一合成

// 每像素緩衝區
uint* sprColor    = stackalloc uint[256];   // 勝出精靈的顏色
byte* sprPriority = stackalloc byte[256];   // 勝出精靈的 priority
byte* sprSet      = stackalloc byte[256];   // 是否有不透明精靈

// Pass 2: 反向 OAM 迴圈，低索引精靈覆蓋高索引 → 最終記錄的是最低索引的勝出者
for (int si = selCount - 1; si >= 0; si--)
{
    // ... (tile 讀取、flip、clip 同前)
    if (pixel == 0) continue;  // 透明像素不參加競爭
    sprSet[screenX]      = 1;
    sprPriority[screenX] = (byte)(priority ? 1 : 0);
    sprColor[screenX]    = NesColors[...];
}

// Pass 3: 合成 — 以勝出精靈的 priority 統一判斷
for (int screenX = 0; screenX < 256; screenX++)
{
    if (sprSet[screenX] == 0) continue;
    array_loc = scanOff + screenX;
    // 勝出精靈 priority=1 且 BG 不透明 → 整個像素隱藏（包括被覆蓋的高索引精靈）
    if (!ShowBackGround || Buffer_BG_array[array_loc] == 0 || sprPriority[screenX] == 0)
        ScreenBuf1x[array_loc] = sprColor[screenX];
}
```

### 影響位置

`AprNes/NesCore/PPU.cs`：`RenderSpritesLine()` 函數（完整重構）

### 修復效果

- 食人花身體正確隱藏在水管磚塊後方，只在水管口露出
- 蘑菇道具正確隱藏在「?」磚/磚塊內，只在磚塊邊緣顯示
- SMB3 的遮罩精靈技巧（mask sprite trick）正確運作

---

## 同步清理

**Commit**：同上

### 移除備份資料夾 `VERBACKUP/`

`AprNes/NesCore/VERBACKUP/` 目錄下存有 2016–2017 年開發初期的舊版備份檔，
包括 CPU、Mapper、PPU 的歷史版本，現已納入 git 版本控制，備份目錄已無必要。

移除清單：
- `VERBACKUP/CPU-20161201-*.cs`（3 個）
- `VERBACKUP/CPU-20161202-1.cs`
- `VERBACKUP/CPU-reduce.cs`
- `VERBACKUP/Mapper-20170106/`（整個目錄）
- `VERBACKUP/PPU_accurate.cs`
- `VERBACKUP/v1.txt`、`v2.txt`

### 移除無用 `mapper.txt`

`AprNes/mapper.txt` 為開發過程中的 AI 對話筆記，非專案原始碼，已刪除。

### 移除 `Main.cs` 遺留的舊 ppu_step 註解

```csharp
// 已移除：
//ppu_step(); ppu_step(); ppu_step();//3x cpu cycles (batch, legacy)
```

舊批次渲染函式 `ppu_step()` 已於前次 session 替換為 `ppu_step_new()`（cycle-accurate），
對應的遺留註解一併清除。
