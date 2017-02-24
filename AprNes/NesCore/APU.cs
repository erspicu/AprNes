
namespace AprNes
{

    //editing !!!!

    public partial class NesCore
    {
        //ported from https://github.com/andrew-hoffman/halfnes/blob/master/src/main/java/com/grapeshot/halfnes/APU.java

        static int samplerate;

        //static Timer[] timers = {new SquareTimer(8, 2), new SquareTimer(8, 2),new TriangleTimer(), new NoiseTimer()};

        static double cyclespersample;
        //static NES nes;
        //CPU cpu;
        //CPURAM cpuram;
        static int sprdma_count;
        static int apucycle = 0, remainder = 0;
        static int[] noiseperiod;
        // different for PAL
        static long accum = 0;
        //static ArrayList<ExpansionSoundChip> expnSound = new ArrayList<>();
        static bool soundFiltering;
        //static int[] TNDLOOKUP = initTndLookup(), SQUARELOOKUP = initSquareLookup();
        static int framectrreload;
        static int framectrdiv = 7456;
        static int dckiller = 0;
        static int lpaccum = 0;
        static bool apuintflag = true, statusdmcint = false, statusframeint = false;
        static int framectr = 0, ctrmode = 4;
        static bool[] lenCtrEnable = { true, true, true, true };
        static int[] volume = new int[4];
        //dmc instance variables
        static int[] dmcperiods;
        static int dmcrate = 0x36, dmcpos = 0, dmcshiftregister = 0, dmcbuffer = 0, dmcvalue = 0, dmcsamplelength = 1,
            dmcsamplesleft = 0, dmcstartaddr = 0xc000, dmcaddr = 0xc000, dmcbitsleft = 8;
        static bool dmcsilence = true, dmcirq = false, dmcloop = false, dmcBufferEmpty = true;
        //length ctr instance variables
        static int[] lengthctr = { 0, 0, 0, 0 };
        static int[] lenctrload = {10, 254, 20, 2, 40, 4, 80, 6,
        160, 8, 60, 10, 14, 12, 26, 14, 12, 16, 24, 18, 48, 20, 96, 22,
        192, 24, 72, 26, 16, 28, 32, 30};
        static bool[] lenctrHalt = { true, true, true, true };
        //linear counter instance vars
        static int linearctr = 0;
        static int linctrreload = 0;
        static bool linctrflag = false;
        //instance variables for envelope units
        static int[] envelopeValue = { 15, 15, 15, 15 };
        static int[] envelopeCounter = { 0, 0, 0, 0 };
        static int[] envelopePos = { 0, 0, 0, 0 };
        static bool[] envConstVolume = { true, true, true, true };
        static bool[] envelopeStartFlag = { false, false, false, false };
        //instance variables for sweep unit
        static bool[] sweepenable = { false, false }, sweepnegate = { false, false }, sweepsilence = { false, false }, sweepreload = { false, false };
        static int[] sweepperiod = { 15, 15 }, sweepshift = { 0, 0 }, sweeppos = { 0, 0 };
        static int cyclesperframe;



        static int[] initTndLookup()
        {
            int[] lookup = new int[203];
            for (int i = 0; i < 203; ++i)
            {
                lookup[i] = (int)((163.67 / (24329.0 / i + 100)) * 49151);
            }
            return lookup;
        }

        static int[] initSquareLookup()
        {
            //fill square, triangle volume lookup tables
            int[] lookup = new int[31];
            for (int i = 0; i < 31; ++i)
            {
                lookup[i] = (int)((95.52 / (8128.0 / i + 100)) * 49151);
            }
            return lookup;
        }


        static void initAPU()
        {
            dmcperiods = new int[] { 428, 380, 340, 320, 286, 254, 226, 214, 190, 160, 142, 128, 106, 84, 72, 54 };
            noiseperiod = new int[] { 4, 8, 16, 32, 64, 96, 128, 160, 202, 254, 380, 508, 762, 1016, 2034, 4068 };
            framectrreload = 7456;
            cyclespersample = 1789773.0 / samplerate;
            cyclesperframe = 29781;
        }

        static int[,] DUTYLOOKUP = new int[,] { { 0, 1, 0, 0, 0, 0, 0, 0 }, { 0, 1, 1, 0, 0, 0, 0, 0 }, { 0, 1, 1, 1, 1, 0, 0, 0 }, { 1, 0, 0, 1, 1, 1, 1, 1 } };


