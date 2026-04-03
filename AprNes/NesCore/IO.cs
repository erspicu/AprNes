namespace AprNes
{
    public unsafe partial class NesCore
    {
        static byte IO_read(ushort addr)
        {
            if (addr < 0x4000) addr = (ushort)(0x2000 | (addr & 7));

            if (addr < 0x4000)  // PPU $2000-$2007
            {
                if      (addr == 0x2002) return ppu_r_2002();
                else if (addr == 0x2004) return ppu_r_2004();
                else if (addr == 0x2007) return ppu_r_2007();
                else                     return openbus;  // write-only PPU regs return PPU open bus
            }
            else  // APU/IO $4015-$4017
            {
                if      (addr == 0x4015) return apu_r_4015();
                else if (addr == 0x4016) return gamepad_r_4016();
                else if (addr == 0x4017) return gamepad_r_4017();
                else                     return cpubus;
            }
        }

        static void IO_write(ushort addr, byte val)
        {
            if (addr < 0x4000) addr = (ushort)(0x2000 | (addr & 7));

            if (addr < 0x4000)  // PPU $2000-$2007
            {
                if      (addr == 0x2000) ppu_w_2000(val);
                else if (addr == 0x2001) ppu_w_2001(val);
                else if (addr == 0x2002) { openbus = val; }
                else if (addr == 0x2003) ppu_w_2003(val);
                else if (addr == 0x2004) ppu_w_2004(val);
                else if (addr == 0x2005) ppu_w_2005(val);
                else if (addr == 0x2006) ppu_w_2006(val);
                else                     ppu_w_2007(val); // 0x2007
            }
            else if (addr < 0x4014)  // APU channels $4000-$4013
            {
                int lo = addr & 0xFF;
                if      (lo == 0x00) apu_4000(val);
                else if (lo == 0x01) apu_4001(val);
                else if (lo == 0x02) apu_4002(val);
                else if (lo == 0x03) apu_4003(val);
                else if (lo == 0x04) apu_4004(val);
                else if (lo == 0x05) apu_4005(val);
                else if (lo == 0x06) apu_4006(val);
                else if (lo == 0x07) apu_4007(val);
                else if (lo == 0x08) apu_4008(val);
                else if (lo == 0x09) apu_4009(val);
                else if (lo == 0x0a) apu_400a(val);
                else if (lo == 0x0b) apu_400b(val);
                else if (lo == 0x0c) apu_400c(val);
                else if (lo == 0x0e) apu_400e(val);
                else if (lo == 0x0f) apu_400f(val);
                else if (lo == 0x10) apu_4010(val);
                else if (lo == 0x11) apu_4011(val);
                else if (lo == 0x12) apu_4012(val);
                else                 apu_4013(val); // 0x13
            }
            else  // OAM DMA + APU/IO $4014-$4017
            {
                if      (addr == 0x4014) ppu_w_4014(val);
                else if (addr == 0x4015) apu_4015(val);
                else if (addr == 0x4016) gamepad_w_4016(val);
                else if (addr == 0x4017)
                {
                    // TriCNES $4017 write model
                    last4017Val = val;
                    ctrmode = ((val & 0x80) != 0) ? 5 : 4;
                    apuintflag = (val & 0x40) != 0;
                    if (ctrmode == 5)
                    {
                        // 5-step: immediate quarter + half frame (deferred via flags)
                        apuHalfFrame = true;
                        apuQuarterFrame = true;
                    }
                    if (apuintflag)
                    {
                        statusframeint = false;
                        irqLineCurrent = false;
                        UpdateIRQLine();
                    }
                    // Deferred reset: 3 cycles if put cycle, 4 if get cycle (TriCNES)
                    apuFrameCounterReset = (byte)(mcApuPutCycle ? 3 : 4);
                }
            }
        }
    }
}
