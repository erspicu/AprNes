# AprNes vs AprNesAvalonia — UI Method 對照清單

> 產生日期: 2026-04-01
> 最後更新: 2026-04-01 (P0+P1+P2+P3+P4+P5+P6 全部完成)
> 目的: 逐一比對 AprNes (WinForms) 的 UI 方法，確認 AprNesAvalonia 是否有對應實作

## 狀態標記

| 標記 | 意義 |
|------|------|
| OK | 已完整實作 |
| PARTIAL | 有對應但功能不完整 |
| TODO | 尚未實作 |
| N/A | 不適用 (架構差異、已由其他機制取代) |

---

## 一、AprNesUI.cs → MainWindow.axaml.cs + EmulatorEngine.cs

### 1.1 初始化與設定

| # | AprNes 方法 | 說明 | Ava 對應 | 狀態 |
|---|------------|------|----------|------|
| 1 | `AprNesUI()` | 建構子：載入設定、語系、UI大小 | `MainWindow()` | OK |
| 2 | `GetInstance()` | Singleton 存取 | N/A (Avalonia 單視窗) | N/A |
| 3 | `LoadConfig()` | 從 AprNes.ini 載入所有設定 | `ApplyIniSettings()` | OK |
| 4 | `initUILang()` | 套用語系到所有 UI 控件 | `ApplyLanguage()` | OK |
| 5 | `initUIsize()` | 依縮放/Analog模式調整視窗大小 | N/A (Avalonia 使用 Stretch.Uniform 自適應) | N/A |
| 6 | `Configure_Write()` | 存回所有設定到 INI | ConfigWindow `SaveToIni()` | OK |
| 7 | `CreateRenderResize()` | 建立濾鏡/掃描線的 Render 物件 | N/A (Avalonia 無 GDI Render 管線，濾鏡由 GPU/Skia 處理) | N/A |
| 8 | `GetDefaultLang()` | 依系統語系決定預設語言 | `App.axaml.cs` 自動偵測 | OK |
| 9 | `MigrateOldIni()` | 將舊版 INI 搬到 configure/ | 無 (Ava 版直接用 configure/) | N/A |

### 1.2 ROM 載入與管理

| # | AprNes 方法 | 說明 | Ava 對應 | 狀態 |
|---|------------|------|----------|------|
| 10 | `button1_Click()` | 開啟檔案對話框選 ROM | `MenuOpenRom_Click()` | OK |
| 11 | `LoadRomFromPath()` | 載入 ROM (含 ZIP/FDS BIOS 驗證)、啟動模擬 | `LoadAndStartRom()` + `EmulatorEngine.LoadRom()` | OK |
| 12 | `GetRomInfo()` | 解析 iNES header 回傳資訊 | `EmulatorEngine.GetRomInfo()` | OK |
| 13 | `LoadSRam()` | 載入 .sav 存檔 | `EmulatorEngine.LoadSRam()` | OK |
| 14 | `SaveSRam()` | 儲存 .sav 存檔 | `EmulatorEngine.SaveSRam()` | OK |

### 1.3 最近開啟 (Recent ROMs)

| # | AprNes 方法 | 說明 | Ava 對應 | 狀態 |
|---|------------|------|----------|------|
| 15 | `InitRecentROMsMenu()` | 建立 Recent ROMs 選單結構 | `InitRecentROMs()` | OK |
| 16 | `BuildRecentROMsMenu()` | 填充 Recent ROMs 項目 (最多10筆) | `BuildRecentROMsMenu()` | OK |
| 17 | `AddRecentROM()` | 新增路徑到最近清單 | `AddRecentROM()` | OK |
| 18 | `RemoveRecentROM()` | 移除不存在的項目 | `RemoveRecentROM()` | OK |
| 19 | `RecentROM_Click()` | 點擊最近 ROM 項目、驗證存在後載入 | `RecentROM_Click()` | OK |

### 1.4 設定檔管理