        static  int getOutputLevel()
        {
            int vol = 0;
            /*vol = SQUARELOOKUP[volume[0] * timers[0].getval()
                    + volume[1] * timers[1].getval()];
            vol += TNDLOOKUP[3 * timers[2].getval()
                    + 2 * volume[3] * timers[3].getval()
                    + dmcvalue];
            if (!expnSound.isEmpty())
            {
                vol *= 0.8;
                for (ExpansionSoundChip c : expnSound)
                {
                    vol += c.getval();
                }
            }*/
            return vol; //as usual, lack of unsigned types causes unending pain.
        }

        static int highpass_filter(int sample)
        {
            //for killing the dc in the signal
            sample += dckiller;
            dckiller -= sample >> 8;//the actual high pass part
            dckiller += (sample > 0 ? -1 : 1);//guarantees the signal decays to exactly zero
            return sample;
        }

        static int lowpass_filter(int sample)
        {
            sample += lpaccum;
            lpaccum -= (int)( sample * 0.9); // !!
            return lpaccum;
        }


        static void clockframecounter()
        {
            //System.err.println("frame ctr clock " + framectr + ' ' + cpu.cycles);
            //should be ~4x a frame, 240 Hz
            //but the problem is this isn't exactly related to the video signal,
            //it's a completely separate timer, so the phase can shift in relation to the
            //video signal. also in the current implementation APU interrupts can only be fired when
            //an APU register is written/read from, or @ end of frame. So both of those need work
            if ((ctrmode == 4)
                    || (ctrmode == 5 && (framectr != 3)))
            {
                setenvelope();
                setlinctr();
            }
            if ((ctrmode == 4 && (framectr == 1 || framectr == 3))
                    || (ctrmode == 5 && (framectr == 1 || framectr == 4)))
            {
                setlength();
                setsweep();
            }
            if (!apuintflag && (framectr == 3) && (ctrmode == 4) && !statusframeint)
            {
                //!!++cpu.interrupt;
                //System.err.println("frame interrupt set at " + cpu.cycles);
                statusframeint = true;

            }
            ++framectr;
            framectr %= ctrmode;
            setvolumes();
        }

        static void setvolumes()
        {
            volume[0] = ((lengthctr[0] <= 0 || sweepsilence[0]) ? 0 : (((envConstVolume[0]) ? envelopeValue[0] : envelopeCounter[0])));
            volume[1] = ((lengthctr[1] <= 0 || sweepsilence[1]) ? 0 : (((envConstVolume[1]) ? envelopeValue[1] : envelopeCounter[1])));
            volume[3] = ((lengthctr[3] <= 0) ? 0 : ((envConstVolume[3]) ? envelopeValue[3] : envelopeCounter[3]));
            //System.err.println("setvolumes " + volume[1]);
        }


        static  void clockdmc()
        {
            if (dmcBufferEmpty && dmcsamplesleft > 0)
            {
                dmcfillbuffer();
            }
            dmcpos = (dmcpos + 1) % dmcrate;
            if (dmcpos == 0)
            {
                if (dmcbitsleft <= 0)
                {
                    dmcbitsleft = 8;
                    if (dmcBufferEmpty)
                    {
                        dmcsilence = true;
                    }
                    else
                    {
                        dmcsilence = false;
                        dmcshiftregister = dmcbuffer;
                        dmcBufferEmpty = true;
                    }
                }
                if (!dmcsilence)
                {
                    //!!dmcvalue += (((dmcshiftregister & (utils.BIT0)) != 0) ? 2 : -2);
                    //DMC output register doesn't wrap around
                    if (dmcvalue > 0x7f)
                    {
                        dmcvalue = 0x7f;
                    }
                    if (dmcvalue < 0)
                    {
                        dmcvalue = 0;
                    }
                    dmcshiftregister >>= 1;
                    --dmcbitsleft;

                }
            }
        }

