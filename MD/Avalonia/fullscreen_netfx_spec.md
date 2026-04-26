# AprNes NetFx — 全螢幕功能規格研究

撰寫日期：2026-04-26
分支：`feature/avalonia-fullscreen`
目的：把 NetFx 現有的全螢幕行為完整盤點，作為 Avalonia 對齊實作的依據

---

## 1. 兩種全螢幕模式

NetFx 把「全螢幕」拆成**獨立的兩條路徑**，由 `NesCore.AnalogEnabled` 決定走哪一條。兩條路有自己的進入/退出函式、自己的 UI 處理、自己的 buffer 管理。

| 旗標 | 型別 | 何時 true |
|---|---|---|
| `ScreenCenterFull` | `bool` | 數位 OR 類比全螢幕都會 true |
| `analogFullScreen` | `bool` | 只有類比全螢幕時 true |
| `IsInFullScreen` | property | `ScreenCenterFull \|\| analogFullScreen`，外部 API |

這個雙旗標設計是因為兩種模式的實作差異夠大，需要分別判斷。

---

## 2. 進入點

### 2.1 鍵盤

`ProcessCmdKey` 攔截兩個鍵（**注意**：用 `ProcessCmdKey` 而非 `KeyDown`，因為 MenuStrip 隱藏時 `ShortcutKeys` 會失效）：

| 鍵 | 行為 |
|---|---|
| `F11` | toggle 全螢幕（在/不在都觸發） |
| `Esc` | **只在全螢幕中時**退出，windowed 時無作用 |

### 2.2 主選單

- View → Fullscreen（`_menuViewToggleFullScreen`，ShortcutKeys=F11） — 一律 toggle

### 2.3 右鍵 Context Menu（`contextMenuStrip1`）

- Screen Mode → FullScreeen（`fullScreeenToolStripMenuItem`） — 一律進入
- Screen Mode → Normal（`normalToolStripMenuItem`） — 一律退出

### 2.4 入口統一邏輯

`_menuViewToggleFullScreen_Click` 依當前狀態分流：

```csharp
if (ScreenCenterFull || analogFullScreen)
    fun8ToolStripMenuItem_Click(null, null);   // 退出（共用退出路徑）
else
    fullScreeenToolStripMenuItem_Click(null, null);  // 進入（共用進入路徑）
```

進入路徑 (`fullScreeenToolStripMenuItem_Click`) 內部再分流：
```csharp
if (NesCore.AnalogEnabled) { EnterAnalogFullScreen(); return; }
// 否則跑數位全螢幕流程（inline 在這個方法內）
```

退出路徑 (`fun8ToolStripMenuItem_Click`) 同樣：
```csharp
if (analogFullScreen) { ExitAnalogFullScreen(); return; }
// 否則跑數位退出流程
```

---

## 3. 數位全螢幕 (`ScreenCenterFull && !analogFullScreen`)

### 3.1 進入 (`fullScreeenToolStripMenuItem_Click`)

```
1. StopRecordingIfActive(true)              ← 進入前停止錄影
2. 若 WindowState != Maximized → Opacity = 0  ← 避免閃爍
3. menuStrip1.Visible = false               ← 隱藏選單列
4. panel1.Visible = false                   ← 暫時藏起遊戲畫面
5. panel1.BorderStyle = None                ← 取消邊框
6. label3.Visible = false                   ← 隱藏 FPS 顯示
7. this.BackColor = Color.Black             ← 背景純黑
8. this.FormBorderStyle = None              ← 無框視窗
9. this.WindowState = Maximized             ← 拉到全螢幕
10. CenterToScreen()
11. panel1 居中放置（NOT stretched）：
    panel1.Left = (ClientSize.Width  - panel1.Width)  / 2
    panel1.Top  = (ClientSize.Height - panel1.Height) / 2
12. label3.Location = (0, 0)                ← FPS 顯示位置重設
13. panel1.Visible = true; label3.Visible = true
14. Refresh(); Opacity = 100
15. ScreenCenterFull = true
16. Configure_Write()                       ← 持久化到 INI
```

