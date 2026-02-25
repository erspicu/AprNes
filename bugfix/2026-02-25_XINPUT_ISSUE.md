# ISSUE: Xbox 手把（XInput）不相容

**日期**: 2026-02-25  
**狀態**: 已確認，待處理

## 問題描述

使用 Xbox 手把（Xbox 360 / Xbox One / Xbox Series）時，模擬器無法偵測到任何輸入，設定介面也無法完成手把按鍵映射。

## 根本原因：WinMM vs XInput 兩套 API

### 目前實作：WinMM joyGetPos（舊 API）

```
winmm.dll → joyGetDevCaps / joyGetPos
```

- 1990 年代的 Windows Multimedia Joystick API
- 只支援最多 32 顆按鈕、2 個類比軸（X/Y）
- 專為老式搖桿設計，無 D-Pad 概念
- **Xbox 手把不走這條路**

### Xbox 手把使用：XInput（現代 API）

```
xinput1_4.dll → XInputGetState
```

- 微軟為 Xbox 360+ 手把專門設計
- 支援 2 支類比搖桿、2 個扳機（LT/RT）、D-Pad、14 顆按鈕
- **所有 Xbox / Xbox One / Xbox Series 手把預設走 XInput**
- `joyGetPos` 完全看不見 XInput 裝置

## 影響分析

```
Xbox 手把插上
  ↓
Windows 把它註冊為 XInput 裝置
  ↓
joyGetDevCaps() 掃描 → 找不到（或只找到殘缺的相容模式）
  ↓
joyinfo_list 為空 → 完全沒有事件輸出
```

Xbox 手把在 WinMM 下的兩種狀況：
1. **完全看不到**（Xbox One / Series 最常見）
2. **看得到但殘缺**：D-Pad 方向鍵不回報為 X/Y 軸，而是 POV hat，`JOYINFO` struct 無此欄位

## API 支援對比

| API | Xbox 手把 | 第三方 USB 手把 | 老式搖桿 |
|-----|-----------|----------------|---------|
| WinMM（目前） | ❌ 不支援 | ⚠️ 部分支援 | ✅ |
| XInput | ✅ 完整支援 | ❌ 僅 Xbox 相容裝置 | ❌ |
| DirectInput | ⚠️ 相容層 | ✅ 大多支援 | ✅ |

## 預計解法

**雙 API 並行**：XInput 優先偵測（處理 Xbox 手把），失敗再 fallback WinMM（處理一般 USB 手把）。

### 需改動的檔案

| 檔案 | 改動 |
|------|------|
| `AprNes/tool/joystick.cs` | 新增 XInput 掃描與輪詢邏輯 |
| `AprNes/tool/NativeAPIShare.cs` | 新增 XInput P/Invoke（`XINPUT_STATE`、`XInputGetState`） |
| `AprNes/UI/AprNesUI.cs` | 手把事件處理相容兩種 API 來源 |
| `AprNes/UI/AprNes_ConfigureUI.cs` | 設定介面區分 XInput / WinMM 裝置 |

### XInput 按鍵對應（參考）

| XInput 按鍵 | wButtons bitmask |
|------------|-----------------|
| A | 0x1000 |
| B | 0x2000 |
| X | 0x4000 |
| Y | 0x8000 |
| LB | 0x0100 |
| RB | 0x0200 |
| D-Pad Up | 0x0001 |
| D-Pad Down | 0x0002 |
| D-Pad Left | 0x0004 |
| D-Pad Right | 0x0008 |
| Start | 0x0010 |
| Back | 0x0020 |