| # | AprNes 方法 | 說明 | Ava 對應 | 狀態 |
|---|------------|------|----------|------|
| 20 | `LoadAnalogConfig()` | 從 AprNesAnalog.ini 載入類比參數 | `MainWindow.LoadAnalogConfig()` | OK |
| 21 | `LoadAudioPlusIni()` | 從 AprNesAudioPlus.ini 載入音效參數 | `MainWindow.LoadAudioPlusIni()` | OK |
| 22 | `SaveAudioPlusIni()` | 存回音效參數到 INI | `AudioPlusConfigWindow.SaveAudioPlusIni()` | OK |
| 23 | `SaveAudioPlusIniPublic()` | 公開包裝供 ConfigUI 呼叫 | N/A (Ava 版直接在 Window 內存) | N/A |
| 24 | `ApplyChipChannelSettings()` | 套用擴展晶片聲道音量設定 | `MainWindow.ApplyChipChannelSettings()` + `AudioPlusConfigWindow` | OK |
| 25 | `InitChipDefaults()` | 初始化擴展晶片預設值 (70%/啟用) | `MainWindow.InitChipDefaults()` + `AudioPlusConfigWindow` | OK |

### 1.5 模擬控制

| # | AprNes 方法 | 說明 | Ava 對應 | 狀態 |
|---|------------|------|----------|------|
| 26 | `Reset()` | 軟體重置 | `MenuSoftReset_Click()` | OK |
| 27 | `HardReset()` | 硬體重置 (斷電重開) | `MenuHardReset_Click()` + `EmulatorEngine.HardReset()` | OK |
| 28 | `ApplyRenderSettings()` | 變更濾鏡後重建 Render 物件 | N/A (Avalonia 無 GDI Render 管線) | N/A |

### 1.6 渲染與顯示

| # | AprNes 方法 | 說明 | Ava 對應 | 狀態 |
|---|------------|------|----------|------|
| 29 | `NESCaptureScreen()` | 截圖存 PNG | `CaptureScreen()` | OK |
| 30 | `VideoOutputDeal()` | 一般模式影像輸出 + FPS 限制 | `EmulatorEngine.OnVideoOutput()` | OK |
| 31 | `AnalogRenderThreadLoop()` | Analog 模式非同步渲染 loop | N/A (Avalonia 用 WriteableBitmap + Dispatcher，無需獨立 GDI 渲染執行緒) | N/A |
| 32 | `StartAnalogRenderThread()` | 啟動 Analog 渲染執行緒 | N/A (同上) | N/A |
| 33 | `StopAnalogRenderThread()` | 停止 Analog 渲染執行緒 | N/A (同上) | N/A |
| 34 | `EnterAnalogFullScreen()` | 進入 Analog 全螢幕 (8:7 PAR) | `ToggleFullscreen()` (Stretch.Uniform 自動 letterbox) | OK |
| 35 | `ExitAnalogFullScreen()` | 離開 Analog 全螢幕 | `ToggleFullscreen()` | OK |
| 36 | `FullScreenModeTransition()` | Analog/一般模式全螢幕切換 | `ToggleFullscreen()` (統一模式) | OK |

### 1.7 選單事件 — 檔案

| # | AprNes 方法 | 說明 | Ava 對應 | 狀態 |
|---|------------|------|----------|------|
| 37 | `fun1ToolStripMenuItem_Click()` | 開啟 ROM | `MenuOpenRom_Click()` | OK |
| 38 | `fun5ToolStripMenuItem_Click()` | 離開 | `MenuExit_Click()` | OK |

### 1.8 選單事件 — 模擬

| # | AprNes 方法 | 說明 | Ava 對應 | 狀態 |
|---|------------|------|----------|------|
| 39 | `fun2ToolStripMenuItem_Click()` | 軟體重置 | `MenuSoftReset_Click()` | OK |
| 40 | `fun7ToolStripMenuItem_Click()` | 硬體重置 | `MenuHardReset_Click()` | OK |
| 41 | `_menuEmulationLimitFps_Click()` | 切換 FPS 限制 | `MenuLimitFPS_Click()` | OK |
| 42 | `_menuEmulationPerdotFSM_Click()` | 切換 AccuracyOptA | `MenuAccuracyOptA_Click()` | OK |
| 43 | `_menuEmulationRegion_Click()` | 區域切換 (NTSC/PAL/Dendy) + 硬重置 | `MenuRegion_Click()` + `ApplyRegionToNesCore()` | OK |

