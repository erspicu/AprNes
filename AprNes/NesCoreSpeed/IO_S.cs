using System.Runtime.CompilerServices;

namespace AprNes
{
    unsafe public partial class NesCoreSpeed
    {
        static byte openbus_S = 0;

        static byte IO_read_S(ushort addr)
        {
            if (addr < 0x4000) addr = (ushort)(0x2000 | (addr & 7));
            switch (addr)
            {
                case 0x2000: return openbus_S;
                case 0x2001: return openbus_S;
                case 0x2002: return ppu_r_2002_S();
                case 0x2003: return openbus_S;
                case 0x2004: return ppu_r_2004_S();
                case 0x2005: return openbus_S;
                case 0x2006: return openbus_S;
                case 0x2007: return ppu_r_2007_S();
                case 0x4015: return apu_r_4015_S();
                case 0x4016: return gamepad_r_4016_S();
                default:     return 0;
            }
        }

        static void IO_write_S(ushort addr, byte val)
        {
            if (addr < 0x4000) addr = (ushort)(0x2000 | (addr & 7));
            switch (addr)
            {
                case 0x2000: ppu_w_2000_S(val); break;
                case 0x2001: ppu_w_2001_S(val); break;
                case 0x2002: openbus_S = val; break;
                case 0x2003: ppu_w_2003_S(val); break;
                case 0x2004: ppu_w_2004_S(val); break;
                case 0x2005: ppu_w_2005_S(val); break;
                case 0x2006: ppu_w_2006_S(val); break;
                case 0x2007: ppu_w_2007_S(val); break;
                case 0x4000: apu_4000_S(val); break;
                case 0x4001: apu_4001_S(val); break;
                case 0x4002: apu_4002_S(val); break;
                case 0x4003: apu_4003_S(val); break;
                case 0x4004: apu_4004_S(val); break;
                case 0x4005: apu_4005_S(val); break;
                case 0x4006: apu_4006_S(val); break;
                case 0x4007: apu_4007_S(val); break;
                case 0x4008: apu_4008_S(val); break;
                case 0x4009: break;
                case 0x400a: apu_400a_S(val); break;
                case 0x400b: apu_400b_S(val); break;
                case 0x400c: apu_400c_S(val); break;
                case 0x400e: apu_400e_S(val); break;
                case 0x400f: apu_400f_S(val); break;
                case 0x4010: apu_4010_S(val); break;
                case 0x4011: apu_4011_S(val); break;
                case 0x4012: apu_4012_S(val); break;
                case 0x4013: apu_4013_S(val); break;
                case 0x4014: ppu_w_4014_S(val); break;
                case 0x4015: apu_4015_S(val); break;
                case 0x4016: gamepad_w_4016_S(val); break;
                case 0x4017: apu_4017_S(val); break;
                default: break;
            }
        }

        // MEM dispatch handlers for PPU/IO regions
        static byte ppu_reg_read_S(ushort a)  { return IO_read_S(a); }
        static void ppu_reg_write_S(ushort a, byte v) { IO_write_S(a, v); }
        static byte io_read_S(ushort a)       { return IO_read_S(a); }
        static void io_write_S(ushort a, byte v)      { IO_write_S(a, v); }
    }
}
