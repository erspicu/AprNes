#define illegal
using System.Windows.Forms;
using System;

namespace AprNes
{
    unsafe public partial class NesCore
    {
        //table port from  https://github.com/bfirsh/jsnes/blob/master/source/cpu.js
        byte[] cycle_tableData = new byte[]{
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
        byte* cycle_table;
        byte r_A = 0, r_X = 0, r_Y = 0, r_SP = 0xFD, flagN = 0, flagV = 0, flagD = 0, flagI = 0, flagZ = 0, flagC = 0;
        ushort r_PC = 0 ;
        byte opcode;

        int cpu_cycles = 0, Interrupt_cycle = 0;
        public bool exit = false;

        byte GetFlag()
        {
            return (byte)((flagN << 7) | (flagV << 6) | (flagD << 3) | (flagI << 2) | (flagZ << 1) | flagC);
        }

        void SetFlag(byte flag)
        {
            flagN = (byte)((flag & 0x80) >> 7);
            flagV = (byte)((flag & 0x40) >> 6);
            flagD = (byte)((flag & 0x8) >> 3);
            flagI = (byte)((flag & 0x4) >> 2);
            flagZ = (byte)((flag & 0x2) >> 1);
            flagC = (byte)(flag & 0x1);
        }

        void NMIInterrupt()
        {
            Mem_w((ushort)(0x100 + r_SP--), (byte)(r_PC >> 8));
            Mem_w((ushort)(0x100 + r_SP--), (byte)r_PC);
            Mem_w((ushort)(0x100 + r_SP--), (byte)(GetFlag() | 0x20));
            r_PC = (ushort)(Mem_r(0xfffa) | (Mem_r(0xfffb) << 8));
            flagI = 1;
            Interrupt_cycle = 7;
        }

        void IRQInterrupt()
        {
            Mem_w((ushort)(0x100 + r_SP--), (byte)(r_PC >> 8));
            Mem_w((ushort)(0x100 + r_SP--), (byte)r_PC);
            Mem_w((ushort)(0x100 + r_SP--), (byte)(GetFlag() | 0x20));
            r_PC = (ushort)(Mem_r(0xfffe) | (Mem_r(0xffff) << 8));
            flagI = 1;
            Interrupt_cycle = 7;
        }

        void cpu_step()
        {
            ushort ushort1, ushort2, ushort3;
            byte byte1, byte2, byte3;
            int int1;

            opcode = Mem_r(r_PC++);
            cpu_cycles = cycle_table[opcode];

            cpu_cycles += Interrupt_cycle;
            Interrupt_cycle = 0;

            //參考了 mynes 去修正與debug許多錯誤 http://sourceforge.net/projects/mynes 
            switch (opcode)
            {
                case 0x69: //ADC  Immediate  fix
                    byte1 = Mem_r(r_PC++);
                    int1 = byte1 + r_A + flagC;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (int1 > 0xff) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (((int1 ^ r_A) & (int1 ^ byte1) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)int1;
                    break;

                case 0x65: //ADC  Zero Page  
                    byte1 = NES_MEM[Mem_r(r_PC++)];
                    int1 = byte1 + r_A + flagC;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (int1 > 0xff) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (((int1 ^ r_A) & (int1 ^ byte1) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)int1;
                    break;

                case 0x75://ADC Zero Page,X 
                    byte1 = NES_MEM[(byte)(Mem_r(r_PC++) + r_X)];
                    int1 = byte1 + r_A + flagC;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (int1 > 0xff) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (((int1 ^ r_A) & (int1 ^ byte1) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)int1;
                    break;

                case 0x6D: //ADC Absolute //fix
                    byte1 = Mem_r((ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)));
                    int1 = byte1 + r_A + flagC;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (int1 > 0xff) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (((int1 ^ r_A) & (int1 ^ byte1) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)int1;
                    break;

                case 0x7D: //ADC  Absolute,X 
                    ushort1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    ushort2 = (ushort)(ushort1 + r_X);
                    byte1 = Mem_r(ushort2);
                    int1 = byte1 + r_A + flagC;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (int1 > 0xff) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (((int1 ^ r_A) & (int1 ^ byte1) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)int1;
                    if ((ushort1 & 0xff00) != (ushort2 & 0xff00)) cpu_cycles++;
                    break;

                case 0x79: //ADC  Absolute,Y fix
                    ushort1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    ushort2 = (ushort)(ushort1 + r_Y);
                    byte1 = Mem_r(ushort2);
                    int1 = byte1 + r_A + flagC;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (int1 > 0xff) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (((int1 ^ r_A) & (int1 ^ byte1) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)int1;
                    if ((ushort1 & 0xff00) != (ushort2 & 0xff00)) cpu_cycles++;
                    break;

                case 0x61: //ADC (Indirect,X) 
                    byte2 = (byte)(Mem_r(r_PC++) + r_X);
                    byte1 = Mem_r((ushort)(NES_MEM[byte2++] | (NES_MEM[byte2] << 8)));
                    int1 = byte1 + r_A + flagC;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (int1 > 0xff) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (((int1 ^ r_A) & (int1 ^ byte1) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)int1;
                    break;

                case 0x71: //ADC (Indirect),Y                    
                    byte2 = Mem_r(r_PC++);
                    ushort1 = (ushort)(NES_MEM[byte2++] | (NES_MEM[byte2] << 8));
                    byte1 = Mem_r((ushort)(ushort1 + r_Y));
                    int1 = byte1 + r_A + flagC;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (int1 > 0xff) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (((int1 ^ r_A) & (int1 ^ byte1) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)int1;
                    if ((ushort1 & 0xff00) != ((ushort1 + r_Y) & 0xff00)) cpu_cycles++;
                    break;

                //--- AND BEGIN
                case 0x29: //AND  Immediate  
                    int1 = Mem_r(r_PC++) & r_A;
                    if (int1 == 0) flagZ = 1; else flagZ = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)int1;
                    break;

                case 0x25: //AND  Zero Page  
                    int1 = NES_MEM[Mem_r(r_PC++)] & r_A;
                    if (int1 == 0) flagZ = 1; else flagZ = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)int1;
                    break;

                case 0x35://AND Zero Page,X 
                    int1 = NES_MEM[(byte)(Mem_r(r_PC++) + r_X)] & r_A;
                    if (int1 == 0) flagZ = 1; else flagZ = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)int1;
                    break;

                case 0x2D: //AND Absolute 
                    int1 = Mem_r((ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8))) & r_A;
                    if (int1 == 0) flagZ = 1; else flagZ = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)int1;
                    break;

                case 0x3D: //AND  Absolute,X 
                    ushort1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    ushort2 = (ushort)(ushort1 + r_X);
                    int1 = Mem_r(ushort2) & r_A;
                    if (int1 == 0) flagZ = 1; else flagZ = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)int1;
                    if ((ushort1 & 0xff00) != (ushort2 & 0xff00)) cpu_cycles++;
                    break;

                case 0x39: //AND  Absolute,Y
                    ushort1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    ushort2 = (ushort)(ushort1 + r_Y);
                    int1 = Mem_r(ushort2) & r_A;
                    if (int1 == 0) flagZ = 1; else flagZ = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)int1;
                    if ((ushort1 & 0xff00) != (ushort2 & 0xff00)) cpu_cycles++;
                    break;

                case 0x21: //AND (Indirect,X) 
                    byte1 = (byte)(Mem_r(r_PC++) + r_X);
                    ushort1 = (ushort)(NES_MEM[byte1++] | (NES_MEM[byte1] << 8));
                    int1 = Mem_r(ushort1) & r_A;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)int1;
                    break;

                case 0x31: //AND (Indirect),Y
                    byte1 = Mem_r(r_PC++);
                    ushort1 = (ushort)(NES_MEM[byte1++] | (NES_MEM[byte1] << 8));
                    int1 = Mem_r((ushort)(ushort1 + r_Y)) & r_A;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)int1;
                    if ((ushort1 & 0xff00) != ((ushort1 + r_Y) & 0xff00)) cpu_cycles++;
                    break;
                //--- AND END 

                case 0x0A://ASL acc
                    if ((r_A & 0x80) > 0) flagC = 1; else flagC = 0;
                    r_A <<= 1;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x06://ASL zp
                    byte2 = Mem_r(r_PC++);
                    byte1 = NES_MEM[byte2];
                    if ((byte1 & 0x80) > 0) flagC = 1; else flagC = 0;
                    byte1 <<= 1;
                    if (byte1 == 0) flagZ = 1; else flagZ = 0;
                    if ((byte1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    NES_MEM[byte2] = byte1;
                    break;

                case 0x16://ASL zp,x
                    byte2 = (byte)(Mem_r(r_PC++) + r_X);
                    byte1 = NES_MEM[byte2];
                    if ((byte1 & 0x80) > 0) flagC = 1; else flagC = 0;
                    byte1 <<= 1;
                    if (byte1 == 0) flagZ = 1; else flagZ = 0;
                    if ((byte1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    NES_MEM[byte2] = byte1;
                    break;

                case 0x0E://ASL abs
                    ushort2 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)));
                    byte1 = Mem_r(ushort2);
                    if ((byte1 & 0x80) > 0) flagC = 1; else flagC = 0;
                    byte1 <<= 1;
                    if (byte1 == 0) flagZ = 1; else flagZ = 0;
                    if ((byte1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    Mem_w(ushort2, byte1);
                    break;

                case 0x1E://ASL abs,x
                    ushort2 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_X);
                    byte1 = Mem_r(ushort2);
                    if ((byte1 & 0x80) > 0) flagC = 1; else flagC = 0;
                    byte1 <<= 1;
                    if (byte1 == 0) flagZ = 1; else flagZ = 0;
                    if ((byte1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    Mem_w(ushort2, byte1);
                    break;

                case 0x90://BCC
                    ushort1 = (ushort)((sbyte)Mem_r(r_PC++) + r_PC);
                    if (flagC == 0)
                    {
                        if ((ushort1 & 0xff00) != (((r_PC - 2) & 0xff00))) cpu_cycles += 2; else cpu_cycles += 1;
                        r_PC = ushort1;
                    }
                    break;

                case 0xB0://BCS
                    ushort1 = (ushort)((sbyte)Mem_r(r_PC++) + r_PC);
                    if (flagC == 1)
                    {
                        if ((ushort1 & 0xff00) != (((r_PC - 2) & 0xff00))) cpu_cycles += 2; else cpu_cycles += 1;
                        r_PC = ushort1;
                    }
                    break;

                case 0xF0://BEQ
                    ushort1 = (ushort)((sbyte)Mem_r(r_PC++) + r_PC);
                    if (flagZ == 1)
                    {
                        if ((ushort1 & 0xff00) != (((r_PC - 2) & 0xff00))) cpu_cycles += 2; else cpu_cycles += 1;
                        r_PC = ushort1;
                    }
                    break;

                case 0x24://BIT zp fix
                    byte1 = NES_MEM[Mem_r(r_PC++)];
                    if ((byte1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if ((byte1 & 0x40) > 0) flagV = 1; else flagV = 0;
                    if ((byte1 & r_A) == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0x2C://BIT abs //FIX
                    ushort t1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    byte1 = Mem_r(t1);
                    if ((byte1 & 0x80) != 0) flagN = 1; else flagN = 0;
                    if ((byte1 & 0x40) > 0) flagV = 1; else flagV = 0;
                    if ((byte1 & r_A) == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0x30://BMI
                    ushort1 = (ushort)((sbyte)Mem_r(r_PC++) + r_PC);
                    if (flagN == 1)
                    {
                        if ((ushort1 & 0xff00) != (((r_PC - 2) & 0xff00))) cpu_cycles += 2; else cpu_cycles += 1;
                        r_PC = ushort1;
                    }
                    break;

                case 0xD0://BNE
                    ushort1 = (ushort)((sbyte)Mem_r(r_PC++) + r_PC);
                    if (flagZ == 0)
                    {
                        if ((ushort1 & 0xff00) != (((r_PC - 2) & 0xff00))) cpu_cycles += 2; else cpu_cycles += 1;
                        r_PC = ushort1;
                    }
                    break;

                case 0x10://BPL
                    ushort1 = (ushort)((sbyte)Mem_r(r_PC++) + r_PC);
                    if (flagN == 0)
                    {
                        if ((ushort1 & 0xff00) != (((r_PC - 2) & 0xff00))) cpu_cycles += 2; else cpu_cycles += 1;
                        r_PC = ushort1;
                    }
                    break;

                case 00://BRK
                    r_PC++;
                    Mem_w((ushort)(r_SP-- + 0x100), (byte)(r_PC >> 8));
                    Mem_w((ushort)(r_SP-- + 0x100), (byte)(r_PC & 0xf));
                    Mem_w((ushort)(r_SP-- + 0x100), (byte)(GetFlag() | 0x30));
                    flagI = 1;
                    r_PC = (ushort)(Mem_r( 0xfffe) | (Mem_r(0xffff) << 8));
                    break;

                case 0x50://BVC
                    ushort1 = (ushort)((sbyte)Mem_r(r_PC++) + r_PC);
                    if (flagV == 0)
                    {
                        if ((ushort1 & 0xff00) != (((r_PC - 2) & 0xff00))) cpu_cycles += 2; else cpu_cycles += 1;
                        r_PC = ushort1;
                    }
                    break;

                case 0x70://BVS
                    ushort1 = (ushort)((sbyte)Mem_r(r_PC++) + r_PC);
                    if (flagV == 1)
                    {
                        if ((ushort1 & 0xff00) != (((r_PC - 2) & 0xff00))) cpu_cycles += 2; else cpu_cycles += 1;
                        r_PC = ushort1;
                    }
                    break;

                case 0x18: flagC = 0; break;//CLC
                case 0xD8: flagD = 0; break;//CLD
                case 0x58: flagI = 0; break;//CLI
                case 0xB8: flagV = 0; break;//CLV

                //--- CMP BEGIN
                case 0xC9: //CMP  Immediate  
                    byte1 = Mem_r(r_PC++);
                    int1 = r_A - byte1;
                    if (int1 == 0) flagZ = 1; else flagZ = 0;
                    if (r_A >= byte1) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0xC5: //CMP  Zero Page  
                    byte1 = NES_MEM[Mem_r(r_PC++)];
                    int1 = r_A - byte1;
                    if (int1 == 0) flagZ = 1; else flagZ = 0;
                    if (r_A >= byte1) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0xD5://CMP Zero Page,X 
                    byte1 = NES_MEM[(byte)(Mem_r(r_PC++) + r_X)];
                    int1 = r_A - byte1;
                    if (int1 == 0) flagZ = 1; else flagZ = 0;
                    if (r_A >= byte1) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0xCD: //CMP Absolute 
                    byte1 = Mem_r((ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)));
                    int1 = r_A - byte1;
                    if (int1 == 0) flagZ = 1; else flagZ = 0;
                    if (r_A >= byte1) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0xDD: //CMP  Absolute,X 
                    ushort1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    ushort2 = (ushort)(ushort1 + r_X);
                    byte1 = Mem_r(ushort2);
                    int1 = r_A - byte1;
                    if (int1 == 0) flagZ = 1; else flagZ = 0;
                    if (r_A >= byte1) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if ((ushort1 & 0xff00) != (ushort2 & 0xff00)) cpu_cycles++;
                    break;

                case 0xD9: //CMP  Absolute,Y
                    ushort1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    ushort2 = (ushort)(ushort1 + r_Y);
                    byte1 = Mem_r(ushort2);
                    int1 = r_A - byte1;
                    if (int1 == 0) flagZ = 1; else flagZ = 0;
                    if (r_A >= byte1) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if ((ushort1 & 0xff00) != (ushort2 & 0xff00)) cpu_cycles++;
                    break;

                case 0xC1: //CMP (Indirect,X)  fix
                    byte2 = (byte)(Mem_r(r_PC++) + r_X);
                    ushort1 = (ushort)(NES_MEM[byte2++] | (NES_MEM[byte2] << 8));
                    byte1 = Mem_r(ushort1);
                    int1 = r_A - byte1;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (r_A >= byte1) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0xD1: //CMP (Indirect),Y
                    byte2 = Mem_r(r_PC++);
                    ushort1 = (ushort)(NES_MEM[byte2++] | (NES_MEM[byte2] << 8));
                    byte1 = Mem_r((ushort)(ushort1 + r_Y));
                    int1 = r_A - byte1;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (r_A >= byte1) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if ((ushort1 & 0xff00) != ((ushort1 + r_Y) & 0xff00)) cpu_cycles++;
                    break;
                //--- CMP END

                case 0xE0: //CPX  Immediate  
                    byte1 = Mem_r(r_PC++);
                    int1 = r_X - byte1;// +(byte)flagC;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (r_X >= byte1) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0xE4: //CPX  Zero Page  
                    byte1 = NES_MEM[Mem_r(r_PC++)];
                    int1 = r_X - byte1;// +(byte)flagC;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (r_X >= byte1) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0xEC: //CPX Absolute 
                    byte1 = Mem_r((ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)));
                    int1 = r_X - byte1;// +(byte)flagC;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (r_X >= byte1) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                //-- CPY BEGIN
                case 0xC0: //CPY  Immediate  
                    byte1 = Mem_r(r_PC++);
                    int1 = r_Y - byte1;// +(byte)flagC;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (r_Y >= byte1) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0xC4: //CPY  Zero Page  
                    byte1 = NES_MEM[Mem_r(r_PC++)];
                    int1 = r_Y - byte1;// +(byte)flagC;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (r_Y >= byte1) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0xCC: //CPY Absolute 
                    byte1 = Mem_r((ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)));
                    int1 = r_Y - byte1;// +(byte)flagC;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (r_Y >= byte1) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;
                //-- CPY END

                case 0xC6://DEC zp
                    byte1 = Mem_r(r_PC++);
                    byte2 = NES_MEM[byte1];
                    NES_MEM[byte1] = --byte2;
                    if ((byte2 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (byte2 == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xD6://DEC zp,x
                    byte1 = (byte)(Mem_r(r_PC++) + r_X);
                    byte2 = NES_MEM[byte1];
                    NES_MEM[byte1] = --byte2;
                    if ((byte2 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (byte2 == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xCE://DEC abs
                    ushort1 = (ushort)(Mem_r(r_PC++) | Mem_r(r_PC++) << 8);
                    byte1 = Mem_r(ushort1);
                    Mem_w(ushort1, --byte1);
                    if ((byte1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (byte1 == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xDE://DEC abs,x
                    ushort1 = (ushort)((Mem_r(r_PC++) | Mem_r(r_PC++) << 8) + r_X);
                    byte1 = Mem_r(ushort1);
                    Mem_w(ushort1, --byte1);
                    if ((byte1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (byte1 == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xCA://DEX
                    if ((--r_X & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_X == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0x88://DEY //fix
                    if ((--r_Y & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_Y == 0) flagZ = 1; else flagZ = 0;
                    break;

                //--- EOR BEGIN
                case 0x49: //EOR  Immediate  
                    int1 = Mem_r(r_PC++) ^ r_A;
                    if (int1 == 0) flagZ = 1; else flagZ = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)int1;
                    break;

                case 0x45: //EOR  Zero Page  
                    int1 = NES_MEM[Mem_r(r_PC++)] ^ r_A;
                    if (int1 == 0) flagZ = 1; else flagZ = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)int1;
                    break;

                case 0x55://EOR Zero Page,X 
                    int1 = NES_MEM[(byte)(Mem_r(r_PC++) + r_X)] ^ r_A;
                    if (int1 == 0) flagZ = 1; else flagZ = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)int1;
                    break;

                case 0x4D: //EOR Absolute 
                    int1 = Mem_r((ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8))) ^ r_A;
                    if (int1 == 0) flagZ = 1; else flagZ = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)int1;
                    break;

                case 0x5D: //EOR  Absolute,X 
                    ushort1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    ushort2 = (ushort)(ushort1 + r_X);
                    int1 = Mem_r(ushort2) ^ r_A;
                    if (int1 == 0) flagZ = 1; else flagZ = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)int1;
                    if ((ushort1 & 0xff00) != (ushort2 & 0xff00)) cpu_cycles++;
                    break;

                case 0x59: //EOR  Absolute,Y
                    ushort1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    ushort2 = (ushort)(ushort1 + r_Y);
                    int1 = Mem_r(ushort2) ^ r_A;
                    if (int1 == 0) flagZ = 1; else flagZ = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)int1;
                    if ((ushort1 & 0xff00) != (ushort2 & 0xff00)) cpu_cycles++;
                    break;

                case 0x41: //EOR (Indirect,X) 
                    byte1 = (byte)(Mem_r(r_PC++) + r_X);
                    int1 = Mem_r((ushort)((NES_MEM[byte1++] | (NES_MEM[byte1] << 8)))) ^ r_A;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)int1;
                    break;

                case 0x51: //EOR (Indirect),Y
                    byte1 = Mem_r(r_PC++);
                    ushort1 = (ushort)(NES_MEM[byte1++] | (NES_MEM[byte1] << 8));
                    int1 = Mem_r((ushort)(ushort1 + r_Y)) ^ r_A;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)int1;
                    if ((ushort1 & 0xff00) != ((ushort1 + r_Y) & 0xff00)) cpu_cycles++;
                    break;
                //--- EOR END  

                case 0xE6://INC zp
                    byte1 = Mem_r(r_PC++);
                    byte2 = NES_MEM[byte1];
                    NES_MEM[byte1] = ++byte2;
                    if ((byte2 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (byte2 == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xF6://INC zp,x
                    byte1 = (byte)(Mem_r(r_PC++) + r_X);
                    byte2 = NES_MEM[byte1];
                    NES_MEM[byte1] = ++byte2;
                    if ((byte2 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (byte2 == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xEE://INC abs
                    ushort1 = (ushort)(Mem_r(r_PC++) | Mem_r(r_PC++) << 8);
                    byte2 = Mem_r(ushort1);
                    Mem_w(ushort1, ++byte2);
                    if ((byte2 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (byte2 == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xFE://INC abs,x
                    ushort1 = (ushort)((Mem_r(r_PC++) | Mem_r(r_PC++) << 8) + r_X);
                    byte2 = Mem_r(ushort1);
                    Mem_w(ushort1, ++byte2);
                    if ((byte2 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (byte2 == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xE8://INX
                    if ((++r_X & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_X == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xC8://INY
                    if ((++r_Y & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_Y == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0x4C://JMP abs                    
                    r_PC = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    break;

                case 0x6C://JMP indirect
                    byte1 = Mem_r(r_PC++);
                    byte2 = Mem_r(r_PC++);
                    r_PC = (ushort)(Mem_r((ushort)((byte1++) | (byte2 << 8))) | (Mem_r((ushort)(byte1 | (byte2 << 8))) << 8));
                    break;

                case 0x20://JSR abs
                    ushort1 = (ushort)(r_PC + 1);
                    Mem_w((ushort)(r_SP-- + 0x100), (byte)(ushort1 >> 8));
                    Mem_w((ushort)(r_SP-- + 0x100), (byte)ushort1);
                    r_PC = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    break;

                case 0xA9://LDA imm
                    r_A = Mem_r(r_PC++);
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xA5://LDA zp
                    r_A = NES_MEM[Mem_r(r_PC++)];
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xB5://LDA zp,x
                    r_A = NES_MEM[(byte)(Mem_r(r_PC++) + r_X)];
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
                    ushort1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    r_A = Mem_r((ushort)(ushort1 + r_X));
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((ushort1 & 0xff00) != ((ushort1 + r_X) & 0xff00)) cpu_cycles++;
                    break;

                case 0xB9://LDA abs,y
                    ushort1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    r_A = Mem_r((ushort)(ushort1 + r_Y));
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((ushort1 & 0xff00) != ((ushort1 + r_Y) & 0xff00)) cpu_cycles++;
                    break;

                case 0xA1://LDA (indirect,x)
                    byte1 = (byte)(Mem_r(r_PC++) + r_X);
                    r_A = Mem_r((ushort)(NES_MEM[byte1++] | (NES_MEM[byte1] << 8)));
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xB1://LDA (indirect),y
                    byte1 = Mem_r(r_PC++);
                    ushort1 = (ushort)(NES_MEM[byte1++] | (NES_MEM[byte1] << 8));
                    r_A = Mem_r((ushort)(ushort1 + r_Y));
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((ushort1 & 0xff00) != ((ushort1 + r_Y) & 0xff00)) cpu_cycles++;
                    break;

                case 0xA2://LDX imm
                    r_X = Mem_r(r_PC++);
                    if ((r_X & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_X == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xA6://LDX zp
                    r_X = NES_MEM[Mem_r(r_PC++)];
                    if ((r_X & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_X == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xB6://LDX zp,y
                    r_X = NES_MEM[(byte)(Mem_r(r_PC++) + r_Y)];
                    if ((r_X & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_X == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xAE://LDX abs
                    r_X = Mem_r((ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)));
                    if ((r_X & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_X == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xBE://LDX abs,y
                    ushort1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    r_X = Mem_r((ushort)(ushort1 + r_Y));
                    if ((r_X & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_X == 0) flagZ = 1; else flagZ = 0;
                    if ((ushort1 & 0xff00) != ((ushort1 + r_Y) & 0xff00)) cpu_cycles++;
                    break;

                case 0xA0://LDY imm
                    r_Y = Mem_r(r_PC++);
                    if ((r_Y & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_Y == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xA4://LDY zp
                    r_Y = NES_MEM[Mem_r(r_PC++)];
                    if ((r_Y & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_Y == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xB4://LDY zp,x
                    r_Y = NES_MEM[(byte)(Mem_r(r_PC++) + r_X)];
                    if ((r_Y & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_Y == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xAC://LDY abs
                    r_Y = Mem_r((ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)));
                    if ((r_Y & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_Y == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xBC://LDY abs,x
                    ushort1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    r_Y = Mem_r((ushort)(ushort1 + r_X));
                    if ((r_Y & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_Y == 0) flagZ = 1; else flagZ = 0;
                    if ((ushort1 & 0xff00) != ((ushort1 + r_X) & 0xff00)) cpu_cycles++;
                    break;

                //----- LSR begin
                case 0x4A://LSR acc
                    if ((r_A & 0x01) > 0) flagC = 1; else flagC = 0;
                    r_A >>= 1;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0x46://LSR zp fix
                    byte2 = Mem_r(r_PC++);
                    byte1 = NES_MEM[byte2];
                    if ((byte1 & 1) > 0) flagC = 1; else flagC = 0;
                    byte1 >>= 1;
                    if ((byte1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if ((byte1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    NES_MEM[byte2] = byte1;
                    break;

                case 0x56://LSR zp,x
                    byte2 = (byte)(Mem_r(r_PC++) + r_X);
                    byte1 = NES_MEM[byte2];
                    if ((byte1 & 1) > 0) flagC = 1; else flagC = 0;
                    byte1 >>= 1;
                    if ((byte1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if ((byte1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    NES_MEM[byte2] = byte1;
                    break;

                case 0x4E://LSR abs fix
                    ushort2 = (ushort)(((Mem_r(r_PC++) << 0) | (Mem_r(r_PC++) << 8)));
                    byte1 = Mem_r(ushort2);
                    if ((byte1 & 1) > 0) flagC = 1; else flagC = 0;
                    byte1 >>= 1;
                    if ((byte1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if ((byte1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    Mem_w(ushort2, byte1);
                    break;

                case 0x5E://LSR abs,x
                    ushort2 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_X);
                    byte1 = Mem_r(ushort2);
                    if ((byte1 & 1) > 0) flagC = 1; else flagC = 0;
                    byte1 >>= 1;
                    if ((byte1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if ((byte1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    Mem_w(ushort2, byte1);
                    break;
                //---- LSR END

                case 0xEA: break;//NOP

                //--- ORA BEGIN
                case 0x09: //ORA  Immediate  
                    int1 = Mem_r(r_PC++) | r_A;
                    if (int1 == 0) flagZ = 1; else flagZ = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)int1;
                    break;

                case 0x05: //ORA  Zero Page  
                    int1 = NES_MEM[Mem_r(r_PC++)] | r_A;
                    if (int1 == 0) flagZ = 1; else flagZ = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)int1;
                    break;

                case 0x15://ORA Zero Page,X 
                    int1 = NES_MEM[(byte)(Mem_r(r_PC++) + r_X)] | r_A;
                    if (int1 == 0) flagZ = 1; else flagZ = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)int1;
                    break;

                case 0x0D: //ORA Absolute 
                    int1 = Mem_r((ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8))) | r_A;
                    if (int1 == 0) flagZ = 1; else flagZ = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)int1;
                    break;

                case 0x1D: //ORA  Absolute,X  fix
                    ushort1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    ushort2 = (ushort)(ushort1 + r_X);
                    int1 = Mem_r(ushort2) | r_A;
                    if (int1 == 0) flagZ = 1; else flagZ = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)int1;
                    if ((ushort1 & 0xff00) != (ushort2 & 0xff00)) cpu_cycles++;
                    break;

                case 0x19: //ORA  Absolute,Y
                    ushort1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    ushort2 = (ushort)(ushort1 + r_Y);
                    int1 = Mem_r(ushort2) | r_A;
                    if (int1 == 0) flagZ = 1; else flagZ = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)int1;
                    if ((ushort1 & 0xff00) != (ushort2 & 0xff00)) cpu_cycles++;
                    break;

                case 0x01: //ORA (Indirect,X) 
                    byte1 = (byte)(Mem_r(r_PC++) + r_X);
                    int1 = Mem_r((ushort)(NES_MEM[byte1++] | (NES_MEM[byte1] << 8))) | r_A;
                    if (int1 == 0) flagZ = 1; else flagZ = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)int1;
                    break;

                case 0x11: //ORA (Indirect),Y
                    byte1 = Mem_r(r_PC++);
                    ushort1 = (ushort)(NES_MEM[byte1++] | (NES_MEM[byte1] << 8));
                    int1 = Mem_r((ushort)(ushort1 + r_Y)) | r_A;
                    if (int1 == 0) flagZ = 1; else flagZ = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    r_A = (byte)int1;
                    if ((ushort1 & 0xff00) != ((ushort1 + r_Y) & 0xff00)) cpu_cycles++;
                    break;
                //--- ORA END    

                case 0x48: Mem_w((ushort)(r_SP-- + 0x100), r_A); break;//PHA
                case 0x08: Mem_w((ushort)(r_SP-- + 0x100), (byte)(GetFlag() | 0x30)); break;//PHP

                case 0x68://PLA
                    r_A = Mem_r((ushort)(++r_SP + 0x100));
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x28: SetFlag(Mem_r((ushort)(++r_SP + 0x100))); break;//PLP

                //----ROL begin
                case 0x2A://ROL acc //fix
                    ushort1 = (ushort)(r_A << 1);
                    if (flagC == 1) ushort1 |= 0x1;
                    if ((r_A & 0x80) != 0) flagC = 1; else flagC = 0;
                    if ((ushort1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if ((ushort1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    r_A = (byte)ushort1;
                    break;

                case 0x26://ROL zp //fix
                    byte2 = Mem_r(r_PC++);
                    byte1 = NES_MEM[byte2];
                    ushort1 = (ushort)(byte1 << 1);
                    if (flagC == 1) ushort1 |= 0x1;
                    if ((byte1 & 0x80) != 0) flagC = 1; else flagC = 0;
                    if ((ushort1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if ((ushort1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    NES_MEM[byte2] = (byte)ushort1; //!!!!!
                    break;

                case 0x36://ROL zp,x
                    byte2 = (byte)(Mem_r(r_PC++) + r_X);
                    byte1 = NES_MEM[byte2];
                    ushort1 = (ushort)(byte1 << 1);
                    if (flagC == 1) ushort1 |= 0x1;
                    if ((byte1 & 0x80) != 0) flagC = 1; else flagC = 0;
                    if ((ushort1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if ((ushort1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    NES_MEM[byte2] = (byte)ushort1; //!!!!!
                    break;

                case 0x2E://ROL abs fix
                    ushort2 = (ushort)((Mem_r(r_PC++) | Mem_r(r_PC++) << 8));
                    byte1 = Mem_r(ushort2);
                    ushort1 = (ushort)(byte1 << 1);
                    if (flagC == 1) ushort1 |= 0x1;
                    if ((byte1 & 0x80) != 0) flagC = 1; else flagC = 0;
                    if ((ushort1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if ((ushort1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    Mem_w(ushort2, (byte)ushort1); //!!!!!
                    break;

                case 0x3E://ROL abs,x fix
                    ushort2 = (ushort)((Mem_r(r_PC++) | Mem_r(r_PC++) << 8) + r_X);
                    byte1 = Mem_r(ushort2);
                    ushort1 = (ushort)(byte1 << 1);
                    if (flagC == 1) ushort1 |= 0x1;
                    if ((byte1 & 0x80) != 0) flagC = 1; else flagC = 0;
                    if ((ushort1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if ((ushort1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    Mem_w(ushort2, (byte)ushort1);
                    break;
                //----ROL end

                //---- ROR begin
                case 0x6A://ROR acc
                    ushort1 = r_A;
                    if (flagC == 1) ushort1 |= 0x100;
                    if ((ushort1 & 0x01) > 0) flagC = 1; else flagC = 0;
                    ushort1 >>= 1;
                    if ((ushort1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (ushort1 == 0) flagZ = 1; else flagZ = 0;
                    r_A = (byte)ushort1;
                    break;

                case 0x66://ROR zp
                    byte1 = Mem_r(r_PC++);
                    ushort1 = NES_MEM[byte1];
                    if (flagC == 1) ushort1 |= 0x100;
                    if ((ushort1 & 0x01) > 0) flagC = 1; else flagC = 0;
                    ushort1 >>= 1;
                    if ((ushort1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (ushort1 == 0) flagZ = 1; else flagZ = 0;
                    ushort1 = (byte)ushort1;
                    NES_MEM[byte1] = (byte)ushort1;
                    break;

                case 0x76://ROR zp,x
                    byte1 = (byte)(Mem_r(r_PC++) + r_X);
                    ushort1 = NES_MEM[byte1];
                    if (flagC == 1) ushort1 |= 0x100;
                    if ((ushort1 & 0x01) > 0) flagC = 1; else flagC = 0;
                    ushort1 >>= 1;
                    if ((ushort1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (ushort1 == 0) flagZ = 1; else flagZ = 0;
                    ushort1 = (byte)ushort1;
                    NES_MEM[byte1] = (byte)ushort1;
                    break;

                case 0x6E://ROR abs
                    ushort2 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    ushort1 = Mem_r(ushort2);
                    if (flagC == 1) ushort1 |= 0x100;
                    if ((ushort1 & 0x01) > 0) flagC = 1; else flagC = 0;
                    ushort1 >>= 1;
                    if ((ushort1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (ushort1 == 0) flagZ = 1; else flagZ = 0;
                    ushort1 = (byte)ushort1;
                    Mem_w(ushort2, (byte)ushort1);
                    break;

                case 0x7E://ROR abs,x
                    ushort2 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_X);
                    ushort1 = Mem_r(ushort2);
                    if (flagC == 1) ushort1 |= 0x100;
                    if ((ushort1 & 0x01) > 0) flagC = 1; else flagC = 0;
                    ushort1 >>= 1;
                    if ((ushort1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (ushort1 == 0) flagZ = 1; else flagZ = 0;
                    ushort1 = (byte)ushort1;
                    Mem_w(ushort2, (byte)ushort1);
                    break;
                // ----ROR end

                case 0x40://RTI
                    SetFlag(Mem_r((ushort)(++r_SP + 0x100)));
                    r_PC = (ushort)(Mem_r((ushort)(++r_SP + 0x100)) | (Mem_r((ushort)(++r_SP + 0x100)) << 8));
                    break;

                case 0x60: r_PC = (ushort)((Mem_r((ushort)(++r_SP + 0x100)) | (Mem_r((ushort)(++r_SP + 0x100)) << 8)) + 1); break;//RTS

                //--- SBC BEGIN
                case 0xE9: //SBC  Immediate  
                    byte1 = (byte)(Mem_r(r_PC++) ^ 0xFF);
                    int1 = r_A + byte1 + flagC;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (int1 > 0xff) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (((int1 ^ r_A) & (int1 ^ byte1) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)int1;
                    break;

                case 0xE5: //SBC  Zero Page  
                    byte1 = (byte)(NES_MEM[Mem_r(r_PC++)] ^ 0xFF);
                    int1 = r_A + byte1 + flagC;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (int1 > 0xff) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (((int1 ^ r_A) & (int1 ^ byte1) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)int1;
                    break;

                case 0xF5://SBC Zero Page,X 
                    byte1 = (byte)(NES_MEM[(byte)(Mem_r(r_PC++) + r_X)] ^ 0xFF);
                    int1 = r_A + byte1 + flagC;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (int1 > 0xff) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (((int1 ^ r_A) & (int1 ^ byte1) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)int1;
                    break;

                case 0xED: //SBC Absolute fix
                    byte1 = (byte)(Mem_r((ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8))) ^ 0xFF);
                    int1 = r_A + byte1 + flagC;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (int1 > 0xff) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (((int1 ^ r_A) & (int1 ^ byte1) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)int1;
                    break;

                case 0xFD: //SBC  Absolute,X 
                    ushort1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    ushort2 = (ushort)(ushort1 + r_X);
                    byte1 = (byte)(Mem_r(ushort2) ^ 0xFF);
                    int1 = r_A + byte1 + flagC;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (int1 > 0xff) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (((int1 ^ r_A) & (int1 ^ byte1) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)int1;
                    if ((ushort1 & 0xff00) != (ushort2 & 0xff00)) cpu_cycles++;
                    break;

                case 0xF9: //SBC  Absolute,Y FIX
                    ushort1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    ushort2 = (ushort)(ushort1 + r_Y);
                    byte1 = (byte)(Mem_r(ushort2) ^ 0xFF);
                    int1 = r_A + byte1 + flagC;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (int1 > 0xff) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (((int1 ^ r_A) & (int1 ^ byte1) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)int1;
                    if ((ushort1 & 0xff00) != (ushort2 & 0xff00)) cpu_cycles++;
                    break;

                case 0xE1: //SBC (Indirect,X) 
                    byte2 = (byte)(Mem_r(r_PC++) + r_X);
                    byte1 = (byte)(Mem_r((ushort)(NES_MEM[byte2++] | (NES_MEM[byte2] << 8))) ^ 0xFF);
                    int1 = r_A + byte1 + flagC;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (int1 > 0xff) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (((int1 ^ r_A) & (int1 ^ byte1) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)int1;
                    break;

                case 0xF1: //SBC (Indirect),Y
                    byte2 = Mem_r(r_PC++);
                    ushort1 = (ushort)(NES_MEM[byte2++] | (NES_MEM[byte2] << 8));
                    byte1 = (byte)(Mem_r((ushort)(ushort1 + r_Y)) ^ 0xFF);
                    int1 = r_A + byte1 + flagC;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (int1 > 0xff) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (((int1 ^ r_A) & (int1 ^ byte1) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)int1;
                    if ((ushort1 & 0xff00) != ((ushort1 + r_Y) & 0xff00)) cpu_cycles++;
                    break;
                //--- SBC END
                case 0x38: flagC = 1; break; //SEC
                case 0xF8: flagD = 1; break; // SED NES 6502 此 FLAG 無作用
                case 0x78: flagI = 1; break; //SEI
                case 0x85: NES_MEM[Mem_r(r_PC++)] = r_A; break;//STA zp
                case 0x95: NES_MEM[(byte)(Mem_r(r_PC++) + r_X)] = r_A; break;//STA zp,x
                case 0x8D: Mem_w((ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)), r_A); break;//STA abs
                case 0x9D: Mem_w((ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_X), r_A); break; //STA abs,x
                case 0x99: Mem_w((ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_Y), r_A); break;//STA abs,Y

                case 0x81://STA (indirect,x)
                    byte1 = (byte)(Mem_r(r_PC++) + r_X);
                    Mem_w((ushort)(NES_MEM[byte1++] | (NES_MEM[byte1] << 8)), r_A);
                    break;

                case 0x91://STA (indirect),y
                    byte1 = Mem_r(r_PC++);
                    Mem_w((ushort)(((ushort)(NES_MEM[byte1++] | (NES_MEM[byte1] << 8))) + r_Y), r_A);
                    break;

                case 0x86: NES_MEM[Mem_r(r_PC++)] = r_X; break; //STX zp
                case 0x96: NES_MEM[(byte)(Mem_r(r_PC++) + r_Y)] = r_X; break; //STX zp,y
                case 0x8E: Mem_w((ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)), r_X); break; //STX abs //fixed 1/3
                case 0x84: NES_MEM[Mem_r(r_PC++)] = r_Y; break;//STY  zp
                case 0x94: NES_MEM[(byte)(Mem_r(r_PC++) + r_X)] = r_Y; break;//STY zp,x
                case 0x8C: Mem_w((ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)), r_Y); break;//STY abs 

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

                case 0x9A: r_SP = r_X; break; //TXS

                case 0x98: //TYA
                    if ((r_Y & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (r_Y == 0) flagZ = 1; else flagZ = 0;
                    r_A = r_Y;
                    break;
#if illegal
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
                    byte1 = Mem_r(r_PC++);
                    r_A = (byte)(((byte1 & r_A) >> 1) | (((byte)flagC) << 7));
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    if ((r_A & 0x40) > 0) flagC = 1; else flagC = 0;
                    if (((r_A << 1 ^ r_A) & 0x40) > 0) flagV = 1; else flagV = 0;
                    break;

                case 0x0B: //ANC
                case 0x2B: //ANC
                    r_A &= Mem_r(r_PC++);
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagC = 1; else flagC = 0;
                    flagN = flagC;
                    break;

                case 0x4B: //ALR
                    r_A &= Mem_r(r_PC++);
                    if ((r_A & 0x1) != 0) flagC = 1; else flagC = 0;
                    r_A >>= 1;
                    if ((r_A & 0x80) != 0) flagN = 1; else flagN = 0;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0xEB: //illegal sbc imm
                    byte1 = (byte)(Mem_r(r_PC++) ^ 0xFF);
                    int1 = r_A + byte1 + flagC;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if (int1 > 0xff) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) > 0) flagN = 1; else flagN = 0;
                    if (((int1 ^ r_A) & (int1 ^ byte1) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)int1;
                    break;

                case 0x03: //SLO (  ASL M THEN (M "OR" A) -> A,M  )
                    byte2 = (byte)(Mem_r(r_PC++) + r_X);
                    ushort1 = (ushort)(NES_MEM[byte2++] | (NES_MEM[byte2] << 8));
                    byte1 = Mem_r(ushort1);
                    if ((byte1 & 0x80) > 0) flagC = 1; else flagC = 0;
                    byte1 <<= 1;
                    Mem_w(ushort1, byte1);
                    r_A |= byte1;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x07: //SLO (  ASL M THEN (M "OR" A) -> A,M  )
                    byte2 = Mem_r(r_PC++);
                    byte1 = NES_MEM[byte2];
                    if ((byte1 & 0x80) > 0) flagC = 1; else flagC = 0;
                    byte1 <<= 1;
                    NES_MEM[byte2] = byte1;
                    r_A |= byte1;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x13: //SLO (  ASL M THEN (M "OR" A) -> A,M  )
                    byte2 = Mem_r(r_PC++);
                    ushort1 = (ushort)(NES_MEM[byte2++] | (NES_MEM[byte2] << 8));
                    byte1 = Mem_r((ushort)(ushort1 + r_Y));
                    if ((byte1 & 0x80) > 0) flagC = 1; else flagC = 0;
                    byte1 <<= 1;
                    Mem_w((ushort)(ushort1 + r_Y), byte1);
                    r_A |= byte1;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x17: //SLO (  ASL M THEN (M "OR" A) -> A,M  )
                    byte2 = (byte)(Mem_r(r_PC++) + r_X);
                    byte1 = NES_MEM[byte2];
                    if ((byte1 & 0x80) > 0) flagC = 1; else flagC = 0;
                    byte1 <<= 1;
                    NES_MEM[byte2] = byte1;
                    r_A |= byte1;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x1B: //SLO (  ASL M THEN (M "OR" A) -> A,M  )
                    ushort1 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_Y);
                    byte1 = Mem_r(ushort1);
                    if ((byte1 & 0x80) > 0) flagC = 1; else flagC = 0;
                    byte1 <<= 1;
                    Mem_w(ushort1, byte1);
                    r_A |= byte1;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x0F: //SLO (  ASL M THEN (M "OR" A) -> A,M  )
                    ushort1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    byte1 = Mem_r(ushort1);
                    if ((byte1 & 0x80) > 0) flagC = 1; else flagC = 0;
                    byte1 <<= 1;
                    Mem_w(ushort1, byte1);
                    r_A |= byte1;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x1F: //SLO (  ASL M THEN (M "OR" A) -> A,M  )
                    ushort2 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    ushort1 = (ushort)(ushort2 + r_X);
                    byte1 = Mem_r(ushort1);
                    if ((byte1 & 0x80) > 0) flagC = 1; else flagC = 0;
                    byte1 <<= 1;
                    Mem_w(ushort1, byte1);
                    r_A |= byte1;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x23: //RLA    ( ROL M  THEN (M "AND" A) -> A )   
                    byte3 = (byte)(Mem_r(r_PC++) + r_X);
                    ushort1 = (ushort)(NES_MEM[byte3++] | (NES_MEM[byte3] << 8));
                    byte2 = Mem_r(ushort1);
                    byte1 = (byte)((byte2 << 1) | flagC);
                    Mem_w(ushort1, byte1);
                    if ((byte2 & 0x80) > 0) flagC = 1; else flagC = 0;
                    r_A &= byte1;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x27: //RLA    ( ROL M  THEN (M "AND" A) -> A )  
                    byte3 = Mem_r(r_PC++);
                    byte2 = NES_MEM[byte3];
                    byte1 = (byte)((byte2 << 1) | flagC);
                    NES_MEM[byte3] = byte1;
                    if ((byte2 & 0x80) > 0) flagC = 1; else flagC = 0;
                    r_A &= byte1;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x2F:// RLA
                    ushort1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    byte2 = Mem_r(ushort1);
                    byte1 = (byte)((byte2 << 1) | flagC);
                    Mem_w(ushort1, byte1);
                    if ((byte2 & 0x80) > 0) flagC = 1; else flagC = 0;
                    r_A &= byte1;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x3F://RLA
                    ushort1 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_X);
                    byte2 = Mem_r(ushort1);
                    byte1 = (byte)((byte2 << 1) | flagC);
                    Mem_w(ushort1, byte1);
                    if ((byte2 & 0x80) > 0) flagC = 1; else flagC = 0;
                    r_A &= byte1;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x3B://RLA
                    ushort1 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_Y);
                    byte2 = Mem_r(ushort1);
                    byte1 = (byte)((byte2 << 1) | flagC);
                    Mem_w(ushort1, byte1);
                    if ((byte2 & 0x80) > 0) flagC = 1; else flagC = 0;
                    r_A &= byte1;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x33: //RLA    ( ROL M  THEN (M "AND" A) -> A )   
                    byte3 = Mem_r(r_PC++);
                    ushort1 = (ushort)(NES_MEM[byte3++] | (NES_MEM[byte3] << 8));
                    byte2 = Mem_r((ushort)(ushort1 + r_Y));
                    byte1 = (byte)(byte2 << 1);
                    byte1 |= (byte)(flagC);
                    Mem_w((ushort)(ushort1 + r_Y), byte1);
                    if ((byte2 & 0x80) > 0) flagC = 1; else flagC = 0;
                    r_A &= byte1;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x37: //RLA    ( ROL M  THEN (M "AND" A) -> A )   
                    byte3 = (byte)(Mem_r(r_PC++) + r_X);
                    byte2 = NES_MEM[byte3];
                    byte1 = (byte)(byte2 << 1);
                    byte1 |= (byte)(flagC);
                    NES_MEM[byte3] = byte1;
                    if ((byte2 & 0x80) > 0) flagC = 1; else flagC = 0;
                    r_A &= byte1;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x43://SRE (LSR M  THEN (M "EOR" A) -> A ) 
                    byte2 = (byte)(Mem_r(r_PC++) + r_X);
                    ushort1 = (ushort)(NES_MEM[byte2++] | (NES_MEM[byte2] << 8));
                    byte1 = Mem_r(ushort1);
                    if ((byte1 & 1) > 0) flagC = 1; else flagC = 0;
                    byte1 >>= 1;
                    Mem_w(ushort1, byte1);
                    r_A ^= byte1;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x47://SRE (LSR M  THEN (M "EOR" A) -> A )
                    byte2 = Mem_r(r_PC++);
                    byte1 = NES_MEM[byte2];
                    if ((byte1 & 1) > 0) flagC = 1; else flagC = 0;
                    byte1 >>= 1;
                    NES_MEM[byte2] = byte1;
                    r_A ^= byte1;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x4F://SRE (LSR M  THEN (M "EOR" A) -> A )
                    ushort1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    byte1 = Mem_r(ushort1);
                    if ((byte1 & 1) > 0) flagC = 1; else flagC = 0;
                    byte1 >>= 1;
                    Mem_w(ushort1, byte1);
                    r_A ^= byte1;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x5F://SRE  
                    ushort1 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_X);
                    byte1 = Mem_r(ushort1);
                    if ((byte1 & 1) != 0) flagC = 1; else flagC = 0;
                    byte1 >>= 1;
                    Mem_w(ushort1, byte1);
                    r_A ^= byte1;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) != 0) flagN = 1; else flagN = 0;
                    break;

                case 0x5B://SRE  
                    ushort1 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_Y);
                    byte1 = Mem_r(ushort1);
                    if ((byte1 & 1) != 0) flagC = 1; else flagC = 0;
                    byte1 >>= 1;
                    Mem_w(ushort1, byte1);
                    r_A ^= byte1;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) != 0) flagN = 1; else flagN = 0;
                    break;

                case 0x53://SRE (LSR M  THEN (M "EOR" A) -> A )
                    byte2 = Mem_r(r_PC++);
                    ushort1 = (ushort)(NES_MEM[byte2++] | (NES_MEM[byte2] << 8));
                    byte1 = Mem_r((ushort)(ushort1 + r_Y));
                    if ((byte1 & 1) > 0) flagC = 1; else flagC = 0;
                    byte1 >>= 1;
                    Mem_w((ushort)(ushort1 + r_Y), byte1);
                    r_A ^= byte1;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x57://SRE (LSR M  THEN (M "EOR" A) -> A )
                    byte2 = (byte)(Mem_r(r_PC++) + r_X);
                    byte1 = NES_MEM[byte2];
                    if ((byte1 & 1) > 0) flagC = 1; else flagC = 0;
                    byte1 >>= 1;
                    NES_MEM[byte2] = byte1;
                    r_A ^= byte1;
                    if (r_A == 0) flagZ = 1; else flagZ = 0;
                    if ((r_A & 0x80) > 0) flagN = 1; else flagN = 0;
                    break;

                case 0x63:// RRA (ROR M THEN (A + M + C) -> A  )  ok 
                    byte3 = (byte)(Mem_r(r_PC++) + r_X);
                    ushort1 = (ushort)(NES_MEM[byte3++] | (NES_MEM[byte3] << 8));
                    byte2 = Mem_r(ushort1);
                    byte1 = (byte)((byte2 >> 1) | ((flagC == 0) ? 0 : 0x80));
                    Mem_w(ushort1, byte1);
                    if ((byte2 & 1) > 0) flagC = 1; else flagC = 0;
                    int1 = r_A + byte1 + flagC;
                    if ((int1 & 0x80) != 0) flagN = 1; else flagN = 0;
                    if (((int1 ^ r_A) & (int1 ^ byte1) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)int1;
                    if ((int1 >> 8) > 0) flagC = 1; else flagC = 0;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0x67:// RRA (ROR M THEN (A + M + C) -> A  ) ok
                    byte3 = Mem_r(r_PC++);
                    byte2 = NES_MEM[byte3];
                    byte1 = (byte)((byte2 >> 1) | ((flagC == 0) ? 0 : 0x80));
                    NES_MEM[byte3] = byte1;
                    if ((byte2 & 1) > 0) flagC = 1; else flagC = 0;
                    int1 = r_A + byte1 + flagC;
                    if ((int1 & 0x80) != 0) flagN = 1; else flagN = 0;
                    if (((int1 ^ r_A) & (int1 ^ byte1) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)int1;
                    if ((int1 >> 8) > 0) flagC = 1; else flagC = 0;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0x6F://RRA
                    ushort1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));//ok
                    byte2 = Mem_r(ushort1);
                    byte1 = (byte)((byte2 >> 1) | ((flagC == 0) ? 0 : 0x80));
                    Mem_w(ushort1, byte1);
                    if ((byte2 & 1) > 0) flagC = 1; else flagC = 0;
                    int1 = r_A + byte1 + flagC;
                    if ((int1 & 0x80) != 0) flagN = 1; else flagN = 0;
                    if (((int1 ^ r_A) & (int1 ^ byte1) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)int1;
                    if ((int1 >> 8) > 0) flagC = 1; else flagC = 0;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0x73:// RRA (ROR M THEN (A + M + C) -> A  ) ok
                    byte3 = Mem_r(r_PC++);
                    ushort1 = (ushort)(NES_MEM[byte3++] | (NES_MEM[byte3] << 8));
                    byte2 = Mem_r((ushort)(ushort1 + r_Y));
                    byte1 = (byte)((byte2 >> 1) | ((flagC == 0) ? 0 : 0x80));
                    Mem_w((ushort)(ushort1 + r_Y), byte1);
                    if ((byte2 & 1) > 0) flagC = 1; else flagC = 0;
                    int1 = r_A + byte1 + flagC;
                    if ((int1 & 0x80) != 0) flagN = 1; else flagN = 0;
                    if (((int1 ^ r_A) & (int1 ^ byte1) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)int1;
                    if ((int1 >> 8) > 0) flagC = 1; else flagC = 0;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0x77:// RRA (ROR M THEN (A + M + C) -> A  ) ok
                    byte3 = (byte)(Mem_r(r_PC++) + r_X);
                    byte2 = NES_MEM[byte3];
                    byte1 = (byte)((byte2 >> 1) | ((flagC == 0) ? 0 : 0x80));
                    NES_MEM[byte3] = byte1;
                    if ((byte2 & 1) > 0) flagC = 1; else flagC = 0;
                    int1 = r_A + byte1 + flagC;
                    if ((int1 & 0x80) != 0) flagN = 1; else flagN = 0;
                    if (((int1 ^ r_A) & (int1 ^ byte1) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)int1;
                    if ((int1 >> 8) > 0) flagC = 1; else flagC = 0;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0x7B:// RRA (ROR M THEN (A + M + C) -> A  )
                    ushort1 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_Y);
                    byte2 = Mem_r(ushort1);
                    byte1 = (byte)((byte2 >> 1) | ((flagC == 0) ? 0 : 0x80));
                    Mem_w(ushort1, byte1);
                    if ((byte2 & 1) > 0) flagC = 1; else flagC = 0;
                    int1 = r_A + byte1 + flagC;
                    if ((int1 & 0x80) != 0) flagN = 1; else flagN = 0;
                    if (((int1 ^ r_A) & (int1 ^ byte1) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)int1;
                    if ((int1 >> 8) > 0) flagC = 1; else flagC = 0;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0x7F: //RRA
                    ushort1 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_X);
                    byte2 = Mem_r(ushort1);
                    byte1 = (byte)((byte2 >> 1) | ((flagC == 0) ? 0 : 0x80));
                    Mem_w(ushort1, byte1);
                    if ((byte2 & 1) > 0) flagC = 1; else flagC = 0;
                    int1 = r_A + byte1 + flagC;
                    if ((int1 & 0x80) != 0) flagN = 1; else flagN = 0;
                    if (((int1 ^ r_A) & (int1 ^ byte1) & 0x80) != 0) flagV = 1; else flagV = 0;
                    r_A = (byte)int1;
                    if ((int1 >> 8) > 0) flagC = 1; else flagC = 0;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    break;

                case 0x83://SAX ( (A "AND" (MSB(adr)+1)  "AND" X) -> M 
                    byte1 = (byte)(Mem_r(r_PC++) + r_X);
                    Mem_w((ushort)(NES_MEM[byte1++] | (NES_MEM[byte1] << 8)), (byte)(r_X & r_A));
                    break;

                case 0x87://SAX ( (A "AND" (MSB(adr)+1)  "AND" X) -> M 
                    Mem_w(Mem_r(r_PC++), (byte)(r_X & r_A));
                    break;

                case 0x8F://SAX ( (A "AND" (MSB(adr)+1)  "AND" X) -> M 
                    Mem_w((ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)), (byte)(r_X & r_A));
                    break;

                case 0x9C://SHY
                    ushort1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    byte1 = (byte)(r_Y & (((ushort1 & 0xff00) >> 8) + 1));
                    ushort1 = (ushort)((ushort1 & 0xff00) | (byte)(ushort1 + r_X));
                    if ((ushort1 & 0xff) < r_X) ushort1 = (ushort)((ushort1 & 0xff) | (byte1 << 8));
                    Mem_w(ushort1, byte1);
                    break;

                case 0x9E://SHX
                    ushort1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    byte1 = (byte)(r_X & (((ushort1 & 0xff00) >> 8) + 1));
                    ushort1 = (ushort)((ushort1 & 0xff00) | (byte)(ushort1 + r_Y));
                    if ((ushort1 & 0xff) < r_Y) ushort1 = (ushort)((ushort1 & 0xff) | (byte1 << 8));
                    Mem_w(ushort1, byte1);
                    break;

                case 0x97://SAX ( (A "AND" (MSB(adr)+1)  "AND" X) -> M 
                    NES_MEM[(byte)(Mem_r(r_PC++) + r_Y)] = (byte)(r_X & r_A);
                    break;

                case 0xB7://SAX ( (A "AND" (MSB(adr)+1)  "AND" X) -> M 
                    r_X = r_A = NES_MEM[(byte)(Mem_r(r_PC++) + r_Y)];
                    if (r_X == 0) flagZ = 1; else flagZ = 0;
                    if ((r_X & 0x80) != 0) flagN = 1; else flagN = 0;
                    break;

                case 0xA3://LAX
                    byte1 = (byte)(Mem_r(r_PC++) + r_X);
                    r_X = r_A = Mem_r((ushort)(NES_MEM[byte1++] | (NES_MEM[byte1] << 8)));
                    if (r_X == 0) flagZ = 1; else flagZ = 0;
                    if ((r_X & 0x80) != 0) flagN = 1; else flagN = 0;
                    break;

                case 0xA7://LAX
                    r_X = r_A = NES_MEM[Mem_r(r_PC++)];
                    if (r_X == 0) flagZ = 1; else flagZ = 0;
                    if ((r_X & 0x80) != 0) flagN = 1; else flagN = 0;
                    break;

                case 0xAB://LAX
                    r_X = r_A = Mem_r(r_PC++);
                    if (r_X == 0) flagZ = 1; else flagZ = 0;
                    if ((r_X & 0x80) != 0) flagN = 1; else flagN = 0;
                    break;

                case 0xAF://LAX
                    r_X = r_A = Mem_r((ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)));
                    if (r_X == 0) flagZ = 1; else flagZ = 0;
                    if ((r_X & 0x80) != 0) flagN = 1; else flagN = 0;
                    break;

                case 0xBF://LAX
                    r_X = r_A = Mem_r((ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_Y));
                    if (r_X == 0) flagZ = 1; else flagZ = 0;
                    if ((r_X & 0x80) != 0) flagN = 1; else flagN = 0;
                    break;

                case 0xB3://LAX
                    byte1 = Mem_r(r_PC++);
                    r_X = r_A = Mem_r((ushort)((NES_MEM[byte1++] | (NES_MEM[byte1] << 8)) + r_Y));
                    if (r_X == 0) flagZ = 1; else flagZ = 0;
                    if ((r_X & 0x80) != 0) flagN = 1; else flagN = 0;
                    break;

                case 0xCB:// AXS
                    int1 = (r_A & r_X) - Mem_r(r_PC++);
                    if ((int1 & 0x80) != 0) flagN = 1; else flagN = 0;
                    if ((byte)int1 == 0) flagZ = 1; else flagZ = 0;
                    if ((~int1 >> 8) != 0) flagC = 1; else flagC = 0;
                    r_X = (byte)int1;
                    break;

                case 0xC3: //DCP
                    byte2 = (byte)(Mem_r(r_PC++) + r_X);
                    ushort1 = (ushort)((NES_MEM[byte2++] | (NES_MEM[byte2] << 8)));
                    byte1 = Mem_r(ushort1);
                    Mem_w(ushort1, --byte1);
                    int1 = r_A - byte1;
                    if (int1 == 0) flagZ = 1; else flagZ = 0;
                    if ((~int1) >> 8 != 0) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) != 0) flagN = 1; else flagN = 0;
                    break;

                case 0xC7: //DCP
                    byte2 = Mem_r(r_PC++);
                    byte1 = NES_MEM[byte2];
                    NES_MEM[byte2] = --byte1;
                    int1 = r_A - byte1;
                    if (int1 == 0) flagZ = 1; else flagZ = 0;
                    if ((~int1) >> 8 != 0) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) != 0) flagN = 1; else flagN = 0;
                    break;

                case 0xCF: //DCP
                    ushort1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    byte1 = Mem_r(ushort1);
                    Mem_w(ushort1, --byte1);
                    int1 = r_A - byte1;
                    if (int1 == 0) flagZ = 1; else flagZ = 0;
                    if ((~int1) >> 8 != 0) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) != 0) flagN = 1; else flagN = 0;
                    break;

                case 0xDF:
                    ushort1 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_X);
                    byte1 = Mem_r(ushort1);
                    Mem_w(ushort1, --byte1);
                    int1 = r_A - byte1;
                    if (int1 == 0) flagZ = 1; else flagZ = 0;
                    if ((~int1) >> 8 != 0) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) != 0) flagN = 1; else flagN = 0;
                    break;

                case 0xD3: //DCP
                    byte2 = Mem_r(r_PC++);
                    ushort3 = (ushort)((NES_MEM[byte2++] | (NES_MEM[byte2] << 8)) + r_Y);
                    byte1 = Mem_r(ushort3);
                    Mem_w(ushort3, --byte1);
                    int1 = r_A - byte1;
                    if (int1 == 0) flagZ = 1; else flagZ = 0;
                    if ((~int1) >> 8 != 0) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) != 0) flagN = 1; else flagN = 0;
                    break;

                case 0xD7: //DCP
                    byte2 = (byte)(Mem_r(r_PC++) + r_X);
                    byte1 = NES_MEM[byte2];
                    NES_MEM[byte2] = --byte1;
                    int1 = r_A - byte1;
                    if (int1 == 0) flagZ = 1; else flagZ = 0;
                    if ((~int1) >> 8 != 0) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) != 0) flagN = 1; else flagN = 0;
                    break;

                case 0xDB:// DCP
                    ushort1 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_Y);
                    byte1 = Mem_r(ushort1);
                    Mem_w(ushort1, --byte1);
                    int1 = r_A - byte1;
                    if (int1 == 0) flagZ = 1; else flagZ = 0;
                    if ((~int1) >> 8 != 0) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) != 0) flagN = 1; else flagN = 0;
                    break;

                case 0xE3://ISC
                    byte2 = (byte)(Mem_r(r_PC++) + r_X);
                    ushort3 = (ushort)((NES_MEM[byte2++] | (NES_MEM[byte2] << 8)));
                    byte1 = Mem_r(ushort3);
                    Mem_w(ushort3, ++byte1);
                    int1 = r_A + (byte1 ^ 0xff) + flagC;
                    if (((int1 ^ r_A) & (int1 ^ (byte1 ^ 0xff)) & 0x80) != 0) flagV = 1; else flagV = 0;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if ((int1) >> 8 != 0) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) != 0) flagN = 1; else flagN = 0;
                    r_A = (byte)int1;
                    break;

                case 0xE7://ISC
                    byte2 = Mem_r(r_PC++);
                    byte1 = NES_MEM[byte2];
                    NES_MEM[byte2] = ++byte1;
                    int1 = r_A + (byte1 ^ 0xff) + flagC;
                    if (((int1 ^ r_A) & (int1 ^ (byte1 ^ 0xff)) & 0x80) != 0) flagV = 1; else flagV = 0;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if ((int1) >> 8 != 0) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) != 0) flagN = 1; else flagN = 0;
                    r_A = (byte)int1;
                    break;

                case 0xEF://ISC
                    ushort1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
                    byte1 = Mem_r(ushort1);
                    Mem_w(ushort1, ++byte1);
                    int1 = r_A + (byte1 ^ 0xff) + flagC;
                    if (((int1 ^ r_A) & (int1 ^ (byte1 ^ 0xff)) & 0x80) != 0) flagV = 1; else flagV = 0;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if ((int1) >> 8 != 0) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) != 0) flagN = 1; else flagN = 0;
                    r_A = (byte)int1;
                    break;

                case 0xF3://ISC
                    byte2 = Mem_r(r_PC++);
                    ushort3 = (ushort)(((NES_MEM[byte2++] | (NES_MEM[byte2] << 8))) + r_Y);
                    byte1 = Mem_r(ushort3);
                    Mem_w(ushort3, ++byte1);
                    int1 = r_A + (byte1 ^ 0xff) + flagC;
                    if (((int1 ^ r_A) & (int1 ^ (byte1 ^ 0xff)) & 0x80) != 0) flagV = 1; else flagV = 0;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if ((int1) >> 8 != 0) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) != 0) flagN = 1; else flagN = 0;
                    r_A = (byte)int1;
                    break;

                case 0xF7://ISC
                    byte2 = (byte)(Mem_r(r_PC++) + r_X);
                    byte1 = NES_MEM[byte2];
                    NES_MEM[byte2] = ++byte1;
                    int1 = r_A + (byte1 ^ 0xff) + flagC;
                    if (((int1 ^ r_A) & (int1 ^ (byte1 ^ 0xff)) & 0x80) != 0) flagV = 1; else flagV = 0;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if ((int1) >> 8 != 0) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) != 0) flagN = 1; else flagN = 0;
                    r_A = (byte)int1;
                    break;

                case 0xFB://ISC
                    ushort1 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_Y);
                    byte1 = Mem_r(ushort1);
                    Mem_w(ushort1, ++byte1);
                    int1 = r_A + (byte1 ^ 0xff) + flagC;
                    if (((int1 ^ r_A) & (int1 ^ (byte1 ^ 0xff)) & 0x80) != 0) flagV = 1; else flagV = 0;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if ((int1) >> 8 != 0) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) != 0) flagN = 1; else flagN = 0;
                    r_A = (byte)int1;
                    break;

                case 0xFF://ISC
                    ushort1 = (ushort)((Mem_r(r_PC++) | (Mem_r(r_PC++) << 8)) + r_X);
                    byte1 = Mem_r(ushort1);
                    Mem_w(ushort1, ++byte1);
                    int1 = r_A + (byte1 ^ 0xff) + flagC;
                    if (((int1 ^ r_A) & (int1 ^ (byte1 ^ 0xff)) & 0x80) != 0) flagV = 1; else flagV = 0;
                    if ((int1 & 0xff) == 0) flagZ = 1; else flagZ = 0;
                    if ((int1) >> 8 != 0) flagC = 1; else flagC = 0;
                    if ((int1 & 0x80) != 0) flagN = 1; else flagN = 0;
                    r_A = (byte)int1;
                    break;
                #endregion
#endif
                default: MessageBox.Show("unkonw opcode ! - 0x" + opcode.ToString("X2")); break;
            }
        }
    }
}