### 1.9 選單事件 — 檢視/工具

| # | AprNes 方法 | 說明 | Ava 對應 | 狀態 |
|---|------------|------|----------|------|
| 44 | `_menuToolsScreenshot_Click()` | 截圖 | `MenuScreenshot_Click()` | OK |
| 45 | `_menuHelpShortcuts_Click()` | 顯示快捷鍵 | `MenuShortcuts_Click()` | OK |
| 46 | `_soundMenuItem_Click()` | 音效開關 | `MenuSound_Click()` | OK |
| 47 | `_ultraAnalogMenuItem_Click()` | Ultra Analog 開關 + 重建管線 | `MenuUltraAnalog_Click()` | OK |
| 48 | `_recordVideoMenuItem_Click()` | 錄影開關 (FFmpeg) | `MenuRecordVideo_Click()` | OK |
| 49 | `_recordAudioMenuItem_Click()` | 錄音開關 | `MenuRecordAudio_Click()` | OK |

### 1.10 選單事件 — 右鍵選單 / 對話框

| # | AprNes 方法 | 說明 | Ava 對應 | 狀態 |
|---|------------|------|----------|------|
| 50 | `fun3ToolStripMenuItem_Click()` | 開啟設定 | `MenuConfiguration_Click()` | OK |
| 51 | `fun4ToolStripMenuItem_Click()` | ROM 資訊 | `MenuRomInfo_Click()` | OK |
| 52 | `fun6ToolStripMenuItem_Click()` | 關於 | `MenuAbout_Click()` | OK |
| 53 | `fun8ToolStripMenuItem_Click()` | 全螢幕切換 (含 Analog) | `MenuFullscreen_Click()` → `ToggleFullscreen()` | OK |
| 54 | `fullScreeenToolStripMenuItem_Click()` | 進入全螢幕 | `ToggleFullscreen()` | OK |
| 55 | `normalToolStripMenuItem_Click()` | 離開全螢幕 | `ToggleFullscreen()` | OK |

### 1.11 輸入與計時

| # | AprNes 方法 | 說明 | Ava 對應 | 狀態 |
|---|------------|------|----------|------|
| 56 | `ProcessCmdKey()` | 全域快捷鍵 (F11/Esc/方向鍵) | `OnKeyDown()` | OK |
| 57 | `AprNesUI_KeyUp()` | 按鍵放開 | `OnKeyUp()` | OK |
| 58 | `fps_count_timer_Tick()` | FPS 計時器更新顯示 | `_fpsTimer.Tick` lambda | OK |
| 59 | `AprNesUI_FormClosing()` | 關閉視窗：停止錄製/Analog/SRAM/音效 | `Closing` lambda → `_emu.Dispose()` (含 SaveSRam) | OK |

### 1.12 音效/錄製

| # | AprNes 方法 | 說明 | Ava 對應 | 狀態 |
|---|------------|------|----------|------|
| 60 | `StopRecordingIfActive()` | 停止進行中的錄製 | `StopRecordingIfActive()` | OK |
| 61 | `StopRecordingOnSettingsChange()` | 設定變更時停止錄製 | `StopRecordingOnSettingsChange()` | OK |
| 62 | `UpdateSoundMenuText()` | 更新音效選單文字 | `UpdateMenuStates()` | OK |
| 63 | `UpdateUltraAnalogMenuText()` | 更新 Ultra Analog 選單文字 | `UpdateMenuStates()` | OK |
| 64 | `UpdateRecordMenuVisibility()` | 更新錄製選單打勾狀態 | `UpdateRecordMenuVisibility()` | OK |
| 65 | `UpdateRegionCheckmarks()` | 更新區域選單打勾狀態 | `UpdateMenuStates()` | OK |
| 66 | `GetFfmpegPath()` | 取得 FFmpeg 路徑 | `GetFfmpegPath()` | OK |