**關鍵特徵**：
- panel1 維持 `_emu.OutputW/OutputH` 大小（**沒有放大**），周圍是黑色 letterbox
- 不重新分配任何 buffer
- 不重建 RenderObj
- 不暫停 emu thread

### 3.2 退出（共用 `fun8ToolStripMenuItem_Click`）

```
1. StopRecordingIfActive(true)
2. 若 analogFullScreen → ExitAnalogFullScreen() return
3. menuStrip1.Visible = true                ← 還原選單
4. panel1.BorderStyle = FixedSingle
5. this.BackColor = SystemColors.Menu
6. this.WindowState = Normal
7. this.FormBorderStyle = FixedSingle
8. ScreenCenterFull = false
9. initUIsize()                              ← 重排視窗 + RenderObj.init
```

`initUIsize()` 會：
- 重設 panel1 size 為 `renderWidth × renderHeight`（依 `AnalogEnabled` 決定 `256×N×210×N` 或 `256×N×240×N`）
- panel1.Location = (5, 35)
- this.Size 調整到 `panel + 26 寬 + 92 高`
- label3.Location = (5, renderHeight + 37)
- Dispose grfx + 重建 + RenderObj.init

---

## 4. 類比全螢幕 (`analogFullScreen`)

差異很大 — 因為類比走 CRT pipeline，需要重新分配 `AnalogScreenBuf` 到新尺寸才能填滿螢幕。

### 4.1 8:7 PAR letterbox 計算

```csharp
const double AnalogContentAR = (256.0 * 8.0 / 7.0) / 210.0;  // ≈ 1.3933

int screenW = Screen.PrimaryScreen.Bounds.Width;
int screenH = Screen.PrimaryScreen.Bounds.Height;
double screenAR = (double)screenW / screenH;

if (screenAR > AnalogContentAR) {
    // 螢幕比內容寬 → 黑邊在左右
    displayH = screenH;
    displayW = (int)(screenH * AnalogContentAR);
} else {
    // 螢幕比內容窄 → 黑邊在上下
    displayW = screenW;
    displayH = (int)(screenW / AnalogContentAR);
}
int padX = (screenW - displayW) / 2;
int padY = (screenH - displayH) / 2;
```

注意：使用 `Screen.PrimaryScreen` — **永遠以主螢幕為準**，不偵測視窗目前在哪個螢幕。

### 4.2 進入 (`EnterAnalogFullScreen`)

```
1. 若 WindowState != Maximized → Opacity = 0
2. 保存原始狀態（用於退出時還原）：
   savedPanelW/H/X/Y, savedFormW/H
3. 計算 8:7 PAR letterbox（見 4.1）
4. 暫停 emu+render 執行緒（PauseEmuAndRender）
5. NesCore.Crt_SetFullscreenSize(displayW, displayH)
   → Crt_DstW/H 從此回傳全螢幕尺寸（不再是 256×N / 210×N）
6. 重新分配 AnalogScreenBuf + AnalogScreenBufBack（依新 Crt_DstW * Crt_DstH）
7. NesCore.SyncAnalogConfig(); Ntsc_Init(); Crt_Init();
8. UI：menuStrip1.Visible=false / panel1.BorderStyle=None / label3.Visible=false
   BackColor=Black / FormBorderStyle=None / WindowState=Maximized
9. panel1.Size = (displayW, displayH)        ← 跟著 letterbox 大小
   panel1.Location = (padX, padY)            ← letterbox 居中
10. 重建 grfx + RenderObj (= new Render_Analog())
11. label3.Location = (0, 0); panel1/label3 都顯示
12. Refresh; Opacity=100
13. ScreenCenterFull = true; analogFullScreen = true
14. 恢復 emu+render
15. Configure_Write()
```

### 4.3 退出 (`ExitAnalogFullScreen`)

