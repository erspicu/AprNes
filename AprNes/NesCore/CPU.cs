using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace AprNes
{
    public partial class NesCore
    {
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

        byte flagN = 0, flagV = 0, flagB = 0, flagD = 0, flagI = 0, flagZ = 0, flagC = 0;
        byte r_A = 0, r_X = 0, r_Y = 0, r_SP = 0xFD;
        ushort r_PC = 0;
        byte opcode;
        int cpu_cycles = 0;
        public bool exit = false;

        public byte GetFlag()
        {
            return (byte)((flagN << 7) | (flagV << 6) | (0 << 5) | (flagB << 4) | (flagD << 3) | (flagI << 2) | (flagZ << 1) | flagC | 0x30);
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

        //for temp value use
        ushort us1, us2, us3, us4;
        sbyte sb1;
        byte b1, b2, b3, b4;
        int i1, i2, i3, i4, i5;
        byte a1, a2;
        ushort addr;
        byte t1_l, t1_h;

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
                    b2 = Mem_r(r_PC++);
                    i1 = b2 + r_A + (byte)flagC;
                    if ((i1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (i1 > 0xff) flagC = 1; else flagC = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (((i1 ^ r_A) & (i1 ^ b2) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)i1;
                    break;

                case 0x65: //ADC  Zero Page  
                    b2 = Mem_r(Mem_r(r_PC++));
                    i1 = b2 + r_A + (byte)flagC;
                    if ((i1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (i1 > 0xff) flagC = 1; else flagC = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (((i1 ^ r_A) & (i1 ^ b2) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)i1;
                    break;

                case 0x75://ADC Zero Page,X 
                    b2 = Mem_r((byte)(Mem_r(r_PC++) + r_X));
                    i1 = b2 + r_A + (byte)flagC;
                    if ((i1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (i1 > 0xff) flagC = 1; else flagC = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (((i1 ^ r_A) & (i1 ^ b2) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)i1;
                    break;

                case 0x6D: //ADC Absolute //fix
                    b2 = Mem_r((ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)));
                    i1 = b2 + r_A + (byte)flagC;
                    if ((i1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (i1 > 0xff) flagC = 1; else flagC = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (((i1 ^ r_A) & (i1 ^ b2) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)i1;
                    break;

                case 0x7D: //ADC  Absolute,X 
                    us4 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    us3 = (ushort)(us4 + r_X);
                    b2 = Mem_r(us3);
                    i1 = b2 + r_A + (byte)flagC;
                    if ((i1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (i1 > 0xff) flagC = 1; else flagC = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (((i1 ^ r_A) & (i1 ^ b2) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)i1;
                    if ((us4 & 0xff00) != (us3 & 0xff00)) cpu_cycles++;
                    break;

                case 0x79: //ADC  Absolute,Y fix
                    us4 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    us3 = (ushort)(us4 + r_Y);
                    b2 = Mem_r(us3);
                    i1 = b2 + r_A + (byte)flagC;
                    if ((i1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (i1 > 0xff) flagC = 1; else flagC = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (((i1 ^ r_A) & (i1 ^ b2) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)i1;
                    if ((us4 & 0xff00) != (us3 & 0xff00)) cpu_cycles++;
                    break;

                case 0x61: //ADC (Indirect,X) 
                    b4 = (byte)(Mem_r(r_PC++) + r_X);
                    a1 = Mem_r(b4++);
                    a2 = Mem_r(b4);
                    us3 = (ushort)((a2 << 8) | a1);
                    b2 = Mem_r(us3);
                    i1 = b2 + r_A + (byte)flagC;
                    if ((i1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (i1 > 0xff) flagC = 1; else flagC = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (((i1 ^ r_A) & (i1 ^ b2) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)i1;
                    break;

                case 0x71: //ADC (Indirect),Y                    
                    b4 = Mem_r(r_PC++);
                    a1 = Mem_r(b4++);
                    a2 = Mem_r(b4);
                    us3 = (ushort)((a2 << 8) | a1);
                    b2 = Mem_r((ushort)(us3 + r_Y));
                    i1 = b2 + r_A + (byte)flagC;
                    if ((i1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (i1 > 0xff) flagC = 1; else flagC = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (((i1 ^ r_A) & (i1 ^ b2) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)i1;
                    if ((us3 & 0xff00) != ((us3 + r_Y) & 0xff00)) cpu_cycles++;
                    break;

                //--- AND BEGIN
                case 0x29: //AND  Immediate  
                    b2 = Mem_r(r_PC++);
                    i1 = b2 & r_A;
                    if (i1 == 0) flagZ = 1; else flagZ = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)i1;
                    break;

                case 0x25: //AND  Zero Page  
                    b2 = Mem_r(Mem_r(r_PC++));
                    i1 = b2 & r_A;
                    if (i1 == 0) flagZ = 1; else flagZ = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)i1;
                    break;

                case 0x35://AND Zero Page,X 
                    b2 = Mem_r((byte)(Mem_r(r_PC++) + r_X));
                    i1 = b2 & r_A;
                    if (i1 == 0) flagZ = 1; else flagZ = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)i1;
                    break;

                case 0x2D: //AND Absolute 
                    us3 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    b2 = Mem_r(us3);
                    i1 = b2 & r_A;
                    if (i1 == 0) flagZ = 1; else flagZ = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)i1;
                    break;

                case 0x3D: //AND  Absolute,X 
                    us4 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    us3 = (ushort)(us4 + r_X);
                    b2 = Mem_r(us3);
                    i1 = b2 & r_A;
                    if (i1 == 0) flagZ = 1; else flagZ = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)i1;
                    if ((us4 & 0xff00) != (us3 & 0xff00)) cpu_cycles++;
                    break;

                case 0x39: //AND  Absolute,Y
                    us4 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    us3 = (ushort)(us4 + r_Y);
                    b2 = Mem_r(us3);
                    i1 = b2 & r_A;
                    if (i1 == 0) flagZ = 1; else flagZ = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)i1;
                    if ((us4 & 0xff00) != (us3 & 0xff00)) cpu_cycles++;
                    break;

                case 0x21: //AND (Indirect,X) 
                    b4 = (byte)(Mem_r(r_PC++) + r_X);
                    a1 = Mem_r(b4++);
                    a2 = Mem_r(b4);
                    us3 = (ushort)((a2 << 8) | a1);
                    b2 = Mem_r(us3);
                    i1 = b2 & r_A;
                    if ((i1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)i1;
                    break;

                case 0x31: //AND (Indirect),Y
                    b4 = Mem_r(r_PC++);
                    a1 = Mem_r(b4++);
                    a2 = Mem_r(b4);
                    us3 = (ushort)((a2 << 8) | a1);
                    b2 = Mem_r((ushort)(us3 + r_Y));
                    i1 = b2 & r_A;
                    if ((i1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)i1;
                    if ((us3 & 0xff00) != ((us3 + r_Y) & 0xff00)) cpu_cycles++;
                    break;
                //--- AND END 

                case 0x0A://ASL acc
                    if ((r_A & 0x80) > 0) flagC = 1; else flagC = 0;
                    r_A <<= 1;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x06://ASL zp
                    b2 = Mem_r(r_PC++);
                    b1 = Mem_r(b2);
                    if ((b1 & 0x80) > 0) flagC = 1; else flagC = 0;
                    b1 <<= 1;
                    if (b1 == 0) flagZ = 1; else flagZ = 0;
                    if ((b1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    Mem_w(b2, b1);
                    break;

                case 0x16://ASL zp,x
                    b2 = (byte)((Mem_r(r_PC++) + r_X) & 0xff);
                    b1 = Mem_r(b2);
                    if ((b1 & 0x80) > 0) flagC = 1; else flagC = 0;
                    b1 <<= 1;
                    if (b1 == 0) flagZ = 1; else flagZ = 0;
                    if ((b1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    Mem_w(b2, b1);
                    break;

                case 0x0E://ASL abs
                    us2 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)));
                    b1 = Mem_r(us2);
                    if ((b1 & 0x80) > 0) flagC = 1; else flagC = 0;
                    b1 <<= 1;
                    if (b1 == 0) flagZ = 1; else flagZ = 0;
                    if ((b1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    Mem_w(us2, b1);
                    break;

                case 0x1E://ASL abs,x
                    us2 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_X);
                    b1 = Mem_r(us2);
                    if ((b1 & 0x80) > 0) flagC = 1; else flagC = 0;
                    b1 <<= 1;
                    if (b1 == 0) flagZ = 1; else flagZ = 0;
                    if ((b1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    Mem_w(us2, b1);
                    break;

                case 0x90://BCC
                    sb1 = (sbyte)Mem_r(r_PC++);
                    addr = (ushort)(r_PC + sb1);
                    if (flagC == 0)
                    {
                        if ((addr & 0xff00) != (((r_PC - 2) & 0xff00)))
                            cpu_cycles += 2; //FIX
                        else
                            cpu_cycles += 1; //FIX
                        r_PC = addr;
                    }
                    break;

                case 0xB0://BCS
                    sb1 = (sbyte)Mem_r(r_PC++);
                    addr = (ushort)(r_PC + sb1);
                    if (flagC == 1)
                    {
                        if ((addr & 0xff00) != (((r_PC - 2) & 0xff00)))
                            cpu_cycles += 2;
                        else
                            cpu_cycles += 1;
                        r_PC = addr;
                    }
                    break;

                case 0xF0://BEQ
                    sb1 = (sbyte)Mem_r(r_PC++);
                    addr = (ushort)(r_PC + sb1);
                    if (flagZ == 1)
                    {
                        if ((addr & 0xff00) != (((r_PC - 2) & 0xff00)))
                            cpu_cycles += 2;
                        else
                            cpu_cycles += 1;
                        r_PC = addr;
                    }
                    break;

                case 0x24://BIT zp fix
                    b1 = Mem_r(Mem_r(r_PC++));
                    if ((b1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if ((b1 & 0x40) > 0) flagV = 1; else flagV = 0;
                    if ((b1 & r_A) == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0x2C://BIT abs //FIX
                    b1 = Mem_r((ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)));
                    if ((b1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if ((b1 & 0x40) > 0) flagV = 1; else flagV = 0;
                    if ((b1 & r_A) == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0x30://BMI
                    sb1 = (sbyte)Mem_r(r_PC++);
                    addr = (ushort)(r_PC + sb1);
                    if (flagN == 1)
                    {
                        if ((addr & 0xff00) != (((r_PC - 2) & 0xff00)))
                            cpu_cycles += 2;
                        else
                            cpu_cycles += 1;
                        r_PC = addr;
                    }
                    break;

                case 0xD0://BNE
                    sb1 = (sbyte)Mem_r(r_PC++);
                    addr = (ushort)(r_PC + sb1);
                    if (flagZ == 0)
                    {
                        if ((addr & 0xff00) != (((r_PC - 2) & 0xff00)))
                            cpu_cycles += 2;
                        else
                            cpu_cycles += 1;
                        r_PC = addr;
                    }
                    break;

                case 0x10://BPL
                    sb1 = (sbyte)Mem_r(r_PC++);
                    addr = (ushort)(r_PC + sb1);
                    if (flagN == 0)
                    {
                        if ((addr & 0xff00) != (((r_PC - 2) & 0xff00)))
                            cpu_cycles += 2;
                        else
                            cpu_cycles += 1;
                        r_PC = addr;
                    }
                    break;

                case 00://BRK
                    r_PC++;
                    Mem_w((ushort)(r_SP + 0x100), (byte)(r_PC >> 8));
                    r_SP--;
                    Mem_w((ushort)(r_SP + 0x100), (byte)(r_PC & 0xf));
                    r_SP--;
                    flagB = 1;
                    b1 = (byte)((flagN << 7) | (flagV << 6) | (1 << 5) | (flagB << 4) | (flagD << 3) | (flagI << 2) | (flagZ << 1) | flagC);
                    Mem_w((ushort)(r_SP + 0x100), b1);
                    r_SP--;
                    flagI = 0;
                    r_PC = (ushort)(Mem_r(0xFFFE) | (Mem_r(0xFFFF) << 8));
                    break;

                case 0x50://BVC
                    sb1 = (sbyte)Mem_r(r_PC++);
                    addr = (ushort)(r_PC + sb1);
                    if (flagV == 0)
                    {
                        if ((addr & 0xff00) != (((r_PC - 2) & 0xff00)))
                            cpu_cycles += 2;
                        else
                            cpu_cycles += 1;
                        r_PC = addr;
                    }
                    break;

                case 0x70://BVS
                    sb1 = (sbyte)Mem_r(r_PC++);
                    addr = (ushort)(r_PC + sb1);
                    if (flagV == 1)
                    {
                        if ((addr & 0xff00) != (((r_PC - 2) & 0xff00)))
                            cpu_cycles += 2;
                        else
                            cpu_cycles += 1;
                        r_PC = addr;
                    }
                    break;

                case 0x18://CLC
                    flagC = 0;
                    break;

                case 0xD8://CLD
                    flagD = 0;
                    break;

                case 0x58://CLI
                    flagI = 0;
                    break;

                case 0xB8://CLV
                    flagV = 0;
                    break;

                //--- CMP BEGIN
                case 0xC9: //CMP  Immediate  
                    b2 = Mem_r(r_PC++);
                    i1 = r_A - b2;
                    if (i1 == 0) flagZ = 1; else flagZ = 0;
                    if (r_A >= b2) flagC = 1; else flagC = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0xC5: //CMP  Zero Page  
                    b2 = Mem_r(Mem_r(r_PC++));
                    i1 = r_A - b2;
                    if (i1 == 0) flagZ = 1; else flagZ = 0;
                    if (r_A >= b2) flagC = 1; else flagC = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0xD5://CMP Zero Page,X 
                    b2 = Mem_r((byte)(Mem_r(r_PC++) + r_X));
                    i1 = r_A - b2;
                    if (i1 == 0) flagZ = 1; else flagZ = 0;
                    if (r_A >= b2) flagC = 1; else flagC = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0xCD: //CMP Absolute 
                    us3 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    b2 = Mem_r(us3);
                    i1 = r_A - b2;
                    if (i1 == 0) flagZ = 1; else flagZ = 0;
                    if (r_A >= b2) flagC = 1; else flagC = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0xDD: //CMP  Absolute,X 
                    us4 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    us3 = (ushort)(us4 + r_X);
                    b2 = Mem_r(us3);
                    i1 = r_A - b2;
                    if (i1 == 0) flagZ = 1; else flagZ = 0;
                    if (r_A >= b2) flagC = 1; else flagC = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if ((us4 & 0xff00) != (us3 & 0xff00)) cpu_cycles++;
                    break;

                case 0xD9: //CMP  Absolute,Y
                    us4 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    us3 = (ushort)(us4 + r_Y);
                    b2 = Mem_r(us3);
                    i1 = r_A - b2;
                    if (i1 == 0) flagZ = 1; else flagZ = 0;
                    if (r_A >= b2) flagC = 1; else flagC = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if ((us4 & 0xff00) != (us3 & 0xff00)) cpu_cycles++;
                    break;

                case 0xC1: //CMP (Indirect,X)  fix
                    b4 = (byte)(Mem_r(r_PC++) + r_X);
                    a1 = Mem_r(b4++);
                    a2 = Mem_r(b4);
                    us1 = (ushort)((a2 << 8) | a1);
                    b2 = Mem_r(us1);
                    i3 = r_A - b2;
                    if ((i3 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (r_A >= b2) flagC = 1; else flagC = 0;
                    if ((i3 & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0xD1: //CMP (Indirect),Y
                    b4 = Mem_r(r_PC++);
                    a1 = Mem_r(b4++);
                    a2 = Mem_r(b4);
                    us1 = (ushort)((a2 << 8) | a1);
                    b2 = Mem_r((ushort)(us1 + r_Y));
                    i3 = r_A - b2;
                    if ((i3 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (r_A >= b2) flagC = 1; else flagC = 0;
                    if ((i3 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if ((us1 & 0xff00) != ((us1 + r_Y) & 0xff00)) cpu_cycles++;
                    break;
                //--- CMP END

                case 0xE0: //CPX  Immediate  
                    b2 = Mem_r(r_PC++);
                    i1 = r_X - b2;// +(byte)flagC;
                    if ((i1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (r_X >= b2) flagC = 1; else flagC = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0xE4: //CPX  Zero Page  
                    b2 = Mem_r(Mem_r(r_PC++));
                    i1 = r_X - b2;// +(byte)flagC;
                    if ((i1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (r_X >= b2) flagC = 1; else flagC = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0xEC: //CPX Absolute 
                    us3 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    b2 = Mem_r(us3);
                    i1 = r_X - b2;// +(byte)flagC;
                    if ((i1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (r_X >= b2) flagC = 1; else flagC = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                //-- CPY BEGIN
                case 0xC0: //CPY  Immediate  
                    b2 = Mem_r(r_PC++);
                    i1 = r_Y - b2;// +(byte)flagC;
                    if ((i1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (r_Y >= b2) flagC = 1; else flagC = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0xC4: //CPY  Zero Page  
                    b2 = Mem_r(Mem_r(r_PC++));
                    i1 = r_Y - b2;// +(byte)flagC;
                    if ((i1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (r_Y >= b2) flagC = 1; else flagC = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0xCC: //CPY Absolute 
                    us3 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    b2 = Mem_r(us3);
                    i1 = r_Y - b2;// +(byte)flagC;
                    if ((i1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (r_Y >= b2) flagC = 1; else flagC = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;
                //-- CPY END

                case 0xC6://DEC zp
                    b1 = Mem_r(r_PC++);
                    b2 = Mem_r(b1);
                    Mem_w(b1, --b2);
                    if ((b2 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (b2 == 0) flagZ = 1; else flagZ = 0;
                    break;


                case 0xD6://DEC zp,x
                    b1 = (byte)((Mem_r(r_PC++) + r_X) & 0xFF);
                    b2 = Mem_r(b1);
                    Mem_w(b1, --b2);
                    if ((b2 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (b2 == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xCE://DEC abs
                    us1 = (ushort)(Mem_r(r_PC++) | Mem_r(r_PC++) << 8);
                    b2 = Mem_r(us1);
                    Mem_w(us1, --b2);
                    if ((b2 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (b2 == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xDE://DEC abs,x
                    us1 = (ushort)(((Mem_r(r_PC++) | Mem_r(r_PC++) << 8) + r_X) & 0xFFFF);
                    b2 = Mem_r(us1);
                    b2--;
                    Mem_w(us1, b2);
                    if ((b2 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (b2 == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xCA://DEX
                    r_X--;
                    if ((r_X & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_X == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0x88://DEY //fix
                    r_Y--;
                    if ((r_Y & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_Y == 0) flagZ = 1; else flagZ = 0;
                    break;

                //--- EOR BEGIN
                case 0x49: //EOR  Immediate  
                    b2 = Mem_r(r_PC++);
                    i1 = b2 ^ r_A;
                    if (i1 == 0) flagZ = 1; else flagZ = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)i1;
                    break;

                case 0x45: //EOR  Zero Page  
                    b2 = Mem_r(Mem_r(r_PC++));
                    i1 = b2 ^ r_A;
                    if (i1 == 0) flagZ = 1; else flagZ = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)i1;
                    break;

                case 0x55://EOR Zero Page,X 
                    b2 = Mem_r((byte)(Mem_r(r_PC++) + r_X));
                    i1 = b2 ^ r_A;
                    if (i1 == 0) flagZ = 1; else flagZ = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)i1;
                    break;

                case 0x4D: //EOR Absolute 
                    us3 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    b2 = Mem_r(us3);
                    i1 = b2 ^ r_A;
                    if (i1 == 0) flagZ = 1; else flagZ = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)i1;
                    break;

                case 0x5D: //EOR  Absolute,X 
                    us4 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    us3 = (ushort)(us4 + r_X);
                    b2 = Mem_r(us3);
                    i1 = b2 ^ r_A;
                    if (i1 == 0) flagZ = 1; else flagZ = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)i1;
                    if ((us4 & 0xff00) != (us3 & 0xff00)) cpu_cycles++;
                    break;

                case 0x59: //EOR  Absolute,Y
                    us4 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    us3 = (ushort)(us4 + r_Y);
                    b2 = Mem_r(us3);
                    i1 = b2 ^ r_A;
                    if (i1 == 0) flagZ = 1; else flagZ = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)i1;
                    if ((us4 & 0xff00) != (us3 & 0xff00)) cpu_cycles++;
                    break;

                case 0x41: //EOR (Indirect,X) 
                    b4 = (byte)(Mem_r(r_PC++) + r_X);
                    a1 = Mem_r(b4++);
                    a2 = Mem_r(b4);
                    us3 = (ushort)((a2 << 8) | a1);
                    b2 = Mem_r(us3);
                    i1 = b2 ^ r_A;// +(byte)flagC;
                    if ((i1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)i1;
                    break;

                case 0x51: //EOR (Indirect),Y
                    b4 = Mem_r(r_PC++);
                    a1 = Mem_r(b4++);
                    a2 = Mem_r(b4);
                    us3 = (ushort)((a2 << 8) | a1);
                    b2 = Mem_r((ushort)(us3 + r_Y));
                    i1 = b2 ^ r_A;// +(byte)flagC;
                    if ((i1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)i1;
                    if ((us3 & 0xff00) != ((us3 + r_Y) & 0xff00)) cpu_cycles++;
                    break;
                //--- EOR END    

                case 0xE6://INC zp
                    b1 = Mem_r(r_PC++);
                    b2 = Mem_r(b1);
                    Mem_w(b1, ++b2);
                    if ((b2 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (b2 == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xF6://INC zp,x
                    b1 = (byte)((Mem_r(r_PC++) + r_X) & 0xFF);
                    b2 = Mem_r(b1);
                    Mem_w(b1, ++b2);
                    if ((b2 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (b2 == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xEE://INC abs
                    us1 = (ushort)(Mem_r(r_PC++) | Mem_r(r_PC++) << 8);
                    b2 = Mem_r(us1);
                    Mem_w(us1, ++b2);
                    if ((b2 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (b2 == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xFE://INC abs,x
                    us1 = (ushort)(((Mem_r(r_PC++) | Mem_r(r_PC++) << 8) + r_X) & 0xFFFF);
                    b2 = Mem_r(us1);
                    Mem_w(us1, ++b2);
                    if ((b2 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (b2 == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xE8://INX
                    r_X++;
                    if ((r_X & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_X == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xC8://INY
                    r_Y++;
                    if ((r_Y & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_Y == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0x4C://JMP abs                    
                    r_PC = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    break;

                case 0x6C://JMP indirect
                    t1_l = Mem_r(r_PC++);
                    t1_h = Mem_r(r_PC++);
                    us1 = (ushort)(t1_l | (t1_h << 8));
                    byte t2_l = Mem_r(us1);
                    t1_l++;
                    byte t2_h = Mem_r((ushort)(t1_l | (t1_h << 8)));
                    r_PC = (ushort)(t2_l | (t2_h << 8));
                    break;

                case 0x20://JSR abs
                    us1 = (ushort)(r_PC + 1);
                    b2 = (byte)(us1 >> 8);
                    Mem_w((ushort)(r_SP + 0x100), b2);
                    r_SP--;
                    b3 = (byte)(us1 & 0xFF);
                    Mem_w((ushort)(r_SP + 0x100), b3);
                    r_SP--;
                    r_PC = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    break;

                case 0xA9://LDA imm
                    r_A = Mem_r(r_PC++);
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xA5://LDA zp
                    r_A = Mem_r(Mem_r(r_PC++));
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xB5://LDA zp,x
                    r_A = Mem_r((ushort)((Mem_r(r_PC++) + r_X) & 0xFF));
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    break
                        ;
                case 0xAD://LDA abs
                    r_A = Mem_r((ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)));
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xBD://LDA abs,x
                    us1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    r_A = Mem_r((ushort)(us1 + r_X));
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((us1 & 0xff00) != ((us1 + r_X) & 0xff00)) cpu_cycles++;
                    break;

                case 0xB9://LDA abs,y
                    us1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    r_A = Mem_r((ushort)(us1 + r_Y));
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((us1 & 0xff00) != ((us1 + r_Y) & 0xff00)) cpu_cycles++;
                    break;

                case 0xA1://LDA (indirect,x)
                    b4 = (byte)(Mem_r(r_PC++) + r_X);
                    a1 = Mem_r(b4++);
                    a2 = Mem_r(b4);
                    us1 = (ushort)((a2 << 8) | a1);
                    r_A = Mem_r(us1);
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xB1://LDA (indirect),y
                    b4 = Mem_r(r_PC++);
                    a1 = Mem_r(b4++);
                    a2 = Mem_r(b4);
                    us1 = (ushort)((a2 << 8) | a1);
                    r_A = Mem_r((ushort)(us1 + r_Y));
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((us1 & 0xff00) != ((us1 + r_Y) & 0xff00)) cpu_cycles++;
                    break;

                case 0xA2://LDX imm
                    r_X = Mem_r(r_PC++);
                    if ((r_X & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_X == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xA6://LDX zp
                    r_X = Mem_r(Mem_r(r_PC++));
                    if ((r_X & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_X == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xB6://LDX zp,y
                    r_X = Mem_r((ushort)((Mem_r(r_PC++) + r_Y) & 0xFF));
                    if ((r_X & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_X == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xAE://LDX abs
                    r_X = Mem_r((ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)));
                    if ((r_X & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_X == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xBE://LDX abs,y
                    us1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    r_X = Mem_r((ushort)(us1 + r_Y));
                    if ((r_X & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_X == 0) flagZ = 1; else flagZ = 0;
                    if ((us1 & 0xff00) != ((us1 + r_Y) & 0xff00)) cpu_cycles++;
                    break;

                case 0xA0://LDY imm
                    r_Y = Mem_r(r_PC++);
                    if ((r_Y & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_Y == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xA4://LDY zp
                    r_Y = Mem_r(Mem_r(r_PC++));
                    if ((r_Y & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_Y == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xB4://LDY zp,x
                    r_Y = Mem_r((ushort)((Mem_r(r_PC++) + r_X) & 0xFF));
                    if ((r_Y & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_Y == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xAC://LDY abs
                    r_Y = Mem_r((ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)));
                    if ((r_Y & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_Y == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xBC://LDY abs,x
                    us1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    r_Y = Mem_r((ushort)(us1 + r_X));
                    if ((r_Y & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_Y == 0) flagZ = 1; else flagZ = 0;
                    if ((us1 & 0xff00) != ((us1 + r_X) & 0xff00)) cpu_cycles++;
                    break;

                //----- LSR begin
                case 0x4A://LSR acc
                    if ((r_A & 0x01) > 0) flagC = 1; else flagC = 0;
                    r_A >>= 1;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0x46://LSR zp fix
                    b2 = Mem_r(r_PC++);
                    b1 = Mem_r(b2);
                    if ((b1 & 1) > 0) flagC = 1; else flagC = 0;
                    b1 >>= 1;
                    if ((b1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if ((b1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    Mem_w(b2, b1);
                    break;

                case 0x56://LSR zp,x
                    b2 = (byte)((Mem_r(r_PC++) + r_X) & 0xff);
                    b1 = Mem_r(b2);
                    if ((b1 & 1) > 0) flagC = 1; else flagC = 0;
                    b1 >>= 1;
                    if ((b1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if ((b1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    Mem_w(b2, b1);
                    break;

                case 0x4E://LSR abs fix
                    us2 = (ushort)(((Mem_r(r_PC++) << 0) | (Mem_r(r_PC++) << 8)));
                    b1 = Mem_r(us2);
                    if ((b1 & 1) > 0) flagC = 1; else flagC = 0;
                    b1 >>= 1;
                    if ((b1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if ((b1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    Mem_w(us2, b1);
                    break;

                case 0x5E://LSR abs,x
                    us2 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_X);
                    b1 = Mem_r(us2);
                    if ((b1 & 1) > 0) flagC = 1; else flagC = 0;
                    b1 >>= 1;
                    if ((b1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if ((b1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    Mem_w(us2, b1);
                    Mem_w(us2, b1);
                    break;
                //---- LSR END

                case 0xEA://NOP
                    break;

                //--- ORA BEGIN
                case 0x09: //ORA  Immediate  
                    b2 = Mem_r(r_PC++);
                    i1 = b2 | r_A;
                    if (i1 == 0) flagZ = 1; else flagZ = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)i1;
                    break;

                case 0x05: //ORA  Zero Page  
                    b2 = Mem_r(Mem_r(r_PC++));
                    i1 = b2 | r_A;
                    if (i1 == 0) flagZ = 1; else flagZ = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)i1;
                    break;

                case 0x15://ORA Zero Page,X 
                    b2 = Mem_r((byte)(Mem_r(r_PC++) + r_X));
                    i1 = b2 | r_A;
                    if (i1 == 0) flagZ = 1; else flagZ = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)i1;
                    break;

                case 0x0D: //ORA Absolute 
                    us3 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    b2 = Mem_r(us3);
                    i1 = b2 | r_A;
                    if (i1 == 0) flagZ = 1; else flagZ = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)i1;
                    break;

                case 0x1D: //ORA  Absolute,X  fix
                    us4 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    us3 = (ushort)(us4 + r_X);
                    b2 = Mem_r(us3);
                    i1 = b2 | r_A;
                    if (i1 == 0) flagZ = 1; else flagZ = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)i1;
                    if ((us4 & 0xff00) != (us3 & 0xff00)) cpu_cycles++;
                    break;

                case 0x19: //ORA  Absolute,Y
                    us4 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    us3 = (ushort)(us4 + r_Y);
                    b2 = Mem_r(us3);
                    i1 = b2 | r_A;
                    if (i1 == 0) flagZ = 1; else flagZ = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)i1;
                    if ((us4 & 0xff00) != (us3 & 0xff00)) cpu_cycles++;
                    break;

                case 0x01: //ORA (Indirect,X) 
                    b4 = (byte)(Mem_r(r_PC++) + r_X);
                    a1 = Mem_r(b4++);
                    a2 = Mem_r(b4);
                    us3 = (ushort)((a2 << 8) | a1);
                    b2 = Mem_r(us3);
                    i1 = b2 | r_A;// +(byte)flagC;
                    if (i1 == 0) flagZ = 1; else flagZ = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)i1;
                    break;

                case 0x11: //ORA (Indirect),Y
                    b4 = Mem_r(r_PC++);
                    a1 = Mem_r(b4++);
                    a2 = Mem_r(b4);
                    us3 = (ushort)((a2 << 8) | a1);
                    b2 = Mem_r((ushort)(us3 + r_Y));
                    i1 = b2 | r_A;// +(byte)flagC;
                    if (i1 == 0) flagZ = 1; else flagZ = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)i1;
                    if ((us3 & 0xff00) != ((us3 + r_Y) & 0xff00)) cpu_cycles++;
                    break;
                //--- ORA END    

                case 0x48://PHA
                    Mem_w((ushort)(r_SP + 0x100), r_A);
                    r_SP--;
                    break;

                case 0x08://PHP
                    b1 = (byte)((flagN << 7) | (flagV << 6) | (1 << 5) | (flagB << 4) | (flagD << 3) | (flagI << 2) | (flagZ << 1) | flagC | 0x30); // fix
                    Mem_w((ushort)(r_SP + 0x100), b1);
                    r_SP--;
                    break;

                case 0x68://PLA
                    r_SP++;
                    r_A = Mem_r((ushort)(r_SP + 0x100));
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x28://PLP
                    r_SP++;
                    b1 = Mem_r((ushort)(r_SP + 0x100));
                    flagN = (byte)((b1 & 0x80) >> 7);
                    flagV = (byte)((b1 & 0x40) >> 6);
                    flagB = (byte)((b1 & 0x10) >> 4);
                    flagD = (byte)((b1 & 0x8) >> 3);
                    flagI = (byte)((b1 & 0x4) >> 2);
                    flagZ = (byte)((b1 & 0x2) >> 1);
                    flagC = (byte)(b1 & 0x1);
                    break;

                //----ROL begin
                case 0x2A://ROL acc //fix
                    us1 = (ushort)(r_A << 1);
                    if (flagC == 1) us1 |= 0x1;
                    if ((r_A & 0x80) != 0) flagC = 1; else flagC = 0;
                    if ((us1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if ((us1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    r_A = (byte)us1;
                    break;

                case 0x26://ROL zp //fix
                    b3 = Mem_r(r_PC++);
                    b2 = Mem_r(b3);
                    us1 = (ushort)(b2 << 1);
                    if (flagC == 1) us1 |= 0x1;
                    if ((b2 & 0x80) != 0) flagC = 1; else flagC = 0;
                    if ((us1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if ((us1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    Mem_w((ushort)(b3 & 0xff), (byte)us1); //!!!!!
                    break;

                case 0x36://ROL zp,x
                    b3 = (byte)(Mem_r(r_PC++) + r_X);
                    b2 = Mem_r(b3);
                    us1 = (ushort)(b2 << 1);
                    if (flagC == 1) us1 |= 0x1;
                    if ((b2 & 0x80) != 0) flagC = 1; else flagC = 0;
                    if ((us1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if ((us1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    Mem_w((ushort)(b3 & 0xff), (byte)us1); //!!!!!
                    break;

                case 0x2E://ROL abs fix
                    us3 = (ushort)((Mem_r(r_PC++) | Mem_r(r_PC++) << 8));
                    b2 = Mem_r(us3);
                    us1 = (ushort)(b2 << 1);
                    if (flagC == 1) us1 |= 0x1;
                    if ((b2 & 0x80) != 0) flagC = 1; else flagC = 0;
                    if ((us1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if ((us1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    Mem_w(us3, (byte)us1); //!!!!!
                    break;

                case 0x3E://ROL abs,x fix
                    us3 = (ushort)((Mem_r(r_PC++) | Mem_r(r_PC++) << 8) + r_X);
                    b2 = Mem_r(us3);
                    us1 = (ushort)(b2 << 1);
                    if (flagC == 1) us1 |= 0x1;
                    if ((b2 & 0x80) != 0) flagC = 1; else flagC = 0;
                    if ((us1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if ((us1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    Mem_w(us3, (byte)us1);
                    break;
                //----ROL end

                //---- ROR begin
                case 0x6A://ROR acc
                    us1 = r_A;
                    if (flagC == 1) us1 |= 0x100;
                    if ((us1 & 0x01) > 0) flagC = 1; else flagC = 0;
                    us1 >>= 1;
                    if ((us1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (us1 == 0) flagZ = 1; else flagZ = 0;
                    r_A = (byte)us1;
                    break;

                case 0x66://ROR zp
                    b2 = Mem_r(r_PC++);
                    us1 = Mem_r(b2);
                    if (flagC == 1) us1 |= 0x100;
                    if ((us1 & 0x01) > 0) flagC = 1; else flagC = 0;
                    us1 >>= 1;
                    if ((us1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (us1 == 0) flagZ = 1; else flagZ = 0;
                    us1 = (byte)(us1 & 0xff);
                    Mem_w(b2, (byte)us1);
                    break;

                case 0x76://ROR zp,x
                    b2 = (byte)(Mem_r(r_PC++) + r_X);
                    us1 = Mem_r(b2);
                    if (flagC == 1) us1 |= 0x100;
                    if ((us1 & 0x01) > 0) flagC = 1; else flagC = 0;
                    us1 >>= 1;
                    if ((us1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (us1 == 0) flagZ = 1; else flagZ = 0;
                    us1 = (byte)(us1 & 0xff);
                    Mem_w(b2, (byte)us1);
                    break;

                case 0x6E://ROR abs
                    us2 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    us1 = Mem_r(us2);
                    if (flagC == 1) us1 |= 0x100;
                    if ((us1 & 0x01) > 0) flagC = 1; else flagC = 0;
                    us1 >>= 1;
                    if ((us1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (us1 == 0) flagZ = 1; else flagZ = 0;
                    us1 = (byte)(us1 & 0xff);
                    Mem_w(us2, (byte)us1);
                    break;

                case 0x7E://ROR abs,x
                    us2 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_X);
                    us1 = Mem_r(us2);
                    if (flagC == 1) us1 |= 0x100;
                    if ((us1 & 0x01) > 0) flagC = 1; else flagC = 0;
                    us1 >>= 1;
                    if ((us1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (us1 == 0) flagZ = 1; else flagZ = 0;
                    us1 = (byte)(us1 & 0xff);
                    Mem_w(us2, (byte)us1);
                    break;
                // ----ROR end

                case 0x40://RTI
                    r_SP++;
                    b1 = Mem_r((ushort)(r_SP + 0x100));
                    flagN = (byte)((b1 & 0x80) >> 7);
                    flagV = (byte)((b1 & 0x40) >> 6);
                    flagB = (byte)((b1 & 0x10) >> 4);
                    flagD = (byte)((b1 & 0x8) >> 3);
                    flagI = (byte)((b1 & 0x4) >> 2);
                    flagZ = (byte)((b1 & 0x2) >> 1);
                    flagC = (byte)(b1 & 0x1);
                    r_SP++;
                    b2 = Mem_r((ushort)(r_SP + 0x100));
                    r_SP++;
                    b3 = Mem_r((ushort)(r_SP + 0x100));
                    r_PC = (ushort)(b2 | (b3 << 8));
                    break;

                case 0x60://RTS
                    r_SP++;
                    b2 = Mem_r((ushort)(r_SP + 0x100));
                    r_SP++;
                    b3 = Mem_r((ushort)(r_SP + 0x100));
                    r_PC = (ushort)(b2 | (b3 << 8));
                    r_PC++;
                    break;

                //--- SBC BEGIN
                case 0xE9: //SBC  Immediate  
                    b2 = Mem_r(r_PC++);
                    b2 ^= 0xFF; //fix
                    i1 = r_A + b2 + (byte)flagC;
                    if ((i1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (i1 > 0xff) flagC = 1; else flagC = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (((i1 ^ r_A) & (i1 ^ b2) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)i1;
                    break;

                case 0xE5: //SBC  Zero Page  
                    b2 = Mem_r(Mem_r(r_PC++));
                    b2 ^= 0xFF; //fix
                    i1 = r_A + b2 + (byte)flagC;
                    if ((i1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (i1 > 0xff) flagC = 1; else flagC = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (((i1 ^ r_A) & (i1 ^ b2) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)i1;
                    break;

                case 0xF5://SBC Zero Page,X 
                    b2 = Mem_r((byte)(Mem_r(r_PC++) + r_X));
                    b2 ^= 0xFF; //fix
                    i1 = r_A + b2 + (byte)flagC;
                    if ((i1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (i1 > 0xff) flagC = 1; else flagC = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (((i1 ^ r_A) & (i1 ^ b2) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)i1;
                    break;

                case 0xED: //SBC Absolute fix
                    us3 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    b2 = Mem_r(us3);
                    b2 ^= 0xFF; //fix
                    i1 = r_A + b2 + (byte)flagC;
                    if ((i1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (i1 > 0xff) flagC = 1; else flagC = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (((i1 ^ r_A) & (i1 ^ b2) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)i1;
                    break;

                case 0xFD: //SBC  Absolute,X 
                    us4 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    us3 = (ushort)(us4 + r_X);
                    b2 = Mem_r(us3);
                    b2 ^= 0xFF; //fix
                    i1 = r_A + b2 + (byte)flagC;
                    if ((i1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (i1 > 0xff) flagC = 1; else flagC = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (((i1 ^ r_A) & (i1 ^ b2) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)i1;
                    if ((us4 & 0xff00) != (us3 & 0xff00)) cpu_cycles++;
                    break;

                case 0xF9: //SBC  Absolute,Y FIX
                    us4 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    us3 = (ushort)(us4 + r_Y);
                    b2 = Mem_r(us3);
                    b2 ^= 0xFF; //fix
                    i1 = r_A + b2 + (byte)flagC;
                    if ((i1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (i1 > 0xff) flagC = 1; else flagC = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (((i1 ^ r_A) & (i1 ^ b2) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)i1;
                    if ((us4 & 0xff00) != (us3 & 0xff00)) cpu_cycles++;
                    break;

                case 0xE1: //SBC (Indirect,X) 
                    b4 = (byte)(Mem_r(r_PC++) + r_X);
                    a1 = Mem_r(b4++);
                    a2 = Mem_r(b4);
                    us3 = (ushort)((a2 << 8) | a1);
                    b2 = Mem_r(us3);
                    b2 ^= 0xFF; //fix
                    i1 = r_A + b2 + (byte)flagC;
                    if ((i1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (i1 > 0xff) flagC = 1; else flagC = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (((i1 ^ r_A) & (i1 ^ b2) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)i1;
                    break;

                case 0xF1: //SBC (Indirect),Y
                    b4 = Mem_r(r_PC++);
                    a1 = Mem_r(b4++);
                    a2 = Mem_r(b4);
                    us3 = (ushort)((a2 << 8) | a1);
                    b2 = Mem_r((ushort)(us3 + r_Y));
                    b2 ^= 0xFF; //fix
                    i1 = r_A + b2 + (byte)flagC;
                    if ((i1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (i1 > 0xff) flagC = 1; else flagC = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (((i1 ^ r_A) & (i1 ^ b2) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)i1;
                    if ((us3 & 0xff00) != ((us3 + r_Y) & 0xff00)) cpu_cycles++;
                    break;

                //--- SBC END
                case 0x38://SEC
                    flagC = 1;
                    break;

                case 0xF8:// SED NES 6502 此 FLAG 無作用
                    flagD = 1;
                    break;

                case 0x78: //SEI
                    flagI = 1;
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
                    us1 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_X);
                    Mem_w(us1, r_A);
                    break;

                case 0x99://STA abs,Y
                    Mem_w((ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_Y), r_A);
                    break;

                case 0x81://STA (indirect,x)
                    b4 = (byte)(Mem_r(r_PC++) + r_X);
                    a1 = Mem_r(b4++);
                    a2 = Mem_r(b4);
                    us1 = (ushort)((a2 << 8) | a1);
                    Mem_w(us1, r_A);
                    break;

                case 0x91://STA (indirect),y
                    b4 = Mem_r(r_PC++);
                    a1 = Mem_r(b4++);
                    a2 = Mem_r(b4);
                    us1 = (ushort)((a2 << 8) | a1);
                    Mem_w((ushort)(us1 + r_Y), r_A);
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
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    r_X = r_A;
                    break;

                case 0xA8://TAY
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    r_Y = r_A;
                    break;

                case 0xBA://TSX
                    if ((r_SP & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_SP == 0) flagZ = 1; else flagZ = 0;
                    r_X = r_SP;
                    break;

                case 0x8A://TXA
                    if ((r_X & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_X == 0) flagZ = 1; else flagZ = 0;
                    r_A = r_X;
                    break;

                case 0x9A: //TXS
                    r_SP = r_X;
                    break;

                case 0x98: //TYA
                    if ((r_Y & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_Y == 0) flagZ = 1; else flagZ = 0;
                    r_A = r_Y;
                    break;

                #region illagel code

                // http://visual6502.org/wiki/index.php?title=6502_all_256_Opcodes
                // http://macgui.com/kb/article/46

                //do nothing
                case 0x1A:
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
                    b1 = Mem_r(r_PC++);
                    r_A = (byte)(((b1 & r_A) >> 1) | (((byte)flagC) << 7));
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    if ((r_A & 0x40) > 0) flagC = 1; else flagC = 0;
                    if (((r_A << 1 ^ r_A) & 0x40) > 0) flagV = 1; else flagV = 0;
                    break;

                case 0x0B: //ANC
                case 0x2B: //ANC
                    b2 = Mem_r(r_PC++);
                    r_A &= b2;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0X80) > 0) flagC = 1; else flagC = 0;
                    flagN = flagC;
                    break;

                case 0x4B: //ALR
                    b2 = Mem_r(r_PC++);
                    r_A &= b2;
                    if ((r_A & 0x1) != 0) flagC = 1; else flagC = 0;
                    r_A >>= 1;
                    if ((r_A & 0x80) != 0) flagN = 1; else flagN = 0;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xEB: //illegal sbc imm
                    b2 = Mem_r(r_PC++);
                    b2 ^= 0xFF; //fix
                    i1 = r_A + b2 + (byte)flagC;
                    if ((i1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (i1 > 0xff) flagC = 1; else flagC = 0;
                    if ((i1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (((i1 ^ r_A) & (i1 ^ b2) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)i1;
                    break;

                case 0x03: //SLO (  ASL M THEN (M "OR" A) -> A,M  )
                    b4 = (byte)(Mem_r(r_PC++) + r_X);
                    a1 = Mem_r(b4++);
                    a2 = Mem_r(b4);
                    us1 = (ushort)((a2 << 8) | a1);
                    b2 = Mem_r(us1);
                    if ((b2 & 0x80) > 0) flagC = 1; else flagC = 0;
                    b2 <<= 1;
                    Mem_w(us1, b2);
                    r_A |= b2;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x07: //SLO (  ASL M THEN (M "OR" A) -> A,M  )
                    b1 = Mem_r(r_PC++);
                    b2 = Mem_r(b1);
                    if ((b2 & 0x80) > 0) flagC = 1; else flagC = 0;
                    b2 <<= 1;
                    Mem_w(b1, b2);
                    r_A |= b2;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x13: //SLO (  ASL M THEN (M "OR" A) -> A,M  )
                    b4 = Mem_r(r_PC++);
                    a1 = Mem_r(b4++);
                    a2 = Mem_r(b4);
                    us1 = (ushort)((a2 << 8) | a1);
                    b2 = Mem_r((ushort)(us1 + r_Y));
                    if ((b2 & 0x80) > 0) flagC = 1; else flagC = 0;
                    b2 <<= 1;
                    Mem_w((ushort)(us1 + r_Y), b2);
                    r_A |= b2;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x17: //SLO (  ASL M THEN (M "OR" A) -> A,M  )
                    b1 = (byte)((Mem_r(r_PC++) + r_X) & 0xff);
                    b2 = Mem_r(b1);
                    if ((b2 & 0x80) > 0) flagC = 1; else flagC = 0;
                    b2 <<= 1;
                    Mem_w(b1, b2);
                    r_A |= b2;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x1B: //SLO (  ASL M THEN (M "OR" A) -> A,M  )
                    us1 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_Y);
                    b2 = Mem_r(us1);
                    if ((b2 & 0x80) > 0) flagC = 1; else flagC = 0;
                    b2 <<= 1;
                    Mem_w(us1, b2);
                    r_A |= b2;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x0F: //SLO (  ASL M THEN (M "OR" A) -> A,M  )
                    us1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    b2 = Mem_r((ushort)(us1));
                    if ((b2 & 0x80) > 0) flagC = 1; else flagC = 0;
                    b2 <<= 1;
                    Mem_w(us1, b2);
                    r_A |= b2;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x1F: //SLO (  ASL M THEN (M "OR" A) -> A,M  )
                    us4 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    us1 = (ushort)(us4 + r_X);
                    b2 = Mem_r(us1);
                    if ((b2 & 0x80) > 0) flagC = 1; else flagC = 0;
                    b2 <<= 1;
                    Mem_w(us1, b2);
                    r_A |= b2;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x23: //RLA    ( ROL M  THEN (M "AND" A) -> A )   
                    b4 = (byte)(Mem_r(r_PC++) + r_X);
                    a1 = Mem_r(b4++);
                    a2 = Mem_r(b4);
                    us1 = (ushort)((a2 << 8) | a1);
                    b2 = Mem_r(us1);
                    b3 = (byte)(b2 << 1);
                    b3 |= (byte)(flagC);
                    Mem_w(us1, b3);
                    if ((b2 & 0x80) > 0) flagC = 1; else flagC = 0;
                    r_A &= b3;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x27: //RLA    ( ROL M  THEN (M "AND" A) -> A )   
                    b1 = Mem_r(r_PC++);
                    b2 = Mem_r(b1);
                    b3 = (byte)(b2 << 1);
                    b3 |= (byte)(flagC);
                    Mem_w(b1, b3);
                    if ((b2 & 0x80) > 0) flagC = 1; else flagC = 0;
                    r_A &= b3;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x2F:// RLA
                    us1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    b2 = Mem_r((ushort)(us1));
                    b3 = (byte)(b2 << 1);
                    b3 |= (byte)(flagC);
                    Mem_w(us1, b3);
                    if ((b2 & 0x80) > 0) flagC = 1; else flagC = 0;
                    r_A &= b3;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x3F://RLA
                    us1 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_X);
                    b2 = Mem_r(us1);
                    b3 = (byte)(b2 << 1);
                    b3 |= (byte)(flagC);
                    Mem_w(us1, b3);
                    if ((b2 & 0x80) > 0) flagC = 1; else flagC = 0;
                    r_A &= b3;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x3B://RLA
                    us1 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_Y);
                    b2 = Mem_r(us1);
                    b3 = (byte)(b2 << 1);
                    b3 |= (byte)(flagC);
                    Mem_w(us1, b3);
                    if ((b2 & 0x80) > 0) flagC = 1; else flagC = 0;
                    r_A &= b3;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x33: //RLA    ( ROL M  THEN (M "AND" A) -> A )   
                    b4 = Mem_r(r_PC++);
                    a1 = Mem_r(b4++);
                    a2 = Mem_r(b4);
                    us1 = (ushort)((a2 << 8) | a1);
                    b2 = Mem_r((ushort)(us1 + r_Y));
                    b3 = (byte)(b2 << 1);
                    b3 |= (byte)(flagC);
                    Mem_w((ushort)(us1 + r_Y), b3);
                    if ((b2 & 0x80) > 0) flagC = 1; else flagC = 0;
                    r_A &= b3;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x37: //RLA    ( ROL M  THEN (M "AND" A) -> A )   
                    b1 = (byte)((Mem_r(r_PC++) + r_X) & 0xff);
                    b2 = Mem_r(b1);
                    b3 = (byte)(b2 << 1);
                    b3 |= (byte)(flagC);
                    Mem_w(b1, b3);
                    if ((b2 & 0x80) > 0) flagC = 1; else flagC = 0;
                    r_A &= b3;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x43://SRE (LSR M  THEN (M "EOR" A) -> A )
                    b4 = Mem_r(r_PC++);
                    b4 += r_X;
                    a1 = Mem_r(b4);
                    b4++;
                    a2 = Mem_r(b4);
                    us1 = (ushort)((a2 << 8) | a1);
                    b2 = Mem_r(us1);
                    if ((b2 & 1) > 0) flagC = 1; else flagC = 0;
                    b2 >>= 1;
                    Mem_w(us1, b2);
                    r_A ^= b2;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x47://SRE (LSR M  THEN (M "EOR" A) -> A )
                    b1 = Mem_r(r_PC++);
                    b2 = Mem_r(b1);
                    if ((b2 & 1) > 0) flagC = 1; else flagC = 0;
                    b2 >>= 1;
                    Mem_w(b1, b2);
                    r_A ^= b2;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x4F://SRE (LSR M  THEN (M "EOR" A) -> A )
                    us1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    b2 = Mem_r((ushort)(us1));
                    if ((b2 & 1) > 0) flagC = 1; else flagC = 0;
                    b2 >>= 1;
                    Mem_w(us1, b2);
                    r_A ^= b2;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x5F://SRE  
                    us1 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_X);
                    b2 = Mem_r(us1);
                    if ((b2 & 1) != 0) flagC = 1; else flagC = 0;
                    b2 >>= 1;
                    Mem_w(us1, b2);
                    r_A ^= b2;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) != 0) flagN = 1; else flagN = 0;
                    break;

                case 0x5B://SRE  
                    us1 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_Y);
                    b2 = Mem_r(us1);
                    if ((b2 & 1) != 0) flagC = 1; else flagC = 0;
                    b2 >>= 1;
                    Mem_w(us1, b2);
                    r_A ^= b2;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) != 0) flagN = 1; else flagN = 0;
                    break;

                case 0x53://SRE (LSR M  THEN (M "EOR" A) -> A )
                    b4 = Mem_r(r_PC++);
                    a1 = Mem_r(b4++);
                    a2 = Mem_r(b4);
                    us1 = (ushort)((a2 << 8) | a1);
                    b2 = Mem_r((ushort)(us1 + r_Y));
                    if ((b2 & 1) > 0) flagC = 1; else flagC = 0;
                    b2 >>= 1;
                    Mem_w((ushort)(us1 + r_Y), b2);
                    r_A ^= b2;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x57://SRE (LSR M  THEN (M "EOR" A) -> A )
                    b1 = (byte)((Mem_r(r_PC++) + r_X) & 0xff);
                    b2 = Mem_r(b1);
                    if ((b2 & 1) > 0) flagC = 1; else flagC = 0;
                    b2 >>= 1;
                    Mem_w(b1, b2);
                    r_A ^= b2;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x63:// RRA (ROR M THEN (A + M + C) -> A  )
                    b4 = (byte)(Mem_r(r_PC++) + r_X);
                    a1 = Mem_r(b4++);
                    a2 = Mem_r(b4);
                    us1 = (ushort)((a2 << 8) | a1);
                    b2 = Mem_r(us1);
                    byte c = 0x80;
                    if (flagC == 0) c = 0;
                    b3 = (byte)((b2 >> 1) | c);
                    Mem_w(us1, (byte)b3);
                    if ((b2 & 1) > 0) flagC = 1; else flagC = 0;
                    i5 = r_A + b3 + (byte)flagC;
                    if ((i5 & 0x80) != 0) flagN = 1; else flagN = 0;
                    if (((i5 ^ r_A) & (i5 ^ b3) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)i5;
                    if ((i5 >> 8) > 0) flagC = 1; else flagC = 0;
                    if ((i5 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0x67:// RRA (ROR M THEN (A + M + C) -> A  )
                    b1 = Mem_r(r_PC++);
                    b2 = Mem_r(b1);
                    b3 = (byte)((b2 >> 1) | ((flagC == 0) ? 0 : 0x80));
                    Mem_w(b1, (byte)b3);
                    if ((b2 & 1) > 0) flagC = 1; else flagC = 0;
                    i5 = r_A + b3 + (byte)flagC;
                    if ((i5 & 0x80) != 0) flagN = 1; else flagN = 0;
                    if (((i5 ^ r_A) & (i5 ^ b3) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)i5;
                    if ((i5 >> 8) > 0) flagC = 1; else flagC = 0;
                    if ((i5 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0x6F://RRA
                    us1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    b2 = Mem_r((ushort)(us1));
                    b3 = (byte)((b2 >> 1) | ((flagC == 0) ? 0 : 0x80));
                    Mem_w(us1, b3);
                    if ((b2 & 1) > 0) flagC = 1; else flagC = 0;
                    i5 = r_A + b3 + (byte)flagC;
                    if ((i5 & 0x80) != 0) flagN = 1; else flagN = 0;
                    if (((i5 ^ r_A) & (i5 ^ b3) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)i5;
                    if ((i5 >> 8) > 0) flagC = 1; else flagC = 0;
                    if ((i5 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0x73:// RRA (ROR M THEN (A + M + C) -> A  )
                    b4 = Mem_r(r_PC++);
                    a1 = Mem_r(b4++);
                    a2 = Mem_r(b4);
                    us1 = (ushort)((a2 << 8) | a1);
                    b2 = Mem_r((ushort)(us1 + r_Y));
                    b3 = (byte)((b2 >> 1) | ((flagC == 0) ? 0 : 0x80));
                    Mem_w((ushort)(us1 + r_Y), (byte)b3);
                    if ((b2 & 1) > 0) flagC = 1; else flagC = 0;
                    i5 = r_A + b3 + (byte)flagC;
                    if ((i5 & 0x80) != 0) flagN = 1; else flagN = 0;
                    if (((i5 ^ r_A) & (i5 ^ b3) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)i5;
                    if ((i5 >> 8) > 0) flagC = 1; else flagC = 0;
                    if ((i5 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0x77:// RRA (ROR M THEN (A + M + C) -> A  )
                    b1 = (byte)((Mem_r(r_PC++) + r_X) & 0xff);
                    b2 = Mem_r(b1);
                    b3 = (byte)((b2 >> 1) | ((flagC == 0) ? 0 : 0x80));
                    Mem_w(b1, b3);
                    if ((b2 & 1) > 0) flagC = 1; else flagC = 0;
                    i5 = r_A + b3 + (byte)flagC;
                    if ((i5 & 0x80) != 0) flagN = 1; else flagN = 0;
                    if (((i5 ^ r_A) & (i5 ^ b3) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)i5;
                    if ((i5 >> 8) > 0) flagC = 1; else flagC = 0;
                    if ((i5 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0x7B:// RRA (ROR M THEN (A + M + C) -> A  )
                    us1 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_Y);
                    b2 = Mem_r(us1);
                    b3 = (byte)((b2 >> 1) | ((flagC == 0) ? 0 : 0x80));
                    Mem_w(us1, b3);
                    if ((b2 & 1) > 0) flagC = 1; else flagC = 0;
                    i5 = r_A + b3 + (byte)flagC;
                    if ((i5 & 0x80) != 0) flagN = 1; else flagN = 0;
                    if (((i5 ^ r_A) & (i5 ^ b3) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)i5;
                    if ((i5 >> 8) > 0) flagC = 1; else flagC = 0;
                    if ((i5 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0x7F: //RRA
                    us1 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_X);
                    b2 = Mem_r(us1);
                    b3 = (byte)((b2 >> 1) | ((flagC == 0) ? 0 : 0x80));
                    Mem_w(us1, b3);
                    if ((b2 & 1) > 0) flagC = 1; else flagC = 0;
                    i5 = r_A + b3 + (byte)flagC;
                    if ((i5 & 0x80) != 0) flagN = 1; else flagN = 0;
                    if (((i5 ^ r_A) & (i5 ^ b3) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)i5;
                    if ((i5 >> 8) > 0) flagC = 1; else flagC = 0;
                    if ((i5 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0x83://SAX ( (A "AND" (MSB(adr)+1)  "AND" X) -> M 
                    b4 = (byte)(Mem_r(r_PC++) + r_X);
                    a1 = Mem_r(b4++);
                    a2 = Mem_r(b4);
                    us1 = (ushort)((a2 << 8) | a1);
                    b2 = Mem_r(us1);
                    Mem_w(us1, (byte)(r_X & r_A));
                    break;

                case 0x87://SAX ( (A "AND" (MSB(adr)+1)  "AND" X) -> M 
                    b1 = Mem_r(r_PC++);
                    b2 = Mem_r(b1);
                    Mem_w(b1, (byte)(r_X & r_A));
                    break;

                case 0x8F://SAX ( (A "AND" (MSB(adr)+1)  "AND" X) -> M 
                    us1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    b2 = Mem_r((ushort)(us1));
                    Mem_w(us1, (byte)(r_X & r_A));
                    break;

                case 0x9C://SHY
                    us1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    b2 = Mem_r(us1);
                    b3 = (byte)(r_Y & (((us1 & 0xff00) >> 8) + 1));
                    us1 = (ushort)((us1 & 0xff00) | (byte)((us1 & 0xff) + r_X));
                    if ((us1 & 0xff) < r_X) us1 = (ushort)((us1 & 0xff) | (b3 << 8));
                    Mem_w(us1, b3);
                    break;

                case 0x9E://SHX
                    us1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    b2 = Mem_r(us1);
                    b3 = (byte)(r_X & (((us1 & 0xff00) >> 8) + 1));
                    us1 = (ushort)((us1 & 0xff00) | (byte)((us1 & 0xff) + r_Y));
                    if ((us1 & 0xff) < r_Y) us1 = (ushort)((us1 & 0xff) | (b3 << 8));
                    Mem_w(us1, b3);
                    break;

                case 0x97://SAX ( (A "AND" (MSB(adr)+1)  "AND" X) -> M 
                    b1 = (byte)((Mem_r(r_PC++) + r_Y) & 0xff);
                    b2 = Mem_r(b1);
                    Mem_w(b1, (byte)(r_X & r_A));
                    break;

                case 0xB7://SAX ( (A "AND" (MSB(adr)+1)  "AND" X) -> M 
                    b1 = (byte)((Mem_r(r_PC++) + r_Y) & 0xff);
                    b2 = Mem_r(b1);
                    r_X = r_A = b2;
                    if (r_X == 0) flagZ = 1; else flagZ = 0;
                    if ((r_X & 0x80) != 0) flagN = 1; else flagN = 0;
                    break;

                case 0xA3://LAX
                    b4 = (byte)(Mem_r(r_PC++) + r_X);
                    a1 = Mem_r(b4++);
                    a2 = Mem_r(b4);
                    b2 = Mem_r((ushort)((a2 << 8) | a1));
                    r_X = r_A = b2;
                    if (r_X == 0) flagZ = 1; else flagZ = 0;
                    if ((r_X & 0x80) != 0) flagN = 1; else flagN = 0;
                    break;

                case 0xA7://LAX
                    b1 = Mem_r(r_PC++);
                    b2 = Mem_r(b1);
                    r_X = r_A = b2;
                    if (r_X == 0) flagZ = 1; else flagZ = 0;
                    if ((r_X & 0x80) != 0) flagN = 1; else flagN = 0;
                    break;

                case 0xAB://LAX
                    b1 = Mem_r(r_PC++);
                    r_X = r_A = b1;
                    if (r_X == 0) flagZ = 1; else flagZ = 0;
                    if ((r_X & 0x80) != 0) flagN = 1; else flagN = 0;
                    break;

                case 0xAF://LAX
                    us1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    b2 = Mem_r((ushort)(us1));
                    r_X = r_A = b2;
                    if (r_X == 0) flagZ = 1; else flagZ = 0;
                    if ((r_X & 0x80) != 0) flagN = 1; else flagN = 0;
                    break;

                case 0xBF://LAX
                    us1 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_Y);
                    b2 = Mem_r(us1);
                    r_X = r_A = b2;
                    if (r_X == 0) flagZ = 1; else flagZ = 0;
                    if ((r_X & 0x80) != 0) flagN = 1; else flagN = 0;
                    break;

                case 0xB3://LAX
                    b4 = Mem_r(r_PC++);
                    a1 = Mem_r(b4);
                    a2 = Mem_r(++b4);
                    us1 = (ushort)((a2 << 8) | a1);
                    b2 = Mem_r((ushort)(us1 + r_Y));
                    r_X = r_A = b2;
                    if (r_X == 0) flagZ = 1; else flagZ = 0;
                    if ((r_X & 0x80) != 0) flagN = 1; else flagN = 0;
                    break;

                case 0xC3: //DCP
                    b1 = (byte)(Mem_r(r_PC++) + r_X);
                    us3 = (ushort)((Mem_r(b1++) | (Mem_r(b1) << 8)));
                    b2 = Mem_r(us3);
                    Mem_w(us3, --b2);
                    i4 = r_A - b2;
                    if (i4 == 0) flagZ = 1; else flagZ = 0;
                    if ((~i4) >> 8 != 0) flagC = 1; else flagC = 0;
                    if ((i4 & 0X80) != 0) flagN = 1; else flagN = 0;
                    break;

                case 0xC7: //DCP
                    b1 = Mem_r(r_PC++);
                    b2 = Mem_r(b1);
                    Mem_w(b1, --b2);
                    i4 = r_A - b2;
                    if (i4 == 0) flagZ = 1; else flagZ = 0;
                    if ((~i4) >> 8 != 0) flagC = 1; else flagC = 0;
                    if ((i4 & 0X80) != 0) flagN = 1; else flagN = 0;
                    break;

                case 0xCB:// AXS
                    b1 = Mem_r(r_PC++);
                    i2 = (r_A & r_X) - b1;
                    if ((i2 & 0x80) != 0) flagN = 1; else flagN = 0;
                    if ((byte)i2 == 0) flagZ = 1; else flagZ = 0;
                    if ((~i2 >> 8) != 0) flagC = 1; else flagC = 0;
                    r_X = (byte)i2;
                    break;

                case 0xCF: //DCP
                    us1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    b2 = Mem_r((ushort)(us1));
                    Mem_w(us1, --b2);
                    i4 = r_A - b2;
                    if (i4 == 0) flagZ = 1; else flagZ = 0;
                    if ((~i4) >> 8 != 0) flagC = 1; else flagC = 0;
                    if ((i4 & 0X80) != 0) flagN = 1; else flagN = 0;
                    break;

                case 0xDF:
                    us1 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_X);
                    b2 = Mem_r(us1);
                    Mem_w(us1, --b2);
                    i4 = r_A - b2;
                    if (i4 == 0) flagZ = 1; else flagZ = 0;
                    if ((~i4) >> 8 != 0) flagC = 1; else flagC = 0;
                    if ((i4 & 0X80) != 0) flagN = 1; else flagN = 0;
                    break;

                case 0xD3: //DCP
                    b1 = Mem_r(r_PC++);
                    us3 = (ushort)((Mem_r(b1++) | (Mem_r(b1) << 8)) + r_Y);
                    b2 = Mem_r(us3);
                    Mem_w(us3, --b2);
                    i4 = r_A - b2;
                    if (i4 == 0) flagZ = 1; else flagZ = 0;
                    if ((~i4) >> 8 != 0) flagC = 1; else flagC = 0;
                    if ((i4 & 0X80) != 0) flagN = 1; else flagN = 0;
                    break;

                case 0xD7: //DCP
                    b1 = (byte)((Mem_r(r_PC++) + r_X) & 0xff);
                    b2 = Mem_r(b1);
                    Mem_w(b1, --b2);
                    i4 = r_A - b2;
                    if (i4 == 0) flagZ = 1; else flagZ = 0;
                    if ((~i4) >> 8 != 0) flagC = 1; else flagC = 0;
                    if ((i4 & 0X80) != 0) flagN = 1; else flagN = 0;
                    break;

                case 0xDB:// DCP
                    us1 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_Y);
                    b2 = Mem_r(us1);
                    Mem_w(us1, --b2);
                    i4 = r_A - b2;
                    if (i4 == 0) flagZ = 1; else flagZ = 0;
                    if ((~i4) >> 8 != 0) flagC = 1; else flagC = 0;
                    if ((i4 & 0X80) != 0) flagN = 1; else flagN = 0;
                    break;

                case 0xE3://ISC
                    b1 = (byte)(Mem_r(r_PC++) + r_X);
                    us3 = (ushort)((Mem_r(b1++) | (Mem_r(b1) << 8)));
                    b2 = Mem_r(us3);
                    Mem_w(us3, ++b2);
                    i4 = r_A + (b2 ^ 0xff) + (byte)flagC;
                    if (((i4 ^ r_A) & (i4 ^ (b2 ^ 0xff)) & 0x80) != 0) flagV = 1; else flagV = 0;
                    if ((i4 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if ((i4) >> 8 != 0) flagC = 1; else flagC = 0;
                    if ((i4 & 0X80) != 0) flagN = 1; else flagN = 0;
                    r_A = (byte)i4;
                    break;

                case 0xE7://ISC
                    b1 = Mem_r(r_PC++);
                    b2 = Mem_r(b1);
                    Mem_w(b1, ++b2);
                    i4 = r_A + (b2 ^ 0xff) + (byte)flagC;
                    if (((i4 ^ r_A) & (i4 ^ (b2 ^ 0xff)) & 0x80) != 0) flagV = 1; else flagV = 0;
                    if ((i4 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if ((i4) >> 8 != 0) flagC = 1; else flagC = 0;
                    if ((i4 & 0X80) != 0) flagN = 1; else flagN = 0;
                    r_A = (byte)i4;
                    break;

                case 0xEF://ISC
                    us1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    b2 = Mem_r((ushort)(us1));
                    Mem_w(us1, ++b2);
                    i4 = r_A + (b2 ^ 0xff) + (byte)flagC;
                    if (((i4 ^ r_A) & (i4 ^ (b2 ^ 0xff)) & 0x80) != 0) flagV = 1; else flagV = 0;
                    if ((i4 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if ((i4) >> 8 != 0) flagC = 1; else flagC = 0;
                    if ((i4 & 0X80) != 0) flagN = 1; else flagN = 0;
                    r_A = (byte)i4;
                    break;


                case 0xF3://ISC
                    b1 = Mem_r(r_PC++);
                    us3 = (ushort)(((Mem_r(b1++) | (Mem_r(b1) << 8))) + r_Y);
                    b2 = Mem_r(us3);
                    Mem_w(us3, ++b2);
                    i4 = r_A + (b2 ^ 0xff) + (byte)flagC;
                    if (((i4 ^ r_A) & (i4 ^ (b2 ^ 0xff)) & 0x80) != 0) flagV = 1; else flagV = 0;
                    if ((i4 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if ((i4) >> 8 != 0) flagC = 1; else flagC = 0;
                    if ((i4 & 0X80) != 0) flagN = 1; else flagN = 0;
                    r_A = (byte)i4;
                    break;

                case 0xF7://ISC
                    b1 = (byte)((Mem_r(r_PC++) + r_X) & 0xff);
                    b2 = Mem_r(b1);
                    Mem_w(b1, ++b2);
                    i4 = r_A + (b2 ^ 0xff) + (byte)flagC;
                    if (((i4 ^ r_A) & (i4 ^ (b2 ^ 0xff)) & 0x80) != 0) flagV = 1; else flagV = 0;
                    if ((i4 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if ((i4) >> 8 != 0) flagC = 1; else flagC = 0;
                    if ((i4 & 0X80) != 0) flagN = 1; else flagN = 0;
                    r_A = (byte)i4;
                    break;

                case 0xFB://ISC
                    us1 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_Y);
                    b2 = Mem_r(us1);
                    Mem_w(us1, ++b2);
                    i4 = r_A + (b2 ^ 0xff) + (byte)flagC;
                    if (((i4 ^ r_A) & (i4 ^ (b2 ^ 0xff)) & 0x80) != 0) flagV = 1; else flagV = 0;
                    if ((i4 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if ((i4) >> 8 != 0) flagC = 1; else flagC = 0;
                    if ((i4 & 0X80) != 0) flagN = 1; else flagN = 0;
                    r_A = (byte)i4;
                    break;

                case 0xFF://ISC
                    us1 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_X);
                    b2 = Mem_r(us1);
                    Mem_w(us1, ++b2);
                    i4 = r_A + (b2 ^ 0xff) + (byte)flagC;
                    if (((i4 ^ r_A) & (i4 ^ (b2 ^ 0xff)) & 0x80) != 0) flagV = 1; else flagV = 0;
                    if ((i4 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if ((i4) >> 8 != 0) flagC = 1; else flagC = 0;
                    if ((i4 & 0X80) != 0) flagN = 1; else flagN = 0;
                    r_A = (byte)i4;
                    break;
                #endregion



                default:
                    MessageBox.Show("unkonw opcode ! - 0x" + opcode.ToString("X2"));
                    break;
            }
        }
    }
}