### 1.13 工具方法

| # | AprNes 方法 | 說明 | Ava 對應 | 狀態 |
|---|------------|------|----------|------|
| 67 | `LogError()` | 寫入錯誤日誌 | `LogError()` | OK |
| 68 | `BeginHighResPeriod()` | 啟用高精度計時器 (1ms) | N/A (.NET 10 Timer 精度已足夠，無需 winmm.dll) | N/A |
| 69 | `EndHighResPeriod()` | 還原計時器精度 | N/A (同上) | N/A |
| 70 | `SRamPath()` | 回傳 .sav 路徑 | `EmulatorEngine.SRamPath()` | OK |
| 71 | `FileWriteAllText()` | 寫入設定檔 helper | `IniFile.Save()` | OK |

### 1.14 手把輸入 (AprNesUI.cs 輪詢執行緒)

| # | AprNes 方法 | 說明 | Ava 對應 | 狀態 |
|---|------------|------|----------|------|
| 72 | `NES_init_KeyMap()` | 初始化鍵盤/手把對應字典 | `EmulatorEngine.ApplyKeyMap()` + `Win32GamepadBackend.LoadMapping()` | OK |
| 73 | `polling_listener()` | 手把輸入輪詢執行緒 | `EmulatorEngine.GamepadPollLoop()` | OK |
| 74 | `JoyPadWayName()` | 軸向值轉方向名稱 | `Win32GamepadBackend.WayName()` | OK |

---

## 二、AprNes_ConfigureUI.cs → ConfigWindow.axaml.cs

### 2.1 初始化

| # | AprNes 方法 | 說明 | Ava 對應 | 狀態 |
|---|------------|------|----------|------|
| 1 | `AprNes_ConfigureUI()` | 建構子 | `ConfigWindow()` | OK |
| 2 | `GetInstance()` | Singleton | N/A | N/A |
| 3 | `init()` | 載入語系、初始化下拉選單 | `LoadFromIni()` + `ApplyLanguage()` | OK |
| 4 | `InitResizeComboBoxes()` | 填充濾鏡下拉選單 | AXAML 靜態定義 | OK |
| 5 | `RegisterJoyActivation()` | 綁定手把 TextBox 的 Click/Enter 事件 | `GP_Click()` + `WaitForButton()` | OK |
| 6 | `RegisterP2KeyboardEvents()` | 綁定 P2 鍵盤 TextBox 事件 | AXAML 綁定 `KB_Click` | OK |

### 2.2 語系

| # | AprNes 方法 | 說明 | Ava 對應 | 狀態 |
|---|------------|------|----------|------|
| 7 | `comboBox1_LangChanged()` | 切換語系時即時更新 UI | `LangCombo_SelectionChanged()` | OK |
| 8 | `LangStr()` | 安全取得語系字串 | `L()` | OK |

### 2.3 濾鏡

| # | AprNes 方法 | 說明 | Ava 對應 | 狀態 |
|---|------------|------|----------|------|
| 9 | `FindFilterIndex()` | 找濾鏡索引 | `LoadFromIni()` 用 SelectedIndex | OK |
| 10 | `GetFilterScale()` | 解析倍率 | 無 (Ava 版直接用 index) | N/A |
| 11 | `sizelevel_Changed()` | 濾鏡變更時更新解析度標籤 | `CmbFilter_Changed()` | OK |
| 12 | `UpdateResolutionLabel()` | 更新解析度顯示 | `UpdateResolutionLabel()` | OK |

### 2.4 Analog 設定