```
1. 暫停 emu+render（PauseEmuAndRender）
2. NesCore.Crt_ClearFullscreenSize()
   → Crt_DstW/H 回到 256 * crt_analogSize / 210 * crt_analogSize
3. 重新分配 AnalogScreenBuf + Back 回原始大小
4. NesCore.SyncAnalogConfig(); Ntsc_Init(); Crt_Init();
5. UI 還原：menuStrip1.Visible=true / BorderStyle=FixedSingle / BackColor=Menu
   WindowState=Normal / FormBorderStyle=FixedSingle
6. ScreenCenterFull = false; analogFullScreen = false
7. label3.Location = (208, 8)
8. initUIsize()    ← 重排視窗、grfx/RenderObj 重建
9. 恢復 emu+render
10. Configure_Write()
```

---

## 5. CRT 全螢幕尺寸覆寫機制

`AprNes/NesCore/NTSC_CRT/CrtScreen.Shared.cs:72-76`：

```csharp
static int? _fullscreenW = null, _fullscreenH = null;
public static int Crt_DstW => _fullscreenW ?? 256 * crt_analogSize;
public static int Crt_DstH => _fullscreenH ?? 210 * crt_analogSize;
public static void Crt_SetFullscreenSize(int w, int h) { _fullscreenW = w; _fullscreenH = h; }
public static void Crt_ClearFullscreenSize() { _fullscreenW = null; _fullscreenH = null; }
```

這是給類比全螢幕專用的 hook。Avalonia 對齊時要把 `EmulatorEngine.ApplyRenderSettings`（5.5 commit `0ff564e` 已用 `Crt_DstW/H` 為準）的 `newW/newH` 自動跟到全螢幕尺寸 — 已經 wired，只要呼叫 `Crt_SetFullscreenSize` 即可生效。

---

## 6. 持久化（INI）

| Key | 型別 | 預設 | 說明 |
|---|---|---|---|
| `ScreenFull` | `bool` (true/false) | `false` | 上次關閉時是否在全螢幕 |

讀取：`AppConfigure["ScreenFull"]` → `ScreenCenterFull = bool.Parse(...)`（`AprNesUI.cs:454`）

寫入：`AppConfigure["ScreenFull"] = ScreenCenterFull.ToString();`（`AprNesUI.cs:684`）

下次啟動時若 `ScreenCenterFull=true`，`initUIsize()` 在最後會主動呼叫 `fullScreeenToolStripMenuItem_Click(null, null)`（`AprNesUI.cs:206-213`）：

```csharp
if (ScreenCenterFull) {
    label3.Visible = false;
    fullScreeenToolStripMenuItem_Click(null, null);
    panel1.Visible = true;
    return;
}
```

---

## 7. 全螢幕中切換 AnalogMode（`FullScreenModeTransition`）

User 在全螢幕中開啟 ConfigureUI 切換 Analog ↔ Digital 模式時，因為兩種全螢幕的 UI/buffer 處理完全不同，必須安全過渡：

```csharp
// AprNesUI.cs:1789
public void FullScreenModeTransition(bool prevAnalog) {
    bool wasAnalogFS = analogFullScreen;
    bool wasNormalFS = ScreenCenterFull && !analogFullScreen;
    bool nowAnalog   = NesCore.AnalogEnabled;
    if (!wasAnalogFS && !wasNormalFS) return;

    // 1. 先退出目前全螢幕
    if (wasAnalogFS) ExitAnalogFullScreen();
    else fun8ToolStripMenuItem_Click(null, null);

    // 2. 在 windowed 模式下套用設定
    initUIsize();
    ApplyRenderSettings();

    // 3. 重新進入正確的全螢幕
    fullScreeenToolStripMenuItem_Click(null, null);
}
```

ConfigureUI 的 OK button 觸發點：

```csharp
// AprNes_ConfigureUI.cs:414
if (AprNesUI.GetInstance().IsInFullScreen && prevAnalogEnabled != newAnalogEnabled)
    AprNesUI.GetInstance().FullScreenModeTransition(prevAnalogEnabled);
```

---

## 8. 全螢幕中的右鍵選單行為

`contextMenuStrip1.Opening` 事件動態調整選項可用性（`AprNesUI.cs:2112-2118`）：

