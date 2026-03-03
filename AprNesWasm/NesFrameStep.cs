// NesFrameStep.cs — 擴充 NesCore partial class，提供 WASM 單執行緒步進介面
// 因為是 partial class，可直接呼叫 private cpu_step() 和存取 frame_count。

using System.Collections.Generic;

namespace AprNes
{
    unsafe public partial class NesCore
    {
        // 音效樣本暫存（每幀清空，由 AudioSampleReady 填入）
        static readonly List<short> _wasmAudioBuf = new List<short>(2048);
        static bool _wasmAudioCollect = false;

        // 初始化 WASM 模式：重設關鍵狀態、訂閱音效回呼、關閉 FPS 限制
        public static void WasmInit()
        {
            exit         = false;       // 重設（stop 後可重新載入新遊戲）
            LimitFPS     = false;
            HeadlessMode = false;       // WASM 不需要寫入 debug log 檔案
            AudioEnabled = true;
            // 避免重複訂閱
            AudioSampleReady -= WasmAudioHandler;
            AudioSampleReady += WasmAudioHandler;
        }

        static void WasmAudioHandler(short s)
        {
            if (_wasmAudioCollect) _wasmAudioBuf.Add(s);
        }

        /// <summary>
        /// 執行模擬器直到完成一個畫面（~29780 CPU cycles）後返回。
        /// 傳回本幀收集的音效樣本（44100 Hz mono int16）。
        /// </summary>
        public static short[] StepOneFrame()
        {
            _wasmAudioBuf.Clear();
            _wasmAudioCollect = true;

            int startFrame = frame_count;
            int safety = 0;
            while (frame_count == startFrame && !exit && safety < 120000)
            {
                cpu_step();
                safety++;
            }

            _wasmAudioCollect = false;
            return _wasmAudioBuf.ToArray();
        }

        /// <summary>
        /// 複製 ScreenBuf1x（ARGB uint*）為 RGBA byte[]，供 Canvas ImageData 使用。
        /// </summary>
        public static byte[] GetScreenRgba()
        {
            byte[] rgba = new byte[256 * 240 * 4];
            for (int i = 0; i < 256 * 240; i++)
            {
                uint argb = ScreenBuf1x[i];
                rgba[i * 4 + 0] = (byte)(argb >> 16); // R
                rgba[i * 4 + 1] = (byte)(argb >> 8);  // G
                rgba[i * 4 + 2] = (byte)(argb);        // B
                rgba[i * 4 + 3] = 0xFF;                // A
            }
            return rgba;
        }
    }
}
