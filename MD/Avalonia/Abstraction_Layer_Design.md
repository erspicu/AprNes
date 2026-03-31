# AprNesAvalonia 平台抽象層設計

**建立日期：2026-03-31**

---

## 概述

AprNesAvalonia 以跨平台為目標，但 Avalonia 本身不提供音效和手把 API。
需要自行建立抽象層，Win32 平台可直接引用 AprNes 現有的 tool/ 程式碼。

---

## 需要抽象層的系統

### 1. 音效輸出 — `IAudioBackend`

```csharp
public interface IAudioBackend
{
    void Open(int sampleRate, int channels, int bitsPerSample);
    void Close();
    void SubmitSample(short left, short right);
    bool IsAvailable { get; }
}
```

| 平台 | 實作 | 來源 |
|------|------|------|
| Win32 | `Win32WaveOutBackend` | 直接引用 `AprNes/tool/WaveOutPlayer.cs` |
| Linux | `SDL2AudioBackend` 或 `PulseAudioBackend` | 新寫 |
| macOS | `SDL2AudioBackend` 或 `CoreAudioBackend` | 新寫 |

### 2. 手把輸入 — `IGamepadBackend`

```csharp
public interface IGamepadBackend
{
    void Initialize();
    void Poll();  // 每幀呼叫
    void Shutdown();

    // 按鈕狀態查詢
    bool IsButtonPressed(int playerIndex, GamepadButton button);

    // 設定模式：等待按鍵並回傳
    GamepadButtonInfo? WaitForButton(int timeoutMs);

    bool IsAvailable { get; }
    int ConnectedCount { get; }
}
```

| 平台 | 實作 | 來源 |
|------|------|------|
| Win32 | `Win32InputBackend` | 直接引用 `AprNes/tool/joystick.cs` + `DirectInputHelper.cs` |
| Linux | `SDL2GamepadBackend` 或 `EvdevBackend` | 新寫 |
| macOS | `SDL2GamepadBackend` | 新寫 |

---

## 不需要抽象層的系統

| 系統 | 原因 |
|------|------|
| **畫面輸出** | Avalonia 自帶 `WriteableBitmap`、`Image` 控件，跨平台統一 |
| **畫面濾鏡** | xBRZ / ScaleX / Scanline 是純 `uint*` 運算，平台無關 |
| **類比模擬** | Ntsc.cs / CrtScreen.cs 是純 buffer 運算，平台無關 |
| **鍵盤輸入** | Avalonia 自帶 `KeyDown` / `KeyUp` 事件 |
| **檔案 I/O** | .NET `System.IO` 跨平台 |
| **截圖** | Avalonia `Bitmap.Save()` 跨平台 |

---

## Win32 直接引用策略

在 `AprNesAvalonia.csproj` 中用 `<Compile Include>` link AprNes 的 tool/ 檔案：

```xml
<!-- Win32 audio backend (WinMM) -->
<Compile Include="../AprNes/tool/WaveOutPlayer.cs" Link="Platform/Win32/WaveOutPlayer.cs"
         Condition="$([MSBuild]::IsOSPlatform('Windows'))" />

<!-- Win32 gamepad backend (DirectInput + XInput) -->
<Compile Include="../AprNes/tool/joystick.cs" Link="Platform/Win32/joystick.cs"
         Condition="$([MSBuild]::IsOSPlatform('Windows'))" />
<Compile Include="../AprNes/tool/DirectInputHelper.cs" Link="Platform/Win32/DirectInputHelper.cs"
         Condition="$([MSBuild]::IsOSPlatform('Windows'))" />

<!-- 共用畫面濾鏡（全平台） -->
<Compile Include="../AprNes/tool/libXBRz.cs" Link="Filters/libXBRz.cs" />
<Compile Include="../AprNes/tool/Scalex.cs" Link="Filters/Scalex.cs" />
<Compile Include="../AprNes/tool/LibScanline.cs" Link="Filters/LibScanline.cs" />
```

Runtime 選擇 backend：

```csharp
public static class PlatformFactory
{
    public static IAudioBackend CreateAudioBackend()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new Win32WaveOutBackend();
        // Linux/macOS: return new SDL2AudioBackend();
        throw new PlatformNotSupportedException();
    }

    public static IGamepadBackend CreateGamepadBackend()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new Win32InputBackend();
        // Linux/macOS: return new SDL2GamepadBackend();
        throw new PlatformNotSupportedException();
    }
}
```

---

## 實施優先序

1. **Phase 1（UI 先行）**：不需要抽象層，用現有 WaveOutPlayer 直接跑
2. **Phase 2（功能接線）**：定義 IAudioBackend / IGamepadBackend，Win32 實作包裝現有程式碼
3. **Phase 3（跨平台）**：新增 SDL2 / PulseAudio 等非 Win32 實作