```csharp
contextMenuStrip1.Opening += (s, ev) => {
    UpdateRecordMenuVisibility();
    bool inFS = ScreenCenterFull || analogFullScreen;
    _ultraAnalogMenuItem.Enabled = !inFS;     // UltraAnalog 切換 disabled
    fun3ToolStripMenuItem.Enabled = !inFS;    // Config 整個 disabled
};
```

理由：
- **UltraAnalog 切換**會導致 buffer 重新分配，全螢幕中危險
- **Config dialog** 開啟時可能觸發 UI 重排，全螢幕中可能畫面跑掉

---

## 9. 錄影互動

`StopRecordingIfActive(true)` 在每個 fullscreen 切換點都會呼叫（不論進入或退出）。`true` 表示會跳訊息框告知使用者錄影被停止。

理由：CRT 尺寸或 panel 尺寸改變會造成 VideoRecorder 寫入錯誤畫面尺寸的 frame，最簡單的做法是直接停止。

---

## 10. UI 元素的全螢幕狀態總表

| 元素 | Windowed | Digital FS | Analog FS |
|---|---|---|---|
| `menuStrip1` (主選單) | Visible | **Hidden** | **Hidden** |
| `label3` (FPS 顯示) | Visible @ (5, h+37) | Hidden | Visible @ (0, 0) |
| `panel1.BorderStyle` | `FixedSingle` | `None` | `None` |
| `panel1.Size` | render size | render size（不變） | letterbox size |
| `panel1.Location` | (5, 35) | 螢幕居中 | (padX, padY) |
| `this.BackColor` | `SystemColors.Menu` | `Color.Black` | `Color.Black` |
| `this.FormBorderStyle` | `FixedSingle` | `None` | `None` |
| `this.WindowState` | `Normal` | `Maximized` | `Maximized` |
| `Opacity` | 100 | 0→100（閃爍避免） | 0→100 |

---

## 11. 多螢幕

**完全沒有**多螢幕偵測。一律用 `Screen.PrimaryScreen.Bounds`。
若使用者在副螢幕跑遊戲然後按 F11，會有以下行為：
- 數位 FS：視窗變 Maximized，Windows 會拉到當前螢幕（OK）
- 類比 FS：letterbox 計算用主螢幕的解析度，但 Maximized 拉到當前螢幕 → 若兩螢幕解析度不同，letterbox 算錯

這是個 NetFx 既存缺陷，Avalonia 對齊時可以順便修。

---

## 12. NetFx vs Avalonia 現況差距

| 規格 | NetFx | Avalonia 現況 | 對齊難度 |
|---|---|---|---|
| 數位/類比分流 | 兩條獨立路徑 | 一條共用 `WindowState=FullScreen` | 中 |
| Hide menu / status bar | ✓ | ✗（Menu 仍可見） | 低 |
| 8:7 PAR letterbox | ✓（類比） | ✗（直接拉滿） | 中 |
| `Crt_SetFullscreenSize` 呼叫 | ✓ | ✗ | 低 |
| AnalogScreenBuf realloc | ✓ | ✗ | 低（已有 ApplyRenderSettings 流程可複用） |
| F11 toggle | ✓ | ✓ | — |
| Esc 退出 | ✓（`ProcessCmdKey`） | ✓（`OnKeyDown`） | — |
| Context menu 進入 | ✓ | ?（需確認） | 低 |
| 持久化（`ScreenFull` INI） | ✓ | ✗ | 低 |
| 啟動時還原 FS 狀態 | ✓ | ✗ | 低 |
| FS 中切 AnalogMode 安全過渡 | ✓ (`FullScreenModeTransition`) | ✗ | 中 |
| FS 中 disable 危險 menu 項 | ✓ | ✗ | 低 |
| 進入/退出停止錄影 | ✓ | ?（需確認） | 低 |
| 多螢幕偵測 | ✗（用 PrimaryScreen） | ✓（Avalonia 自動） | — Avalonia 比較好 |
| 雙閃爍避免（`Opacity=0`） | ✓ | ?（Avalonia 是否需要） | 低 |

---

## 13. 實作建議優先級（給 Avalonia 對齊用）