        static void dmcfillbuffer()
        {
            if (dmcsamplesleft > 0)
            {
                dmcbuffer = 0; // !! cpuram.read(dmcaddr++);
                dmcBufferEmpty = false;
                // !!cpu.stealcycles(4);
                //DPCM Does steal cpu cycles - this should actually vary between 1-4
                //can't do this properly without a cycle accurate cpu/ppu
                if (dmcaddr > 0xffff)
                {
                    dmcaddr = 0x8000;
                }
                --dmcsamplesleft;
                if (dmcsamplesleft == 0)
                {
                    if (dmcloop)
                    {
                        restartdmc();
                    }
                    else if (dmcirq && !statusdmcint)
                    {
                        //this is supposed to fire after we've just READ the
                        //last byte, not when coming back AFTER reading the last byte
                        //and finding that there are no more bytes left to read.
                        //that meant all dmc timing was too long.
                        // !! ++cpu.interrupt;
                        statusdmcint = true;
                        //System.err.println("dmc irq fire");
                    }

                }
            }
            else
            {
                dmcsilence = true;
            }
        }

        static void restartdmc()
        {
            dmcaddr = dmcstartaddr;
            dmcsamplesleft = dmcsamplelength;
            dmcsilence = false;
        }

        static void setlength()
        {
            for (int i = 0; i < 4; ++i)
            {
                if (!lenctrHalt[i] && lengthctr[i] > 0)
                {
                    --lengthctr[i];
                    if (lengthctr[i] == 0)
                    {
                        setvolumes();
                    }
                }
            }
        }

        static void setlinctr()
        {
            if (linctrflag)
            {
                linearctr = linctrreload;
            }
            else if (linearctr > 0)
            {
                --linearctr;
            }
            if (!lenctrHalt[2])
            {
                linctrflag = false;
            }
        }

        static void setenvelope()
        {
            //System.err.println("envelope");
            for (int i = 0; i < 4; ++i)
            {
                if (envelopeStartFlag[i])
                {
                    envelopeStartFlag[i] = false;
                    envelopePos[i] = envelopeValue[i] + 1;
                    envelopeCounter[i] = 15;
                }
                else
                {
                    --envelopePos[i];
                }
                if (envelopePos[i] <= 0)
                {
                    envelopePos[i] = envelopeValue[i] + 1;
                    if (envelopeCounter[i] > 0)
                    {
                        --envelopeCounter[i];
                    }
                    else if (lenctrHalt[i] && envelopeCounter[i] <= 0)
                    {
                        envelopeCounter[i] = 15;
                    }
                }
            }
        }




        static void setsweep()
        {
            //System.err.println("sweep");
            for (int i = 0; i < 2; ++i)
            {
                sweepsilence[i] = false;
                if (sweepreload[i])
                {
                    sweepreload[i] = false;
                    sweeppos[i] = sweepperiod[i];
                }
                ++sweeppos[i];
                int rawperiod = 0; // !! (timers[i].getperiod() >> 1);
                int shiftedperiod = (rawperiod >> sweepshift[i]);
                if (sweepnegate[i])
                {
                    //invert bits of period
                    //add 1 on second channel only
                    shiftedperiod = -shiftedperiod + i;
                }
                shiftedperiod += rawperiod;
                if ((rawperiod < 8) || shiftedperiod > 0x7ff)
                {
                    // silence channel
                    sweepsilence[i] = true;
                }
                else if (sweepenable[i] && (sweepshift[i] != 0) && lengthctr[i] > 0
                      && sweeppos[i] > sweepperiod[i])
                {
                    sweeppos[i] = 0;
                    // !! timers[i].setperiod(shiftedperiod << 1);
                }
            }
        }






        //-----------------------

        static void apu_step()
        {
            //unimpl
        }

        static void apu_4000(byte val)
        {

        }
        static void apu_4001(byte val)
        {
        }
        static void apu_4002(byte val)
        {
        }
        static void apu_4003(byte val)
        {
        }
        static void apu_4004(byte val)
        {
        }
        static void apu_4005(byte val)
        {
        }
        static void apu_4006(byte val)
        {
        }
        static void apu_4007(byte val)
        {
        }
        static void apu_4008(byte val)
        {
        }
        static void apu_4009(byte val)
        {
        }
        static void apu_400a(byte val)
        {
        }
        static void apu_400b(byte val)
        {
        }
        static void apu_400c(byte val)
        {
        }
        static void apu_400e(byte val)
        {
        }
        static void apu_400f(byte val)
        {
        }
        static void apu_4010(byte val)
        {
        }
        static void apu_4011(byte val)
        {
        }
        static void apu_4012(byte val)
        {
        }
        static void apu_4013(byte val)
        {
        }
        static void apu_4015(byte val)
        {
        }
    }
}