| # | AprNes 方法 | 說明 | Ava 對應 | 狀態 |
|---|------------|------|----------|------|
| 13 | `AnalogSetting_Click()` | 開啟 Analog 進階設定 | `BtnAnalogAdvanced_Click()` | OK |
| 14 | `UpdateAnalogEnableState()` | 依勾選狀態啟用/停用子控件 | `UpdateAnalogEnableState()` | OK |
| 15 | `useAnalog_CheckedChanged()` | Analog 勾選變更 | `ChkAnalog.Click` → `UpdateAnalogEnableState()` | OK |
| 16 | `ultraAnalog_CheckedChanged()` | Ultra Analog 勾選變更 | `ChkUltraAnalog.Click` → `UpdateAnalogEnableState()` | OK |

### 2.5 音效設定

| # | AprNes 方法 | 說明 | Ava 對應 | 狀態 |
|---|------------|------|----------|------|
| 17 | `AudioAdvanceSetting_Click()` | 開啟音效進階設定 | `BtnAudioAdvanced_Click()` | OK |
| 18 | `SoundcheckBox_CheckedChanged()` | 音效勾選變更 | `ChkSound_Click()` — 即時切換 `NesCore.AudioEnabled` | OK |
| 19 | `SoundtrackBar_Scroll()` | 音量滑桿拖動 | `VolumeSlider.ValueChanged` | OK |
| 20 | `UpdateSoundUI()` | 更新音效顯示文字 | `UpdateVolumeLabel()` | OK |

### 2.6 手把設定

| # | AprNes 方法 | 說明 | Ava 對應 | 狀態 |
|---|------------|------|----------|------|
| 21 | `ExpectedJoyInputType()` | 判斷控件接受的輸入類型 | N/A (Avalonia GP_Click 統一接受按鈕+軸向) | N/A |
| 22 | `Setup_JoyPad_define()` | 捕獲手把輸入並對應到 NES 按鈕 | `GP_Click()` + `WaitForButton()` → `_gpIniKeys` | OK |
| 23 | `SetupP2JoyPad()` | P2 手把設定 helper | `GP_Click()` 統一 P1/P2 | OK |
| 24 | `JoyPadWayName()` | 軸向值轉方向名稱 | `Win32GamepadBackend.WayName()` | OK |

### 2.7 鍵盤設定

| # | AprNes 方法 | 說明 | Ava 對應 | 狀態 |
|---|------------|------|----------|------|
| 25 | `textBox_KeyConfig_KeyUp()` | 捕獲鍵盤輸入 | `OnWindowKeyDown()` | OK |
| 26 | `textBox_KeyConfig_MouseClick()` | 點擊 TextBox 進入捕獲模式 | `KB_Click()` | OK |

### 2.8 存檔與關閉

| # | AprNes 方法 | 說明 | Ava 對應 | 狀態 |
|---|------------|------|----------|------|
| 27 | `BeforClose()` | 關閉前套用所有設定 | `SaveToIni()` | OK |
| 28 | `OK()` | 確定按鈕 | `BtnOK_Click()` | OK |
| 29 | `AprNes_ConfigureUI_Shown()` | 對話框開啟時初始化手把顯示 | `LoadFromIni()` | OK |
| 30 | `choose_dir_Click()` | 截圖目錄選擇 | `BtnBrowseScreenshot_Click()` | OK |

---

## 三、AprNes_AnalogConfigureUI.cs → AnalogConfigWindow.axaml.cs

| # | AprNes 方法 | 說明 | Ava 對應 | 狀態 |
|---|------------|------|----------|------|
| 1 | `AprNes_AnalogConfigureUI()` | 建構子、載入參數、綁定事件 | `AnalogConfigWindow()` + `LoadFromFields()` + `WireEvents()` | OK |
| 2 | `InitLang()` / `L()` | 語系初始化 | `ApplyLanguage()` / `L()` | OK |
| 3 | `LoadFromFields()` | 從 NesCore 載入所有參數到 UI | `LoadFromFields()` | OK |
| 4 | `ApplyToFields()` | 套用 UI 值到 NesCore + 重建 LUT | `ApplyToFields()` | OK |
| 5 | `SaveIni()` | 存回 AprNesAnalog.ini | `SaveIni()` | OK |
| 6 | `WireEvents()` | 綁定 Slider ValueChanged 事件 | `WireEvents()` | OK |
| 7 | `ComboPreset_Changed()` | Preset 設定檔切換 | `ComboPreset_Changed()` | OK |
| 8 | `SetNtsc()` | 套用 NTSC preset | `SetNtsc()` | OK |
| 9 | `SetCrt()` | 套用 CRT preset | `SetCrt()` | OK |
| 10 | `UpdateAllLabels()` | 更新所有滑桿數值標籤 | `UpdateAllLabels()` | OK |