| 優先度 | 項目 | 理由 |
|---|---|---|
| 高 | Hide Menu + StatusBar | 使用者最直接看到的差距 |
| 高 | 8:7 PAR letterbox（類比） | 沒有的話 CRT 畫面比例跑掉 |
| 高 | `Crt_SetFullscreenSize` + buffer realloc | 類比畫質正確的前提 |
| 中 | Esc / F11 / Context menu / Menu 入口統一 | 行為一致性 |
| 中 | 持久化 + 啟動還原 | 使用者體驗 |
| 中 | Stop recording on FS toggle | 避免錄壞 |
| 中 | FS 中切 AnalogMode 安全過渡 | 同 NetFx `FullScreenModeTransition` |
| 低 | Disable UltraAnalog / Config 在 FS 中 | 防呆 |
| 低 | 多螢幕：用 `Screens.ScreenFromVisual(this)` 取代 PrimaryScreen | Avalonia 已有，順手做 |
| 低 | Opacity 閃爍避免 | Avalonia 切換 WindowState 是否會閃需測試後決定 |

---

## 14. 相關檔案 / 行號 索引

| 內容 | 檔案:行 |
|---|---|
| `EnterAnalogFullScreen` | `AprNes/UI/AprNesUI.cs:2244-2346` |
| `ExitAnalogFullScreen` | `AprNes/UI/AprNesUI.cs:2348-2407` |
| `fullScreeenToolStripMenuItem_Click`（數位進入 + 入口分流） | `AprNes/UI/AprNesUI.cs:2430-2453` |
| `fun8ToolStripMenuItem_Click`（共用退出 + 出口分流） | `AprNes/UI/AprNesUI.cs:2409-2420` |
| `normalToolStripMenuItem_Click`（數位退出） | `AprNes/UI/AprNesUI.cs:2455-2468` |
| `_menuViewToggleFullScreen_Click`（F11 toggle） | `AprNes/UI/AprNesUI.cs:2422-2428` |
| `FullScreenModeTransition` | `AprNes/UI/AprNesUI.cs:1789-1809` |
| `ProcessCmdKey`（F11 / Esc 攔截） | `AprNes/UI/AprNesUI.cs:1546-1561` |
| `IsInFullScreen` property | `AprNes/UI/AprNesUI.cs:1811` |
| `ScreenCenterFull` 宣告 | `AprNes/UI/AprNesUI.cs:2234` |
| `analogFullScreen` 宣告 | `AprNes/UI/AprNesUI.cs:2235` |
| 8:7 PAR 常數 | `AprNes/UI/AprNesUI.cs:2241-2242` |
| ContextMenu Opening 動態 disable | `AprNes/UI/AprNesUI.cs:2112-2118` |
| `Crt_SetFullscreenSize` API | `AprNes/NesCore/NTSC_CRT/CrtScreen.Shared.cs:72-76` |
| INI 讀寫 `ScreenFull` | `AprNes/UI/AprNesUI.cs:454, 684` |
| 啟動時還原 FS | `AprNes/UI/AprNesUI.cs:206-213` (`initUIsize`) |
| ConfigureUI 觸發 `FullScreenModeTransition` | `AprNes/UI/AprNes_ConfigureUI.cs:414-417` |

---

## 15. Avalonia 現況快照（給對齊參考）

`AprNesAvalonia/MainWindow.axaml.cs:686-706` `ToggleFullscreen()`：

```csharp
private void ToggleFullscreen() {
    _isFullscreen = !_isFullscreen;
    if (_isFullscreen) {
        WindowState = WindowState.FullScreen;
        GameCanvas.Width  = double.NaN;       // 自動拉伸
        GameCanvas.Height = double.NaN;
        GameBorder.Margin = new Thickness(0);
    } else {
        WindowState = WindowState.Normal;
        GameCanvas.Width  = _emu.OutputW;
        GameCanvas.Height = _emu.OutputH;
        GameBorder.Margin = new Thickness(0);
    }
}
```

僅做：WindowState 切換 + GameCanvas size hint 切換。**全部 NetFx 該做的事都沒做**。

---

下一步：根據這份規格，按優先級逐項實作 Avalonia 對齊。
