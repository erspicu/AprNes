using System;
using System.Threading;
using NesTestFramework;

namespace AprNesAdapter
{
    /// <summary>
    /// Reference adapter: wraps AprNes's static NesCore into IEmulatorCore.
    /// Single-instance only (NesCore is entirely static).
    /// Uses the same VideoOutput + _event.Set() pattern as TestRunnerCore.
    /// </summary>
    public unsafe class AprNesEmulatorCore : IEmulatorCore
    {
        private volatile bool _frameCompleted;
        private Thread _emuThread;
        private bool _hooked;

        public void SetRegion(NesRegion region)
        {
            AprNes.NesCore.Region = region switch
            {
                NesRegion.PAL => AprNes.NesCore.RegionType.PAL,
                NesRegion.Dendy => AprNes.NesCore.RegionType.Dendy,
                _ => AprNes.NesCore.RegionType.NTSC
            };
        }

        public bool LoadRom(byte[] romData)
        {
            AprNes.NesCore.HeadlessMode = true;
            AprNes.NesCore.AudioEnabled = false;
            AprNes.NesCore.LimitFPS = false;
            try
            {
                AprNes.NesCore.init(romData);
                return true;
            }
            catch { return false; }
        }

        private void OnVideoOutput(object sender, EventArgs e)
        {
            _frameCompleted = true;
            // Resume NesCore from _event.WaitOne() inside RenderScreen
            // (it will loop back and block again at the next WaitOne)
        }

        public void RunOneFrame()
        {
            // Hook once, keep across all frames
            if (!_hooked)
            {
                AprNes.NesCore.VideoOutput += OnVideoOutput;
                _hooked = true;
            }

            _frameCompleted = false;

            // Start emulation thread on first call
            if (_emuThread == null || !_emuThread.IsAlive)
            {
                AprNes.NesCore.exit = false;
                _emuThread = new Thread(() => AprNes.NesCore.run()) { IsBackground = true };
                _emuThread.Start();
                // First frame: run() enters loop → RenderScreen → VideoOutput → _event.WaitOne()
                // Wait for first VideoOutput to fire
                while (!_frameCompleted)
                    Thread.Sleep(0);
                return;
            }

            // Subsequent frames: signal _event to resume, wait for VideoOutput
            AprNes.NesCore._event.Set();
            while (!_frameCompleted)
                Thread.Sleep(0);
        }

        public void SoftReset()
        {
            AprNes.NesCore.SoftReset();
        }

        public byte ReadCpuMemory(ushort address)
        {
            return AprNes.NesCore.NES_MEM[address];
        }

        public void GetNametable0(byte[] buffer)
        {
            byte* ppu = AprNes.NesCore.ppu_ram;
            for (int i = 0; i < 960; i++)
                buffer[i] = ppu[0x2000 + i];
        }

        public void GetScreenPixels(uint[] buffer)
        {
            // Phase A5: emu output is palette indices in ntsc_rowPalettes; convert via NesColors.
            byte* pal = AprNes.NesCore.ntsc_rowPalettes;
            uint* colors = AprNes.NesCore.NesColors;
            if (pal == null || colors == null) return;
            for (int i = 0; i < 256 * 240; i++)
                buffer[i] = colors[pal[i]];
        }

        public void SetP1Buttons(bool a, bool b, bool select, bool start,
                                  bool up, bool down, bool left, bool right)
        {
            // Clear all buttons first
            for (byte i = 0; i < 8; i++)
                AprNes.NesCore.P1_ButtonUnPress(i);

            if (a)      AprNes.NesCore.P1_ButtonPress(0);
            if (b)      AprNes.NesCore.P1_ButtonPress(1);
            if (select) AprNes.NesCore.P1_ButtonPress(2);
            if (start)  AprNes.NesCore.P1_ButtonPress(3);
            if (up)     AprNes.NesCore.P1_ButtonPress(4);
            if (down)   AprNes.NesCore.P1_ButtonPress(5);
            if (left)   AprNes.NesCore.P1_ButtonPress(6);
            if (right)  AprNes.NesCore.P1_ButtonPress(7);
        }

        public void Dispose()
        {
            AprNes.NesCore.exit = true;
            AprNes.NesCore._event.Set(); // unblock WaitOne() so run() can check exit
            _emuThread?.Join(2000);
            if (_hooked) { AprNes.NesCore.VideoOutput -= OnVideoOutput; _hooked = false; }
        }
    }
}
