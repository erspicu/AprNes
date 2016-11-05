using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace AprNes
{
    public partial class NesCore
    {
        private enum Flag_Status
        {
            clear = 0,
            set = 1,
        }

        //table port from  https://github.com/bfirsh/jsnes/blob/master/source/cpu.js
        byte[] cycle_table = new byte[]{
    /*0x00*/ 7,6,2,8,3,3,5,5,3,2,2,2,4,4,6,6,
    /*0x10*/ 2,5,2,8,4,4,6,6,2,4,2,7,4,4,7,7,
    /*0x20*/ 6,6,2,8,3,3,5,5,4,2,2,2,4,4,6,6,
    /*0x30*/ 2,5,2,8,4,4,6,6,2,4,2,7,4,4,7,7,
    /*0x40*/ 6,6,2,8,3,3,5,5,3,2,2,2,3,4,6,6,
    /*0x50*/ 2,5,2,8,4,4,6,6,2,4,2,7,4,4,7,7,
    /*0x60*/ 6,6,2,8,3,3,5,5,4,2,2,2,5,4,6,6,
    /*0x70*/ 2,5,2,8,4,4,6,6,2,4,2,7,4,4,7,7,
    /*0x80*/ 2,6,2,6,3,3,3,3,2,2,2,2,4,4,4,4,
    /*0x90*/ 2,6,2,6,4,4,4,4,2,5,2,5,5,5,5,5,
    /*0xA0*/ 2,6,2,6,3,3,3,3,2,2,2,2,4,4,4,4,
    /*0xB0*/ 2,5,2,5,4,4,4,4,2,4,2,4,4,4,4,4,
    /*0xC0*/ 2,6,2,8,3,3,5,5,2,2,2,2,4,4,6,6,
    /*0xD0*/ 2,5,2,8,4,4,6,6,2,4,2,7,4,4,7,7,
    /*0xE0*/ 2,6,3,8,3,3,5,5,2,2,2,2,4,4,6,6,
    /*0xF0*/ 2,5,2,8,4,4,6,6,2,4,2,7,4,4,7,7
    };

        Flag_Status flagN = Flag_Status.clear, flagV = Flag_Status.clear, flagB = Flag_Status.clear, flagD = Flag_Status.clear,
            flagI = Flag_Status.clear, flagZ = Flag_Status.clear, flagC = Flag_Status.clear;

        byte r_A = 0, r_X = 0, r_Y = 0, r_SP = 0xFD;
        ushort r_PC = 0;

        byte opcode;

        int cpu_cycles = 0;

        public bool exit = false;

        public byte GetFlag()
        {
            return (byte)(((((byte)flagN) << 7) | (((byte)flagV) << 6) | (0 << 5) | (((byte)flagB) << 4) |
                               (((byte)flagD) << 3) | (((byte)flagI) << 2) | (((byte)flagZ) << 1) | (byte)flagC | 0x30));
        }

        public void PushStack(byte val)
        {
            Mem_w((ushort)(0x100 + r_SP), val);
            r_SP--;
        }

        public void NMIInterrupt()
        {

            PushStack((byte)(r_PC >> 8));
            PushStack((byte)r_PC);
            PushStack(GetFlag());
            r_PC = (ushort)(Mem_r(0xfffa) | (Mem_r(0xfffb) << 8));
        }

        private void cpu_step()
        {

            if (NMI_set)
            {
                NMIInterrupt();
                NMI_set = false;
            }

            if (dma_cost > 0)
            {
                dma_cost--;
                cpu_cycles = 1;
                return;
            }

            opcode = Mem_r(r_PC);
            cpu_cycles = cycle_table[opcode];
            r_PC++;

            //參考了 mynes 去修正與debug許多錯誤 http://sourceforge.net/projects/mynes 
            switch (opcode)
            {
                case 0x69: //ADC  Immediate  fix
                    {
                        byte t2 = Mem_r(r_PC++);
                        int t1 = t2 + r_A + (byte)flagC;
                        if ((t1 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if (t1 > 0xff) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (((t1 ^ r_A) & (t1 ^ t2) & 0x80) != 0) flagV = Flag_Status.set; else flagV = Flag_Status.clear;
                        r_A = (byte)t1;
                    }
                    break;

                case 0x65: //ADC  Zero Page  
                    {
                        byte t2 = Mem_r(Mem_r(r_PC++));
                        int t1 = t2 + r_A + (byte)flagC;
                        if ((t1 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if (t1 > 0xff) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (((t1 ^ r_A) & (t1 ^ t2) & 0x80) != 0) flagV = Flag_Status.set; else flagV = Flag_Status.clear;
                        r_A = (byte)t1;
                    }
                    break;

                case 0x75://ADC Zero Page,X 
                    {
                        byte t2 = Mem_r((byte)(Mem_r(r_PC++) + r_X));
                        int t1 = t2 + r_A + (byte)flagC;
                        if ((t1 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if (t1 > 0xff) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (((t1 ^ r_A) & (t1 ^ t2) & 0x80) != 0) flagV = Flag_Status.set; else flagV = Flag_Status.clear;
                        r_A = (byte)t1;
                    }
                    break;

                case 0x6D: //ADC Absolute //fix
                    {
                        byte t2 = Mem_r((ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)));
                        int t1 = t2 + r_A + (byte)flagC;
                        if ((t1 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if (t1 > 0xff) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (((t1 ^ r_A) & (t1 ^ t2) & 0x80) != 0) flagV = Flag_Status.set; else flagV = Flag_Status.clear;
                        r_A = (byte)t1;
                    }
                    break;

                case 0x7D: //ADC  Absolute,X 
                    {
                        ushort t4 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                        ushort t3 = (ushort)(t4 + r_X);
                        byte t2 = Mem_r(t3);
                        int t1 = t2 + r_A + (byte)flagC;
                        if ((t1 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if (t1 > 0xff) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (((t1 ^ r_A) & (t1 ^ t2) & 0x80) != 0) flagV = Flag_Status.set; else flagV = Flag_Status.clear;
                        r_A = (byte)t1;
                        if ((t4 & 0xff00) != (t3 & 0xff00)) cpu_cycles++;
                    }
                    break;

                case 0x79: //ADC  Absolute,Y fix
                    {
                        ushort t4 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                        ushort t3 = (ushort)(t4 + r_Y);
                        byte t2 = Mem_r(t3);
                        int t1 = t2 + r_A + (byte)flagC;
                        if ((t1 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if (t1 > 0xff) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (((t1 ^ r_A) & (t1 ^ t2) & 0x80) != 0) flagV = Flag_Status.set; else flagV = Flag_Status.clear;
                        r_A = (byte)t1;
                        if ((t4 & 0xff00) != (t3 & 0xff00)) cpu_cycles++;
                    }
                    break;

                case 0x61: //ADC (Indirect,X) 
                    {
                        byte t4 = (byte)(Mem_r(r_PC++) + r_X);
                        byte a1 = Mem_r(t4++);
                        byte a2 = Mem_r(t4);
                        ushort t3 = (ushort)((a2 << 8) | a1);
                        byte t2 = Mem_r(t3);
                        int t1 = t2 + r_A + (byte)flagC;
                        if ((t1 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if (t1 > 0xff) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (((t1 ^ r_A) & (t1 ^ t2) & 0x80) != 0) flagV = Flag_Status.set; else flagV = Flag_Status.clear;
                        r_A = (byte)t1;
                    }
                    break;

                case 0x71: //ADC (Indirect),Y
                    {
                        byte t4 = Mem_r(r_PC++);
                        byte a1 = Mem_r(t4++);
                        byte a2 = Mem_r(t4);
                        ushort t3 = (ushort)((a2 << 8) | a1);
                        byte t2 = Mem_r((ushort)(t3 + r_Y));
                        int t1 = t2 + r_A + (byte)flagC;
                        if ((t1 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if (t1 > 0xff) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (((t1 ^ r_A) & (t1 ^ t2) & 0x80) != 0) flagV = Flag_Status.set; else flagV = Flag_Status.clear;
                        r_A = (byte)t1;
                        if ((t3 & 0xff00) != ((t3 + r_Y) & 0xff00)) cpu_cycles++;
                    }
                    break;

                //--- AND BEGIN
                case 0x29: //AND  Immediate  
                    {
                        byte t2 = Mem_r(r_PC++);
                        int t1 = t2 & r_A;
                        if (t1 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        r_A = (byte)t1;
                    }
                    break;

                case 0x25: //AND  Zero Page  
                    {
                        byte t2 = Mem_r(Mem_r(r_PC++));
                        int t1 = t2 & r_A;
                        if (t1 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        r_A = (byte)t1;
                    }
                    break;

                case 0x35://AND Zero Page,X 
                    {
                        byte t2 = Mem_r((byte)(Mem_r(r_PC++) + r_X));
                        int t1 = t2 & r_A;
                        if (t1 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        r_A = (byte)t1;
                    }
                    break;

                case 0x2D: //AND Absolute 
                    {
                        ushort t3 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                        byte t2 = Mem_r(t3);
                        int t1 = t2 & r_A;
                        if (t1 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        r_A = (byte)t1;
                    }
                    break;

                case 0x3D: //AND  Absolute,X 
                    {
                        ushort t4 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                        ushort t3 = (ushort)(t4 + r_X);
                        byte t2 = Mem_r(t3);
                        int t1 = t2 & r_A;
                        if (t1 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        r_A = (byte)t1;
                        if ((t4 & 0xff00) != (t3 & 0xff00)) cpu_cycles++;
                    }
                    break;

                case 0x39: //AND  Absolute,Y
                    {
                        ushort t4 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                        ushort t3 = (ushort)(t4 + r_Y);
                        byte t2 = Mem_r(t3);
                        int t1 = t2 & r_A;
                        if (t1 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        r_A = (byte)t1;
                        if ((t4 & 0xff00) != (t3 & 0xff00)) cpu_cycles++;
                    }
                    break;

                case 0x21: //AND (Indirect,X) 
                    {
                        byte t4 = (byte)(Mem_r(r_PC++) + r_X);
                        byte a1 = Mem_r(t4++);
                        byte a2 = Mem_r(t4);
                        ushort t3 = (ushort)((a2 << 8) | a1);
                        byte t2 = Mem_r(t3);
                        int t1 = t2 & r_A;
                        if ((t1 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        r_A = (byte)t1;
                    }
                    break;

                case 0x31: //AND (Indirect),Y
                    {

                        byte t4 = Mem_r(r_PC++);
                        byte a1 = Mem_r(t4++);
                        byte a2 = Mem_r(t4);
                        ushort t3 = (ushort)((a2 << 8) | a1);
                        byte t2 = Mem_r((ushort)(t3 + r_Y));
                        int t1 = t2 & r_A;
                        if ((t1 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        r_A = (byte)t1;
                        if ((t3 & 0xff00) != ((t3 + r_Y) & 0xff00)) cpu_cycles++;
                    }
                    break;
                //--- AND END 

                case 0x0A://ASL acc
                    if ((r_A & 0x80) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                    r_A <<= 1;
                    if (r_A == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    if ((r_A & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    break;

                case 0x06://ASL zp
                    {
                        byte t2 = Mem_r(r_PC++);
                        byte t1 = Mem_r(t2);
                        if ((t1 & 0x80) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        t1 <<= 1;
                        if (t1 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        Mem_w(t2, t1);
                    }
                    break;

                case 0x16://ASL zp,x
                    {
                        byte t2 = (byte)((Mem_r(r_PC++) + r_X) & 0xff);
                        byte t1 = Mem_r(t2);
                        if ((t1 & 0x80) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        t1 <<= 1;
                        if (t1 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        Mem_w(t2, t1);
                    }
                    break;

                case 0x0E://ASL abs
                    {
                        ushort t2 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)));
                        byte t1 = Mem_r(t2);
                        if ((t1 & 0x80) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        t1 <<= 1;
                        if (t1 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        Mem_w(t2, t1);
                    }
                    break;

                case 0x1E://ASL abs,x
                    {
                        ushort t2 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_X);
                        byte t1 = Mem_r(t2);
                        if ((t1 & 0x80) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        t1 <<= 1;
                        if (t1 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        Mem_w(t2, t1);
                    }
                    break;

                case 0x90://BCC
                    {
                        sbyte t1 = (sbyte)Mem_r(r_PC++);
                        ushort addr = (ushort)(r_PC + t1);

                        if (flagC == Flag_Status.clear)
                        {
                            if ((addr & 0xff00) != (((r_PC - 2) & 0xff00)))
                                cpu_cycles += 2; //FIX
                            else
                                cpu_cycles += 1; //FIX
                            r_PC = addr;
                        }
                    }
                    break;

                case 0xB0://BCS
                    {
                        sbyte t1 = (sbyte)Mem_r(r_PC++);
                        ushort addr = (ushort)(r_PC + t1);
                        if (flagC == Flag_Status.set)
                        {
                            if ((addr & 0xff00) != (((r_PC - 2) & 0xff00)))
                                cpu_cycles += 2;
                            else
                                cpu_cycles += 1;
                            r_PC = addr;
                        }
                    }
                    break;

                case 0xF0://BEQ
                    {
                        sbyte t1 = (sbyte)Mem_r(r_PC++);
                        ushort addr = (ushort)(r_PC + t1);
                        if (flagZ == Flag_Status.set)
                        {
                            if ((addr & 0xff00) != (((r_PC - 2) & 0xff00)))
                                cpu_cycles += 2;
                            else
                                cpu_cycles += 1;
                            r_PC = addr;
                        }
                    }
                    break;

                case 0x24://BIT zp fix
                    {
                        byte t1 = Mem_r(Mem_r(r_PC++));
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if ((t1 & 0x40) > 0) flagV = Flag_Status.set; else flagV = Flag_Status.clear;
                        if ((t1 & r_A) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    }
                    break;

                case 0x2C://BIT abs //FIX
                    {
                        byte t1 = Mem_r((ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)));
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if ((t1 & 0x40) > 0) flagV = Flag_Status.set; else flagV = Flag_Status.clear;
                        if ((t1 & r_A) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    }
                    break;

                case 0x30://BMI
                    {
                        sbyte t1 = (sbyte)Mem_r(r_PC++);
                        ushort addr = (ushort)(r_PC + t1);
                        if (flagN == Flag_Status.set)
                        {
                            if ((addr & 0xff00) != (((r_PC - 2) & 0xff00)))
                                cpu_cycles += 2;
                            else
                                cpu_cycles += 1;
                            r_PC = addr;
                        }
                    }
                    break;

                case 0xD0://BNE
                    {
                        //cpu_cycles = 2;
                        sbyte t1 = (sbyte)Mem_r(r_PC++);
                        ushort addr = (ushort)(r_PC + t1);
                        if (flagZ == Flag_Status.clear)
                        {
                            if ((addr & 0xff00) != (((r_PC - 2) & 0xff00)))
                                cpu_cycles += 2;
                            else
                                cpu_cycles += 1;
                            r_PC = addr;
                        }
                    }
                    break;

                case 0x10://BPL
                    {
                        sbyte t1 = (sbyte)Mem_r(r_PC++);
                        ushort addr = (ushort)(r_PC + t1);

                        //cpu_cycles = 2;

                        if (flagN == Flag_Status.clear)
                        {
                            if ((addr & 0xff00) != (((r_PC - 2) & 0xff00)))
                                cpu_cycles += 2;
                            else
                                cpu_cycles += 1;
                            r_PC = addr;
                        }
                    }
                    break;

                case 00://BRK
                    {
                        r_PC++;
                        Mem_w((ushort)(r_SP + 0x100), (byte)(r_PC >> 8));
                        r_SP--;
                        Mem_w((ushort)(r_SP + 0x100), (byte)(r_PC & 0xf));
                        r_SP--;
                        flagB = Flag_Status.set;
                        byte t1 = (byte)((((byte)flagN) << 7) | (((byte)flagV) << 6) | (1 << 5) | (((byte)flagB) << 4) |
                           (((byte)flagD) << 3) | (((byte)flagI) << 2) | (((byte)flagZ) << 1) | (byte)flagC);
                        Mem_w((ushort)(r_SP + 0x100), t1);
                        r_SP--;
                        flagI = Flag_Status.clear;
                        r_PC = (ushort)(Mem_r(0xFFFE) | (Mem_r(0xFFFF) << 8));
                    }
                    break;

                case 0x50://BVC
                    {
                        sbyte t1 = (sbyte)Mem_r(r_PC++);
                        ushort addr = (ushort)(r_PC + t1);
                        if (flagV == Flag_Status.clear)
                        {
                            if ((addr & 0xff00) != (((r_PC - 2) & 0xff00)))
                                cpu_cycles += 2;
                            else
                                cpu_cycles += 1;
                            r_PC = addr;
                        }
                    }
                    break;

                case 0x70://BVS
                    {
                        sbyte t1 = (sbyte)Mem_r(r_PC++);
                        ushort addr = (ushort)(r_PC + t1);
                        if (flagV == Flag_Status.set)
                        {
                            if ((addr & 0xff00) != (((r_PC - 2) & 0xff00)))
                                cpu_cycles += 2;
                            else
                                cpu_cycles += 1;
                            r_PC = addr;
                        }
                    }
                    break;

                case 0x18://CLC
                    flagC = Flag_Status.clear;
                    break;

                case 0xD8://CLD
                    flagD = Flag_Status.clear;
                    break;

                case 0x58://CLI
                    flagI = Flag_Status.clear;
                    break;

                case 0xB8://CLV
                    flagV = Flag_Status.clear;
                    break;

                //--- CMP BEGIN
                case 0xC9: //CMP  Immediate  
                    {
                        byte t2 = Mem_r(r_PC++);
                        int t1 = r_A - t2;
                        if (t1 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if (r_A >= t2) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0xC5: //CMP  Zero Page  
                    {
                        byte t2 = Mem_r(Mem_r(r_PC++));
                        int t1 = r_A - t2;
                        if (t1 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if (r_A >= t2) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0xD5://CMP Zero Page,X 
                    {
                        byte t2 = Mem_r((byte)(Mem_r(r_PC++) + r_X));
                        int t1 = r_A - t2;
                        if (t1 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if (r_A >= t2) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0xCD: //CMP Absolute 
                    {
                        ushort t3 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                        byte t2 = Mem_r(t3);
                        int t1 = r_A - t2;
                        if (t1 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if (r_A >= t2) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0xDD: //CMP  Absolute,X 
                    {
                        ushort t4 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                        ushort t3 = (ushort)(t4 + r_X);
                        byte t2 = Mem_r(t3);
                        int t1 = r_A - t2;
                        if (t1 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if (r_A >= t2) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if ((t4 & 0xff00) != (t3 & 0xff00)) cpu_cycles++;
                    }
                    break;

                case 0xD9: //CMP  Absolute,Y
                    {
                        ushort t4 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                        ushort t3 = (ushort)(t4 + r_Y);
                        byte t2 = Mem_r(t3);
                        int t1 = r_A - t2;
                        if (t1 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if (r_A >= t2) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if ((t4 & 0xff00) != (t3 & 0xff00)) cpu_cycles++;
                    }
                    break;

                case 0xC1: //CMP (Indirect,X)  fix
                    {
                        byte t4 = (byte)(Mem_r(r_PC++) + r_X);
                        byte a1 = Mem_r(t4++);
                        byte a2 = Mem_r(t4);
                        ushort t1 = (ushort)((a2 << 8) | a1);
                        byte t2 = Mem_r(t1);
                        int t3 = r_A - t2;
                        if ((t3 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if (r_A >= t2) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t3 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0xD1: //CMP (Indirect),Y
                    {
                        byte t4 = Mem_r(r_PC++);
                        byte a1 = Mem_r(t4++);
                        byte a2 = Mem_r(t4);
                        ushort t1 = (ushort)((a2 << 8) | a1);
                        byte t2 = Mem_r((ushort)(t1 + r_Y));
                        int t3 = r_A - t2;
                        if ((t3 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if (r_A >= t2) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t3 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if ((t1 & 0xff00) != ((t1 + r_Y) & 0xff00)) cpu_cycles++;
                    }
                    break;
                //--- CMP END

                case 0xE0: //CPX  Immediate  
                    {
                        byte t2 = Mem_r(r_PC++);
                        int t1 = r_X - t2;// +(byte)flagC;
                        if ((t1 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if (r_X >= t2) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0xE4: //CPX  Zero Page  
                    {
                        byte t2 = Mem_r(Mem_r(r_PC++));
                        int t1 = r_X - t2;// +(byte)flagC;
                        if ((t1 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if (r_X >= t2) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0xEC: //CPX Absolute 
                    {
                        ushort t3 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                        byte t2 = Mem_r(t3);
                        int t1 = r_X - t2;// +(byte)flagC;
                        if ((t1 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if (r_X >= t2) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                //-- CPY BEGIN
                case 0xC0: //CPY  Immediate  
                    {
                        byte t2 = Mem_r(r_PC++);
                        int t1 = r_Y - t2;// +(byte)flagC;
                        if ((t1 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if (r_Y >= t2) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0xC4: //CPY  Zero Page  
                    {
                        byte t2 = Mem_r(Mem_r(r_PC++));
                        int t1 = r_Y - t2;// +(byte)flagC;
                        if ((t1 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if (r_Y >= t2) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0xCC: //CPY Absolute 
                    {
                        ushort t3 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                        byte t2 = Mem_r(t3);
                        int t1 = r_Y - t2;// +(byte)flagC;
                        if ((t1 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if (r_Y >= t2) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;
                //-- CPY END

                case 0xC6://DEC zp
                    {
                        byte t1 = Mem_r(r_PC++);
                        byte t2 = Mem_r(t1);
                        Mem_w(t1, --t2);
                        if ((t2 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (t2 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    }
                    break;


                case 0xD6://DEC zp,x
                    {
                        byte t1 = (byte)((Mem_r(r_PC++) + r_X) & 0xFF);
                        byte t2 = Mem_r(t1);
                        Mem_w(t1, --t2);
                        if ((t2 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (t2 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    }
                    break;

                case 0xCE://DEC abs
                    {
                        ushort t1 = (ushort)(Mem_r(r_PC++) | Mem_r(r_PC++) << 8);
                        byte t2 = Mem_r(t1);
                        Mem_w(t1, --t2);
                        if ((t2 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (t2 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    }
                    break;

                case 0xDE://DEC abs,x
                    {
                        ushort t1 = (ushort)(((Mem_r(r_PC++) | Mem_r(r_PC++) << 8) + r_X) & 0xFFFF);
                        byte t2 = Mem_r(t1);
                        t2--;
                        Mem_w(t1, t2);
                        if ((t2 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (t2 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    }
                    break;

                case 0xCA://DEX
                    r_X--;
                    if ((r_X & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    if (r_X == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    break;

                case 0x88://DEY //fix
                    r_Y--;
                    if ((r_Y & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    if (r_Y == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    break;

                //--- EOR BEGIN
                case 0x49: //EOR  Immediate  
                    {
                        byte t2 = Mem_r(r_PC++);
                        int t1 = t2 ^ r_A;
                        if (t1 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        r_A = (byte)t1;
                    }
                    break;

                case 0x45: //EOR  Zero Page  
                    {
                        byte t2 = Mem_r(Mem_r(r_PC++));
                        int t1 = t2 ^ r_A;
                        if (t1 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        r_A = (byte)t1;
                    }
                    break;

                case 0x55://EOR Zero Page,X 
                    {
                        byte t2 = Mem_r((byte)(Mem_r(r_PC++) + r_X));
                        int t1 = t2 ^ r_A;
                        if (t1 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        r_A = (byte)t1;

                    }
                    break;

                case 0x4D: //EOR Absolute 
                    {
                        ushort t3 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                        byte t2 = Mem_r(t3);
                        int t1 = t2 ^ r_A;
                        if (t1 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        r_A = (byte)t1;
                    }
                    break;

                case 0x5D: //EOR  Absolute,X 
                    {
                        ushort t4 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                        ushort t3 = (ushort)(t4 + r_X);
                        byte t2 = Mem_r(t3);
                        int t1 = t2 ^ r_A;
                        if (t1 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        r_A = (byte)t1;
                        if ((t4 & 0xff00) != (t3 & 0xff00)) cpu_cycles++;
                    }
                    break;

                case 0x59: //EOR  Absolute,Y
                    {
                        ushort t4 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                        ushort t3 = (ushort)(t4 + r_Y);
                        byte t2 = Mem_r(t3);
                        int t1 = t2 ^ r_A;
                        if (t1 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        r_A = (byte)t1;
                        if ((t4 & 0xff00) != (t3 & 0xff00)) cpu_cycles++;
                    }
                    break;

                case 0x41: //EOR (Indirect,X) 
                    {
                        byte t4 = (byte)(Mem_r(r_PC++) + r_X);
                        byte a1 = Mem_r(t4++);
                        byte a2 = Mem_r(t4);
                        ushort t3 = (ushort)((a2 << 8) | a1);
                        byte t2 = Mem_r(t3);
                        int t1 = t2 ^ r_A;// +(byte)flagC;
                        if ((t1 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        r_A = (byte)t1;
                    }
                    break;

                case 0x51: //EOR (Indirect),Y
                    {
                        byte t4 = Mem_r(r_PC++);
                        byte a1 = Mem_r(t4++);
                        byte a2 = Mem_r(t4);
                        ushort t3 = (ushort)((a2 << 8) | a1);
                        byte t2 = Mem_r((ushort)(t3 + r_Y));
                        int t1 = t2 ^ r_A;// +(byte)flagC;
                        if ((t1 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        r_A = (byte)t1;
                        if ((t3 & 0xff00) != ((t3 + r_Y) & 0xff00)) cpu_cycles++;
                    }
                    break;
                //--- EOR END    

                case 0xE6://INC zp
                    {
                        byte t1 = Mem_r(r_PC++);
                        byte t2 = Mem_r(t1);
                        Mem_w(t1, ++t2);
                        if ((t2 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (t2 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    }
                    break;

                case 0xF6://INC zp,x
                    {
                        byte t1 = (byte)((Mem_r(r_PC++) + r_X) & 0xFF);
                        byte t2 = Mem_r(t1);
                        Mem_w(t1, ++t2);
                        if ((t2 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (t2 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    }
                    break;

                case 0xEE://INC abs
                    {
                        ushort t1 = (ushort)(Mem_r(r_PC++) | Mem_r(r_PC++) << 8);
                        byte t2 = Mem_r(t1);
                        Mem_w(t1, ++t2);
                        if ((t2 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (t2 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    }
                    break;

                case 0xFE://INC abs,x
                    {
                        ushort t1 = (ushort)(((Mem_r(r_PC++) | Mem_r(r_PC++) << 8) + r_X) & 0xFFFF);
                        byte t2 = Mem_r(t1);
                        Mem_w(t1, ++t2);
                        if ((t2 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (t2 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    }
                    break;

                case 0xE8://INX
                    r_X++;
                    if ((r_X & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    if (r_X == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    break;

                case 0xC8://INY
                    r_Y++;
                    if ((r_Y & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    if (r_Y == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    break;

                case 0x4C://JMP abs                    
                    r_PC = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    break;

                case 0x6C://JMP indirect
                    {
                        byte t1_l = Mem_r(r_PC++);
                        byte t1_h = Mem_r(r_PC++);
                        ushort t1 = (ushort)(t1_l | (t1_h << 8));
                        byte t2_l = Mem_r(t1);
                        t1_l++;
                        byte t2_h = Mem_r((ushort)(t1_l | (t1_h << 8)));
                        r_PC = (ushort)(t2_l | (t2_h << 8));
                    }
                    break;

                case 0x20://JSR abs
                    {
                        ushort t1 = (ushort)(r_PC + 1);
                        byte t2 = (byte)(t1 >> 8);
                        Mem_w((ushort)(r_SP + 0x100), t2);
                        r_SP--;
                        byte t3 = (byte)(t1 & 0xFF);
                        Mem_w((ushort)(r_SP + 0x100), t3);
                        r_SP--;
                        r_PC = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    }
                    break;

                case 0xA9://LDA imm
                    r_A = Mem_r(r_PC++);
                    if ((r_A & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    if (r_A == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    break;

                case 0xA5://LDA zp
                    r_A = Mem_r(Mem_r(r_PC++));
                    if ((r_A & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    if (r_A == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    break;

                case 0xB5://LDA zp,x
                    r_A = Mem_r((ushort)((Mem_r(r_PC++) + r_X) & 0xFF));
                    if ((r_A & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    if (r_A == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    break
                        ;
                case 0xAD://LDA abs
                    r_A = Mem_r((ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)));
                    if ((r_A & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    if (r_A == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    break;

                case 0xBD://LDA abs,x
                    {
                        ushort t1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                        r_A = Mem_r((ushort)(t1 + r_X));
                        if ((r_A & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (r_A == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t1 & 0xff00) != ((t1 + r_X) & 0xff00)) cpu_cycles++;
                    }
                    break;

                case 0xB9://LDA abs,y
                    {
                        ushort t1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                        r_A = Mem_r((ushort)(t1 + r_Y));
                        if ((r_A & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (r_A == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t1 & 0xff00) != ((t1 + r_Y) & 0xff00)) cpu_cycles++;
                    }
                    break;

                case 0xA1://LDA (indirect,x)
                    {
                        byte t4 = (byte)(Mem_r(r_PC++) + r_X);
                        byte a1 = Mem_r(t4++);
                        byte a2 = Mem_r(t4);
                        ushort t1 = (ushort)((a2 << 8) | a1);
                        r_A = Mem_r(t1);
                        if ((r_A & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (r_A == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    }
                    break;

                case 0xB1://LDA (indirect),y
                    {
                        byte t4 = Mem_r(r_PC++);
                        byte a1 = Mem_r(t4++);
                        byte a2 = Mem_r(t4);
                        ushort t1 = (ushort)((a2 << 8) | a1);
                        r_A = Mem_r((ushort)(t1 + r_Y));
                        if ((r_A & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (r_A == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t1 & 0xff00) != ((t1 + r_Y) & 0xff00)) cpu_cycles++;
                    }
                    break;

                case 0xA2://LDX imm
                    r_X = Mem_r(r_PC++);
                    if ((r_X & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    if (r_X == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    break;

                case 0xA6://LDX zp
                    r_X = Mem_r(Mem_r(r_PC++));
                    if ((r_X & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    if (r_X == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    break;

                case 0xB6://LDX zp,y
                    r_X = Mem_r((ushort)((Mem_r(r_PC++) + r_Y) & 0xFF));
                    if ((r_X & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    if (r_X == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    break;

                case 0xAE://LDX abs
                    r_X = Mem_r((ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)));
                    if ((r_X & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    if (r_X == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    break;

                case 0xBE://LDX abs,y
                    {
                        ushort t1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                        r_X = Mem_r((ushort)(t1 + r_Y));
                        if ((r_X & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (r_X == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t1 & 0xff00) != ((t1 + r_Y) & 0xff00)) cpu_cycles++;
                    }
                    break;

                case 0xA0://LDY imm
                    r_Y = Mem_r(r_PC++);
                    if ((r_Y & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    if (r_Y == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    break;

                case 0xA4://LDY zp
                    r_Y = Mem_r(Mem_r(r_PC++));
                    if ((r_Y & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    if (r_Y == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    break;

                case 0xB4://LDY zp,x
                    r_Y = Mem_r((ushort)((Mem_r(r_PC++) + r_X) & 0xFF));
                    if ((r_Y & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    if (r_Y == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    break;

                case 0xAC://LDY abs
                    r_Y = Mem_r((ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)));
                    if ((r_Y & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    if (r_Y == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    break;

                case 0xBC://LDY abs,x
                    {
                        ushort t1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                        r_Y = Mem_r((ushort)(t1 + r_X));
                        if ((r_Y & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (r_Y == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t1 & 0xff00) != ((t1 + r_X) & 0xff00)) cpu_cycles++;
                    }
                    break;

                //----- LSR begin
                case 0x4A://LSR acc
                    if ((r_A & 0x01) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                    r_A >>= 1;
                    if ((r_A & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    if (r_A == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    break;

                case 0x46://LSR zp fix
                    {
                        byte t2 = Mem_r(r_PC++);
                        byte t1 = Mem_r(t2);
                        if ((t1 & 1) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        t1 >>= 1;
                        if ((t1 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        Mem_w(t2, t1);
                    }
                    break;

                case 0x56://LSR zp,x
                    {
                        byte t2 = (byte)((Mem_r(r_PC++) + r_X) & 0xff);
                        byte t1 = Mem_r(t2);
                        if ((t1 & 1) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        t1 >>= 1;
                        if ((t1 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        Mem_w(t2, t1);
                    }
                    break;

                case 0x4E://LSR abs fix
                    {
                        ushort t2 = (ushort)(((Mem_r(r_PC++) << 0) | (Mem_r(r_PC++) << 8)));
                        byte t1 = Mem_r(t2);
                        if ((t1 & 1) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        t1 >>= 1;
                        if ((t1 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        Mem_w(t2, t1);
                    }
                    break;

                case 0x5E://LSR abs,x
                    {
                        ushort t2 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_X);
                        byte t1 = Mem_r(t2);
                        if ((t1 & 1) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        t1 >>= 1;
                        if ((t1 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        Mem_w(t2, t1);
                        Mem_w(t2, t1);
                    }
                    break;
                //---- LSR END

                case 0xEA://NOP
                    break;

                //--- ORA BEGIN
                case 0x09: //ORA  Immediate  
                    {
                        byte t2 = Mem_r(r_PC++);
                        int t1 = t2 | r_A;
                        if (t1 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        r_A = (byte)t1;
                    }
                    break;

                case 0x05: //ORA  Zero Page  
                    {
                        byte t2 = Mem_r(Mem_r(r_PC++));
                        int t1 = t2 | r_A;
                        if (t1 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        r_A = (byte)t1;
                    }
                    break;

                case 0x15://ORA Zero Page,X 
                    {
                        byte t2 = Mem_r((byte)(Mem_r(r_PC++) + r_X));
                        int t1 = t2 | r_A;
                        if (t1 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        r_A = (byte)t1;
                    }
                    break;

                case 0x0D: //ORA Absolute 
                    {
                        ushort t3 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                        byte t2 = Mem_r(t3);
                        int t1 = t2 | r_A;
                        if (t1 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        r_A = (byte)t1;
                    }
                    break;

                case 0x1D: //ORA  Absolute,X  fix
                    {
                        ushort t4 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                        ushort t3 = (ushort)(t4 + r_X);
                        byte t2 = Mem_r(t3);
                        int t1 = t2 | r_A;
                        if (t1 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        r_A = (byte)t1;
                        if ((t4 & 0xff00) != (t3 & 0xff00)) cpu_cycles++;
                    }
                    break;

                case 0x19: //ORA  Absolute,Y
                    {
                        ushort t4 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                        ushort t3 = (ushort)(t4 + r_Y);
                        byte t2 = Mem_r(t3);
                        int t1 = t2 | r_A;
                        if (t1 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        r_A = (byte)t1;
                        if ((t4 & 0xff00) != (t3 & 0xff00)) cpu_cycles++;
                    }
                    break;

                case 0x01: //ORA (Indirect,X) 
                    {
                        byte t4 = (byte)(Mem_r(r_PC++) + r_X);
                        byte a1 = Mem_r(t4++);
                        byte a2 = Mem_r(t4);
                        ushort t3 = (ushort)((a2 << 8) | a1);
                        byte t2 = Mem_r(t3);
                        int t1 = t2 | r_A;// +(byte)flagC;
                        if (t1 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        r_A = (byte)t1;
                    }
                    break;

                case 0x11: //ORA (Indirect),Y
                    {
                        byte t4 = Mem_r(r_PC++);
                        byte a1 = Mem_r(t4++);
                        byte a2 = Mem_r(t4);
                        ushort t3 = (ushort)((a2 << 8) | a1);
                        byte t2 = Mem_r((ushort)(t3 + r_Y));
                        int t1 = t2 | r_A;// +(byte)flagC;
                        if (t1 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        r_A = (byte)t1;
                        if ((t3 & 0xff00) != ((t3 + r_Y) & 0xff00)) cpu_cycles++;

                    }
                    break;
                //--- ORA END    

                case 0x48://PHA
                    Mem_w((ushort)(r_SP + 0x100), r_A);
                    r_SP--;
                    break;

                case 0x08://PHP
                    {
                        byte t1 = (byte)((((byte)flagN) << 7) | (((byte)flagV) << 6) | (1 << 5) | (((byte)flagB) << 4) |
                           (((byte)flagD) << 3) | (((byte)flagI) << 2) | (((byte)flagZ) << 1) | (byte)flagC | 0x30); // fix
                        Mem_w((ushort)(r_SP + 0x100), t1);
                        r_SP--;
                    }
                    break;

                case 0x68://PLA
                    {
                        r_SP++;
                        r_A = Mem_r((ushort)(r_SP + 0x100));
                        if (r_A == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((r_A & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0x28://PLP
                    {
                        r_SP++;
                        byte t1 = Mem_r((ushort)(r_SP + 0x100));
                        flagN = (Flag_Status)((t1 & 0x80) >> 7);
                        flagV = (Flag_Status)((t1 & 0x40) >> 6);
                        flagB = (Flag_Status)((t1 & 0x10) >> 4);
                        flagD = (Flag_Status)((t1 & 0x8) >> 3);
                        flagI = (Flag_Status)((t1 & 0x4) >> 2);
                        flagZ = (Flag_Status)((t1 & 0x2) >> 1);
                        flagC = (Flag_Status)(t1 & 0x1);
                    }
                    break;

                //----ROL begin
                case 0x2A://ROL acc //fix
                    {
                        ushort t1 = (ushort)(r_A << 1);
                        if (flagC == Flag_Status.set) t1 |= 0x1;
                        if ((r_A & 0x80) != 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if ((t1 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        r_A = (byte)t1;
                    }
                    break;

                case 0x26://ROL zp //fix
                    {
                        byte t3 = Mem_r(r_PC++);
                        byte t2 = Mem_r(t3);
                        ushort t1 = (ushort)(t2 << 1);
                        if (flagC == Flag_Status.set) t1 |= 0x1;
                        if ((t2 & 0x80) != 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if ((t1 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        Mem_w((ushort)(t3 & 0xff), (byte)t1); //!!!!!
                    }
                    break;

                case 0x36://ROL zp,x
                    {
                        byte t3 = (byte)(Mem_r(r_PC++) + r_X);
                        byte t2 = Mem_r(t3);
                        ushort t1 = (ushort)(t2 << 1);
                        if (flagC == Flag_Status.set) t1 |= 0x1;
                        if ((t2 & 0x80) != 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if ((t1 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        Mem_w((ushort)(t3 & 0xff), (byte)t1); //!!!!!
                    }
                    break;

                case 0x2E://ROL abs fix
                    {
                        ushort t3 = (ushort)((Mem_r(r_PC++) | Mem_r(r_PC++) << 8));
                        byte t2 = Mem_r(t3);
                        ushort t1 = (ushort)(t2 << 1);
                        if (flagC == Flag_Status.set) t1 |= 0x1;
                        if ((t2 & 0x80) != 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if ((t1 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        Mem_w(t3, (byte)t1); //!!!!!
                    }
                    break;

                case 0x3E://ROL abs,x fix
                    {
                        ushort t3 = (ushort)((Mem_r(r_PC++) | Mem_r(r_PC++) << 8) + r_X);
                        byte t2 = Mem_r(t3);
                        ushort t1 = (ushort)(t2 << 1);
                        if (flagC == Flag_Status.set) t1 |= 0x1;
                        if ((t2 & 0x80) != 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if ((t1 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        Mem_w(t3, (byte)t1);
                    }
                    break;
                //----ROL end

                //---- ROR begin
                case 0x6A://ROR acc
                    {
                        ushort t1 = r_A;
                        if (flagC == Flag_Status.set) t1 |= 0x100;
                        if ((t1 & 0x01) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        t1 >>= 1;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (t1 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        r_A = (byte)t1;
                    }
                    break;

                case 0x66://ROR zp
                    {
                        byte t2 = Mem_r(r_PC++);
                        ushort t1 = Mem_r(t2);
                        if (flagC == Flag_Status.set) t1 |= 0x100;
                        if ((t1 & 0x01) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        t1 >>= 1;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (t1 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        t1 = (byte)(t1 & 0xff);
                        Mem_w(t2, (byte)t1);
                    }
                    break;

                case 0x76://ROR zp,x
                    {
                        byte t2 = (byte)(Mem_r(r_PC++) + r_X);
                        ushort t1 = Mem_r(t2);
                        if (flagC == Flag_Status.set) t1 |= 0x100;
                        if ((t1 & 0x01) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        t1 >>= 1;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (t1 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        t1 = (byte)(t1 & 0xff);
                        Mem_w(t2, (byte)t1);
                    }
                    break;

                case 0x6E://ROR abs
                    {
                        ushort t2 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                        ushort t1 = Mem_r(t2);
                        if (flagC == Flag_Status.set) t1 |= 0x100;
                        if ((t1 & 0x01) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        t1 >>= 1;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (t1 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        t1 = (byte)(t1 & 0xff);
                        Mem_w(t2, (byte)t1);
                    }
                    break;

                case 0x7E://ROR abs,x
                    {
                        ushort t2 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_X);
                        ushort t1 = Mem_r(t2);
                        if (flagC == Flag_Status.set) t1 |= 0x100;
                        if ((t1 & 0x01) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        t1 >>= 1;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (t1 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        t1 = (byte)(t1 & 0xff);
                        Mem_w(t2, (byte)t1);
                    }
                    break;
                // ----ROR end

                case 0x40://RTI
                    {
                        r_SP++;
                        byte t1 = Mem_r((ushort)(r_SP + 0x100));
                        flagN = (Flag_Status)((t1 & 0x80) >> 7);
                        flagV = (Flag_Status)((t1 & 0x40) >> 6);
                        flagB = (Flag_Status)((t1 & 0x10) >> 4);
                        flagD = (Flag_Status)((t1 & 0x8) >> 3);
                        flagI = (Flag_Status)((t1 & 0x4) >> 2);
                        flagZ = (Flag_Status)((t1 & 0x2) >> 1);
                        flagC = (Flag_Status)(t1 & 0x1);
                        r_SP++;
                        byte t2 = Mem_r((ushort)(r_SP + 0x100));
                        r_SP++;
                        byte t3 = Mem_r((ushort)(r_SP + 0x100));
                        r_PC = (ushort)(t2 | (t3 << 8));
                    }
                    break;

                case 0x60://RTS
                    {
                        r_SP++;
                        byte t2 = Mem_r((ushort)(r_SP + 0x100));
                        r_SP++;
                        byte t3 = Mem_r((ushort)(r_SP + 0x100));
                        r_PC = (ushort)(t2 | (t3 << 8));
                        r_PC++;
                    }
                    break;

                //--- SBC BEGIN
                case 0xE9: //SBC  Immediate  
                    {
                        byte t2 = Mem_r(r_PC++);
                        t2 ^= 0xFF; //fix
                        int t1 = r_A + t2 + (byte)flagC;
                        if ((t1 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if (t1 > 0xff) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (((t1 ^ r_A) & (t1 ^ t2) & 0x80) != 0) flagV = Flag_Status.set; else flagV = Flag_Status.clear;
                        r_A = (byte)t1;
                    }
                    break;

                case 0xE5: //SBC  Zero Page  
                    {
                        byte t2 = Mem_r(Mem_r(r_PC++));
                        t2 ^= 0xFF; //fix
                        int t1 = r_A + t2 + (byte)flagC;
                        if ((t1 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if (t1 > 0xff) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (((t1 ^ r_A) & (t1 ^ t2) & 0x80) != 0) flagV = Flag_Status.set; else flagV = Flag_Status.clear;
                        r_A = (byte)t1;
                    }
                    break;

                case 0xF5://SBC Zero Page,X 
                    {
                        byte t2 = Mem_r((byte)(Mem_r(r_PC++) + r_X));
                        t2 ^= 0xFF; //fix
                        int t1 = r_A + t2 + (byte)flagC;
                        if ((t1 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if (t1 > 0xff) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (((t1 ^ r_A) & (t1 ^ t2) & 0x80) != 0) flagV = Flag_Status.set; else flagV = Flag_Status.clear;
                        r_A = (byte)t1;
                    }
                    break;

                case 0xED: //SBC Absolute fix
                    {
                        ushort t3 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                        byte t2 = Mem_r(t3);
                        t2 ^= 0xFF; //fix
                        int t1 = r_A + t2 + (byte)flagC;
                        if ((t1 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if (t1 > 0xff) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (((t1 ^ r_A) & (t1 ^ t2) & 0x80) != 0) flagV = Flag_Status.set; else flagV = Flag_Status.clear;
                        r_A = (byte)t1;
                    }
                    break;

                case 0xFD: //SBC  Absolute,X 
                    {
                        ushort t4 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                        ushort t3 = (ushort)(t4 + r_X);
                        byte t2 = Mem_r(t3);
                        t2 ^= 0xFF; //fix
                        int t1 = r_A + t2 + (byte)flagC;
                        if ((t1 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if (t1 > 0xff) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (((t1 ^ r_A) & (t1 ^ t2) & 0x80) != 0) flagV = Flag_Status.set; else flagV = Flag_Status.clear;
                        r_A = (byte)t1;
                        if ((t4 & 0xff00) != (t3 & 0xff00)) cpu_cycles++;
                    }
                    break;

                case 0xF9: //SBC  Absolute,Y FIX
                    {
                        ushort t4 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                        ushort t3 = (ushort)(t4 + r_Y);
                        byte t2 = Mem_r(t3);
                        t2 ^= 0xFF; //fix
                        int t1 = r_A + t2 + (byte)flagC;
                        if ((t1 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if (t1 > 0xff) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (((t1 ^ r_A) & (t1 ^ t2) & 0x80) != 0) flagV = Flag_Status.set; else flagV = Flag_Status.clear;
                        r_A = (byte)t1;
                        if ((t4 & 0xff00) != (t3 & 0xff00)) cpu_cycles++;
                    }
                    break;

                case 0xE1: //SBC (Indirect,X) 
                    {
                        byte t4 = (byte)(Mem_r(r_PC++) + r_X);
                        byte a1 = Mem_r(t4++);
                        byte a2 = Mem_r(t4);
                        ushort t3 = (ushort)((a2 << 8) | a1);
                        byte t2 = Mem_r(t3);
                        t2 ^= 0xFF; //fix
                        int t1 = r_A + t2 + (byte)flagC;
                        if ((t1 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if (t1 > 0xff) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (((t1 ^ r_A) & (t1 ^ t2) & 0x80) != 0) flagV = Flag_Status.set; else flagV = Flag_Status.clear;
                        r_A = (byte)t1;
                    }
                    break;

                case 0xF1: //SBC (Indirect),Y
                    {
                        byte t4 = Mem_r(r_PC++);
                        byte a1 = Mem_r(t4++);
                        byte a2 = Mem_r(t4);
                        ushort t3 = (ushort)((a2 << 8) | a1);
                        byte t2 = Mem_r((ushort)(t3 + r_Y));
                        t2 ^= 0xFF; //fix
                        int t1 = r_A + t2 + (byte)flagC;
                        if ((t1 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if (t1 > 0xff) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (((t1 ^ r_A) & (t1 ^ t2) & 0x80) != 0) flagV = Flag_Status.set; else flagV = Flag_Status.clear;
                        r_A = (byte)t1;
                        if ((t3 & 0xff00) != ((t3 + r_Y) & 0xff00)) cpu_cycles++;
                    }
                    break;

                //--- SBC END
                case 0x38://SEC
                    flagC = Flag_Status.set;
                    break;

                case 0xF8:// SED NES 6502 此 FLAG 無作用
                    flagD = Flag_Status.set;
                    break;

                case 0x78: //SEI
                    flagI = Flag_Status.set;
                    break;

                case 0x85://STA zp
                    Mem_w(Mem_r(r_PC++), r_A);
                    break;

                case 0x95://STA zp,x
                    Mem_w((ushort)((Mem_r(r_PC++) + r_X) & 0xff), r_A);
                    break;

                case 0x8D://STA abs
                    Mem_w((ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)), r_A);
                    break;

                case 0x9D://STA abs,x
                    ushort T1 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_X);
                    Mem_w(T1, r_A);
                    break;

                case 0x99://STA abs,Y
                    Mem_w((ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_Y), r_A);
                    break;

                case 0x81://STA (indirect,x)
                    {
                        byte t4 = (byte)(Mem_r(r_PC++) + r_X);
                        byte a1 = Mem_r(t4++);
                        byte a2 = Mem_r(t4);
                        ushort t1 = (ushort)((a2 << 8) | a1);
                        Mem_w(t1, r_A);
                    }
                    break;

                case 0x91://STA (indirect),y
                    {
                        byte t4 = Mem_r(r_PC++);
                        byte a1 = Mem_r(t4++);
                        byte a2 = Mem_r(t4);
                        ushort t1 = (ushort)((a2 << 8) | a1);
                        Mem_w((ushort)(t1 + r_Y), r_A);
                    }
                    break;

                case 0x86://STX zp
                    Mem_w(Mem_r(r_PC++), r_X);
                    break;

                case 0x96://STX zp,y
                    Mem_w((ushort)((Mem_r(r_PC++) + r_Y) & 0xff), r_X);
                    break;

                case 0x8E://STX abs //fixed 1/3
                    Mem_w((ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)), r_X);
                    break;

                case 0x84://STY  zp
                    Mem_w(Mem_r(r_PC++), r_Y);
                    break;

                case 0x94://STY zp,x
                    Mem_w((ushort)((Mem_r(r_PC++) + r_X) & 0xff), r_Y);
                    break;

                case 0x8C://STY abs                    
                    Mem_w((ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)), r_Y);
                    break;

                case 0xAA: //TAX
                    if ((r_A & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    if (r_A == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    r_X = r_A;
                    break;

                case 0xA8://TAY
                    if ((r_A & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    if (r_A == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    r_Y = r_A;
                    break;

                case 0xBA://TSX
                    if ((r_SP & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    if (r_SP == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    r_X = r_SP;
                    break;

                case 0x8A://TXA
                    if ((r_X & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    if (r_X == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    r_A = r_X;
                    break;

                case 0x9A: //TXS
                    r_SP = r_X;
                    break;

                case 0x98: //TYA
                    if ((r_Y & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    if (r_Y == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    r_A = r_Y;
                    break;

                #region illagel code

                // http://visual6502.org/wiki/index.php?title=6502_all_256_Opcodes
                // http://macgui.com/kb/article/46

                case 0x1A: //do nothing
                case 0x3A:
                case 0x5A:
                case 0x7A:
                case 0xDA:
                case 0xFA:
                    break;

                case 0x80:
                case 0x82:
                case 0x89:
                case 0xC2:
                case 0xE2:
                case 0x04:
                case 0x44:
                case 0x64:
                case 0x14:
                case 0x34:
                case 0x54:
                case 0xD4:
                case 0xF4:
                case 0x74:
                    r_PC += 1;
                    break;

                case 0x0C:
                case 0x1C:
                case 0x3C:
                case 0x5C:
                case 0x7C:
                case 0xDC:
                case 0xFC:
                    r_PC += 2;
                    break;

                case 0x6B:
                    {
                        byte t1 = Mem_r(r_PC++);
                        r_A = (byte)(((t1 & r_A) >> 1) | (((byte)flagC) << 7));
                        if (r_A == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((r_A & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if ((r_A & 0x40) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if (((r_A << 1 ^ r_A) & 0x40) > 0) flagV = Flag_Status.set; else flagV = Flag_Status.clear;
                    }
                    break;

                case 0x0B: //ANC
                case 0x2B: //ANC
                    {
                        byte t2 = Mem_r(r_PC++);
                        r_A &= t2;
                        if (r_A == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((r_A & 0X80) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        flagN = flagC;
                    }
                    break;

                case 0x4B: //ALR
                    {
                        byte t2 = Mem_r(r_PC++);
                        r_A &= t2;
                        if ((r_A & 0x1) != 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        r_A >>= 1;
                        if ((r_A & 0x80) != 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (r_A == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    }
                    break;

                case 0xEB: //illegal sbc imm
                    {
                        byte t2 = Mem_r(r_PC++);
                        t2 ^= 0xFF; //fix
                        int t1 = r_A + t2 + (byte)flagC;
                        if ((t1 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if (t1 > 0xff) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t1 & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (((t1 ^ r_A) & (t1 ^ t2) & 0x80) != 0) flagV = Flag_Status.set; else flagV = Flag_Status.clear;
                        r_A = (byte)t1;
                    }
                    break;

                case 0x03: //SLO (  ASL M THEN (M "OR" A) -> A,M  )
                    {
                        byte t4 = (byte)(Mem_r(r_PC++) + r_X);
                        byte a1 = Mem_r(t4++);
                        byte a2 = Mem_r(t4);
                        ushort t1 = (ushort)((a2 << 8) | a1);
                        byte t2 = Mem_r(t1);
                        if ((t2 & 0x80) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        t2 <<= 1;
                        Mem_w(t1, t2);
                        r_A |= t2;
                        if (r_A == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((r_A & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0x07: //SLO (  ASL M THEN (M "OR" A) -> A,M  )
                    {
                        byte t1 = Mem_r(r_PC++);
                        byte t2 = Mem_r(t1);
                        if ((t2 & 0x80) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        t2 <<= 1;
                        Mem_w(t1, t2);
                        r_A |= t2;
                        if (r_A == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((r_A & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0x13: //SLO (  ASL M THEN (M "OR" A) -> A,M  )
                    {
                        byte t4 = Mem_r(r_PC++);
                        byte a1 = Mem_r(t4++);
                        byte a2 = Mem_r(t4);
                        ushort t1 = (ushort)((a2 << 8) | a1);
                        byte t2 = Mem_r((ushort)(t1 + r_Y));
                        if ((t2 & 0x80) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        t2 <<= 1;
                        Mem_w((ushort)(t1 + r_Y), t2);
                        r_A |= t2;
                        if (r_A == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((r_A & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0x17: //SLO (  ASL M THEN (M "OR" A) -> A,M  )
                    {
                        byte t1 = (byte)((Mem_r(r_PC++) + r_X) & 0xff);
                        byte t2 = Mem_r(t1);
                        if ((t2 & 0x80) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        t2 <<= 1;
                        Mem_w(t1, t2);
                        r_A |= t2;
                        if (r_A == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((r_A & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0x1B: //SLO (  ASL M THEN (M "OR" A) -> A,M  )
                    {
                        ushort t1 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_Y);
                        byte t2 = Mem_r(t1);
                        if ((t2 & 0x80) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        t2 <<= 1;
                        Mem_w(t1, t2);
                        r_A |= t2;
                        if (r_A == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((r_A & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0x0F: //SLO (  ASL M THEN (M "OR" A) -> A,M  )
                    {
                        ushort t1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                        byte t2 = Mem_r((ushort)(t1));
                        if ((t2 & 0x80) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        t2 <<= 1;
                        Mem_w(t1, t2);
                        r_A |= t2;
                        if (r_A == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((r_A & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0x1F: //SLO (  ASL M THEN (M "OR" A) -> A,M  )
                    {
                        ushort t4 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                        ushort t1 = (ushort)(t4 + r_X);
                        byte t2 = Mem_r(t1);
                        if ((t2 & 0x80) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        t2 <<= 1;
                        Mem_w(t1, t2);
                        r_A |= t2;
                        if (r_A == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((r_A & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0x23: //RLA    ( ROL M  THEN (M "AND" A) -> A )   
                    {
                        byte t4 = (byte)(Mem_r(r_PC++) + r_X);
                        byte a1 = Mem_r(t4++);
                        byte a2 = Mem_r(t4);
                        ushort t1 = (ushort)((a2 << 8) | a1);
                        byte t2 = Mem_r(t1);
                        byte t3 = (byte)(t2 << 1);
                        t3 |= (byte)(flagC);
                        Mem_w(t1, t3);
                        if ((t2 & 0x80) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        r_A &= t3;
                        if (r_A == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((r_A & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0x27: //RLA    ( ROL M  THEN (M "AND" A) -> A )   
                    {
                        byte t1 = Mem_r(r_PC++);
                        byte t2 = Mem_r(t1);
                        byte t3 = (byte)(t2 << 1);
                        t3 |= (byte)(flagC);
                        Mem_w(t1, t3);
                        if ((t2 & 0x80) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        r_A &= t3;
                        if (r_A == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((r_A & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0x2F:// RLA
                    {
                        ushort t1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                        byte t2 = Mem_r((ushort)(t1));
                        byte t3 = (byte)(t2 << 1);
                        t3 |= (byte)(flagC);
                        Mem_w(t1, t3);
                        if ((t2 & 0x80) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        r_A &= t3;
                        if (r_A == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((r_A & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0x3F://RLA
                    {
                        ushort t1 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_X);
                        byte t2 = Mem_r(t1);
                        byte t3 = (byte)(t2 << 1);
                        t3 |= (byte)(flagC);
                        Mem_w(t1, t3);
                        if ((t2 & 0x80) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        r_A &= t3;
                        if (r_A == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((r_A & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0x3B://RLA
                    {
                        ushort t1 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_Y);
                        byte t2 = Mem_r(t1);
                        byte t3 = (byte)(t2 << 1);
                        t3 |= (byte)(flagC);
                        Mem_w(t1, t3);
                        if ((t2 & 0x80) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        r_A &= t3;
                        if (r_A == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((r_A & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0x33: //RLA    ( ROL M  THEN (M "AND" A) -> A )   
                    {
                        byte t4 = Mem_r(r_PC++);
                        byte a1 = Mem_r(t4++);
                        byte a2 = Mem_r(t4);
                        ushort t1 = (ushort)((a2 << 8) | a1);
                        byte t2 = Mem_r((ushort)(t1 + r_Y));
                        byte t3 = (byte)(t2 << 1);
                        t3 |= (byte)(flagC);
                        Mem_w((ushort)(t1 + r_Y), t3);
                        if ((t2 & 0x80) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        r_A &= t3;
                        if (r_A == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((r_A & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0x37: //RLA    ( ROL M  THEN (M "AND" A) -> A )   
                    {
                        byte t1 = (byte)((Mem_r(r_PC++) + r_X) & 0xff);
                        byte t2 = Mem_r(t1);
                        byte t3 = (byte)(t2 << 1);
                        t3 |= (byte)(flagC);
                        Mem_w(t1, t3);
                        if ((t2 & 0x80) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        r_A &= t3;
                        if (r_A == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((r_A & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0x43://SRE (LSR M  THEN (M "EOR" A) -> A )
                    {
                        byte t4 = Mem_r(r_PC++);
                        t4 += r_X;
                        byte a1 = Mem_r(t4);
                        t4++;
                        byte a2 = Mem_r(t4);
                        ushort t1 = (ushort)((a2 << 8) | a1);
                        byte t2 = Mem_r(t1);
                        if ((t2 & 1) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        t2 >>= 1;
                        Mem_w(t1, t2);
                        r_A ^= t2;
                        if (r_A == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((r_A & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0x47://SRE (LSR M  THEN (M "EOR" A) -> A )
                    {
                        byte t1 = Mem_r(r_PC++);
                        byte t2 = Mem_r(t1);
                        if ((t2 & 1) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        t2 >>= 1;
                        Mem_w(t1, t2);
                        r_A ^= t2;
                        if (r_A == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((r_A & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0x4F://SRE (LSR M  THEN (M "EOR" A) -> A )
                    {
                        ushort t1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                        byte t2 = Mem_r((ushort)(t1));
                        if ((t2 & 1) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        t2 >>= 1;
                        Mem_w(t1, t2);
                        r_A ^= t2;
                        if (r_A == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((r_A & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0x5F://SRE  
                    {
                        ushort t1 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_X);
                        byte t2 = Mem_r(t1);
                        if ((t2 & 1) != 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        t2 >>= 1;
                        Mem_w(t1, t2);
                        r_A ^= t2;
                        if (r_A == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((r_A & 0x80) != 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0x5B://SRE  
                    {
                        ushort t1 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_Y);
                        byte t2 = Mem_r(t1);
                        if ((t2 & 1) != 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        t2 >>= 1;
                        Mem_w(t1, t2);
                        r_A ^= t2;
                        if (r_A == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((r_A & 0x80) != 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;
                case 0x53://SRE (LSR M  THEN (M "EOR" A) -> A )
                    {
                        byte t4 = Mem_r(r_PC++);
                        byte a1 = Mem_r(t4++);
                        byte a2 = Mem_r(t4);
                        ushort t1 = (ushort)((a2 << 8) | a1);
                        byte t2 = Mem_r((ushort)(t1 + r_Y));
                        if ((t2 & 1) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        t2 >>= 1;
                        Mem_w((ushort)(t1 + r_Y), t2);
                        r_A ^= t2;
                        if (r_A == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((r_A & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0x57://SRE (LSR M  THEN (M "EOR" A) -> A )
                    {
                        byte t1 = (byte)((Mem_r(r_PC++) + r_X) & 0xff);
                        byte t2 = Mem_r(t1);
                        if ((t2 & 1) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        t2 >>= 1;
                        Mem_w(t1, t2);
                        r_A ^= t2;
                        if (r_A == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((r_A & 0x80) > 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0x63:// RRA (ROR M THEN (A + M + C) -> A  )
                    {
                        byte t4 = (byte)(Mem_r(r_PC++) + r_X);
                        byte a1 = Mem_r(t4++);
                        byte a2 = Mem_r(t4);
                        ushort t1 = (ushort)((a2 << 8) | a1);
                        byte t2 = Mem_r(t1);
                        byte c = 0x80;
                        if (flagC == Flag_Status.clear) c = 0;
                        byte t3 = (byte)((t2 >> 1) | c);
                        Mem_w(t1, (byte)t3);
                        if ((t2 & 1) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        int t5 = r_A + t3 + (byte)flagC;
                        if ((t5 & 0x80) != 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (((t5 ^ r_A) & (t5 ^ t3) & 0x80) != 0) flagV = Flag_Status.set; else flagV = Flag_Status.clear;
                        r_A = (byte)t5;
                        if ((t5 >> 8) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t5 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    }
                    break;

                case 0x67:// RRA (ROR M THEN (A + M + C) -> A  )
                    {
                        byte t1 = Mem_r(r_PC++);
                        byte t2 = Mem_r(t1);
                        byte c = 0x80;
                        if (flagC == Flag_Status.clear) c = 0;
                        byte t3 = (byte)((t2 >> 1) | c);
                        Mem_w(t1, (byte)t3);
                        if ((t2 & 1) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        int t5 = r_A + t3 + (byte)flagC;
                        if ((t5 & 0x80) != 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (((t5 ^ r_A) & (t5 ^ t3) & 0x80) != 0) flagV = Flag_Status.set; else flagV = Flag_Status.clear;
                        r_A = (byte)t5;
                        if ((t5 >> 8) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t5 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    }
                    break;

                case 0x6F://RRA
                    {
                        ushort t1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                        byte t2 = Mem_r((ushort)(t1));
                        byte c = 0x80;
                        if (flagC == Flag_Status.clear) c = 0;
                        byte t3 = (byte)((t2 >> 1) | c);
                        Mem_w(t1, t3);
                        if ((t2 & 1) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        int t5 = r_A + t3 + (byte)flagC;
                        if ((t5 & 0x80) != 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (((t5 ^ r_A) & (t5 ^ t3) & 0x80) != 0) flagV = Flag_Status.set; else flagV = Flag_Status.clear;
                        r_A = (byte)t5;
                        if ((t5 >> 8) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t5 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    }
                    break;

                case 0x73:// RRA (ROR M THEN (A + M + C) -> A  )
                    {

                        byte t4 = Mem_r(r_PC++);
                        byte a1 = Mem_r(t4++);
                        byte a2 = Mem_r(t4);
                        ushort t1 = (ushort)((a2 << 8) | a1);
                        byte t2 = Mem_r((ushort)(t1 + r_Y));
                        byte c = 0x80;
                        if (flagC == Flag_Status.clear) c = 0;
                        byte t3 = (byte)((t2 >> 1) | c);
                        Mem_w((ushort)(t1 + r_Y), (byte)t3);
                        if ((t2 & 1) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        int t5 = r_A + t3 + (byte)flagC;
                        if ((t5 & 0x80) != 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (((t5 ^ r_A) & (t5 ^ t3) & 0x80) != 0) flagV = Flag_Status.set; else flagV = Flag_Status.clear;
                        r_A = (byte)t5;
                        if ((t5 >> 8) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t5 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    }
                    break;

                case 0x77:// RRA (ROR M THEN (A + M + C) -> A  )
                    {

                        byte t1 = (byte)((Mem_r(r_PC++) + r_X) & 0xff);
                        byte t2 = Mem_r(t1);
                        byte c = 0x80;
                        if (flagC == Flag_Status.clear) c = 0;
                        byte t3 = (byte)((t2 >> 1) | c);
                        Mem_w(t1, t3);
                        if ((t2 & 1) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        int t5 = r_A + t3 + (byte)flagC;
                        if ((t5 & 0x80) != 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (((t5 ^ r_A) & (t5 ^ t3) & 0x80) != 0) flagV = Flag_Status.set; else flagV = Flag_Status.clear;
                        r_A = (byte)t5;
                        if ((t5 >> 8) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t5 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    }
                    break;

                case 0x7B:// RRA (ROR M THEN (A + M + C) -> A  )
                    {
                        ushort t1 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_Y);
                        byte t2 = Mem_r(t1);
                        byte c = 0x80;
                        if (flagC == Flag_Status.clear) c = 0;
                        byte t3 = (byte)((t2 >> 1) | c);
                        Mem_w(t1, t3);
                        if ((t2 & 1) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        int t5 = r_A + t3 + (byte)flagC;
                        if ((t5 & 0x80) != 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (((t5 ^ r_A) & (t5 ^ t3) & 0x80) != 0) flagV = Flag_Status.set; else flagV = Flag_Status.clear;
                        r_A = (byte)t5;
                        if ((t5 >> 8) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t5 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    }
                    break;

                case 0x7F: //RRA
                    {
                        ushort t1 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_X);
                        byte t2 = Mem_r(t1);
                        byte c = 0x80;
                        if (flagC == Flag_Status.clear) c = 0;
                        byte t3 = (byte)((t2 >> 1) | c);
                        Mem_w(t1, t3);
                        if ((t2 & 1) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        int t5 = r_A + t3 + (byte)flagC;
                        if ((t5 & 0x80) != 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if (((t5 ^ r_A) & (t5 ^ t3) & 0x80) != 0) flagV = Flag_Status.set; else flagV = Flag_Status.clear;
                        r_A = (byte)t5;
                        if ((t5 >> 8) > 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t5 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                    }
                    break;

                case 0x83://SAX ( (A "AND" (MSB(adr)+1)  "AND" X) -> M 
                    {
                        byte t4 = (byte)(Mem_r(r_PC++) + r_X);
                        byte a1 = Mem_r(t4++);
                        byte a2 = Mem_r(t4);
                        ushort t1 = (ushort)((a2 << 8) | a1);
                        byte t2 = Mem_r(t1);
                        Mem_w(t1, (byte)(r_X & r_A));
                    }
                    break;

                case 0x87://SAX ( (A "AND" (MSB(adr)+1)  "AND" X) -> M 
                    {
                        byte t1 = Mem_r(r_PC++);
                        byte t2 = Mem_r(t1);
                        Mem_w(t1, (byte)(r_X & r_A));
                    }
                    break;

                case 0x8F://SAX ( (A "AND" (MSB(adr)+1)  "AND" X) -> M 
                    {
                        ushort t1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                        byte t2 = Mem_r((ushort)(t1));
                        Mem_w(t1, (byte)(r_X & r_A));
                    }
                    break;

                case 0x9C://SHY
                    {

                        ushort t1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                        byte t2 = Mem_r(t1);
                        byte t3 = (byte)(r_Y & (((t1 & 0xff00) >> 8) + 1));
                        t1 = (ushort)((t1 & 0xff00) | (byte)((t1 & 0xff) + r_X));
                        if ((t1 & 0xff) < r_X)
                            t1 = (ushort)((t1 & 0xff) | (t3 << 8));
                        Mem_w(t1, t3);
                    }
                    break;


                case 0x9E://SHX
                    {

                        ushort t1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                        byte t2 = Mem_r(t1);
                        byte t3 = (byte)(r_X & (((t1 & 0xff00) >> 8) + 1));
                        t1 = (ushort)((t1 & 0xff00) | (byte)((t1 & 0xff) + r_Y));
                        if ((t1 & 0xff) < r_Y)
                            t1 = (ushort)((t1 & 0xff) | (t3 << 8));
                        Mem_w(t1, t3);
                    }
                    break;


                case 0x97://SAX ( (A "AND" (MSB(adr)+1)  "AND" X) -> M 
                    {
                        byte t1 = (byte)((Mem_r(r_PC++) + r_Y) & 0xff);
                        byte t2 = Mem_r(t1);
                        Mem_w(t1, (byte)(r_X & r_A));
                    }
                    break;

                case 0xB7://SAX ( (A "AND" (MSB(adr)+1)  "AND" X) -> M 
                    {
                        byte t1 = (byte)((Mem_r(r_PC++) + r_Y) & 0xff);
                        byte t2 = Mem_r(t1);
                        r_X = r_A = t2;
                        if (r_X == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((r_X & 0x80) != 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;


                case 0xA3://LAX
                    {
                        byte t4 = (byte)(Mem_r(r_PC++) + r_X);
                        byte a1 = Mem_r(t4++);
                        byte a2 = Mem_r(t4);
                        byte t2 = Mem_r((ushort)((a2 << 8) | a1));
                        r_X = r_A = t2;
                        if (r_X == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((r_X & 0x80) != 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0xA7://LAX
                    {
                        byte t1 = Mem_r(r_PC++);
                        byte t2 = Mem_r(t1);
                        r_X = r_A = t2;
                        if (r_X == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((r_X & 0x80) != 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0xAB://LAX
                    {
                        byte t1 = Mem_r(r_PC++);
                        r_X = r_A = t1;
                        if (r_X == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((r_X & 0x80) != 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0xAF://LAX
                    {
                        ushort t1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                        byte t2 = Mem_r((ushort)(t1));
                        r_X = r_A = t2;
                        if (r_X == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((r_X & 0x80) != 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0xBF://LAX
                    {
                        ushort t1 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_Y);
                        byte t2 = Mem_r(t1);
                        r_X = r_A = t2;
                        if (r_X == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((r_X & 0x80) != 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;


                case 0xB3://LAX
                    {
                        byte t4 = Mem_r(r_PC++);
                        byte a1 = Mem_r(t4);
                        byte a2 = Mem_r(++t4);
                        ushort t1 = (ushort)((a2 << 8) | a1);
                        byte t2 = Mem_r((ushort)(t1 + r_Y));
                        r_X = r_A = t2;
                        if (r_X == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((r_X & 0x80) != 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0xC3: //DCP
                    {
                        byte t1 = (byte)(Mem_r(r_PC++) + r_X);
                        ushort t3 = (ushort)((Mem_r(t1++) | (Mem_r(t1) << 8)));
                        byte t2 = Mem_r(t3);
                        Mem_w(t3, --t2);
                        int t4 = r_A - t2;
                        if (t4 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((~t4) >> 8 != 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t4 & 0X80) != 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0xC7: //DCP
                    {

                        byte t1 = Mem_r(r_PC++);
                        byte t2 = Mem_r(t1);
                        Mem_w(t1, --t2);
                        int t4 = r_A - t2;
                        if (t4 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((~t4) >> 8 != 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t4 & 0X80) != 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0xCB:// AXS
                    {
                        byte t1 = Mem_r(r_PC++);
                        int t2 = (r_A & r_X) - t1;
                        if ((t2 & 0x80) != 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        if ((byte)t2 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((~t2 >> 8) != 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        r_X = (byte)t2;
                    }
                    break;
                case 0xCF: //DCP
                    {
                        ushort t1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                        byte t2 = Mem_r((ushort)(t1));
                        Mem_w(t1, --t2);
                        int t4 = r_A - t2;
                        if (t4 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((~t4) >> 8 != 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t4 & 0X80) != 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0xDF:
                    {
                        ushort t1 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_X);
                        byte t2 = Mem_r(t1);
                        Mem_w(t1, --t2);
                        int t4 = r_A - t2;
                        if (t4 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((~t4) >> 8 != 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t4 & 0X80) != 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0xD3: //DCP
                    {
                        byte t1 = Mem_r(r_PC++);
                        ushort t3 = (ushort)((Mem_r(t1++) | (Mem_r(t1) << 8)) + r_Y);
                        byte t2 = Mem_r(t3);
                        Mem_w(t3, --t2);
                        int t4 = r_A - t2;
                        if (t4 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((~t4) >> 8 != 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t4 & 0X80) != 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0xD7: //DCP
                    {
                        byte t1 = (byte)((Mem_r(r_PC++) + r_X) & 0xff);
                        byte t2 = Mem_r(t1);
                        Mem_w(t1, --t2);
                        int t4 = r_A - t2;
                        if (t4 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((~t4) >> 8 != 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t4 & 0X80) != 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0xDB:// DCP
                    {
                        ushort t1 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_Y);
                        byte t2 = Mem_r(t1);
                        Mem_w(t1, --t2);
                        int t4 = r_A - t2;
                        if (t4 == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((~t4) >> 8 != 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t4 & 0X80) != 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                    }
                    break;

                case 0xE3://ISC
                    {
                        byte t1 = (byte)(Mem_r(r_PC++) + r_X);
                        ushort t3 = (ushort)((Mem_r(t1++) | (Mem_r(t1) << 8)));
                        byte t2 = Mem_r(t3);
                        Mem_w(t3, ++t2);
                        int t4 = r_A + (t2 ^ 0xff) + (byte)flagC;
                        if (((t4 ^ r_A) & (t4 ^ (t2 ^ 0xff)) & 0x80) != 0) flagV = Flag_Status.set; else flagV = Flag_Status.clear;
                        if ((t4 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t4) >> 8 != 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t4 & 0X80) != 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        r_A = (byte)t4;
                    }
                    break;

                case 0xE7://ISC
                    {

                        byte t1 = Mem_r(r_PC++);
                        byte t2 = Mem_r(t1);
                        Mem_w(t1, ++t2);
                        int t4 = r_A + (t2 ^ 0xff) + (byte)flagC;
                        if (((t4 ^ r_A) & (t4 ^ (t2 ^ 0xff)) & 0x80) != 0) flagV = Flag_Status.set; else flagV = Flag_Status.clear;
                        if ((t4 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t4) >> 8 != 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t4 & 0X80) != 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        r_A = (byte)t4;
                    }
                    break;

                case 0xEF://ISC
                    {
                        ushort t1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                        byte t2 = Mem_r((ushort)(t1));
                        Mem_w(t1, ++t2);
                        int t4 = r_A + (t2 ^ 0xff) + (byte)flagC;
                        if (((t4 ^ r_A) & (t4 ^ (t2 ^ 0xff)) & 0x80) != 0) flagV = Flag_Status.set; else flagV = Flag_Status.clear;
                        if ((t4 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t4) >> 8 != 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t4 & 0X80) != 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        r_A = (byte)t4;
                    }
                    break;


                case 0xF3://ISC
                    {
                        byte t1 = Mem_r(r_PC++);
                        ushort t3 = (ushort)(((Mem_r(t1++) | (Mem_r(t1) << 8))) + r_Y);
                        byte t2 = Mem_r(t3);
                        Mem_w(t3, ++t2);
                        int t4 = r_A + (t2 ^ 0xff) + (byte)flagC;
                        if (((t4 ^ r_A) & (t4 ^ (t2 ^ 0xff)) & 0x80) != 0) flagV = Flag_Status.set; else flagV = Flag_Status.clear;
                        if ((t4 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t4) >> 8 != 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t4 & 0X80) != 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        r_A = (byte)t4;
                    }
                    break;

                case 0xF7://ISC
                    {
                        byte t1 = (byte)((Mem_r(r_PC++) + r_X) & 0xff);
                        byte t2 = Mem_r(t1);
                        Mem_w(t1, ++t2);
                        int t4 = r_A + (t2 ^ 0xff) + (byte)flagC;
                        if (((t4 ^ r_A) & (t4 ^ (t2 ^ 0xff)) & 0x80) != 0) flagV = Flag_Status.set; else flagV = Flag_Status.clear;
                        if ((t4 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t4) >> 8 != 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t4 & 0X80) != 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        r_A = (byte)t4;
                    }
                    break;

                case 0xFB://ISC
                    {
                        ushort t1 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_Y);
                        byte t2 = Mem_r(t1);
                        Mem_w(t1, ++t2);
                        int t4 = r_A + (t2 ^ 0xff) + (byte)flagC;
                        if (((t4 ^ r_A) & (t4 ^ (t2 ^ 0xff)) & 0x80) != 0) flagV = Flag_Status.set; else flagV = Flag_Status.clear;
                        if ((t4 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t4) >> 8 != 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t4 & 0X80) != 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        r_A = (byte)t4;
                    }
                    break;

                case 0xFF://ISC
                    {
                        ushort t1 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_X);
                        byte t2 = Mem_r(t1);
                        Mem_w(t1, ++t2);
                        int t4 = r_A + (t2 ^ 0xff) + (byte)flagC;
                        if (((t4 ^ r_A) & (t4 ^ (t2 ^ 0xff)) & 0x80) != 0) flagV = Flag_Status.set; else flagV = Flag_Status.clear;
                        if ((t4 & 0xff) == 0) flagZ = Flag_Status.set; else flagZ = Flag_Status.clear;
                        if ((t4) >> 8 != 0) flagC = Flag_Status.set; else flagC = Flag_Status.clear;
                        if ((t4 & 0X80) != 0) flagN = Flag_Status.set; else flagN = Flag_Status.clear;
                        r_A = (byte)t4;
                    }
                    break;
                #endregion

                default:
                    //MessageBox.Show("unkonw opcode ! - 0x" + opcode.ToString("X2"));
                    break;
            }
        }
    }
}
