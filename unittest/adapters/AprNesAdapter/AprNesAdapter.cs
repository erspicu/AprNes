using System;
using System.Threading;
using NesTestFramework;

namespace AprNesAdapter
{
    /// <summary>
    /// Reference adapter: wraps AprNes's static NesCore into IEmulatorCore.
    /// Single-instance only (NesCore is entirely static).
    /// </summary>
    public unsafe class AprNesEmulatorCore : IEmulatorCore
    {
        private volatile bool _frameCompleted;
        private Thread _emuThread;

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

        public void RunOneFrame()
        {
            _frameCompleted = false;
            EventHandler handler = null;
            handler = (s, e) =>
            {
                _frameCompleted = true;
                AprNes.NesCore.exit = true;
            };
            AprNes.NesCore.VideoOutput += handler;
            AprNes.NesCore.exit = false;

            // Run on a separate thread (NesCore.run() blocks until exit=true)
            if (_emuThread == null || !_emuThread.IsAlive)
            {
                _emuThread = new Thread(() => AprNes.NesCore.run()) { IsBackground = true };
                _emuThread.Start();
            }
            else
            {
                AprNes.NesCore._event.Set(); // resume from VBlank wait
            }

            // Wait for frame completion
            while (!_frameCompleted)
                Thread.Sleep(0);

            AprNes.NesCore.VideoOutput -= handler;
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
            uint* screen = AprNes.NesCore.ScreenBuf1x;
            for (int i = 0; i < 256 * 240; i++)
                buffer[i] = screen[i];
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
            _emuThread?.Join(1000);
        }
    }
}
