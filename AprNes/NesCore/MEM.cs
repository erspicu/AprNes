using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AprNes
{
    unsafe public partial class NesCore
    {
        public static byte* NES_MEM;

        static bool in_tick = false;  // prevent recursive tick from DMC fetch

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void tick()
        {
            if (in_tick) return;
            in_tick = true;

            // Promote nmi_delay from previous cycle → nmi_pending (1-cycle hardware delay)
            if (nmi_delay) { nmi_pending = true; nmi_delay = false; }

            // Per-dot NMI edge detection: rising edge → nmi_delay (not nmi_pending)
            for (int i = 0; i < 3; i++)
            {
                ppu_step_new();
                bool nmi_output = isVblank && NMIable;
                if (nmi_output && !nmi_output_prev)
                    nmi_delay = true;       // Rising edge → 1-cycle delay before pending
                nmi_output_prev = nmi_output;
            }

            apu_step();

            // Track IRQ line per-tick for penultimate-cycle polling
            irqLinePrev = irqLineCurrent;
            irqLineCurrent = statusframeint || statusdmcint || statusmapperint;

            in_tick = false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static byte Mem_r(ushort address)
        {
            tick();
            byte val = mem_read_fun[address](address);
            cpubus = val;
            return val;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Mem_w(ushort address, byte value)
        {
            tick();
            cpubus = value;
            mem_write_fun[address](address, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static byte ZP_r(byte addr) { tick(); return NES_MEM[addr]; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ZP_w(byte addr, byte value) { tick(); NES_MEM[addr] = value; }

        static Action<ushort, byte>[] mem_write_fun = null;
        static Func<ushort, byte>[] mem_read_fun = null;

        static Action<byte>[] ppu_write_fun = null;
        static Func<int, byte>[] ppu_read_fun = null;

        static void init_function()
        {
            mem_write_fun = new Action<ushort, byte>[0x10000];
            mem_read_fun = new Func<ushort, byte>[0x10000];

            ppu_write_fun = new Action<byte>[0x10000];
            ppu_read_fun = new Func<int, byte>[0x10000];

            for (int address = 0; address < 0x10000; address++)
            {
                if (address < 0x2000) mem_write_fun[address] = new Action<ushort, byte>((addr, val) => { NES_MEM[addr & 0x7ff] = val; });
                else if (address < 0x4020) mem_write_fun[address] = new Action<ushort, byte>(IO_write);
                else if (address < 0x4100) mem_write_fun[address] = new Action<ushort, byte>((addr, val) => { }); // $4020-$40FF: open bus (no effect on write)
                else if (address < 0x6000) mem_write_fun[address] = new Action<ushort, byte>(MapperObj.MapperW_ExpansionROM);
                else if (address < 0x8000) mem_write_fun[address] = new Action<ushort, byte>(MapperObj.MapperW_RAM);
                else mem_write_fun[address] = new Action<ushort, byte>(MapperObj.MapperW_PRG);
            }
            for (int address = 0; address < 0x10000; address++)
            {
                if (address < 0x2000) mem_read_fun[address] = new Func<ushort, byte>((addr) => { return NES_MEM[addr & 0x7ff]; });
                else if (address < 0x4020) mem_read_fun[address] = new Func<ushort, byte>(IO_read);
                else if (address < 0x4100) mem_read_fun[address] = new Func<ushort, byte>((addr) => { return cpubus; }); // $4020-$40FF: CPU open bus
                else if (address < 0x6000) mem_read_fun[address] = new Func<ushort, byte>(MapperObj.MapperR_ExpansionROM);
                else if (address < 0x8000) mem_read_fun[address] = new Func<ushort, byte>(MapperObj.MapperR_RAM);
                else mem_read_fun[address] = new Func<ushort, byte>(MapperObj.MapperR_RPG);
            }


            for (int address = 0; address < 0x10000; address++)
            {

                int vram_addr_wrap = 0;
                if ((address & 0x3F00) == 0x3F00)
                {


                    vram_addr_wrap = address & 0x2FFF;

                    if (vram_addr_wrap < 0x2000)
                    {
                        ppu_read_fun[address] = new Func<int, byte>((val) =>
                        {


                            ppu_2007_temp = ppu_ram[val & ((val & 0x03) == 0 ? 0x0C : 0x1F) + 0x3f00];
                            ppu_2007_buffer = MapperObj.MapperR_CHR(val & 0x2FFF);

                            ppu_2007_temp = (byte)((openbus & 0xC0) | (ppu_2007_temp & 0x3F));//add openbus fix


                            vram_addr = (ushort)((val + VramaddrIncrement) & 0x7FFF);
                            if (mapper == 4) NotifyMapperA12(vram_addr);
                            openbus = ppu_2007_temp;
                            open_bus_decay_timer = 77777;//fixed add

                            return openbus;
                        });
                    }
                    else
                    {

                        ppu_read_fun[address] = new Func<int, byte>((val) =>
                        {

                            ppu_2007_temp = ppu_ram[val & ((val & 0x03) == 0 ? 0x0C : 0x1F) + 0x3f00];
                            ppu_2007_buffer = ppu_ram[val & 0x2FFF];

                            ppu_2007_temp = (byte)((openbus & 0xC0) | (ppu_2007_temp & 0x3F));//add openbus fix

                            vram_addr = (ushort)((val + VramaddrIncrement) & 0x7FFF);
                            if (mapper == 4) NotifyMapperA12(vram_addr);
                            openbus = ppu_2007_temp;
                            open_bus_decay_timer = 77777;//fixed add
                            return openbus;
                        });
                    }
                }
                else
                {

                    vram_addr_wrap = address & 0x3FFF;

                    if (vram_addr_wrap < 0x2000)
                    {

                        ppu_read_fun[address] = new Func<int, byte>((val) =>
                        {
                            ppu_2007_temp = ppu_2007_buffer; //need read from buffer
                            ppu_2007_buffer = MapperObj.MapperR_CHR(val & 0x3FFF);//Pattern Table
                            vram_addr = (ushort)((val + VramaddrIncrement) & 0x7FFF);
                            if (mapper == 4) NotifyMapperA12(vram_addr);
                            openbus = ppu_2007_temp;
                            open_bus_decay_timer = 77777;//fixed add
                            return openbus;
                        });
                    }
                    else if (vram_addr_wrap < 0x3F00)
                    {
                        ppu_read_fun[address] = new Func<int, byte>((val) =>
                        {


                            ppu_2007_temp = ppu_2007_buffer; //need read from buffer
                            ppu_2007_buffer = ppu_ram[val & 0x2FFF]; //Name Table & Attribute Table ($3000-$3EFF mirrors $2000-$2EFF)
                            vram_addr = (ushort)((val + VramaddrIncrement) & 0x7FFF);
                            if (mapper == 4) NotifyMapperA12(vram_addr);
                            openbus = ppu_2007_temp;
                            open_bus_decay_timer = 77777;//fixed add
                            return openbus;
                        });
                    }
                    else
                    {

                        ppu_read_fun[address] = new Func<int, byte>((val) =>
                        {
                            ppu_2007_temp = ppu_2007_buffer; //need read from buffer
                            int _vram_addr_wrap = val & 0x2FFF;
                            ppu_2007_buffer = ppu_ram[_vram_addr_wrap & ((_vram_addr_wrap & 0x03) == 0 ? 0x0C : 0x1F) + 0x3f00]; // //Sprite Palette & Image Palette
                            vram_addr = (ushort)((val + VramaddrIncrement) & 0x7FFF);
                            if (mapper == 4) NotifyMapperA12(vram_addr);
                            openbus = ppu_2007_temp;
                            open_bus_decay_timer = 77777;//fixed add
                            return openbus;
                        });


                    }
                }

            }


            for (int address = 0; address < 0x10000; address++)
            {

                int vram_addr_wrap = 0;

                vram_addr_wrap = address & 0x3FFF;
                if (vram_addr_wrap < 0x2000)
                {
                    ppu_write_fun[address] = new Action<byte>((val) =>
                    {
                        int _vram_addr_wrap = vram_addr & 0x3FFF;
                        openbus = val;
                        if (CHR_ROM_count == 0) ppu_ram[_vram_addr_wrap] = val;
                        vram_addr = (ushort)((vram_addr + VramaddrIncrement) & 0x7FFF);
                        if (mapper == 4) NotifyMapperA12(vram_addr);
                    });
                }
                else if (vram_addr_wrap < 0x3f00) //Name Table & Attribute Table
                {
                    ppu_write_fun[address] = new Action<byte>((val) =>
                   {
                       int _vram_addr_wrap = vram_addr & 0x2FFF; // $3000-$3EFF mirrors $2000-$2EFF
                       int _addr_range = _vram_addr_wrap & 0xc00;
                       openbus = val;
                       int mirror = *Vertical;
                       if (mirror >= 2)
                       {
                           // One-screen mirroring: all 4 nametables map to same 1KB
                           int rel = _vram_addr_wrap & 0x3FF;
                           ppu_ram[0x2000 + rel] = ppu_ram[0x2400 + rel] = ppu_ram[0x2800 + rel] = ppu_ram[0x2C00 + rel] = val;
                       }
                       else if (mirror == 1)
                       {
                           if (_addr_range < 0x800) ppu_ram[_vram_addr_wrap] = ppu_ram[_vram_addr_wrap | 0x800] = val;
                           else ppu_ram[_vram_addr_wrap] = ppu_ram[_vram_addr_wrap & 0x37ff] = val;
                       }
                       else
                       {
                           if (_addr_range < 0x400) ppu_ram[_vram_addr_wrap] = ppu_ram[_vram_addr_wrap | 0x400] = val;
                           else if (_addr_range < 0x800) ppu_ram[_vram_addr_wrap] = ppu_ram[_vram_addr_wrap & 0x3bff] = val;
                           else if (_addr_range < 0xc00) ppu_ram[_vram_addr_wrap] = ppu_ram[_vram_addr_wrap | 0x400] = val;
                           else ppu_ram[_vram_addr_wrap] = ppu_ram[_vram_addr_wrap & 0x3bff] = val;
                       }
                       vram_addr = (ushort)((vram_addr + VramaddrIncrement) & 0x7FFF);
                       if (mapper == 4) NotifyMapperA12(vram_addr);
                   });
                }
                else
                {
                    ppu_write_fun[address] = new Action<byte>((val) =>
                   {
                       int _vram_addr_wrap = vram_addr & 0x3FFF;
                       openbus = val;
                       ppu_ram[(_vram_addr_wrap & ((_vram_addr_wrap & 0x03) == 0 ? 0x0C : 0x1F)) + 0x3f00] = val; //Sprite Palette & Image Palette
                       vram_addr = (ushort)((vram_addr + VramaddrIncrement) & 0x7FFF);
                       if (mapper == 4) NotifyMapperA12(vram_addr);
                   });
                }



            }
        }
    }
}