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

        // ── WASM 診斷計數器 ─────────────────────────────────────────────────────
        /// <summary>StepOneFrame 觸發 safety limit 的累計次數（> 0 代表 PPU 幀沒完成）</summary>
        public static int WasmSafetyHits = 0;
        /// <summary>最後一幀花了幾個 cpu_step()（正常約 8000~10000）</summary>
        public static int WasmLastSteps  = 0;
        /// <summary>NMI 觸發累計次數（應每幀 +1）</summary>
        public static int WasmNmiCount   = 0;
        /// <summary>上一幀結束時的 CPU PC</summary>
        public static ushort WasmLastPC  = 0;
        /// <summary>版本戳記，確認新版有載入</summary>
        public static string WasmVersion = "v20260303b";

        // 初始化 WASM 模式：重設關鍵狀態、訂閱音效回呼、關閉 FPS 限制
        public static void WasmInit()
        {
            exit         = false;       // 重設（stop 後可重新載入新遊戲）
            LimitFPS     = false;
            HeadlessMode = false;       // WASM 不需要寫入 debug log 檔案
            AudioEnabled = true;
            WasmSafetyHits = 0;
            WasmLastSteps  = 0;
            WasmNmiCount   = 0;
            WasmLastPC     = 0;
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
            bool nmi_just_deferred = false;

            while (frame_count == startFrame && !exit && safety < 120000)
            {
                // === 與 run() 完全相同的 NMI/IRQ 觸發邏輯 ===
                if (nmi_pending && !nmi_just_deferred)
                {
                    nmi_pending = false;
                    NMIInterrupt();
                    WasmNmiCount++;
                    if (nmi_pending) nmi_just_deferred = true;
                }
                else if (nmi_just_deferred)
                {
                    nmi_just_deferred = false;
                }
                else if (irq_pending)
                {
                    irq_pending = false;
                    IRQInterrupt();
                    if (nmi_pending) nmi_just_deferred = true;
                }

                byte prevFlagI = flagI;
                cpu_step();

                if (opcode == 0x00 && nmi_pending)
                    nmi_just_deferred = true;

                if (opcode != 0x00)
                {
                    byte irqPollI = (opcode == 0x40) ? flagI : prevFlagI;
                    irq_pending = (irqPollI == 0 && irqLinePrev);
                }

                safety++;
            }

            _wasmAudioCollect = false;
            WasmLastSteps = safety;
            WasmLastPC = r_PC;
            if (frame_count == startFrame) WasmSafetyHits++;
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
