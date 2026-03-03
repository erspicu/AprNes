// AprNesAOT only – extends AprNesUI with JIT-vs-AOT benchmark comparison.
// This partial class is compiled only in the AprNesAOT project.

using System;
using System.Threading;
using System.Windows.Forms;

namespace AprNes
{
    public partial class AprNesUI
    {
        /// <summary>Called once during form Load to inject the AOT benchmark menu item.</summary>
        internal void AddAotBenchmarkMenuItem()
        {
            var sep  = new ToolStripSeparator();
            var item = new ToolStripMenuItem("Benchmark – JIT vs AOT DLL");
            item.Click += AotBenchmarkClick;
            contextMenuStrip1.Items.Add(sep);
            contextMenuStrip1.Items.Add(item);
        }

        void AotBenchmarkClick(object sender, EventArgs e)
        {
            if (current_rom_bytes == null)
            {
                MessageBox.Show("請先載入 ROM", "Benchmark");
                return;
            }

            bool aotAvailable = NesCoreBenchmark.IsAvailable();
            string msg =
                "Benchmark 將以最大速度執行各 5 秒：\n" +
                "  1. JIT (.NET 8 managed NesCore)\n" +
                (aotAvailable
                    ? "  2. AOT (NesCoreNative.dll)\n"
                    : "  2. AOT DLL – 未偵測到 NesCoreNative.dll，跳過\n") +
                "\n期間模擬器將停止，完成後需重新載入 ROM。\n\n繼續嗎？";

            if (MessageBox.Show(msg, "Benchmark", MessageBoxButtons.YesNo) != DialogResult.Yes)
                return;

            const int seconds = 5;

            // ── Stop current emulation ─────────────────────────────────────────
            fps_count_timer.Enabled = false;
            NesCore.exit = true;
            NesCore._event.Set();
            nes_t?.Join(1000);
            WaveOutPlayer.CloseAudio();
            running = false;

            // ── JIT benchmark ──────────────────────────────────────────────────
            int jitFrames = 0;
            {
                NesCore.exit = false;
                NesCore.init(current_rom_bytes);
                NesCore.LimitFPS = false;

                EventHandler counter = (s2, e2) => Interlocked.Increment(ref jitFrames);
                NesCore.VideoOutput -= new EventHandler(VideoOutputDeal);
                NesCore.VideoOutput += counter;

                var t = new Thread(NesCore.run) { IsBackground = true };
                t.Start();
                Thread.Sleep(seconds * 1000);
                NesCore.exit = true;
                NesCore._event.Set();
                t.Join(2000);

                NesCore.VideoOutput -= counter;
            }

            // ── AOT DLL benchmark ──────────────────────────────────────────────
            int aotFrames = -1;
            if (aotAvailable)
                aotFrames = NesCoreBenchmark.RunAotBenchmark(current_rom_bytes, seconds);

            // ── Show result ────────────────────────────────────────────────────
            string result =
                $"Benchmark 結果（{seconds} 秒）\n\n" +
                $"JIT (.NET 8)  : {jitFrames,6} 幀  ({jitFrames / (float)seconds,7:F1} FPS)\n";

            if (aotFrames >= 0)
            {
                result +=
                    $"AOT DLL       : {aotFrames,6} 幀  ({aotFrames / (float)seconds,7:F1} FPS)\n\n" +
                    $"AOT / JIT 比率 : {(float)aotFrames / jitFrames:F3}x";
            }
            else
            {
                result += aotAvailable
                    ? "AOT DLL       : 初始化失敗"
                    : "AOT DLL       : NesCoreNative.dll 未找到";
            }

            MessageBox.Show(result, "Benchmark 結果");
        }

        // ── .NET 8 UI 客製調整 ──────────────────────────────────────────────────

        /// <summary>目前 SetDefaultFont 已確保 scale=1.0，無需額外調整</summary>
        partial void AotUIAdjust() { }

        /// <summary>ShowDialog 前暫停 TopMost，避免 .NET 8 子視窗被遮蔽</summary>
        partial void AotPreShowDialog()  => this.TopMost = false;

        /// <summary>ShowDialog 後恢復 TopMost</summary>
        partial void AotPostShowDialog() => this.TopMost = true;
    }
}
