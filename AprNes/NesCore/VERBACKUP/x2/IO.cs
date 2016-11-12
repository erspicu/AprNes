using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace AprNes
{
    public partial class NesCore
    {

        //IO read & write routor
        byte IO_read(ushort addr)
        {
            switch (addr)
            {
                case 0x2002: return ppu_r_2002() ;
                case 0x2007: return ppu_r_2007() ;
                case 0x4015: return 0;
                case 0x4016: return gamepad_r_4016();
                case 0x4017: return 0x40;
                default:
                    //MessageBox.Show("unkonw IO Port read " + addr.ToString("x4"));
                    return 0;
            }
        }
        void IO_write(ushort addr, byte val)
        {
            switch (addr)
            {
                case 0x2000: ppu_w_2000(val); break;
                case 0x2001: ppu_w_2001(val); break;
                case 0x2003: ppu_w_2003(val); break;
                case 0x2004: ppu_w_2004(val); break;
                case 0x2005: ppu_w_2005(val); break;
                case 0x2006: ppu_w_2006(val); break;
                case 0x2007: ppu_w_2007(val); break;
                case 0x4000: break;
                case 0x4001: break;
                case 0x4002: break;
                case 0x4003: break;
                case 0x4004: break;
                case 0x4005: break;
                case 0x4006: break;
                case 0x4007: break;
                case 0x4008: break;
                case 0x4009: break;
                case 0x400a: break;
                case 0x400b: break;
                case 0x400c: break;
                case 0x400e: break;
                case 0x400f: break;
                case 0x4010: break;
                case 0x4011: break;
                case 0x4012: break;
                case 0x4013: break;
                case 0x4014: ppu_w_4014(val); break;
                case 0x4015: break;
                case 0x4016: gamepad_w_4016(val); break;
                case 0x4017: break;
                default: 
                    //MessageBox.Show("unkonw IO Port write " + addr.ToString("x4")); 
                    break;
            }
        }
    }
}