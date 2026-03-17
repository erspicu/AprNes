using System.Runtime.CompilerServices;

namespace AprNes
{
    unsafe public partial class NesCoreSpeed
    {
        static public byte* NES_MEM_S;

        // Memory dispatch tables (65536 entries)
        static unsafe delegate*<ushort, byte>[]        mem_read_fun_S  = new delegate*<ushort, byte>[65536];
        static unsafe delegate*<ushort, byte, void>[]  mem_write_fun_S = new delegate*<ushort, byte, void>[65536];

        // Timing counters (ppu_scanline_S, ppu_x_S declared here; cpu_cycles_S in CPU_S.cs)
        static int ppu_dot_S    = 0;        // PPU dot accumulator (3 per CPU cycle)
        static int ppu_scanline_S = 0;      // current scanline (0-261)
        static int ppu_x_S     = 0;         // current dot within scanline (0-340)

        // ----------------------------------------------------------------
        static void init_mem_S()
        {
            // Default: open bus reads 0, writes ignored
            for (int i = 0; i < 65536; i++)
            {
                mem_read_fun_S[i]  = &read_openbus_S;
                mem_write_fun_S[i] = &write_ignore_S;
            }

            // $0000-$1FFF: CPU RAM (mirrored)
            for (int i = 0x0000; i < 0x2000; i++)
            {
                mem_read_fun_S[i]  = &read_ram_S;
                mem_write_fun_S[i] = &write_ram_S;
            }

            // $2000-$3FFF: PPU registers (mirrored every 8 bytes)
            for (int i = 0x2000; i < 0x4000; i++)
            {
                mem_read_fun_S[i]  = &ppu_reg_read_S;
                mem_write_fun_S[i] = &ppu_reg_write_S;
            }

            // $4000-$4017: APU / IO
            for (int i = 0x4000; i <= 0x4017; i++)
            {
                mem_read_fun_S[i]  = &io_read_S;
                mem_write_fun_S[i] = &io_write_S;
            }

            // $4020-$5FFF: Expansion ROM
            for (int i = 0x4020; i < 0x6000; i++)
            {
                mem_read_fun_S[i]  = &exp_read_S;
                mem_write_fun_S[i] = &exp_write_S;
            }

            // $6000-$7FFF: SRAM / work RAM
            for (int i = 0x6000; i < 0x8000; i++)
            {
                mem_read_fun_S[i]  = &sram_read_S;
                mem_write_fun_S[i] = &sram_write_S;
            }

            // $8000-$FFFF: PRG ROM / mapper
            for (int i = 0x8000; i <= 0xFFFF; i++)
            {
                mem_read_fun_S[i]  = &prg_read_S;
                mem_write_fun_S[i] = &prg_write_S;
            }
        }

        // ----------------------------------------------------------------
        // tick: advance PPU (3 dots) + APU (1 step) per CPU cycle
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void tick_S()
        {
            // PPU: 3 dots per CPU cycle
            ppu_x_S += 3;
            if (ppu_x_S >= 341)
            {
                ppu_x_S -= 341;
                end_scanline_S();
            }
            apu_step_S();
        }

        // SP-25: Inline hot paths for RAM and PRG ROM reads — bypass function pointer dispatch
        // for the two most common address ranges (~80% of all data reads).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static byte Mem_r_S(ushort addr)
        {
            if (addr < 0x2000) return NES_MEM_S[addr & 0x7FF];
            if (addr >= 0x8000) return prgBankPtrs_S[(addr >> 13) & 7][addr & 0x1FFF];
            return mem_read_fun_S[addr](addr);
        }

        // SP-25: Inline RAM writes — bypass function pointer dispatch for $0000-$1FFF.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Mem_w_S(ushort addr, byte val)
        {
            if (addr < 0x2000) { NES_MEM_S[addr & 0x7FF] = val; return; }
            mem_write_fun_S[addr](addr, val);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static byte ZP_r_S(byte addr)
        {
            return NES_MEM_S[addr];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ZP_w_S(byte addr, byte val)
        {
            NES_MEM_S[addr] = val;
        }

        // ----------------------------------------------------------------
        // Dispatch handlers
        static byte  read_openbus_S(ushort a) { return 0; }
        static void  write_ignore_S(ushort a, byte v) { }

        static byte  read_ram_S(ushort a)          { return NES_MEM_S[a & 0x7FF]; }
        static void  write_ram_S(ushort a, byte v)  { NES_MEM_S[a & 0x7FF] = v; }

        static byte  sram_read_S(ushort a)          { return NES_MEM_S[a]; }
        static void  sram_write_S(ushort a, byte v)  { MapperObj_S.MapperW_RAM(a, v); }

        // SP-6: Direct PRG bank pointer access (no virtual call, no branch chain)
        static byte  prg_read_S(ushort a)           { return prgBankPtrs_S[(a >> 13) & 7][a & 0x1FFF]; }
        static void  prg_write_S(ushort a, byte v)   { MapperObj_S.MapperW_PRG(a, v); }

        static byte  exp_read_S(ushort a)           { return MapperObj_S.MapperR_EXP(a); }
        static void  exp_write_S(ushort a, byte v)   { MapperObj_S.MapperW_EXP(a, v); }
    }
}
