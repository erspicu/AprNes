using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AprNes
{

    //把system與UI有關的顯示處理切割出去到NES Core外層

    unsafe static public partial class NesCore
    {
        static public volatile int frame_count = 0;
        static public int ppu_cycles_x = 0, scanline = -1; // 241;

        // Palette ref: http://www.thealmightyguru.com/Games/Hacking/Wiki/index.php?title=NES_Palette
        // Fills NesColors (uint*) and default palette into ppu_ram[0x3F00..0x3F1F] (byte*)
        static void initPalette()
        {
            if (Region == RegionType.PAL)
            {
                // PAL palette from 2C07 voltage levels + YUV decoding
                generatePaletteFromVoltages(
                    new float[] { -0.117f, 0.000f, 0.223f, 0.490f },
                    new float[] {  0.306f, 0.543f, 0.741f, 1.000f },
                    true);
            }
            else
            {
                // NTSC/Dendy hardcoded palette (verified with 174 blargg + 136 AccuracyCoin tests)
                NesColors[ 0]=0xFF7C7C7C; NesColors[ 1]=0xFF0000FC; NesColors[ 2]=0xFF0000BC; NesColors[ 3]=0xFF4428BC;
                NesColors[ 4]=0xFF940084; NesColors[ 5]=0xFFA80020; NesColors[ 6]=0xFFA81000; NesColors[ 7]=0xFF881400;
                NesColors[ 8]=0xFF503000; NesColors[ 9]=0xFF007800; NesColors[10]=0xFF006800; NesColors[11]=0xFF005800;
                NesColors[12]=0xFF004058; NesColors[13]=0xFF000000; NesColors[14]=0xFF000000; NesColors[15]=0xFF000000;
                NesColors[16]=0xFFBCBCBC; NesColors[17]=0xFF0078F8; NesColors[18]=0xFF0058F8; NesColors[19]=0xFF6844FC;
                NesColors[20]=0xFFD800CC; NesColors[21]=0xFFE40058; NesColors[22]=0xFFF83800; NesColors[23]=0xFFE45C10;
                NesColors[24]=0xFFAC7C00; NesColors[25]=0xFF00B800; NesColors[26]=0xFF00A800; NesColors[27]=0xFF00A844;
                NesColors[28]=0xFF008888; NesColors[29]=0xFF000000; NesColors[30]=0xFF000000; NesColors[31]=0xFF000000;
                NesColors[32]=0xFFF8F8F8; NesColors[33]=0xFF3CBCFC; NesColors[34]=0xFF6888FC; NesColors[35]=0xFF9878F8;
                NesColors[36]=0xFFF878F8; NesColors[37]=0xFFF85898; NesColors[38]=0xFFF87858; NesColors[39]=0xFFFCA044;
                NesColors[40]=0xFFF8B800; NesColors[41]=0xFFB8F818; NesColors[42]=0xFF58D854; NesColors[43]=0xFF58F898;
                NesColors[44]=0xFF00E8D8; NesColors[45]=0xFF787878; NesColors[46]=0xFF000000; NesColors[47]=0xFF000000;
                NesColors[48]=0xFFFCFCFC; NesColors[49]=0xFFA4E4FC; NesColors[50]=0xFFB8B8F8; NesColors[51]=0xFFD8B8F8;
                NesColors[52]=0xFFF8B8F8; NesColors[53]=0xFFF8A4C0; NesColors[54]=0xFFF0D0B0; NesColors[55]=0xFFFCE0A8;
                NesColors[56]=0xFFF8D878; NesColors[57]=0xFFD8F878; NesColors[58]=0xFFB8F8B8; NesColors[59]=0xFFB8F8D8;
                NesColors[60]=0xFF00FCFC; NesColors[61]=0xFFF8D8F8; NesColors[62]=0xFF000000; NesColors[63]=0xFF000000;
            }
        }

        /// <summary>
        /// Generate 64-color NES palette from PPU DAC voltage levels.
        /// Each color index encodes a luminance column (bits 5-4) and hue (bits 3-0).
        /// The PPU outputs a square wave between Lo/Hi voltages; phase determines hue.
        /// </summary>
        static void generatePaletteFromVoltages(float[] lo, float[] hi, bool palMode)
        {
            for (int idx = 0; idx < 64; idx++)
            {
                int row = (idx >> 4) & 3;
                int hue = idx & 0xF;
                float y = 0, cb = 0, cr = 0;

                if (hue == 0) // achromatic (gray)
                {
                    y = (lo[row] + hi[row]) * 0.5f;
                }
                else if (hue <= 12) // chromatic
                {
                    float phase = (float)((hue - 1) * Math.PI / 6.0);
                    for (int s = 0; s < 12; s++)
                    {
                        float angle = (float)(s * Math.PI / 6.0);
                        // Signal is HIGH when sample is within ±90° of hue phase
                        float diff = angle - phase;
                        if (diff > Math.PI)  diff -= (float)(2 * Math.PI);
                        if (diff < -Math.PI) diff += (float)(2 * Math.PI);
                        float sig = (Math.Abs(diff) <= Math.PI * 0.5f) ? hi[row] : lo[row];
                        y  += sig;
                        cb += sig * (float)Math.Cos(angle);
                        cr += sig * (float)Math.Sin(angle);
                    }
                    y  /= 12f;
                    cb /= 6f;
                    cr /= 6f;
                }
                else if (hue == 13) // darker achromatic
                {
                    y = lo[row];
                }
                // hue 14, 15 = black (y=0, cb=cr=0)

                float r, g, b;
                if (palMode) // YUV → RGB
                {
                    r = y + 1.140f * cr;
                    g = y - 0.395f * cb - 0.581f * cr;
                    b = y + 2.032f * cb;
                }
                else // YIQ → RGB (NTSC)
                {
                    r = y + 0.956f * cb + 0.621f * cr;
                    g = y - 0.272f * cb - 0.647f * cr;
                    b = y - 1.107f * cb + 1.704f * cr;
                }

                int ri = Math.Max(0, Math.Min(255, (int)(r * 255)));
                int gi = Math.Max(0, Math.Min(255, (int)(g * 255)));
                int bi = Math.Max(0, Math.Min(255, (int)(b * 255)));
                NesColors[idx] = 0xFF000000 | ((uint)ri << 16) | ((uint)gi << 8) | (uint)bi;
            }
        }

        static void initPaletteRam()
        {
            // table from blargg_ppu power_up_palette.asm
            ppu_ram[0x3F00]=0x09; ppu_ram[0x3F01]=0x01; ppu_ram[0x3F02]=0x00; ppu_ram[0x3F03]=0x01;
            ppu_ram[0x3F04]=0x00; ppu_ram[0x3F05]=0x02; ppu_ram[0x3F06]=0x02; ppu_ram[0x3F07]=0x0D;
            ppu_ram[0x3F08]=0x08; ppu_ram[0x3F09]=0x10; ppu_ram[0x3F0A]=0x08; ppu_ram[0x3F0B]=0x24;
            ppu_ram[0x3F0C]=0x00; ppu_ram[0x3F0D]=0x00; ppu_ram[0x3F0E]=0x04; ppu_ram[0x3F0F]=0x2C;
            ppu_ram[0x3F10]=0x09; ppu_ram[0x3F11]=0x01; ppu_ram[0x3F12]=0x34; ppu_ram[0x3F13]=0x03;
            ppu_ram[0x3F14]=0x00; ppu_ram[0x3F15]=0x04; ppu_ram[0x3F16]=0x00; ppu_ram[0x3F17]=0x14;
            ppu_ram[0x3F18]=0x08; ppu_ram[0x3F19]=0x3A; ppu_ram[0x3F1A]=0x00; ppu_ram[0x3F1B]=0x02;
            ppu_ram[0x3F1C]=0x00; ppu_ram[0x3F1D]=0x20; ppu_ram[0x3F1E]=0x2C; ppu_ram[0x3F1F]=0x08;
            RebuildPaletteCache();
        }

        //ppu ctrl 0x2000
        static int BaseNameTableAddr = 0, VramaddrIncrement = 1, SpPatternTableAddr = 0, BgPatternTableAddr = 0;
        static public bool Spritesize8x16 = false;
        static bool NMIable = false;

        //ppu mask 0x2001 — four-tier flag system (TriCNES model)
        // Tier 1: _Instant — set immediately on $2001 write. Used for: odd frame skip, OAM corruption,
        //         renderingEnabled (core PPU state), vram increment, sprite 0 re-eval
        // Tier 2: ShowBackGround/ShowSprites — delayed by ppu2001UpdateDelay (2-3 PPU cycles).
        //         Used for: pixel rendering, backdrop fill, sprite compositing
        // Tier 3: ppuRenderingEnabled — end-of-dot delay of Tier 1. Used for: tile fetch, sprite eval
        public static bool ShowBackGround = false, ShowSprites = false; // Tier 2 (delayed)
        static bool ShowBackGround_Instant = false, ShowSprites_Instant = false; // Tier 1 (immediate)
        static bool ShowBgLeft8 = true, ShowSprLeft8 = true; // bit1/bit2 (delayed with $2001)
        static byte ppuEmphasis = 0; // $2001[7:5] emphasis bits (for NTSC signal amplitude)

        // NTSC 類比模式：每條掃描線的原始調色盤索引緩衝區（256 bytes，0x00-0x3F）
        // 由 RenderBGTile 和 RenderSpritesLine 在 AnalogEnabled=true 時填入
        static byte[] ntscScanBuf = new byte[256];

        // MMC5 extended attribute mode (per-tile palette + CHR bank from ExRAM)
        static ushort extAttrNTOffset;  // nametable offset saved at phase 1
        static int extAttrChrBank;      // 4KB CHR bank computed at phase 3

        //ppu status 0x2002.
        static bool isSpriteOverflow = false, isSprite0hit = false, isVblank = false;

        static int vram_addr_internal = 0, vram_addr = 0, FineX = 0;
        static bool vram_latch = false;
        static byte ppu_2007_buffer = 0;
        // $2007 state machine (TriCNES model: PPU_Data_StateMachine)
        // States: 9=idle, 0=just accessed, 1=buffer update, 3=write execute, 4=increment, 8=deferred write
        static int ppu2007SM = 9;
        static bool ppu2007SM_isRead = false;
        static byte ppu2007SM_writeValue = 0;
        static bool ppu2007SM_bufferLate = false; // alignment: buffer updated at state 4 instead of 1
        static int ppu2007SM_addr = 0; // vram_addr snapshot at time of access
        static bool ppu2007SM_interruptedReadToWrite = false; // TriCNES: write during active read SM
        // P3-3: Mystery write flags (TriCNES consecutive $2007 access model)
        static bool ppu2007SM_performMysteryWrite = false;   // TriCNES: PPU_Data_StateMachine_PerformMysteryWrite
        static bool ppu2007SM_normalWriteBehavior = false;   // TriCNES: PPU_Data_StateMachine_NormalWriteBehavior
        static bool ppu2007SM_updateVramAddrEarly = false;   // TriCNES: PPU_Data_StateMachine_UpdateVRAMAddressEarly
        static bool ppu2007SM_readDelayed = false;           // TriCNES: PPU_Data_StateMachine_Read_Delayed
        static ushort ppu2007SM_mysteryAddr = 0;             // TriCNES: PPU_VRAM_MysteryAddress

        // $2000 delayed control update (TriCNES: PPU_Update2000Delay, 1-2 PPU cycles)
        // ALL fields delayed: NMI enable, pattern table, sprite size, nametable, increment
        static int ppu2000UpdateDelay = 0;
        static byte ppu2000PendingValue = 0;

        // $2001 delayed mask update (TriCNES: PPU_Update2001Delay, 2-3 PPU cycles)
        // _Instant flags set immediately; ShowBackGround/ShowSprites applied after delay
        static int ppu2001UpdateDelay = 0;
        static byte ppu2001PendingValue = 0;
        // Emphasis bits have independent delay (TriCNES: PPU_Update2001EmphasisBitsDelay)
        // Alignment 0,3: 2 cycles; Alignment 1,2: 1 cycle (with immediate Greyscale+Blue at align 0,3)
        static int ppu2001EmphasisDelay = 0;
        static byte ppu2001EmphasisPending = 0;

        // $2005 delayed scroll update (TriCNES model: 1-2 PPU dots after CPU write)
        static int ppu2005UpdateDelay = 0;
        static byte ppu2005PendingValue = 0;

        // $2006 delayed t→v copy (TriCNES model: 3 PPU dots after CPU write)
        // Real hardware doesn't update vram_addr immediately on the second $2006 write;
        // there's a ~4-5 PPU dot delay depending on CPU/PPU alignment.
        static int ppu2006UpdateDelay = 0;
        static int ppu2006PendingAddr = 0;
        static byte* spr_ram;
        static public byte* ppu_ram;

        // TriCNES: PPU_AddressBus — persistent address bus, updated at tile fetch phases.
        // Mapper's PpuClock() reads this every dot for A12 edge detection.
        // Set at BG phases 1/3/5/7 (odd), sprite phases, garbage NT, and rendering-disabled.
        static public int ppuAddressBus;
        static public bool ppuA12Prev; // TriCNES: PPU_A12_Prev — recorded at start of PPU cycle, checked by mapper at end
        // CHR-fetch-only A12 state: updated ONLY at CHR pattern fetch phases (not NT/AT).
        // Used by MMC3 M2 filter — the filter must not see NT/AT addresses ($2xxx, A12=1)
        // because those brief A12=1 spikes during BG fetch would prevent filter saturation.
        static public int ppuChrFetchA12;

        // P4-1: TriCNES-style per-alignment OAM corruption model
        // When rendering disabled mid-scanline → capture corruption index from evalOam2Addr.
        // When rendering re-enabled → apply corruption (copy row 0 over target row),
        // UNLESS alignment 1 or 2 suppresses it.
        static byte* corruptOamRow; // 32 bytes (legacy, kept for allocation)
        static bool prevRenderingEnabled = false;
        static bool oamCorruptPending = false;          // Corruption recorded from disable, awaiting re-enable
        static bool oamCorruptSuppressed = false;       // Alignment 1,2 suppress corruption on re-enable
        static int oamCorruptIndex = 0;                 // 6.5: TriCNES PPU_OAMCorruptionIndex (from OAM2Address)

        // TriCNES delayed OAM corruption model (PPU_Update2001OAMCorruptionDelay)
        static int oamCorruptDelay = 0;                 // Countdown (PPU cycles) before corruption disable flag fires
        static bool oamCorruptWasRendering = false;     // TriCNES: PPU_WasRenderingBefore2001Write
        static byte oamCorrupt2001Value = 0;            // TriCNES: PPU_Update2001Value
        static bool oamCorruptDisabledFlag = false;     // TriCNES: PPU_OAMCorruptionRenderingDisabledOutOfVBlank

        // P4-2: Palette corruption flags
        static public uint* ScreenBuf1x;
        static uint* NesColors; //, targetSize;
        static int* Buffer_BG_array;
        static byte spr_ram_add = 0;

        static bool oddSwap = false;
        static bool ppuRenderingEnabled = false; // Tier 3: Delayed rendering enable (end of PPU dot)

        // TriCNES v2: Palette corruption flags
        static bool ppuPaletteCorruptionFromVChange = false;    // v left palette range ($3F00+) on visible scanline
        static bool ppuPaletteCorruptionFromDisable = false;    // rendering disabled when v >= $3C00

        // Deferred commit: CXinc (TriCNES: PPU_Commit_PatternHighFetch → CXinc at next dot)
        // In TriCNES, CHR high commit + CXinc fires at the NEXT full step (1 dot after phase 7).

        // Tier 4: Alignment-dependent delayed flags for sprite evaluation (TriCNES: _Delayed)
        // Source: Tier 2 (ShowBackGround/ShowSprites), NOT Tier 1 (Instant).
        // Updated before/after sprite eval depending on mcCpuClock & 3.
        static bool ShowBG_EvalDelay = false;   // TriCNES: PPU_Mask_ShowBackground_Delayed
        static bool ShowSpr_EvalDelay = false;  // TriCNES: PPU_Mask_ShowSprites_Delayed

        // ── Per-sprite shift registers (TriCNES P2-3: per-dot sprite rendering) ──
        // Filled at dots 257-320 from secondary OAM tile fetch, rendered at dots 1-256.
        // TriCNES: PPU_SpriteShiftRegisterL/H, PPU_SpriteShifterCounter, PPU_SpriteAttribute
        static byte* sprShiftL;       // Low bitplane shift register
        static byte* sprShiftH;       // High bitplane shift register
        static int* sprXCounter;        // X position countdown (>0=waiting, 0=shifting)
        static byte* sprFetchAttr;     // Attribute byte per slot (palette, priority, flip)
        static byte* sprXPos;           // X position per slot (for counter init at dot 339)
        static int sprSlotCount = 0;                   // Number of valid sprites fetched (from evalSpriteCount)
        static bool spriteAnyActive = false;            // Fast-path: any sprite has non-zero shift data

        // Palette cache: 32-entry (mirrors NES palette RAM layout), rebuilt on palette write
        static uint* palCache;  // NesColors[ppu_ram[0x3F00+i] & 0x3F] for i=0..31
        // sprOam2Addr removed — unified into evalOam2Addr (TriCNES: single OAM2Address)
        static bool sprZeroInSlots = false;            // Sprite 0 is in slot 0

        // ── 3-dot pixel output pipeline (TriCNES P2-2) ──
        // TriCNES: PrevPrevPrevDotColor → PrevPrevDotColor → PrevDotColor → DotColor
        // DrawToScreen uses PrevPrevPrevDotColor (3 dot delay).
        // Pipeline stores composite (BG+sprite) color and palette index.
        static uint dotColor = 0, prevDotColor = 0, prevPrevDotColor = 0, prevPrevPrevDotColor = 0;
        static byte dotPalIdx = 0, prevDotPalIdx = 0, prevPrevDotPalIdx = 0, prevPrevPrevDotPalIdx = 0;

        // ── P4-4: Odd frame skip side effects ──
        // TriCNES: SkippedPreRenderDot341 — set when odd frame skip occurs,
        // persists until scanline 0 dot 2, affects sprite shifter and dummy NT.
        static bool skippedPreRenderDot341 = false;

        // TriCNES NMI model: level signal + edge detection at instruction boundary
        static bool NMILine = false;              // NMI level signal (set at CPUClock==8)
        static bool nmiPinsSignal = false;        // Latched NMILine at last instruction boundary
        static bool nmiPrevPinsSignal = false;    // Previous latch (for edge detection)

        // TriCNES VBL latch pipeline: pendingVblank → ppuVSET → Latch1/Latch2 → isVblank
        static bool ppuVSET = false;              // TriCNES: PPU_VSET
        static bool ppuVSET_Latch1 = false;       // TriCNES: PPU_VSET_Latch1
        static bool ppuVSET_Latch2 = false;       // TriCNES: PPU_VSET_Latch2

        // TriCNES Sprite0 hit pipeline: pending → pending2 → actual (1.5 dot delay)
        static bool pendingSprite0Hit2 = false;    // TriCNES: PPUStatus_PendingSpriteZeroHit2
        static bool canDetectSprite0Hit = true;    // TriCNES: PPU_CanDetectSpriteZeroHit — reset at pre-render, cleared on hit

        // Delayed flag snapshots for $2002 split-timing read
        static bool isSprite0hit_Delayed = false;  // TriCNES: PPUStatus_SpriteZeroHit_Delayed
        static bool isSpriteOverflow_Delayed = false; // TriCNES: PPUStatus_SpriteOverflow_Delayed

        //https://wiki.nesdev.com/w/index.php/PPU_scrolling

        #region cycle-accurate PPU

        // Coarse X increment
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void CXinc()
        {
            if ((vram_addr & 0x001F) == 31)
                vram_addr ^= 0x041F; // clear low 5 bits + flip NT bit in one XOR
            else
                vram_addr += 1;
        }

        // Y increment
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Yinc()
        {
            if ((vram_addr & 0x7000) != 0x7000)
                vram_addr += 0x1000;
            else
            {
                vram_addr &= ~0x7000;
                int y = vram_addr & 0x03E0; // no shift — work in bit position directly
                if (y == 0x03A0)      // 29 << 5
                { vram_addr ^= 0x0800; vram_addr &= ~0x03E0; }
                else if (y == 0x03E0) // 31 << 5
                { vram_addr &= ~0x03E0; }
                else
                { vram_addr += 0x0020; } // 1 << 5
            }
        }

        // ── Raw PPU bus read/write (no $2007 register side effects) ──
        // Used by ppu_r_2007/ppu_w_2007 and future $2007 state machine.
        // Tile fetch uses chrBankPtrs/ppu_ram directly (not these functions).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static byte PpuBusRead(int addr)
        {
            addr &= 0x3FFF;
            if (addr < 0x2000)
                return MapperObj.MapperR_CHR(addr);
            if (addr < 0x3F00)
            {
                int nt_addr = addr & 0x2FFF;
                return ntChrOverrideEnabled
                    ? ntBankPtrs[(nt_addr >> 10) & 3][nt_addr & 0x3FF]
                    : ppu_ram[CIRAMAddr(nt_addr)];
            }
            // Palette ($3F00-$3FFF): mirrored, transparent-mirrored
            { int pa = addr & 0x1F; if ((pa & 3) == 0) pa &= 0x0F; return ppu_ram[0x3F00 + pa]; }
        }

        static void PpuBusWrite(int addr, byte val)
        {
            addr &= 0x3FFF;
            if (addr < 0x2000)
            {
                MapperObj.MapperW_CHR(addr, val);
            }
            else if (addr < 0x3F00)
            {
                int nt_addr = addr & 0x2FFF;
                if (ntChrOverrideEnabled)
                {
                    int slot = (nt_addr >> 10) & 3;
                    if (ntBankWritable[slot])
                        ntBankPtrs[slot][nt_addr & 0x3FF] = val;
                }
                else
                    ppu_ram[CIRAMAddr(nt_addr)] = val;
            }
            else
            {
                int pa = addr & 0x1F; if ((pa & 3) == 0) pa &= 0x0F;
                ppu_ram[0x3F00 + pa] = val;
                RebuildPaletteCache();
            }
        }

        // Rebuild 32-entry palette color cache (called on palette write — rare, ~1-100x/frame)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void RebuildPaletteCache()
        {
            if (palCache == null) return;
            uint* cache = palCache;
            byte* ram = ppu_ram + 0x3F00;
            uint* colors = NesColors;

            for (int i = 0; i < 32; i++)
                cache[i] = colors[ram[i] & 0x3F];

            // Mirror patch: sprite transparent colors → BG transparent colors
            cache[16] = cache[0];
            cache[20] = cache[4];
            cache[24] = cache[8];
            cache[28] = cache[12];
        }

        // $2007 access increment: during rendering → CXinc + Yinc; otherwise → +1/+32
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Increment2007()
        {
            if ((ShowBackGround_Instant || ShowSprites_Instant) && (scanline < 240 || scanline == preRenderLine))
            {
                CXinc();
                Yinc();
            }
            else
            {
                vram_addr = (vram_addr + VramaddrIncrement) & 0x7FFF;
            }
            if (mapperNeedsA12) NotifyMapperA12(vram_addr);
        }

        // P3-3: $2007 state machine tick — called from both ppu_half_step and ppu_step_common
        // TriCNES: PPU_Data_StateMachine logic in _EmulatePPU (lines 1322-1496)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Ppu2007SmTick()
        {
            if (ppu2007SM >= 9) return;

            if (ppu2007SM == 1 && ppu2007SM_isRead && !ppu2007SM_bufferLate)
            {
                int a = ppu2007SM_addr & 0x3FFF;
                ppu_2007_buffer = (a >= 0x3F00) ? PpuBusRead(a & 0x2FFF) : PpuBusRead(a);
            }
            else if (ppu2007SM == 3)
            {
                // P3-3: NormalWriteBehavior vs MysteryWrite (TriCNES lines 1351-1396)
                if (ppu2007SM_normalWriteBehavior)
                {
                    ppu2007SM_normalWriteBehavior = false;
                    if (!ppu2007SM_isRead || !ppu2007SM_readDelayed)
                        PpuBusWrite(ppu2007SM_addr, ppu2007SM_writeValue);
                }
                else if (!ppu2007SM_isRead && ppu2007SM_performMysteryWrite)
                {
                    if (ppu2007SM_mysteryAddr >= 0x3F00)
                        PpuBusWrite((ushort)(ppu2007SM_addr & 0x2FFF), (byte)ppu2007SM_mysteryAddr);
                    else
                    {
                        PpuBusWrite(ppu2007SM_mysteryAddr, (byte)ppu2007SM_mysteryAddr);
                        PpuBusWrite((ushort)ppu2007SM_addr, (byte)ppu2007SM_addr);
                    }
                }
            }
            else if (ppu2007SM == 4)
            {
                if (ppu2007SM_isRead && ppu2007SM_bufferLate)
                {
                    int a = ppu2007SM_addr & 0x3FFF;
                    ppu_2007_buffer = (a >= 0x3F00) ? PpuBusRead(a & 0x2FFF) : PpuBusRead(a);
                    ppu2007SM_bufferLate = false;
                }
                if (ppu2007SM_updateVramAddrEarly)
                {
                    ppu2007SM_updateVramAddrEarly = false;
                    vram_addr = (ushort)((vram_addr + VramaddrIncrement) & 0x3FFF);
                    if (ppu2007SM_isRead)
                    {
                        int a = vram_addr & 0x3FFF;
                        ppu_2007_buffer = (a >= 0x3F00) ? PpuBusRead(a & 0x2FFF) : PpuBusRead(a);
                    }
                }
                Increment2007();
                if ((!ppu2007SM_isRead || !ppu2007SM_readDelayed) && ppu2007SM_performMysteryWrite)
                {
                    if ((mcCpuClock & 3) != 0)
                    {
                        int a = vram_addr & 0x3FFF;
                        if (a >= 0x3F00)
                            PpuBusWrite((ushort)(a & 0x2FFF), ppu2007SM_writeValue);
                        else
                            PpuBusWrite((ushort)a, ppu2007SM_writeValue);
                    }
                }
                ppu2007SM_isRead = ppu2007SM_readDelayed;
                ppu2007SM_performMysteryWrite = false;
            }
            else if (ppu2007SM == 8 && ppu2007SM_interruptedReadToWrite)
            {
                if ((mcCpuClock & 3) != 0)
                    PpuBusWrite(ppu2007SM_addr, ppu2007SM_writeValue);
                ppu2007SM_interruptedReadToWrite = false;
                vram_addr = (ushort)((vram_addr + VramaddrIncrement) & 0x3FFF);
                if (mapperNeedsA12) NotifyMapperA12(vram_addr);
            }
            ppu2007SM++;
        }

        // hori(v) = hori(t)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void CopyHoriV()
        {
            vram_addr = (vram_addr & ~0x041F) | (vram_addr_internal & 0x041F);
        }

        // CIRAM address translation: maps nametable address ($2000-$2FFF) to one of
        // two physical CIRAM pages ($2000-$23FF = page 0, $2400-$27FF = page 1)
        // based on current mirroring mode.  Real hardware has only 2 KB CIRAM;
        // mirroring is done at the address-decode level, not by data duplication.
        // Branchless CIRAM address MUX — magic number 0xF0AC encodes mirror truth table
        // Bit index = (mirror << 2) | ((addr >> 10) & 3), result bit = output bit 10
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int CIRAMAddr(int addr)
        {
            int m = *Vertical;
            if (m > 3) return addr & 0x2FFF; // 4-screen (rare, perfectly predicted false)
            int shift = (m << 2) | ((addr >> 10) & 3);
            return (addr & 0x23FF) | (((0xF0AC >> shift) & 1) << 10);
        }

        // ---- Tile fetch state ----
        static byte NTVal = 0, ATVal = 0;

        // ---- Per-dot render shift registers (TriCNES model: shifted left each half-step) ----
        // Single set used for both pixel output and sprite 0 hit detection.
        // Reloaded via deferred commit (phase 7 sets flag → next half-step loads).
        static int renderLow = 0, renderHigh = 0;
        // Per-dot attribute shift: shifted alongside render registers, serial-in from latch
        static int renderAttrLow = 0, renderAttrHigh = 0;
        // Attribute latch: 2-bit value from which bits are shifted in (TriCNES: PPU_AttributeLatchRegister)
        static byte attrLatch = 0;
        static byte pendingAttrLatch = 0; // TriCNES: PPU_Attribute → committed to attrLatch at load time

        // TriCNES commit chain (PPU_RenderTemp + commit flags)
        // Full step: tile fetch stores to renderTemp, sets commit flags
        // NEXT full step: CommitShiftRegistersAndBitPlanes processes flags (UNGATED)
        // Half step: CommitShiftRegistersAndBitPlanes_HalfDot loads shift registers
        static byte renderTemp = 0;             // TriCNES: PPU_RenderTemp
        static bool commitNTFetch = false;       // TriCNES: PPU_Commit_NametableFetch
        static bool commitATFetch = false;       // TriCNES: PPU_Commit_AttributeFetch
        static bool commitPatLowFetch = false;   // TriCNES: PPU_Commit_PatternLowFetch
        static bool commitPatHighFetch = false;  // TriCNES: PPU_Commit_PatternHighFetch

        // Deferred shift register reload (TriCNES: PPU_Commit_LoadShiftRegisters)
        static byte pendingTileLow = 0, pendingTileHigh = 0;
        static bool commitLoadShiftReg = false;

        // ---- Attribute 3-stage pipeline ----
        // Phase-3 shifts ATVal into p1; phase-7 render reads p3 (2 groups later).
        // This correctly delays attribute by 2 fetch groups with no index drift.

        // (per-dot rendering reads palette directly from ppu_ram + NesColors)



        // TriCNES: PPU_SpriteEvaluation_GetSpriteAddress — compute sprite CHR pattern address
        // for the given secondary OAM slot during sprite tile fetch (dots 257-320).
        static int ComputeSpritePatternAddr(int slot)
        {
            int offset  = slot << 2;
            int sprY    = secondaryOAM[offset];
            int sprTile = secondaryOAM[offset + 1];
            int sprAttr = secondaryOAM[offset + 2];
            int row = (scanline & 0xFF) - sprY;

            if (!Spritesize8x16)
            {
                // 8x8: branchless Y-flip via XOR mask
                // -(sprAttr >> 7) = 0 (no flip) or -1 (flip); & 7 → 0 or 7; row ^= 7 = 7-row
                row ^= -(sprAttr >> 7) & 7;
                return SpPatternTableAddr | (sprTile << 4) | (row & 7);
            }
            else
            {
                // 8x16: branchless flip + bitwise tile half selection
                row ^= -(sprAttr >> 7) & 15;
                return ((sprTile & 1) << 12) | ((sprTile & 0xFE) << 4) | ((row & 8) << 1) | (row & 7);
            }
        }

        // Reverse bits in a byte — 256-byte LUT (unmanaged, no bounds check)
        static byte* FlipTable;
        static void InitFlipTable()
        {
            FlipTable = (byte*)Marshal.AllocHGlobal(256);
            for (int i = 0; i < 256; i++)
            {
                int v = i;
                v = ((v & 0xF0) >> 4) | ((v & 0x0F) << 4);
                v = ((v & 0xCC) >> 2) | ((v & 0x33) << 2);
                v = ((v & 0xAA) >> 1) | ((v & 0x55) << 1);
                FlipTable[i] = (byte)v;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static byte FlipByte(byte b) => FlipTable[b];

        // TriCNES v2: Palette corruption — hardware-tested lookup table.
        // Corrupts palette RAM when v register leaves palette range ($3F00+)
        // on visible scanlines, or when rendering is disabled with v >= $3C00.
        // Only fires on CPU/PPU alignment 2 (mcCpuClock & 3).
        // Ported from TriCNES 20260410 Emulator.cs lines 3251-3566.
        static void CorruptPalettes(int bgColor, int vAddr)
        {
            if ((mcCpuClock & 3) != 2) return;

            int vNibble = vAddr & 0xF;
            byte[] c = new byte[32];
            for (int i = 0; i < 32; i++) c[i] = ppu_ram[0x3F00 + i];

            switch (bgColor)
            {
                case 0:
                    c[vNibble] = (byte)((c[0] & c[vNibble & 0xC]) | (c[0] & c[vNibble]) | (c[vNibble & 0xC] & c[vNibble]));
                    break;
                case 1:
                    switch (vNibble)
                    {
                        case 0:  c[0x0]=(byte)((c[0x1]&c[0xD])|c[0x0]); c[0x4]=c[0x5]; c[0x8]=c[0x9]; c[0xC]=c[0xD]; break;
                        case 1:  break;
                        case 2:  c[0x2]=(byte)((c[0x2]|c[0xD])&c[0x3]); c[0x3]=(byte)((c[0x1]|c[0x2])&c[0x3]); c[0x6]=(byte)((c[0x6]|c[0x5])&c[0x7]); c[0xA]=(byte)((c[0xA]|c[0x9])&c[0xB]); c[0xE]=c[0xD]; c[0xF]=c[0xD]; break;
                        case 3:  c[0x3]&=(byte)(c[0x1]|c[0xD]); c[0xF]=c[0xD]; break;
                        case 4:  c[0x0]=c[0x1]; c[0x4]=(byte)((c[0x5]&c[0xD])|c[0x4]); c[0x8]=c[0x9]; c[0xC]=c[0xD]; break;
                        case 5:  break;
                        case 6:  c[0x2]=(byte)((c[0x2]|c[0x1])&c[0x3]); c[0x6]=(byte)((c[0x6]|c[0x7])&c[0xD]); c[0x7]=(byte)((c[0x7]|c[0x6])&c[0x5]); c[0xA]=(byte)((c[0xA]|c[0x9])&c[0xB]); c[0xE]=c[0xD]; c[0xF]=c[0xD]; break;
                        case 7:  c[0x7]&=(byte)(c[0x5]|c[0xD]); c[0xF]=c[0xD]; break;
                        case 8:  c[0x0]=c[0x1]; c[0x4]=c[0x5]; c[0x8]=(byte)((c[0x9]&c[0xD])|c[0x8]); c[0xC]=c[0xD]; break;
                        case 9:  break;
                        case 0xA: c[0x2]=(byte)((c[0x2]|c[0x1])&c[0x3]); c[0x6]=(byte)((c[0x6]|c[0xD])&c[0x7]); c[0xA]=(byte)((c[0xB]|c[0xD])&c[0xA]); c[0xB]=(byte)((c[0x9]|c[0xA])&c[0xB]); c[0xE]=c[0xD]; c[0xF]=c[0xD]; break;
                        case 0xB: c[0xB]&=(byte)(c[0x9]|c[0xD]); c[0xF]=c[0xD]; break;
                        case 0xC: c[0x0]=c[0x1]; c[0x4]=c[0x5]; c[0x8]=c[0x9]; c[0xC]=c[0xD]; break;
                        case 0xD: break;
                        case 0xE: c[0x2]=(byte)((c[0x2]|c[0x1])&c[0x3]); c[0x6]=(byte)((c[0x6]|c[0xD])&c[0x7]); c[0xA]=(byte)((c[0xA]|c[0x9])&c[0xB]); c[0xE]=c[0xD]; c[0xF]=c[0xD]; break;
                        case 0xF: c[0xF]=c[0xD]; break;
                    }
                    break;
                case 2:
                    switch (vNibble)
                    {
                        case 0:  c[0x0]=(byte)(c[0x0]|(c[0x2]&c[0xE])); c[0x4]=c[0x6]; c[0x8]=c[0xA]; c[0xC]=c[0xE]; break;
                        case 1:  c[0x1]=(byte)((c[0x2]|c[0x1]|c[0xE])&(c[0x3]|c[0xE])); c[0x3]=(byte)((c[0x2]|c[0xE]|0x3C)&c[0x3]); c[0x5]=(byte)((c[0x6]|c[0x7])&c[0x5]); c[0x9]=(byte)((c[0xA]|c[0xB])&c[0x9]); c[0xD]=c[0xE]; c[0xF]=c[0xE]; break;
                        case 2:  break;
                        case 3:  c[0x3]&=(byte)(c[0x2]|c[0xE]); c[0xF]=c[0xE]; break;
                        case 4:  c[0x0]=c[0x2]; c[0x4]=(byte)(c[0x4]|(c[0x6]&c[0xE])); c[0x8]=c[0xA]; c[0xC]=c[0xE]; break;
                        case 5:  c[0x1]=(byte)((c[0x2]|c[0x1])&c[0x3]); c[0x5]=(byte)((c[0xE]|c[0x6])&c[0x5]); c[0x7]=(byte)((c[0xE]|c[0x6])&c[0x7]); c[0xD]=c[0xE]; c[0xF]=c[0xE]; break;
                        case 6:  break;
                        case 7:  c[0x7]&=(byte)(c[0x6]|c[0xE]); break;
                        case 8:  c[0x0]=c[0x2]; c[0x4]=c[0x6]; c[0x8]=(byte)(c[0x8]|(c[0xA]&c[0xE])); c[0xC]=c[0xE]; break;
                        case 9:  c[0x1]=(byte)((c[0x2]|c[0x1])&c[0x3]); c[0x5]=(byte)((c[0x6]|c[0x5])&c[0x7]); c[0x9]=(byte)((c[0xE]|c[0xA]|0x01)&c[0x9]); c[0xB]=(byte)((c[0xE]|c[0xA]|0x31)&c[0xB]); c[0xD]=c[0xE]; c[0xF]=c[0xE]; break;
                        case 0xA: break;
                        case 0xB: c[0xB]&=(byte)(c[0xA]|c[0xE]); c[0xF]=c[0xE]; break;
                        case 0xC: c[0x0]=c[0x2]; c[0x4]=c[0x6]; c[0x8]=c[0xA]; c[0xC]=c[0xE]; break;
                        case 0xD: c[0x1]=(byte)((c[0x2]|c[0x1])&c[0x3]); c[0x5]=(byte)((c[0x6]|c[0x5])&c[0x7]); c[0x9]=(byte)((c[0xA]|c[0x9])&c[0xB]); c[0xD]=c[0xE]; c[0xF]=c[0xE]; break;
                        case 0xE: break;
                        case 0xF: c[0xF]=c[0xE]; break;
                    }
                    break;
                case 3:
                    switch (vNibble)
                    {
                        case 0:  c[0x0]=(byte)(c[0x3]|(c[0xF]&c[0x0])); c[0x4]&=c[0x7]; c[0x8]&=(byte)(c[0x9]|c[0xA]|c[0xB]|c[0xF]|0x22); c[0xC]=c[0xF]; break;
                        case 1:  c[0x1]=(byte)((c[0x1]|c[0xF])&c[0x3]); c[0x5]=c[0x7]; c[0x9]=c[0xB]; c[0xD]=c[0xF]; break;
                        case 2:  c[0x2]=(byte)((c[0x3]|c[0xF])&c[0x3]); c[0x6]=c[0x7]; c[0xA]=c[0xB]; c[0xE]=c[0xF]; break;
                        case 3:  break;
                        case 4:  c[0x0]&=(byte)((c[0xF]^0xFF)|c[0x1]|c[0x2]|c[0x3]|0x7); c[0x4]&=(byte)(c[0x7]|c[0xF]); c[0x8]&=(byte)(c[0xB]|c[0xF]|(c[0xC]^0xFF)); c[0xC]=(byte)((c[0x7]&c[0xF])|c[0xC]); break;
                        case 5:  c[0x1]=c[0x3]; c[0x5]=(byte)((c[0x5]|c[0xF])&c[0x7]); c[0x9]=c[0xB]; c[0xD]=c[0xF]; break;
                        case 6:  c[0x2]=c[0x3]; c[0x6]=(byte)((c[0x6]|c[0xF])&c[0x7]); c[0xA]=c[0xB]; c[0xE]=c[0xF]; break;
                        case 7:  break;
                        case 8:  c[0x0]&=(byte)((c[0xF]^0xFF)|c[0x1]|c[0x2]|c[0x3]|0x23); c[0x4]=c[0x7]; c[0x8]&=(byte)(c[0xB]|c[0xF]|(c[0xC]^0xFF)); c[0xC]=(byte)((c[0xB]&c[0xF])|c[0xC]); break;
                        case 9:  c[0x1]=c[0x3]; c[0x5]=c[0x7]; c[0x9]=(byte)((c[0x9]|c[0xF])&c[0xB]); c[0xD]=c[0xF]; break;
                        case 0xA: c[0x2]=c[0x3]; c[0x6]=c[0x7]; c[0xA]=(byte)((c[0xA]|c[0xF])&c[0xB]); c[0xE]=c[0xF]; break;
                        case 0xB: break;
                        case 0xC: c[0x0]&=(byte)((c[0xF]^0xFF)|c[0x1]|c[0x2]|c[0x3]|0x37); c[0x4]=c[0x7]; c[0x8]&=(byte)(c[0xB]|0x2F); c[0xC]=c[0xF]; break;
                        case 0xD: c[0x1]=c[0x3]; c[0x5]=c[0x7]; c[0x9]=c[0xB]; c[0xD]=c[0xF]; break;
                        case 0xE: c[0x2]=c[0x3]; c[0x6]=c[0x7]; c[0xA]=c[0xB]; c[0xE]=c[0xF]; break;
                        case 0xF: break;
                    }
                    break;
            }
            for (int i = 0; i < 32; i++) ppu_ram[0x3F00 + i] = c[i];
            RebuildPaletteCache();
        }

        static void NotifyMapperA12(int address)
        {
            // cx = PPU_Dot (post-increment), ppu_cycles_x = cx. Timestamp matches TriCNES.
            MapperObj.NotifyA12(address, scanline * 341 + ppu_cycles_x);
        }





        // ═══════════════════════════════════════════════════════════════
        // Scanline Event 常數 — 用於 ppu_step_*() 的快速事件判定
        //
        // 優化原理：VBL/Sprite Reset/VBL End 三個事件只發生在 cx==1 或 cx==2，
        // 但原本每個 dot（0~340）都要做 3 次 (scanline == ? && cx == ?) 比較。
        //
        // 簡化方式：
        //   1. 先用 if (cx <= 2) 做 early-out，339/341 的 dot 直接跳過全部檢查
        //   2. 進入 guard 後，將 scanline 與 cx 打包成單一 int：
        //        L = (scanline << 9) | cx
        //      因為 cx 最大值 340 < 512 (2^9)，不會溢位
        //   3. 與預先計算好的 const 比較，一次比較取代兩次
        //
        // const 在 C# 中是編譯期替換（等同直接寫字面值），JIT 視為 hardcode 常數。
        // ═══════════════════════════════════════════════════════════════

        // ═══════════════════════════════════════════════════════════════
        // Unified PPU step — region differences via precomputed parameters:
        //   NTSC odd frame skip (always enabled — PAL/Dendy stripped)
        // ═══════════════════════════════════════════════════════════════


        #endregion

        static int open_bus_decay_timer = 77777;

        // Pre-computed sprite 0 data for per-pixel hit detection during BG rendering
        static int sprite0_eval_addr;  // OAMADDR at start of sprite evaluation (dot 65), for next scanline's sprite 0
        static bool spriteSizeLatchedForFetch; // Spritesize8x16 latched at dot 261 (sprite 0 CHR fetch timing)

        // ========== Secondary OAM and Per-dot Sprite Evaluation FSM ==========
        static byte* secondaryOAM; // 8 sprites × 4 bytes
        static byte oamCopyBuffer;                  // Last byte read during evaluation (PPU_OAMLatch in TriCNES)
        static byte ppuOamBuffer;                   // P4-3: cached $2004 value, updated per-dot in half-step (TriCNES PPU_OAMBuffer)
        // TriCNES-aligned sprite evaluation state
        // evalOamAddr is an ALIAS for spr_ram_add — TriCNES uses PPUOAMAddress directly as the register
        static byte evalOamAddr { get { return spr_ram_add; } set { spr_ram_add = value; } }
        static byte evalOam2Addr;                   // TriCNES: OAM2Address (0-31, wraps with & 0x1F)
        static byte evalTick;                       // TriCNES: SpriteEvaluationTick (0-3)
        static bool evalOam2Full;                   // TriCNES: SecondaryOAMFull
        static bool evalOamOverflowed;              // TriCNES: OAMAddressOverflowedDuringSpriteEvaluation
        static bool sprite0Added;                   // TriCNES: PPU_NextScanlineContainsSpriteZero
        static bool nineObjectsOnLine;              // TriCNES: NineObjectsOnThisScanline
        static int evalSpriteCount;                 // Number of sprites found (0-8)
        static bool evalSprite0Visible;             // Sprite 0 found in secondary OAM

        // Pre-render line sprite data (loaded at pre-render dot 257 for scanline 0)
        static int prerender_sprite0_x;
        static byte prerender_sprite0_tile_low, prerender_sprite0_tile_high;
        static bool prerender_sprite0_flip_x;

        // Pre-computed sprite overflow cycle for cycle-accurate overflow flag timing
        static int spriteOverflowCycle;



        // OAM corruption: copy first 8 bytes of OAM over the corrupted row (TriCNES CorruptOAM)
        static unsafe void ProcessOamCorruption()
        {
            int idx = oamCorruptIndex;
            if (idx >= 0x20) idx = 0; // TriCNES: wrap at 32
            if (idx > 0)
            {
                // SWAR: copy row 0 (8 bytes) to corrupted row in single 64-bit move
                *(ulong*)(spr_ram + idx * 8) = *(ulong*)spr_ram;
                // Also corrupt secondary OAM (TriCNES: OAM2[index] = OAM2[0])
                secondaryOAM[idx] = secondaryOAM[0];
            }
        }



        // Pre-compute the PPU cycle at which sprite overflow flag should be set.
        // Simulates NES sprite evaluation timing (dots 65-256) with the hardware
        // overflow bug: after finding 8 sprites, byte offset m cycles 0→1→2→3,
        // reading tile/attr/X bytes as Y coordinates.
        // Split-loop + pointer iteration + unsigned range check
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void PrecomputeOverflow()
        {
            spriteOverflowCycle = -1;
            if (!ShowBackGround && !ShowSprites) return;

            uint height = Spritesize8x16 ? 15u : 7u;
            int evalCycle = 66;
            int foundCount = 0;
            byte* p = spr_ram;
            byte* pEnd = spr_ram + 256;

            // Phase 1: find first 8 sprites (pointer iteration, no evalCycle bound needed — max 242)
            while (p < pEnd)
            {
                uint diff = (uint)(scanline - *p);
                p += 4;
                if (diff <= height)
                {
                    evalCycle += 8;
                    if (++foundCount == 8) break;
                }
                else
                    evalCycle += 2;
            }

            // Phase 2: overflow bug evaluation (byte offset m cycles 0,1,2,3)
            if (foundCount == 8)
            {
                int m = 0;
                while (p < pEnd && evalCycle <= 256)
                {
                    uint diff = (uint)(scanline - p[m]);
                    if (diff <= height) { spriteOverflowCycle = evalCycle; return; }
                    m = (m + 1) & 3;
                    evalCycle += 2;
                    p += 4;
                }
            }
        }

        // Initialize sprite evaluation state at dot 65 of visible scanlines
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SpriteEvalInit()
        {
            sprite0Added = false;
            evalOam2Addr = 0;           // TriCNES: OAM2Address = 0 at dot 65
            evalOam2Full = false;       // TriCNES: SecondaryOAMFull = false
            evalTick = 0;              // TriCNES: SpriteEvaluationTick = 0
            evalOamOverflowed = false;  // TriCNES: OAMAddressOverflowedDuringSpriteEvaluation = false
            nineObjectsOnLine = false;  // TriCNES: NineObjectsOnThisScanline = false
            // evalOamAddr IS spr_ram_add (alias) — no init needed, PPUOAMAddress is the register itself
        }

        // Per-dot sprite evaluation: odd dots read, even dots write/check
        // TriCNES uses (PPU_Dot & 1)==1 for odd. AprNes ppu_cycles_x = cx+1 (post-increment),
        // so (ppu_cycles_x & 1)==0 aligns with TriCNES odd dots.
        // Merged SpriteEvalTick + SpriteEvalWrite — guard clauses + pre-computed inRange
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SpriteEvalTick()
        {
            // === Odd cycle: read OAM (guard clause — early return) ===
            if ((ppu_cycles_x & 1) != 0)
            {
                oamCopyBuffer = spr_ram[evalOamAddr];
                if ((evalOamAddr & 3) == 2) oamCopyBuffer &= 0xE7;
                return;
            }

            // === Even cycle: write + state machine ===
            bool ro = scanline == preRenderLine;

            // Overflow path (guard clause — early return)
            if (evalOamOverflowed)
            {
                if (!ro) evalOamAddr = (byte)((evalOamAddr + 4) & 0xFC);
                oamCopyBuffer = secondaryOAM[evalOam2Addr];
                return;
            }

            byte preIncAddr = evalOamAddr;

            if (!evalOam2Full && !ro)
                secondaryOAM[evalOam2Addr] = oamCopyBuffer;

            byte oam2Read = secondaryOAM[evalOam2Addr];

            // Pre-compute range check (single diff calculation)
            int height = Spritesize8x16 ? 16 : 8;
            int diff = (scanline & 0xFF) - oamCopyBuffer;
            bool inRange = diff >= 0 && diff < height;

            if (evalTick == 0) // Tick 0: Y byte
            {
                if (!nineObjectsOnLine && !ro && inRange)
                {
                    if (!evalOam2Full)
                    {
                        if (!ro) evalOamAddr++;
                        evalOam2Addr = (byte)((evalOam2Addr + 1) & 0x1F);
                        if (evalOam2Addr == 0) evalOam2Full = true;
                    }
                    else
                    {
                        nineObjectsOnLine = true;
                        evalOamAddr++;
                    }
                    if (ppu_cycles_x == 66 && !evalOam2Full) sprite0Added = true;
                    if (!ro) evalTick++;
                }
                else
                {
                    if (ppu_cycles_x == 66) sprite0Added = false;
                    if (!ro)
                    {
                        if (evalOam2Full && !nineObjectsOnLine)
                            evalOamAddr += (byte)(((evalOamAddr & 3) == 3) ? 1 : 5); // overflow bug compressed
                        else
                            evalOamAddr = (byte)((evalOamAddr + 4) & 0xFC);
                    }
                }
            }
            else // Ticks 1, 2, 3
            {
                if (evalTick == 3) // Tick 3: X byte pseudo range check
                {
                    if (inRange)
                    {
                        if (!ro) evalOamAddr += (byte)(evalOam2Full ? 4 : 1);
                    }
                    else
                    {
                        if (!evalOam2Full) { if (!ro) evalOamAddr = (byte)((evalOamAddr + 1) & 0xFC); }
                        else evalOamAddr = (byte)((evalOamAddr + 1) & 0xFC); // no ro guard (hardware behavior)
                    }
                }
                else // Ticks 1, 2
                {
                    if (!ro) evalOamAddr++;
                }

                evalTick = (byte)((evalTick + 1) & 3);

                if (!evalOam2Full && !ro)
                {
                    evalOam2Addr = (byte)((evalOam2Addr + 1) & 0x1F);
                    if (evalOam2Addr == 0) evalOam2Full = true;
                }
            }

            if (evalOamAddr < preIncAddr && evalOamAddr < 4)
                evalOamOverflowed = true;

            oamCopyBuffer = oam2Read;
        }

        // Finalize evaluation at dot 256
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SpriteEvalEnd()
        {
            evalSprite0Visible = sprite0Added;
            // Count sprites: evalOam2Addr is the SecOAM write position.
            // If evalOam2Full, we had 8 sprites (evalOam2Addr wrapped to 0).
            evalSpriteCount = evalOam2Full ? 8 : (evalOam2Addr + 3) >> 2;
            if (evalSpriteCount > 8) evalSpriteCount = 8;
        }

        // Pre-render line: sprite 0 data for scanline 0 — bitwise address computation
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void PrecomputePreRenderSprites()
        {
            byte sprY = secondaryOAM[0];
            if (sprY >= 240) return;

            int line = (preRenderLine & 255) - sprY;
            int height = Spritesize8x16 ? 16 : 8;
            if (line < 0 || line >= height) return;

            byte sprTile = secondaryOAM[1];
            byte sprAttr = secondaryOAM[2];
            prerender_sprite0_x = secondaryOAM[3];
            prerender_sprite0_flip_x = (sprAttr & 0x40) != 0;

            int addr;
            if (!Spritesize8x16)
            {
                int r = ((sprAttr & 0x80) != 0) ? (7 - line) : line;
                addr = SpPatternTableAddr | (sprTile << 4) | r;
            }
            else
            {
                if ((sprAttr & 0x80) != 0) line = 15 - line;
                // Branchless 8x16: bank from bit0, base tile from bits7-1,
                // (line & 8) << 1 auto-selects bottom half, (line & 7) = fine Y
                addr = ((sprTile & 1) << 12) | ((sprTile & 0xFE) << 4) | ((line & 8) << 1) | (line & 7);
            }

            prerender_sprite0_tile_low  = chrBankPtrs[(addr >> 10) & 7][addr & 0x3FF];
            int addrH = addr + 8;
            prerender_sprite0_tile_high = chrBankPtrs[(addrH >> 10) & 7][addrH & 0x3FF];
        }



        static public bool screen_lock = false;
        static public volatile bool emuWaiting = false;
        static void RenderScreen()
        {
            if (!AnalogEnabled)
            {
                // === 數位模式同步 path / headless (最常見路徑) ===
                screen_lock = true;
                VideoOutput?.Invoke(null, null);
                screen_lock = false;
                emuWaiting = true;
                _event.WaitOne();
                emuWaiting = false;
            }
            else if (!analogRenderThreadRunning)
            {
                // === Analog 同步 fallback (UI 停止渲染執行緒時 / headless 模式) ===
                screen_lock = true;
                if (UltraAnalog && CrtEnabled) Crt_Render();
                VideoOutput?.Invoke(null, null);
                screen_lock = false;
                emuWaiting = true;
                _event.WaitOne();
                emuWaiting = false;
            }
            else
            {
                // === Async double buffer path (類比模式) ===
                screen_lock = true;
                if (UltraAnalog && CrtEnabled) Crt_Render();
                screen_lock = false;

                // 等上一幀 GDI 完成（如果還在跑）
                analogRenderDone.Wait();
                analogRenderDone.Reset();

                // Swap front/back buffer — GDI 讀 back buffer（上一幀），模擬寫 front buffer（下一幀）
                SwapAnalogBuffers();

                // 通知渲染執行緒開始 blit back buffer
                analogRenderReady.Set();
            }
        }

        static bool pendingVblank = false; // half-dot VBL latch (TriCNES: PPU_PendingVBlank)
        static bool pendingSprite0Hit = false; // half-dot sprite 0 hit latch (TriCNES: PPUStatus_PendingSpriteZeroHit)
        static bool ppu2002ReadPending = false; // TriCNES: PPU_Read2002 (deferred VBL clear)

        // Per-dot sprite compositing buffers (filled at cx==257, consumed per-dot in half-step)
        static uint* sprLineBuf;     // 256 entries: winning sprite color (NesColors[])
        static byte* sprLinePri;     // 256 entries: priority (0=front, 1=behind BG)
        static byte* sprLineSet;     // 256 entries: 1=sprite pixel present, 0=empty
        static byte* sprLinePalIdx;  // 256 entries: raw palette index (for NTSC analog)
        //ref http://wiki.nesdev.com/w/index.php/PPU_scrolling
        static byte ppu_r_2002()
        {
            // TriCNES line 8937-8949: $2002 read
            // 1. VBL flag sampled BEFORE EmulateUntilEndOfRead
            // 2. PPU_Read2002 set BEFORE EmulateUntilEndOfRead (deferred VBL clear)
            // 3. EmulateUntilEndOfRead (7 master ticks)
            // 4. Sprite flags sampled AFTER EmulateUntilEndOfRead (Delayed versions)
            // NO explicit VBL suppression hack — natural deferred handling via ppu2002ReadPending

            byte vblBit = isVblank ? (byte)0x80 : (byte)0x00;
            ppu2002ReadPending = true;

            for (int i = 0; i < 7; i++)
                MasterClockTick();

            openbus = (byte)(vblBit | ((isSprite0hit_Delayed ? 0x40 : 0) | (isSpriteOverflow_Delayed ? 0x20 : 0)) | (openbus & 0x1f));

            vram_latch = false;
            // TriCNES refreshes PPUBusDecay[5..7] (per-bit). AprNes uses single timer — don't refresh here.
            // The high 3 bits (flags) are freshly written; low 5 bits retain existing open bus decay.
            return openbus;
        }

        static byte ppu_r_2007()
        {
            // TriCNES lines 8968-9047: $2007 read handler (single-tick SM)
            byte result;

            // Consecutive read detection: SM==3 && isRead
            if (ppu2007SM == 3 && ppu2007SM_isRead)
            {
                // Alignment-specific behavior (TriCNES lines 8975-9009)
                int phase = mcPpuClock & 3; // maps to PPUClock
                // mcPpuClock: 0↔PPU0, 3↔PPU1, 2↔PPU2, 1↔PPU3
                int ppuPhase = (mcPpuClock == 0) ? 0 : (mcPpuClock == 3) ? 1 : (mcPpuClock == 2) ? 2 : 3;
                if (ppuPhase == 0) { result = ppu_2007_buffer; }
                else if (ppuPhase == 1) { ppu2007SM_updateVramAddrEarly = true; result = ppu_2007_buffer; }
                else if (ppuPhase == 2) { ppu2007SM_updateVramAddrEarly = true; result = (byte)(vram_addr & 0xFF); }
                else { ppu2007SM_updateVramAddrEarly = true; result = (vram_addr >= 0x2000) ? ppu_2007_buffer : (byte)(vram_addr & 0xFF); }
            }
            else
            {
                // Normal read
                if (vram_addr >= 0x3F00)
                {
                    ppuAddressBus = vram_addr;
                    result = PpuBusRead(vram_addr & 0x3FFF);
                    // Palette read: result has palette data, but open bus bits 6-7
                    result = (byte)((openbus & 0xC0) | (result & 0x3F));
                }
                else
                {
                    result = ppu_2007_buffer;
                }
            }

            // Start SM if idle, or restart if interrupted
            if (ppu2007SM >= 9)
            {
                ppu2007SM = 0;
                // Alignment-dependent: buffer updated late on phases 0/1
                int ppuPhase = (mcPpuClock == 0) ? 0 : (mcPpuClock == 3) ? 1 : (mcPpuClock == 2) ? 2 : 3;
                ppu2007SM_bufferLate = (ppuPhase <= 1);
                // DMC DMA edge case (TriCNES line 9036-9039)
                if (dmcDmaRunning && (dmcStatusEnabled || dmcImplicitAbortActive))
                    vram_addr = (ushort)((vram_addr + 1) & 0x3FFF);
            }

            ppu2007SM_isRead = true;
            ppu2007SM_readDelayed = true;
            ppu2007SM_addr = vram_addr;

            openbus = result;
            open_bus_decay_timer = 77777;
            return openbus;
        }

        static byte openbus;
        static public byte cpubus;  // CPU data bus value (last byte read/written by CPU)

        static void ppu_w_2000(byte value)
        {
            openbus = value;

            // TriCNES model: IMMEDIATE application + delayed re-application
            // P3-1: DataBus glitch — some fields use cpubus (last READ value, not written value)
            // for 1-2 PPU cycles. The delayed handler fixes them with the correct value.
            // Glitch-affected fields (TriCNES uses dataBus):
            vram_addr_internal = (ushort)((vram_addr_internal & 0x73ff) | ((cpubus & 3) << 10));
            BaseNameTableAddr = 0x2000 | ((cpubus & 3) << 10);
            VramaddrIncrement = ((cpubus & 4) > 0) ? 32 : 1;
            Spritesize8x16 = ((cpubus & 0x20) > 0);
            // Non-glitch fields (TriCNES uses In directly):
            NMIable = ((value & 0x80) > 0);
            SpPatternTableAddr = ((value & 8) > 0) ? 0x1000 : 0;
            BgPatternTableAddr = ((value & 0x10) > 0) ? 0x1000 : 0;

            // TriCNES: NMILine clearing is NOT done here — it's handled by CPUClock==8 gate
            // (NMILine cleared at operationCycle==0 when !(isVblank && NMIable))

            // Delayed re-application (TriCNES: fixes open bus glitch after 1-2 PPU cycles)
            ppu2000PendingValue = value;
            // TriCNES: PPUClock 0,1=2; PPUClock 2,3=1. mcPpuClock 0↔PPU0, 3↔PPU1, 2↔PPU2, 1↔PPU3
            ppu2000UpdateDelay = ((mcPpuClock & 3) == 0 || (mcPpuClock & 3) == 3) ? 2 : 1;
        }

        static void ppu_w_2001(byte value)
        {
            openbus = value;

            // Tier 1: Instant flags — take effect immediately
            ShowBackGround_Instant = (value & 0x08) != 0;
            ShowSprites_Instant    = (value & 0x10) != 0;

            // P4-1: TriCNES delayed OAM corruption model
            bool newRenderingInstant = ShowBackGround_Instant || ShowSprites_Instant;
            // Record state for delayed corruption check
            oamCorruptWasRendering = prevRenderingEnabled;
            oamCorrupt2001Value = value;
            // Set delay based on alignment (TriCNES line 9518-9527)
            switch (mcPpuClock & 3)
            {
                case 0: case 3: oamCorruptDelay = 2; break;
                case 1: case 2: oamCorruptDelay = 3; break;
            }

            if (prevRenderingEnabled != newRenderingInstant)
            {
                bool outsideVblank = scanline >= 0 && (scanline < 240 || scanline == preRenderLine);
                if (outsideVblank)
                {
                    if (!newRenderingInstant)
                    {
                        // OAM corruption: deferred to delay expiry (NOT immediate)
                        // TriCNES v2: palette corruption when disabling rendering with v >= $3C00
                        // The AT address mux briefly points into palette RAM due to v being used
                        // as NT input when rendering is disabled during an AT fetch phase.
                        if ((vram_addr & 0x3FFF) >= 0x3C00)
                            ppuPaletteCorruptionFromDisable = true;
                    }
                    else
                    {
                        // Re-enabling rendering — suppression gate (TriCNES line 9564)
                        int alignment = mcPpuClock & 3;
                        if (oamCorruptPending && (alignment == 1 || alignment == 2))
                            oamCorruptSuppressed = true;

                        // Sprite 0 hit now uses CalculatePixel model (no pre-computation needed)
                    }
                }
            }
            prevRenderingEnabled = newRenderingInstant;

            // Tier 2: Delayed mask flags (ShowBG/ShowSprites/Left8)
            ppu2001UpdateDelay = ((mcPpuClock & 3) == 2) ? 3 : 2; // TriCNES: phase 2=3, others=2
            ppu2001PendingValue = value;

            // Emphasis bits: independent delay (TriCNES: PPU_Update2001EmphasisBitsDelay)
            // Alignment 0,3: 2 cycles; Alignment 1,2: 1 cycle
            ppu2001EmphasisDelay = ((mcPpuClock & 3) == 0 || (mcPpuClock & 3) == 3) ? 2 : 1;
            ppu2001EmphasisPending = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ppu_w_2003(byte value) //ok
        {
            openbus = value;
            spr_ram_add = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ppu_w_2004(byte value) //ok
        {
            openbus = value;
            // During rendering (visible + pre-render), writes don't modify OAM; OAMADDR increments by 4 and aligns to 4-byte boundary
            if ((scanline < 240 || scanline == preRenderLine) && scanline >= 0 && (ShowBackGround_Instant || ShowSprites_Instant))
            {
                spr_ram_add = (byte)((spr_ram_add + 4) & 0xFC);
            }
            else
            {
                // TriCNES line 9600-9602: attribute byte (offset 2) masked on write
                if ((spr_ram_add & 3) == 2)
                    value &= 0xE3; // bits 2-4 don't exist in hardware
                spr_ram[spr_ram_add++] = value;
            }
        }

        static byte ppu_r_2004()
        {
            // TriCNES: EmulateUntilEndOfRead — advance 7 master clocks before OAM read
            for (int i = 0; i < 7; i++)
                MasterClockTick();

            byte val;
            bool renderingOn = ShowBackGround || ShowSprites;
            if (scanline >= 0 && scanline < 240 && renderingOn)
            {
                val = ppuOamBuffer;
            }
            else
            {
                val = spr_ram[spr_ram_add];
                if ((spr_ram_add & 3) == 2) val &= 0xE3;
            }
            open_bus_decay_timer = 77777;
            return openbus = val;
        }

        static void ppu_w_2005(byte value) //ok
        {
            openbus = value;
            // Delayed scroll update (TriCNES: PPU_Update2005Delay = 1-2 cycles)
            ppu2005PendingValue = value;
            // TriCNES: alignment 0,1,3=1cycle; alignment 2=2cycles
            ppu2005UpdateDelay = ((mcPpuClock & 3) == 2) ? 2 : 1;

            // TriCNES v2: immediate open bus glitch — apply dataBus (cpubus) to t/FineX
            // before the delay handler applies the correct value.
            // In normal writes cpubus == value, but hardware models the bus latency.
            if (!vram_latch) // first write
            {
                FineX = cpubus & 0x07;
                vram_addr_internal = (vram_addr_internal & 0x7FE0) | ((cpubus & 0xF8) >> 3);
            }
            else // second write
            {
                vram_addr_internal = (vram_addr_internal & 0x0C1F) | ((cpubus & 0x7) << 12) | ((cpubus & 0xF8) << 2);
            }
            // latch NOT flipped here — deferred to delay handler (TriCNES line 1302)
        }
        static void ppu_w_2006(byte value)
        {
            openbus = value;
            if (!vram_latch) //first
                vram_addr_internal = (vram_addr_internal & 0x00FF) | ((value & 0x3F) << 8);
            else
            {
                vram_addr_internal = (vram_addr_internal & 0x7F00) | value;
                // Delayed t→v copy: real hardware takes ~4-5 PPU dots after the CPU write.
                // In AprNes's tick-before-write model, 3 PPU dots of the current CPU cycle
                // have already executed, so a delay of 3 more gives ~5-6 total from cycle start.
                ppu2006PendingAddr = vram_addr_internal;
                // TriCNES: alignment 0,1,3=4cycles; alignment 2=5cycles
                // TriCNES: case 0,3=4cycles; case 1,2=5cycles
                ppu2006UpdateDelay = ((mcPpuClock & 3) == 2) ? 5 : 4; // TriCNES: phase 2=5, others=4
            }
            vram_latch = !vram_latch;
        }

        static void ppu_w_2007(byte value)
        {
            // TriCNES lines 9683-9727: $2007 write handler (single-tick SM)
            openbus = value;
            open_bus_decay_timer = 77777;
            ppu2007SM_writeValue = value;

            // Consecutive access detection: SM==3 (1 CPU cycle) or SM==6 (2 CPU cycles)
            if (ppu2007SM == 3 || ppu2007SM == 6)
            {
                ppu2007SM_mysteryAddr = (ushort)((vram_addr & 0xFF00) | value);
                if (!ppu2007SM_isRead)
                    ppu2007SM_performMysteryWrite = true;
                else
                    ppu2007SM_interruptedReadToWrite = true;
            }
            else
            {
                ppu2007SM_normalWriteBehavior = true;
            }

            if (ppu2007SM != 3)
            {
                if (ppu2007SM >= 9)
                    ppu2007SM = 3; // standard write: start at state 3 (write executes next tick)
                else
                    ppu2007SM = 0; // interrupted: restart from 0
                ppu2007SM_isRead = false;
            }
            else
            {
                ppu2007SM_readDelayed = false;
            }

            ppu2007SM_addr = vram_addr;
        }

        static void ppu_w_4014(byte value)//DMA , fixex 2017.01.16 pass sprite_ram test
        {
            // OAM DMA trigger — TriCNES per-cycle model
            spriteDmaTransfer = true;
            spriteDmaOffset = value;
            dmaFirstCycleOam = true;
            dmaOamAligned = false;
            dmaOamAddr = 0;
        }
    }
}