---

## 四、AprNes_AudioPlusConfigureUI.cs → AudioPlusConfigWindow.axaml.cs

| # | AprNes 方法 | 說明 | Ava 對應 | 狀態 |
|---|------------|------|----------|------|
| 1 | `AprNes_AudioPlusConfigureUI()` | 建構子、建立動態 UI | `AudioPlusConfigWindow()` + `BuildExpChannelUI()` | OK |
| 2 | `BuildChannelVolumeUI()` | 動態建立 NES+擴展聲道控件 | NES: AXAML 靜態 + Exp: `BuildExpChannelUI()` 動態 | OK |
| 3 | `CreateChannelRow()` | 建立單一聲道控件列 | `BuildExpChannelUI()` 內建 | OK |
| 4 | `ApplyLang()` | 語系套用 | `ApplyLanguage()` | OK |
| 5 | `LoadFromNesCore()` | 從 NesCore 載入音效參數 | `LoadFromNesCore()` | OK |
| 6 | `SaveToNesCore()` | 套用 UI 值到 NesCore | `SaveToNesCore()` | OK |
| 7 | `UpdateExpChannelVisibility()` | 依晶片顯示/隱藏擴展聲道 | `UpdateExpChannelVisibility()` | OK |
| 8 | `cboMapperChip_Changed()` | 晶片切換事件 | `OnExpChipChanged()` → `UpdateExpChannelVisibility()` | OK |
| 9 | `cboConsoleModel_Changed()` | 主機型號切換 + LPF 控制 | `UpdateCustomEnableState()` | OK |
| 10 | `UpdateAllValueLabels()` | 更新所有數值標籤 | `UpdateAllValueLabels()` | OK |
| 11 | `btnOK_Click()` | 確定、存回、套用管線 | `BtnOK_Click()` — SaveToNesCore + SaveIni + ApplySettings | OK |
| 12 | `btnCancel_Click()` | 取消 | `BtnCancel_Click()` | OK |

---

## 五、AprNes_RomInfoUI.cs → RomInfoWindow.axaml.cs

| # | AprNes 方法 | 說明 | Ava 對應 | 狀態 |
|---|------------|------|----------|------|
| 1 | `AprNes_RomInfoUI()` | 建構子 | `RomInfoWindow()` | OK |
| 2 | `init()` | 取得 ROM 資訊並顯示 | `SetInfo()` | OK |
| 3 | `button1_Click()` | 關閉 | `BtnOK_Click()` | OK |
| 4 | `button2_Click()` | 複製到剪貼簿 | `BtnCopy_Click()` | OK |

---

## 六、AprNes_Info.cs → AboutWindow.axaml.cs

| # | AprNes 方法 | 說明 | Ava 對應 | 狀態 |
|---|------------|------|----------|------|
| 1 | `AprNes_Infocs()` | 建構子 + 版本時間 | `AboutWindow()` | OK |
| 2 | `VersionTime()` | 解析 PE header 取編譯時間 | `DateTime.Now` 替代 | OK (不同做法) |
| 3 | `button1_Click()` | 關閉 | `BtnOK_Click()` | OK |
| 4 | `linkLabel1_LinkClicked()` | 開啟官網 | `SiteLink_Click()` | OK |

---

## 統計摘要

| 狀態 | 數量 |
|------|------|
| OK | 119 |
| PARTIAL | 0 |
| TODO | 0 |
| N/A | 14 |
| **合計** | **133** |

