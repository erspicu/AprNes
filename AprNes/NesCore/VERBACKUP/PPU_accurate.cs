using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AprNes
{
    unsafe public partial class NesCore
    {
        //https://wiki.nesdev.com/w/index.php/PPU_rendering
        //https://wiki.nesdev.com/w/images/d/d1/Ntsc_timing.png
        void __ppu_step()
        {

            if (scanline < 240) //scanline  = 0 ~ 239
            {

                if (ppu_cycles < 1)
                {
                }
                else if (ppu_cycles < 257)
                {
                }
                else if (ppu_cycles < 321)
                {
                }
                else if (ppu_cycles < 337)
                {
                }
                else
                {
                }
            }
            else if (scanline == 240)
            {

            }
            else if (scanline < 261) //scanline = 241 ~260
            {
                //if(scanline == 241 && ppu_cycles == 1 )

            }
            else //scanline = 261 (or as -1 line)
            {

            }

            if (++ppu_cycles == 341)
            {
                ppu_cycles = 0;
                ++scanline;
            }
        }

    }
}
