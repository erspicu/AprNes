# 2026-02-25 ISSUE：多鍵同壓問題調查

## 問題描述

SMB3 遊戲中，右方向鍵 + B（加速）+ A（跳躍）無法同時觸發。但左方向鍵 + B + A 卻可以正常運作。

## 初步懷疑

1. 模擬器鍵盤輸入實作問題
2. `ProcessCmdKey` 的 `keyData` 帶有 modifier bits 導致查表失敗
3. event-driven 輸入模型不可靠

## 調查過程

### 1. 程式碼分析

檢查鍵盤輸入鏈：

```
ProcessCmdKey (WM_KEYDOWN)
  → NES_KeyMAP[(int)keyData]
  → NesCore.P1_ButtonPress()
  → P1_joypad_status[v] = 0x41

AprNesUI_KeyUp
  → NES_KeyMAP[e.KeyValue]
  → NesCore.P1_ButtonUnPress()
  → P1_joypad_status[v] = 0x40
```

發現 `ProcessCmdKey` 的 `keyData` 可能帶有 modifier bits（高 16 位元），而 `KeyUp` 的 `e.KeyValue` 只有純 VK code，理論上可能造成按下/放開不對稱。

### 2. 嘗試改寫為 GetAsyncKeyState 輪詢

將鍵盤輸入改為每幀呼叫 `GetAsyncKeyState()` 輪詢所有映射按鍵，直接查詢物理鍵態，完全繞開 Windows 訊息佇列。

### 3. 驗證工具

建立 `KeyTest/KeyTest.exe`，直接用 `GetAsyncKeyState` 即時顯示目前偵測到的按鍵，確認到底是哪一層出問題。

### 4. 測試結果

| 按鍵組合 | GetAsyncKeyState 結果 |
|----------|----------------------|
| Z + X + ← | ✅ 三鍵全部偵測到 |
| Z + X + → | ❌ → 無法偵測到（只看到 Z + X）|

**GetAsyncKeyState 也無法偵測到 Z + X + →**，代表問題不在模擬器程式碼，而在更底層。

## 根本原因：硬體鍵盤 Matrix Ghosting

### 原理

薄膜鍵盤採用行列掃描矩陣（Row × Column），並非每顆鍵獨立電路：

```
        Col-A    Col-B    Col-C
Row-1 [  Z  ]  [  X  ]  [  ←  ]
Row-2 [  ?  ]  [  ?  ]  [  →  ]  ← 右方向鍵在不同 Row
```

當 Z + X 同時按住，Row-1 的兩個 Column 短路。此時再按 → （不同 Row），電流經由 Z 或 X 產生幽靈路徑（Ghost），鍵盤 firmware 無法確定 → 是否真的被按下，選擇 Block，不回報此鍵。

左方向鍵 ← 恰好在與 Z/X 不衝突的矩陣位置，所以 Z + X + ← 可以正常運作。

### 資料流

```
鍵盤硬體 Matrix → Keyboard Firmware → HID Driver → Windows → GetAsyncKeyState → 模擬器
```

問題發生在最左端（硬體 firmware），任何軟體層面的修改都無法解決。

## 結論

**此問題與模擬器實作無關。** 原始 event-driven 鍵盤處理方式完全正確，polling 方式也不能解決此問題。

## 解決方案（使用者端）

| 方案 | 說明 |
|------|------|
| 更換按鍵映射 | 將 A/B 改為 K/L 或 J/L 等與方向鍵矩陣不衝突的按鍵 |
| 換機械鍵盤 | 大多數支援 6KRO 以上，部分支援 NKRO（N 鍵全無限制） |
| 換 USB 遊戲手把 | 每顆鍵獨立電路，零 ghosting，根本解決 |

## 相關檔案

- `KeyTest/KeyTest.cs` — 鍵盤多鍵同壓驗證工具原始碼
- `KeyTest/KeyTest.exe` — 編譯好的測試工具，可直接執行
