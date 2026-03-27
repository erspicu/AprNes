using System;
using System.Runtime.InteropServices;

namespace AprNes
{
    // Napoleon Senki / IremLrog017 — Mapper 077
    // PRG: 32KB switchable (lower 4 bits of write data select 32KB bank)
    // CHR layout (4 × 2KB slots):
    //   Slot 0 ($0000-$07FF): CHR-ROM, bank selected by bits[7:4] of write data
    //   Slots 1-3 ($0800-$1FFF): CHR-RAM (3 × 2KB pages)
    // 4-screen mirroring from cartridge header
    unsafe public class Mapper077 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        // 6KB CHR-RAM (3 × 2KB pages) — unmanaged for stable pointers
        byte* chrRam;

        int prgBank;    // 32KB PRG bank (bits[3:0])
        int chrRomBank; // 2KB CHR-ROM bank (bits[7:4])

        public MapperA12Mode A12NotifyMode => MapperA12Mode.None;
        public void NotifyA12(int addr, int ppuAbsCycle) { }
        public void CpuCycle() { }

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
            int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM; CHR_ROM = _CHR_ROM; ppu_ram = _ppu_ram;
            PRG_ROM_count = _PRG_ROM_count; CHR_ROM_count = _CHR_ROM_count;
            Vertical = _Vertical;
            if (chrRam == null)
                chrRam = (byte*)Marshal.AllocHGlobal(6 * 1024);
        }

        public void Reset()
        {
            prgBank = 0; chrRomBank = 0;
            for (int i = 0; i < 6 * 1024; i++) chrRam[i] = 0;
            // 4-screen mirroring (use header value — don't force *Vertical here)
            UpdateCHRBanks();
        }

        public void UpdateCHRBanks()
        {
            // Slot 0 ($0000-$07FF): CHR-ROM (switchable 2KB)
            if (CHR_ROM_count > 0)
            {
                int n2k = CHR_ROM_count * 4;
                int page = chrRomBank % n2k;
                NesCore.chrBankPtrs[0] = CHR_ROM + (page << 11);
                NesCore.chrBankPtrs[1] = CHR_ROM + (page << 11) + 1024;
            }
            else
            {
                NesCore.chrBankPtrs[0] = ppu_ram;
                NesCore.chrBankPtrs[1] = ppu_ram + 1024;
            }
            // Slots 1-3 ($0800-$1FFF): CHR-RAM (3×2KB pages 0,1,2)
            NesCore.chrBankPtrs[2] = chrRam;           // ram page 0 ($0800)
            NesCore.chrBankPtrs[3] = chrRam + 1024;    // ram page 0 hi
            NesCore.chrBankPtrs[4] = chrRam + 2048;    // ram page 1 ($1000)
            NesCore.chrBankPtrs[5] = chrRam + 3072;    // ram page 1 hi
            NesCore.chrBankPtrs[6] = chrRam + 4096;    // ram page 2 ($1800)
            NesCore.chrBankPtrs[7] = chrRam + 5120;    // ram page 2 hi
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }
        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }

        public void MapperW_PRG(ushort address, byte value)
        {
            prgBank = value & 0x0F;
            chrRomBank = (value >> 4) & 0x0F;
            UpdateCHRBanks();
        }

        public byte MapperR_RPG(ushort address)
        {
            // PRG_ROM_count = number of 16KB banks; n32k = number of 32KB banks
            int n32k = PRG_ROM_count / 2;
            if (n32k <= 0) n32k = 1;
            return PRG_ROM[(address - 0x8000) + ((prgBank % n32k) << 15)];
        }

        public byte MapperR_CHR(int address)
        {
            return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF];
        }

        public void MapperW_CHR(int addr, byte val)
        {
            // Only CHR-RAM region ($0800-$1FFF) is writable
            byte* ptr = NesCore.chrBankPtrs[(addr >> 10) & 7];
            if (ptr >= chrRam && ptr < chrRam + 6 * 1024)
                ptr[addr & 0x3FF] = val;
        }
        public void Cleanup()
        {
            if (chrRam != null) { Marshal.FreeHGlobal((IntPtr)chrRam); chrRam = null; }
        }
    }
}