---

## 待實作項目優先順序建議

### ~~P0 — 基本功能 (影響正常使用)~~ ✅ 全部完成

1. ~~**HardReset** — 硬體重置實作~~ ✅
2. ~~**LoadSRam / SaveSRam / SRamPath** — 存檔讀寫 (電池 RAM)~~ ✅
3. ~~**Recent ROMs** — 最近開啟清單 (5項, #15-19)~~ ✅
4. ~~**Region 實際套用** — 區域切換需套用到 NesCore + 硬重置 (#43)~~ ✅
5. ~~**FormClosing 完善** — 關閉時存 SRAM (#59)~~ ✅

### ~~P1 — 設定功能 (影響使用體驗)~~ ✅ 全部完成

6. ~~**LoadRomFromPath 完善** — ZIP 解壓、FDS BIOS 驗證 (#11)~~ ✅
7. ~~**ConfigWindow 即時語系切換** — LangCombo 選取後即時更新 (#7)~~ ✅
8. **UpdateResolutionLabel** — 濾鏡變更時更新解析度 (#11-12) → 移至 P4 (依賴 Render 管線)
9. ~~**Analog 子控件啟停** — ChkAnalog 勾選時啟用/停用相關控件 (#14-16)~~ ✅
10. ~~**ChkSound 即時生效** — 勾選時即時開關音效 (#18)~~ ✅

### ~~P2 — 進階功能 (Analog/AudioPlus 設定後端)~~ ✅ 全部完成

11. ~~**AnalogConfigWindow 完整後端** — LoadFromFields/ApplyToFields/SaveIni/WireEvents/Presets (#3-10)~~ ✅
12. ~~**AudioPlusConfigWindow 完整後端** — LoadFromNesCore/SaveToNesCore/擴展聲道動態UI (#3-10)~~ ✅
13. ~~**LoadAnalogConfig / LoadAudioPlusIni / SaveAudioPlusIni** — INI 讀寫 (#20-25)~~ ✅

### ~~P3 — 錄製功能~~ ✅ 全部完成

14. ~~**錄影 (FFmpeg)** — RecordVideo/RecordAudio/StopRecording (#48-49, #60-61, #64, #66)~~ ✅

### ~~P4 — Analog 渲染 + 濾鏡~~ ✅ 全部完成

15. ~~**Analog 渲染管線** — N/A: Avalonia 用 WriteableBitmap + Dispatcher，無需獨立 GDI 渲染執行緒 (#31-33)~~ N/A
16. ~~**Analog 全螢幕** — Avalonia ToggleFullscreen() + Stretch.Uniform 統一處理 (#34-36)~~ ✅
17. ~~**CreateRenderResize / ApplyRenderSettings** — N/A: Avalonia 無 GDI Render 管線 (#7, #28)~~ N/A
18. ~~**UltraAnalog 重建管線** — MenuUltraAnalog_Click + SyncAnalogConfig/Ntsc_Init/Crt_Init (#47)~~ ✅
19. ~~**UpdateResolutionLabel** — CmbFilter_Changed + UpdateResolutionLabel (#11-12)~~ ✅

### ~~P5 — 手把~~ ✅ 全部完成

20. ~~**手把輪詢執行緒** — EmulatorEngine.GamepadPollLoop() (#73)~~ ✅
21. ~~**手把設定捕獲** — GP_Click() + WaitForButton() (#5, #21-24)~~ ✅
22. ~~**手把實際輸入** — Win32GamepadBackend (DirectInput8 + XInput, 共用 joystick.cs)~~ ✅

### ~~P6 — 雜項~~ ✅ 全部完成

23. ~~**initUIsize** — N/A: Avalonia 用 Stretch.Uniform 自適應 (#5)~~ N/A
24. ~~**LogError** — MainWindow.LogError() + NesCore.OnError 綁定 (#67)~~ ✅
25. ~~**HighResPeriod** — N/A: .NET 10 Timer 精度已足夠 (#68-69)~~ N/A
