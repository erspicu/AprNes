
namespace AprNes
{
    public partial class NesCore
    {
        static byte IO_read(ushort addr)
        {
            if (addr < 0x4000) addr = (ushort)(0x2000 | (addr & 7));
            switch (addr)
            {
                case 0x2000: return openbus;
                case 0x2001: return openbus;
                case 0x2002: return ppu_r_2002();
                case 0x2003: return openbus;
                case 0x2004: return ppu_r_2004();
                case 0x2005: return openbus;
                case 0x2006: return openbus;
                case 0x2007: return ppu_r_2007();
                case 0x4015: return 0;
                case 0x4016: return gamepad_r_4016();
                default: return 0x40; //fix 2017.01.19
            }
        }

        static void IO_write(ushort addr, byte val)
        {
            if (addr < 0x4000) addr = (ushort)(0x2000 | (addr & 7));
            switch (addr)
            {
                case 0x2000: ppu_w_2000(val); break;
                case 0x2001: ppu_w_2001(val); break;
                case 0x2002: openbus = val; break;
                case 0x2003: ppu_w_2003(val); break;
                case 0x2004: ppu_w_2004(val); break;
                case 0x2005: ppu_w_2005(val); break;
                case 0x2006: ppu_w_2006(val); break;
                case 0x2007: ppu_w_2007(val); break;
                case 0x4000: apu_4000(val); break;
                case 0x4001: apu_4001(val); break;
                case 0x4002: apu_4002(val); break;
                case 0x4003: apu_4003(val); break;
                case 0x4004: apu_4004(val); break;
                case 0x4005: apu_4005(val); break;
                case 0x4006: apu_4006(val); break;
                case 0x4007: apu_4007(val); break;
                case 0x4008: apu_4008(val); break;
                case 0x4009: apu_4009(val); break;
                case 0x400a: apu_400a(val); break;
                case 0x400b: apu_400b(val); break;
                case 0x400c: apu_400c(val); break;
                case 0x400e: apu_400e(val); break;
                case 0x400f: apu_400f(val); break;
                case 0x4010: apu_4010(val); break;
                case 0x4011: apu_4011(val); break;
                case 0x4012: apu_4012(val); break;
                case 0x4013: apu_4013(val); break;
                case 0x4014: ppu_w_4014(val); break;
                case 0x4015: apu_4015(val); break;
                case 0x4016: gamepad_w_4016(val); break;
                case 0x4017: break;//
                default: break;
            }
        }
    }
}