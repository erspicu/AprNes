using System.Runtime.InteropServices.JavaScript;

namespace AprNesWasm;

/// <summary>
/// 使用 [JSImport] 同步傳遞 RGBA 畫面到 JS（無 Promise/microtask overhead）。
/// byte[] 在 JS 端以 Uint8Array 接收。
/// </summary>
internal partial class NesJsInterop
{
    [JSImport("globalThis.nesInterop.drawFrameUnmarshalled")]
    internal static partial void DrawFrame(byte[] pixels);

    /// <summary>
    /// 讀取第一個已連接手把的 8-bit 按鍵 mask。
    /// bit0=A, 1=B, 2=Select, 3=Start, 4=Up, 5=Down, 6=Left, 7=Right。
    /// 無手把時回傳 -1。
    /// </summary>
    [JSImport("globalThis.nesInterop.getGamepadState")]
    internal static partial int GetGamepadState();
}
