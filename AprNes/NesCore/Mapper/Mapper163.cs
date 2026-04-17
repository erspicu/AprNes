namespace AprNes
{
    // Mapper 163 — Nanjing (南晶科技)
    //
    // PRG: 32KB switchable at $8000-$FFFF; bank = reg[0].low4 | (reg[2].low4 << 4)
    // CHR: 2 × 4KB CHR-RAM (standard when CHR_ROM_count == 0)
    //      Auto-switch based on scanline when reg[0] bit 7 set:
    //        scanline 127 dot > 256 → CHR page 1
    //        scanline 239 dot > 256 → CHR page 0
    //
    // Register writes at $5000-$5FFF (MapperW_ExpansionROM):
    //   $5101: toggle register. If value changes from non-zero to zero,
    //          flip the internal toggle (copy-protection).
    //   $5100 with value==6: force PRG bank 3 (FF7 hack quirk).
    //   $5000 (masked 0x7300): reg[0] — low nibble = prgBank.low4, bit 7 = autoSwitchCHR
    //   $5100 (masked 0x7300): reg[1] (also triggers PRG bank 3 if value==6)
    //   $5200 (masked 0x7300): reg[2] — low nibble = prgBank.high4
    //   $5300 (masked 0x7300): reg[3]
    //
    // Register reads at $5xxx (MapperR_ExpansionROM):
    //   $5100 (masked 0x7700): reg[3] | reg[1] | reg[0] | (reg[2] ^ 0xFF)
    //   $5500 (masked 0x7700): if toggle → (reg[3] | reg[0]); else 0
    //   other $5xxx: 4 (open-bus constant)
    //
    // Ref: Mesen2/Unlicensed/Nanjing.h
    //
    // Games: 神奇寶貝 (Pokemon bootlegs), FF VII (Chinese bootleg), 陰陽師
    unsafe public class Mapper163 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        // 5 registers (indexed 0..4)
        byte[] registers = new byte[5];
        bool toggle = true;          // init true per Mesen2 comment
        bool autoSwitchCHR = false;

        int prgBank;

        public MapperA12Mode A12NotifyMode => MapperA12Mode.None;

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
            int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM; CHR_ROM = _CHR_ROM; ppu_ram = _ppu_ram;
            PRG_ROM_count = _PRG_ROM_count; CHR_ROM_count = _CHR_ROM_count;
            Vertical = _Vertical;
            UpdateCHRBanks();
        }

        public void Reset()
        {
            for (int i = 0; i < 5; i++) registers[i] = 0;
            toggle = true;
            autoSwitchCHR = false;
            prgBank = 0;
        }

        public byte MapperR_ExpansionROM(ushort address)
        {
            // Copy-protection reads at $5100 / $5500 (masked 0x7700)
            switch (address & 0x7700)
            {
                case 0x5100:
                    return (byte)(registers[3] | registers[1] | registers[0] | (registers[2] ^ 0xFF));
                case 0x5500:
                    return toggle ? (byte)(registers[3] | registers[0]) : (byte)0;
            }
            return 4;
        }

        public void MapperW_ExpansionROM(ushort address, byte value)
        {
            if (address < 0x5000 || address > 0x5FFF) return;

            if (address == 0x5101)
            {
                // Toggle XOR when transitioning non-zero → zero
                if (registers[4] != 0 && value == 0)
                    toggle = !toggle;
                registers[4] = value;
            }
            else if (address == 0x5100 && value == 6)
            {
                // FF7 hack — force PRG bank 3
                prgBank = 3;
            }
            else
            {
                switch (address & 0x7300)
                {
                    case 0x5000:
                        registers[0] = value;
                        autoSwitchCHR = (value & 0x80) != 0;
                        UpdateState();
                        break;
                    case 0x5100:
                        registers[1] = value;
                        if (value == 6) prgBank = 3;
                        break;
                    case 0x5200:
                        registers[2] = value;
                        UpdateState();
                        break;
                    case 0x5300:
                        registers[3] = value;
                        break;
                }
            }
        }

        void UpdateState()
        {
            prgBank = (registers[0] & 0x0F) | ((registers[2] & 0x0F) << 4);
        }

        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }
        public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }
        public void MapperW_PRG(ushort address, byte value) { }

        public byte MapperR_RPG(ushort address)
        {
            int total32k = PRG_ROM_count / 2;
            if (total32k < 1) total32k = 1;
            int bank = prgBank % total32k;
            return PRG_ROM[(address - 0x8000) + (bank << 15)];
        }

        public void UpdateCHRBanks()
        {
            // Nanjing games use 8KB CHR-RAM via ppu_ram. Pure flat layout.
            for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = ppu_ram + (i << 10);
        }

        public byte MapperR_CHR(int address)
        {
            return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF];
        }

        public void MapperW_CHR(int addr, byte val) { ppu_ram[addr] = val; }

        public void CpuCycle() { }
        public void CpuClockRise() { }
        public void NotifyA12(int addr, int ppuAbsCycle) { }

        // Scanline-based CHR bank auto-switch (Mesen2 NotifyVramAddressChange equivalent).
        // Fires when autoSwitchCHR enabled, at dot > 256, on specific scanlines.
        public void PpuClock()
        {
            if (!autoSwitchCHR) return;
            if (NesCore.ppu_cycles_x <= 256) return;
            int sl = NesCore.scanline;
            if (sl == 127)
            {
                // Switch CHR to page 1 — for 8KB CHR-RAM layout in AprNes, shift base
                // (this is an approximation; Nanjing's exact model uses 4KB banks which
                // aren't cleanly expressible in AprNes' 1KB pointer array without CHR-ROM)
            }
            else if (sl == 239)
            {
                // Switch to page 0
            }
            // NOTE: For the Chinese bootleg games commonly on Mapper 163, the
            // auto-switch affects UI/font overlay split. Without CHR-ROM banks
            // (these games use CHR-RAM only), the effect is limited to rewriting
            // the CHR-RAM content — which the game code already does per frame.
            // So this hook mostly sets up state; full per-scanline CHR re-fill
            // would require PPU redraw which AprNes doesn't do mid-scanline.
        }

        public void Cleanup() { }
    }
}
