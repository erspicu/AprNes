using System;
using System.Runtime.InteropServices;

namespace AprNes
{
    // CPROM — Mapper 013
    // PRG: 32KB fixed (single 32KB bank)
    // CHR: 16KB CHR-RAM split into two 4KB halves:
    //   Lower 4KB ($0000-$0FFF): always bank 0 (fixed)
    //   Upper 4KB ($1000-$1FFF): switchable, selected by bits[1:0] of any $8000-$FFFF write
    // Total: 4 banks × 4KB = 16KB CHR-RAM
    unsafe public class Mapper013 : IMapper
    {
        byte* PRG_ROM, ppu_ram;
        int PRG_ROM_count;
        int* Vertical;
        int chrBank = 0; // upper 4KB CHR-RAM bank (0-3)

        // 16KB dedicated CHR-RAM buffer (4 × 4KB banks) — unmanaged to allow stable pointers
        byte* chrRam;

        public MapperA12Mode A12NotifyMode => MapperA12Mode.None;
        public void NotifyA12(int addr, int ppuAbsCycle) { }
        public void PpuClock() { }
        public void CpuCycle() { }
        public void CpuClockRise() { }

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
            int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM; ppu_ram = _ppu_ram;
            PRG_ROM_count = _PRG_ROM_count;
            Vertical = _Vertical;
            if (chrRam == null)
                chrRam = (byte*)Marshal.AllocHGlobal(16 * 1024);
        }

        public void Reset()
        {
            chrBank = 0;
            for (int i = 0; i < 16 * 1024; i++) chrRam[i] = 0;
            UpdateCHRBanks();
        }

        public void UpdateCHRBanks()
        {
            // Lower 4KB: always bank 0 (offset 0)
            NesCore.chrBankPtrs[0] = chrRam;
            NesCore.chrBankPtrs[1] = chrRam + 1024;
            NesCore.chrBankPtrs[2] = chrRam + 2048;
            NesCore.chrBankPtrs[3] = chrRam + 3072;
            // Upper 4KB: switchable bank (0-3), each 4KB
            byte* upper = chrRam + (chrBank << 12);
            NesCore.chrBankPtrs[4] = upper;
            NesCore.chrBankPtrs[5] = upper + 1024;
            NesCore.chrBankPtrs[6] = upper + 2048;
            NesCore.chrBankPtrs[7] = upper + 3072;
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }
        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }

        public void MapperW_PRG(ushort address, byte value)
        {
            chrBank = value & 0x03;
            UpdateCHRBanks();
        }

        public byte MapperR_RPG(ushort address)
        {
            // 32KB fixed; PRG_ROM_count = number of 16KB banks
            // The full 32KB starts at offset 0
            return PRG_ROM[address - 0x8000];
        }

        public byte MapperR_CHR(int address)
        {
            return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF];
        }

        public void MapperW_CHR(int addr, byte val)
        {
            NesCore.chrBankPtrs[(addr >> 10) & 7][addr & 0x3FF] = val;
        }
        public void Cleanup()
        {
            if (chrRam != null) { Marshal.FreeHGlobal((IntPtr)chrRam); chrRam = null; }
        }
    }
}
