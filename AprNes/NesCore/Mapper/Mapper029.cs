using System;
using System.Runtime.InteropServices;

namespace AprNes
{
    // Sealie Computing — Mapper 029 (Homebrew)
    // PRG: 16KB switchable at $8000 + 16KB fixed (last bank) at $C000
    // CHR: 32KB CHR-RAM, 8KB pages selected by bits[1:0]
    // WRAM: 8KB at $6000-$7FFF
    // Write $8000-$FFFF: bits[1:0] = CHR 8KB bank, bits[4:2] = PRG 16KB bank
    unsafe public class Mapper029 : IMapper
    {
        byte* PRG_ROM, ppu_ram;
        int PRG_ROM_count;
        int* Vertical;
        int prgBank = 0;
        int chrBank = 0;

        // 32KB CHR-RAM (4 × 8KB banks)
        byte* chrRam;
        // 8KB WRAM
        byte* wram;

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
                chrRam = (byte*)Marshal.AllocHGlobal(32 * 1024);
            if (wram == null)
                wram = (byte*)Marshal.AllocHGlobal(8 * 1024);
        }

        public void Reset()
        {
            prgBank = 0;
            chrBank = 0;
            for (int i = 0; i < 32 * 1024; i++) chrRam[i] = 0;
            for (int i = 0; i < 8 * 1024; i++) wram[i] = 0;
            UpdateCHRBanks();
        }

        public void UpdateCHRBanks()
        {
            byte* base_ = chrRam + (chrBank << 13);
            for (int i = 0; i < 8; i++)
                NesCore.chrBankPtrs[i] = base_ + (i << 10);
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }

        public void MapperW_RAM(ushort address, byte value)
        {
            wram[address - 0x6000] = value;
        }

        public byte MapperR_RAM(ushort address)
        {
            return wram[address - 0x6000];
        }

        public void MapperW_PRG(ushort address, byte value)
        {
            chrBank = value & 0x03;
            prgBank = (value >> 2) & 0x07;
            UpdateCHRBanks();
        }

        public byte MapperR_RPG(ushort address)
        {
            if (address < 0xC000)
            {
                // Switchable 16KB bank at $8000
                int offset = (prgBank % PRG_ROM_count) * 0x4000 + (address - 0x8000);
                return PRG_ROM[offset];
            }
            else
            {
                // Fixed last 16KB bank at $C000
                int offset = (PRG_ROM_count - 1) * 0x4000 + (address - 0xC000);
                return PRG_ROM[offset];
            }
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
            if (wram != null) { Marshal.FreeHGlobal((IntPtr)wram); wram = null; }
        }
    }
}
