using System;
using System.Runtime.CompilerServices;

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
            {
                // NTSC only — PAL palette stripped for architecture validation
                // NTSC hardcoded palette (verified with 174 blargg + 136 AccuracyCoin tests)
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

        static int vram_addr_internal = 0, vram_addr = 0, scrol_y = 0, FineX = 0;
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
        // When rendering disabled mid-scanline → capture corruption index from secOAMAddr.
        // When rendering re-enabled → apply corruption (copy row 0 over target row),
        // UNLESS alignment 1 or 2 suppresses it.
        static byte* corruptOamRow; // 32 bytes (legacy, kept for allocation)
        static bool prevRenderingEnabled = false;
        static bool oamCorruptPending = false;          // Corruption recorded from disable, awaiting re-enable
        static bool oamCorruptSuppressed = false;       // Alignment 1,2 suppress corruption on re-enable
        static int oamCorruptIndex = 0;                 // 6.5: TriCNES PPU_OAMCorruptionIndex (from OAM2Address)

        // P4-2: Palette corruption flags
        static bool paletteCorruptFromDisable = false;  // Rendering disabled during NT fetch with VRAM >= $3C00
        static bool paletteCorruptFromVAddr = false;    // $2006 palette→non-palette transition
        static public uint* ScreenBuf1x;
        static uint* NesColors; //, targetSize;
        static int* Buffer_BG_array;
        static uint* palCacheR; // 4 pre-computed palette colors for renderAttr
        static uint* palCacheN; // 4 pre-computed palette colors for nextAttr
        static byte spr_ram_add = 0;

        static bool oddSwap = false;
        static bool ppuRenderingEnabled = false; // Tier 3: Delayed rendering enable (end of PPU dot)

        // Deferred commit: CXinc (TriCNES: PPU_Commit_PatternHighFetch → CXinc at next dot)
        // In TriCNES, CHR high commit + CXinc fires at the NEXT full step (1 dot after phase 7).
        static bool commitCXinc = false;

        // Tier 4: Alignment-dependent delayed flags for sprite evaluation (TriCNES: _Delayed)
        // Source: Tier 2 (ShowBackGround/ShowSprites), NOT Tier 1 (Instant).
        // Updated before/after sprite eval depending on mcCpuClock & 3.
        static bool ShowBG_EvalDelay = false;   // TriCNES: PPU_Mask_ShowBackground_Delayed
        static bool ShowSpr_EvalDelay = false;  // TriCNES: PPU_Mask_ShowSprites_Delayed

        // ── Per-sprite shift registers (TriCNES P2-3: per-dot sprite rendering) ──
        // Filled at dots 257-320 from secondary OAM tile fetch, rendered at dots 1-256.
        // TriCNES: PPU_SpriteShiftRegisterL/H, PPU_SpriteShifterCounter, PPU_SpriteAttribute
        static byte[] sprShiftL = new byte[8];       // Low bitplane shift register
        static byte[] sprShiftH = new byte[8];       // High bitplane shift register
        static int[] sprXCounter = new int[8];        // X position countdown (>0=waiting, 0=shifting)
        static byte[] sprFetchAttr = new byte[8];     // Attribute byte per slot (palette, priority, flip)
        static byte[] sprXPos = new byte[8];           // X position per slot (for counter init at dot 339)
        static int sprSlotCount = 0;                   // Number of valid sprites fetched (from evalSpriteCount)
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
            {
                vram_addr &= ~0x001F;
                vram_addr ^= 0x0400;
            }
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
                int y = (vram_addr & 0x03E0) >> 5;
                if (y == 29)
                {
                    y = 0;
                    vram_addr ^= 0x0800;
                }
                else if (y == 31)
                    y = 0;
                else
                    y += 1;
                vram_addr = (vram_addr & ~0x03E0) | (y << 5);
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
            // Palette ($3F00-$3FFF): mirrored, transparent-mirrored at $3F10/$3F14/$3F18/$3F1C
            return ppu_ram[(addr & ((addr & 0x03) == 0 ? 0x0C : 0x1F)) + 0x3F00];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void PpuBusWrite(int addr, byte val)
        {
            addr &= 0x3FFF;
            if (addr < 0x2000)
            {
                MapperObj.MapperW_CHR(addr, val);
                return;
            }
            if (addr < 0x3F00)
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
                return;
            }
            // Palette
            ppu_ram[(addr & ((addr & 0x03) == 0 ? 0x0C : 0x1F)) + 0x3F00] = val;
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
                vram_addr = (ushort)((vram_addr + VramaddrIncrement) & 0x7FFF);
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int CIRAMAddr(int addr)
        {
            int mirror = *Vertical;
            if (mirror == 0) // H-mirror: $2000=$2400(p0), $2800=$2C00(p1)
                return (addr & 0x23FF) | ((addr & 0x0800) >> 1);
            if (mirror == 1) // V-mirror: $2000=$2800(p0), $2400=$2C00(p1)
                return addr & 0x27FF;
            if (mirror == 2) // 1-screen A: all → page 0
                return addr & 0x23FF;
            if (mirror == 3) // 1-screen B: all → page 1
                return (addr & 0x23FF) | 0x0400;
            return addr & 0x2FFF; // 4-screen: 4 unique nametables, no mirroring
        }

        // ---- Tile fetch state ----
        static byte NTVal = 0, ATVal = 0, lowTile = 0, highTile = 0;
        static int ioaddr = 0;

        // ---- Per-dot render shift registers (TriCNES model: shifted left each half-step) ----
        // Single set used for both pixel output and sprite 0 hit detection.
        // Reloaded via deferred commit (phase 7 sets flag → next half-step loads).
        static ushort renderLow = 0, renderHigh = 0;
        // Per-dot attribute shift: shifted alongside render registers, serial-in from latch
        static ushort renderAttrLow = 0, renderAttrHigh = 0;
        // Attribute latch: 2-bit value from which bits are shifted in (TriCNES: PPU_AttributeLatchRegister)
        static byte attrLatch = 0;
        static byte pendingAttrLatch = 0; // TriCNES: PPU_Attribute → committed to attrLatch at load time

        // Deferred shift register reload (TriCNES: PPU_Commit_LoadShiftRegisters)
        // Flag set at HALF step phase 7 (not full step), committed at NEXT half step.
        static byte pendingTileLow = 0, pendingTileHigh = 0;
        static bool commitLoadShiftReg = false;

        // ---- Attribute 3-stage pipeline ----
        // Phase-3 shifts ATVal into p1; phase-7 render reads p3 (2 groups later).
        // This correctly delays attribute by 2 fetch groups with no index drift.
        static byte bg_attr_p1 = 0, bg_attr_p2 = 0, bg_attr_p3 = 0;

        // Phase 7 tile reload (pixel output moved to ppu_half_step per-dot)
        // Only updates palette caches — shift register reload happens in ppu_rendering_tick phase 7.
        static void RenderBGTile(int cx)
        {
            // Palette caches still needed for ppu_half_step pixel output
            byte renderAttr = bg_attr_p3;
            byte nextAttr   = bg_attr_p2;
            int baseAddrR = 0x3f00 | (renderAttr << 2);
            int baseAddrN = 0x3f00 | (nextAttr   << 2);
            uint bgColor = NesColors[ppu_ram[0x3f00] & 0x3f];
            palCacheR[0] = palCacheN[0] = bgColor;
            palCacheR[1] = NesColors[ppu_ram[baseAddrR + 1] & 0x3f];
            palCacheR[2] = NesColors[ppu_ram[baseAddrR + 2] & 0x3f];
            palCacheR[3] = NesColors[ppu_ram[baseAddrR + 3] & 0x3f];
            palCacheN[1] = NesColors[ppu_ram[baseAddrN + 1] & 0x3f];
            palCacheN[2] = NesColors[ppu_ram[baseAddrN + 2] & 0x3f];
            palCacheN[3] = NesColors[ppu_ram[baseAddrN + 3] & 0x3f];
            // Pixel output is now done per-dot in ppu_half_step()
        }

        // Per-8-cycle tile fetch: runs each PPU cycle on visible/pre-render scanlines when rendering enabled.
        // BG tiles fetched at cycles 0-255 (visible) and 320-335 (next-scanline prefetch).
        // A12 notifications: BG at phases 0 (NT addr, A12=0) and 4 (CHR low addr, A12=BG table bit12),
        // sprites at phases 0 (garbage NT, A12=0) and 3 (sprite CHR, A12=sprite table bit12).
        // cx = PPU_Dot (post-increment, direct TriCNES match)
        static void ppu_rendering_tick(int cx, int preRenderLn)
        {
            // Tile fetch: TriCNES PPU_Dot < 257 || PPU_Dot > 320 && PPU_Dot <= 336
            if (cx < 257 || (cx > 320 && cx <= 336))
            {
                if ((cx == 1 || cx == 321) && chrABAutoSwitch)
                {
                    byte*[] src = Spritesize8x16 ? (chrBGUseASet ? chrBankPtrsA : chrBankPtrsB) : chrBankPtrsA;
                    for (int i = 0; i < 8; i++) chrBankPtrs[i] = src[i];
                }
                int phase = cx & 7;
                if (phase == 0) {
                    ioaddr = 0x2000 | (vram_addr & 0x0FFF);
                } else if (phase == 1) {
                    ppuAddressBus = ioaddr;  // TriCNES: PPU_AddressBus set at phase 1 (NT fetch)
                    if (mapperA12IsMmc3) NotifyMapperA12(ioaddr); // NT addr A12=0 — at data phase (TriCNES model)
                    if (ntChrOverrideEnabled)
                        NTVal = ntBankPtrs[(ioaddr >> 10) & 3][ioaddr & 0x3FF];
                    else
                        NTVal = ppu_ram[CIRAMAddr(ioaddr)];
                    if (extAttrEnabled) extAttrNTOffset = (ushort)(ioaddr & 0x3FF);
                    if (mmc5Ref != null) mmc5Ref.NotifyVramRead(ioaddr);
                } else if (phase == 2) {
                    ioaddr = 0x23C0 | (vram_addr & 0x0C00) | ((vram_addr >> 4) & 0x38) | ((vram_addr >> 2) & 0x07);
                } else if (phase == 3) {
                    ppuAddressBus = ioaddr;  // TriCNES: PPU_AddressBus set at phase 3 (AT fetch)
                    if (extAttrEnabled && extAttrNTOffset < 960) {
                        byte exVal = extAttrRAM[extAttrNTOffset];
                        extAttrChrBank = (exVal & 0x3F) | (extAttrChrUpperBits << 6);
                        ATVal = (byte)((exVal >> 6) & 3);
                    } else if (ntChrOverrideEnabled) {
                        ATVal = (byte)((ntBankPtrs[(ioaddr >> 10) & 3][ioaddr & 0x3FF] >> (((vram_addr >> 4) & 0x04) | (vram_addr & 0x02))) & 0x03);
                    } else {
                        ATVal = (byte)((ppu_ram[CIRAMAddr(ioaddr)] >> (((vram_addr >> 4) & 0x04) | (vram_addr & 0x02))) & 0x03);
                    }
                    bg_attr_p3 = bg_attr_p2; bg_attr_p2 = bg_attr_p1; bg_attr_p1 = ATVal;
                    // Store pending attribute (TriCNES: PPU_Attribute updated at commit,
                    // attrLatch updated at LoadShiftRegisters in half step)
                    pendingAttrLatch = ATVal;
                    if (mmc5Ref != null) mmc5Ref.NotifyVramRead(ioaddr);
                } else if (phase == 4) {
                    if (extAttrEnabled && extAttrChrSize > 0)
                        ioaddr = (extAttrChrBank << 12) | (NTVal << 4) | ((vram_addr >> 12) & 7);
                    else
                        ioaddr = BgPatternTableAddr | (NTVal << 4) | ((vram_addr >> 12) & 7);
                } else if (phase == 5) {
                    ppuAddressBus = ioaddr;  // TriCNES: PPU_AddressBus set at phase 5 (CHR low fetch)
                    ppuChrFetchA12 = (ioaddr >> 12) & 1;  // CHR-only A12 for MMC3 M2 filter
                    if (mapperNeedsA12) NotifyMapperA12(ioaddr);  // CHR low — at data phase (TriCNES model)
                    if (extAttrEnabled && extAttrChrSize > 0)
                        lowTile = extAttrCHR[ioaddr % extAttrChrSize];
                    else
                        lowTile = chrBankPtrs[(ioaddr >> 10) & 7][ioaddr & 0x3FF];
                    if (mmc5Ref != null) mmc5Ref.NotifyVramRead(ioaddr);
                } else if (phase == 6) {
                    if (extAttrEnabled && extAttrChrSize > 0)
                        ioaddr = (extAttrChrBank << 12) | (NTVal << 4) | ((vram_addr >> 12) & 7) | 8;
                    else
                        ioaddr = BgPatternTableAddr | (NTVal << 4) | ((vram_addr >> 12) & 7) | 8;
                } else {
                    ppuAddressBus = ioaddr;  // TriCNES: PPU_AddressBus set at phase 7 (CHR high fetch)
                    ppuChrFetchA12 = (ioaddr >> 12) & 1;  // CHR-only A12 for MMC3 M2 filter
                    if (mapperNeedsA12 && !mapperA12IsMmc3) NotifyMapperA12(ioaddr);  // MMC2/MMC4: CHR high at data phase
                    if (extAttrEnabled && extAttrChrSize > 0)
                        highTile = extAttrCHR[ioaddr % extAttrChrSize];
                    else
                        highTile = chrBankPtrs[(ioaddr >> 10) & 7][ioaddr & 0x3FF];
                    if (mmc5Ref != null) mmc5Ref.NotifyVramRead(ioaddr);
                    // Palette cache update
                    if (scanline < 240 && cx < 257 && ppuRenderingEnabled)
                        RenderBGTile(cx);
                    // Store tile data for half-step reload
                    // commitLoadShiftReg is set in ppu_half_step at phase 7 (TriCNES model)
                    pendingTileLow = lowTile;
                    pendingTileHigh = highTile;
                    // CXinc deferred to next dot's commit (TriCNES: PPU_Commit_PatternHighFetch)
                    commitCXinc = true;
                }
            }
            else if (cx == 257) // TriCNES D=256 → Yinc
            {
                Yinc();
            }
            else if (cx == 258) // TriCNES D=257 → CopyHoriV
            {
                CopyHoriV();
                // MMC5 CHR A/B: switch to A set (sprites) at dot 257
                if (chrABAutoSwitch && Spritesize8x16)
                    for (int i = 0; i < 8; i++) chrBankPtrs[i] = chrBankPtrsA[i];
            }

            // ── Sprite fetch: driven by ppu_cycles_x (= PPU_Dot, post-increment) ──
            // Independent from tile fetch if-else chain. Allows sprite fetch case 0
            // to run on the SAME physical dot as Yinc (TriCNES: both at D=256→257).
            // TriCNES: gated by (ShowBackground_Delayed || ShowSprites_Delayed)
            if (ppuRenderingEnabled)
            {
                int spriteDot = ppu_cycles_x; // = PPU_Dot (post-increment)
                if (spriteDot >= 257 && spriteDot <= 320)
                {
                    // TriCNES line 2823: PPUOAMAddress reset every dot 257-320
                    if (ShowBG_EvalDelay || ShowSpr_EvalDelay)
                        spr_ram_add = 0;

                    // TriCNES line 2828: OAM2Address=0, SpriteEvaluationTick=0 at dot 257
                    if (spriteDot == 257)
                        evalOam2Addr = 0;

                    // Latch sprite size at dot 262 (= cx 261 equivalent)
                    if (spriteDot == 262) spriteSizeLatchedForFetch = Spritesize8x16;

                    int sprPhase = (spriteDot - 257) & 7;
                    int slot = (spriteDot - 257) >> 3;

                    // Cases 0-3: dummy BG fetch (sets ppuAddressBus to NT/AT addresses)
                    if (sprPhase <= 3)
                    {
                        int bgPhase = spriteDot & 7;
                        if (bgPhase == 1)
                            ppuAddressBus = (ushort)(0x2000 | (vram_addr & 0x0FFF));
                        else if (bgPhase == 3)
                            ppuAddressBus = (ushort)(0x23C0 | (vram_addr & 0x0C00) | ((vram_addr >> 4) & 0x38) | ((vram_addr >> 2) & 0x07));
                    }

                    // OAM2 reads + sprite tile fetch
                    if (sprPhase == 0)
                    {
                        oamCopyBuffer = secondaryOAM[evalOam2Addr];
                        evalOam2Addr++;
                    }
                    else if (sprPhase == 1)
                    {
                        oamCopyBuffer = secondaryOAM[evalOam2Addr];
                        if (mapperNeedsA12) NotifyMapperA12(ppuAddressBus);
                        evalOam2Addr++;
                    }
                    else if (sprPhase == 2)
                    {
                        oamCopyBuffer = secondaryOAM[evalOam2Addr];
                        sprFetchAttr[slot] = oamCopyBuffer;
                        evalOam2Addr++;
                    }
                    else if (sprPhase == 3)
                    {
                        oamCopyBuffer = secondaryOAM[evalOam2Addr];
                        sprXPos[slot] = oamCopyBuffer;
                    }
                    else if (sprPhase == 4)
                    {
                        oamCopyBuffer = secondaryOAM[evalOam2Addr];
                        ppuAddressBus = ComputeSpritePatternAddr(slot);
                        ppuChrFetchA12 = (ppuAddressBus >> 12) & 1;
                    }
                    else if (sprPhase == 5)
                    {
                        oamCopyBuffer = secondaryOAM[evalOam2Addr];
                        if (mapperNeedsA12) NotifyMapperA12(ppuAddressBus);
                        int addr = ppuAddressBus;
                        byte tile = chrBankPtrs[(addr >> 10) & 7][addr & 0x3FF];
                        bool flipH = (sprFetchAttr[slot] & 0x40) != 0;
                        sprShiftL[slot] = flipH ? FlipByte(tile) : tile;
                        if (slot >= sprSlotCount) sprShiftL[slot] = 0;
                    }
                    else if (sprPhase == 6)
                    {
                        oamCopyBuffer = secondaryOAM[evalOam2Addr];
                        ppuAddressBus = ComputeSpritePatternAddr(slot) + 8;
                        ppuChrFetchA12 = (ppuAddressBus >> 12) & 1;
                    }
                    else // sprPhase == 7
                    {
                        oamCopyBuffer = secondaryOAM[evalOam2Addr];
                        if (mapperNeedsA12 && !mapperA12IsMmc3) NotifyMapperA12(ppuAddressBus);
                        int addr = ppuAddressBus;
                        byte tile = chrBankPtrs[(addr >> 10) & 7][addr & 0x3FF];
                        bool flipH = (sprFetchAttr[slot] & 0x40) != 0;
                        sprShiftH[slot] = flipH ? FlipByte(tile) : tile;
                        if (slot >= sprSlotCount) sprShiftH[slot] = 0;
                        evalOam2Addr++;
                    }

                    // MMC5 notifications
                    if (mmc5Ref != null)
                    {
                        if (sprPhase == 1) mmc5Ref.NotifyVramRead(0x2000);
                        else if (sprPhase == 3) mmc5Ref.NotifyVramRead(0x23C0);
                        else if (sprPhase == 5) mmc5Ref.NotifyVramRead(SpPatternTableAddr);
                        else if (sprPhase == 7) mmc5Ref.NotifyVramRead(SpPatternTableAddr | 8);
                    }
                }
            } // end ppuRenderingEnabled sprite fetch gate

            // Dot 321 equivalent (ppu_cycles_x = 322)
            if (ppu_cycles_x == 322 && scanline < 240 && (ShowBackGround_Instant || ShowSprites_Instant))
                oamCopyBuffer = secondaryOAM[0];

            // TriCNES D=280-304 → cx=281-305
            if (scanline == preRenderLn && cx >= 281 && cx <= 305)
            {
                vram_addr = (vram_addr & ~0x7BE0) | (vram_addr_internal & 0x7BE0);
            }

            // Dot 339: initialize sprite X counters (TriCNES line 2996-3009)
            // When rendering enabled: counter = X position; when disabled: counter = 0
            if (cx == 339)
            {
                for (int i = 0; i < 8; i++)
                    sprXCounter[i] = (ShowSprites || ShowBackGround) ? sprXPos[i] : 0;
            }

            // Garbage NT fetches (dots 336-340): TriCNES PPU_Render_ShiftRegistersAndBitPlanes_DummyNT
            // Dot 336/338: PPU_AddressBus = NT addr (A12=0); Dot 340: PPU_AddressBus = CHR pattern addr
            if (cx == 336 || cx == 338)
            {
                // 6.1: TriCNES calls FetchPPU here (tick 0/2) — actual VRAM read
                ppuAddressBus = 0x2000 | (vram_addr & 0x0FFF);
                PpuBusRead(ppuAddressBus); // actual fetch (TriCNES: PPU_RenderTemp = FetchPPU)
            }
            else if (cx == 337 || cx == 339)
            {
                // TriCNES tick 1/3: stores PPU_NextCharacter = PPU_RenderTemp
                // A12 notify 1 dot after address set (TriCNES: PPUClock detects at end of dot)
                NTVal = ppu_ram[CIRAMAddr(ppuAddressBus)]; // store NT byte (TriCNES: PPU_NextCharacter)
                if (mapperNeedsA12) NotifyMapperA12(ppuAddressBus);
            }
            else if (cx == 340)
            {
                // TriCNES tick 4: set CHR address (no fetch)
                ppuAddressBus = BgPatternTableAddr | (NTVal << 4) | ((vram_addr >> 12) & 7);
                ppuChrFetchA12 = (ppuAddressBus >> 12) & 1;
            }

            // MMC5: garbage NT fetches at dots 337 and 339 (same NT address as first tile)
            // These create the 3-consecutive-identical-read pattern for scanline detection
            if (mmc5Ref != null && (cx == 337 || cx == 339))
                mmc5Ref.NotifyVramRead(0x2000 | (vram_addr & 0x0FFF));
        }

        // TriCNES: PPU_SpriteEvaluation_GetSpriteAddress — compute sprite CHR pattern address
        // for the given secondary OAM slot during sprite tile fetch (dots 257-320).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int ComputeSpritePatternAddr(int slot)
        {
            int sprY    = secondaryOAM[slot * 4];
            int sprTile = secondaryOAM[slot * 4 + 1];
            int sprAttr = secondaryOAM[slot * 4 + 2];
            bool flipY  = (sprAttr & 0x80) != 0;
            int row = (scanline & 0xFF) - sprY;  // TriCNES: (PPU_Scanline & 0xFF) - PPU_SpriteYposition

            if (!Spritesize8x16)
            {
                // 8x8 sprites
                int r = flipY ? ((7 - row) & 7) : (row & 7);
                return SpPatternTableAddr | (sprTile << 4) | r;
            }
            else
            {
                // 8x16 sprites: bit 0 of tile selects pattern table
                int table = (sprTile & 1) != 0 ? 0x1000 : 0;
                int tileBase = (sprTile & 0xFE) << 4;
                if (!flipY)
                {
                    if (row < 8)
                        return table | tileBase | (row & 7);
                    else
                        return table | (tileBase + 16) | (row & 7);
                }
                else
                {
                    if (row < 8)
                        return table | (tileBase + 16) | (7 - (row & 7));
                    else
                        return table | tileBase | (7 - (row & 7));
                }
            }
        }

        // Reverse bits in a byte (for sprite horizontal flip at tile load time)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static byte FlipByte(byte b)
        {
            b = (byte)(((b & 0xF0) >> 4) | ((b & 0x0F) << 4));
            b = (byte)(((b & 0xCC) >> 2) | ((b & 0x33) << 2));
            b = (byte)(((b & 0xAA) >> 1) | ((b & 0x55) << 1));
            return b;
        }

        static void NotifyMapperA12(int address)
        {
            // cx = PPU_Dot (post-increment), ppu_cycles_x = cx. Timestamp matches TriCNES.
            MapperObj.NotifyA12(address, scanline * 341 + ppu_cycles_x);
        }

        // ── Half-step: runs AFTER each full ppu_step (mid-dot) ──
        // TriCNES model: _EmulateHalfPPU() at PPUClock==2
        // Handles: per-dot pixel output from shift registers, fine-grained register delays
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ppu_half_step()
        {
            // ── VBL latch pipeline (TriCNES: _EmulateHalfPPU lines 1833-1840) ──
            // Stage 2: pendingVblank → ppuVSET
            ppuVSET = false;
            if (pendingVblank) { pendingVblank = false; ppuVSET = true; }
            // Stage 3: Latch2 = !Latch1 (from previous full step)
            ppuVSET_Latch2 = !ppuVSET_Latch1;

            // ── Sprite0 hit pipeline (TriCNES: 1.5 dot delay) ──
            // Delayed snapshot for $2002 read (before promotion)
            isSprite0hit_Delayed = isSprite0hit;
            // Stage 2→actual
            if (pendingSprite0Hit2) { pendingSprite0Hit2 = false; isSprite0hit = true; }
            // Stage 1→2
            if (pendingSprite0Hit) { pendingSprite0Hit = false; pendingSprite0Hit2 = true; }

            // P4-3: OAMBuffer update (TriCNES _EmulateHalfPPU lines 1842-1860)
            // Uses ppu_cycles_x (post-increment) to match TriCNES PPU_Dot (also post-increment)
            if ((ShowBackGround || ShowSprites) && scanline >= 0 && scanline < 240)
            {
                int dot = ppu_cycles_x; // TriCNES: PPU_Dot (post-increment in _EmulatePPU)
                if (dot == 0 || dot > 320)
                    ppuOamBuffer = secondaryOAM[0];
                else if (dot > 0 && dot <= 64)
                    ppuOamBuffer = 0xFF;
                else if (dot <= 256)
                    ppuOamBuffer = oamCopyBuffer;
                else // dots 257-320
                    ppuOamBuffer = oamCopyBuffer;
            }

            // $2007 state machine half-step tick
            Ppu2007SmTick();

            int hsDot = ppu_cycles_x; // post-increment, matches TriCNES PPU_Dot in _EmulateHalfPPU

            // ── BG shift registers: shift left 1 bit each half-step (TriCNES: PPU_UpdateShiftRegisters) ──
            // TriCNES line 1814: (PPU_Dot > 0 && PPU_Dot <= 257) || (PPU_Dot > 320 && PPU_Dot <= 336)
            if ((scanline < 240 || scanline == preRenderLine)
                && ((hsDot > 0 && hsDot <= 257) || (hsDot > 320 && hsDot <= 336)))
            {
                if (ShowBackGround || ShowSprites) // TriCNES: Tier 2 gate
                {
                    renderLow  <<= 1;
                    renderHigh = (ushort)((renderHigh << 1) | 1);
                    renderAttrLow  = (ushort)((renderAttrLow << 1) | (attrLatch & 1));
                    renderAttrHigh = (ushort)((renderAttrHigh << 1) | ((attrLatch >> 1) & 1));
                }
            }

            // ── Deferred shift register reload (TriCNES: PPU_Render_CommitShiftRegistersAndBitPlanes_HalfDot) ──
            if (commitLoadShiftReg)
            {
                commitLoadShiftReg = false;
                renderLow  = (ushort)((renderLow  & 0xFF00) | pendingTileLow);
                renderHigh = (ushort)((renderHigh & 0xFF00) | pendingTileHigh);
                attrLatch = pendingAttrLatch; // TriCNES: PPU_AttributeLatchRegister = PPU_Attribute (line 3750)
            }

            // ── Half-step tile fetch (TriCNES: PPU_Render_ShiftRegistersAndBitPlanes_HalfDot, line 3604) ──
            // Sets commitLoadShiftReg at phase 7, committed at NEXT half step.
            // Range: PPU_Dot >= 0 && PPU_Dot < 257 || PPU_Dot >= 320 && PPU_Dot < 336
            if ((scanline < 240 || scanline == preRenderLine)
                && ((hsDot >= 0 && hsDot < 257) || (hsDot >= 320 && hsDot < 336)))
            {
                if (ShowBackGround || ShowSprites)
                {
                    if ((hsDot & 7) == 7)
                        commitLoadShiftReg = true;
                }
            }

            // P4-2: Palette corruption effect — only on alignment 2 (TriCNES: CorruptPalettes)
            // At alignment 0 (AprNes fixed), this never fires. Structural placeholder.
            if (paletteCorruptFromDisable || paletteCorruptFromVAddr)
            {
                paletteCorruptFromDisable = false;
                paletteCorruptFromVAddr = false;
            }
        }

        // ── Region-specialized PPU step functions ──
        // Common logic extracted into ppu_step_common + ppu_step_rendering (AggressiveInlining).
        // Region-specific tail (VBL trigger, flag clear, dot skip, scanline wrap) hardcoded per version.
        // Search "★ REGION" for all difference points.

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ppu_step_common(out int cx, out bool renderingEnabled)
        {
            // $2007 state machine (fully deferred: buffer/write/increment)
            Ppu2007SmTick();

            // $2006 delayed t→v copy
            if (ppu2006UpdateDelay > 0 && --ppu2006UpdateDelay == 0)
            {
                int prevAddr = vram_addr;
                vram_addr = ppu2006PendingAddr;
                ppuAddressBus = vram_addr; // TriCNES line 1272
                // Notify A12 only outside active rendering — during rendering, tile fetch phases handle A12
                bool inRendering = (ShowBackGround_Instant || ShowSprites_Instant) && (scanline < 240 || scanline == preRenderLine);
                if (mapperNeedsA12 && !inRendering) NotifyMapperA12(vram_addr);

                // P4-2: Palette corruption when leaving palette range
                // TriCNES: if old addr >= $3F00 and new addr < $3F00, and low nibble != 0
                if ((prevAddr & 0x3FFF) >= 0x3F00 && (vram_addr & 0x3FFF) < 0x3F00)
                {
                    if (scanline >= 0 && scanline < 240 && ppu_cycles_x <= 256)
                    {
                        if ((prevAddr & 0xF) != 0)
                            paletteCorruptFromVAddr = true;
                    }
                }
            }

            // $2005 delayed scroll update
            if (ppu2005UpdateDelay > 0 && --ppu2005UpdateDelay == 0)
            {
                byte v = ppu2005PendingValue;
                // TriCNES: latch checked at DELAY EXPIRY time, not write time (line 1291)
                if (vram_latch)
                {
                    scrol_y = v & 7;
                    vram_addr_internal = (vram_addr_internal & 0x0C1F) | ((v & 0x7) << 12) | ((v & 0xF8) << 2);
                }
                else
                {
                    vram_addr_internal = (vram_addr_internal & 0x7fe0) | ((v & 0xf8) >> 3);
                    FineX = v & 0x07;
                }
                vram_latch = !vram_latch; // TriCNES: latch flipped in deferred handler (line 1302)
            }

            // $2000 delayed control update (TriCNES: all fields delayed 1-2 PPU cycles)
            if (ppu2000UpdateDelay > 0 && --ppu2000UpdateDelay == 0)
            {
                NMIable = ((ppu2000PendingValue & 0x80) > 0);
                VramaddrIncrement = ((ppu2000PendingValue & 4) > 0) ? 32 : 1;
                SpPatternTableAddr = ((ppu2000PendingValue & 8) > 0) ? 0x1000 : 0;
                BgPatternTableAddr = ((ppu2000PendingValue & 0x10) > 0) ? 0x1000 : 0;
                Spritesize8x16 = ((ppu2000PendingValue & 0x20) > 0);
                vram_addr_internal = (ushort)((vram_addr_internal & 0x73ff) | ((ppu2000PendingValue & 3) << 10));
                BaseNameTableAddr = 0x2000 | ((ppu2000PendingValue & 3) << 10);
            }

            // $2001 delayed mask update (Tier 2: ShowBackGround/ShowSprites)
            if (ppu2001UpdateDelay > 0 && --ppu2001UpdateDelay == 0)
            {
                ShowBgLeft8    = (ppu2001PendingValue & 0x02) != 0;
                ShowSprLeft8   = (ppu2001PendingValue & 0x04) != 0;
                ShowBackGround = (ppu2001PendingValue & 0x08) != 0;
                ShowSprites    = (ppu2001PendingValue & 0x10) != 0;
            }

            // $2001 emphasis bits independent delay
            if (ppu2001EmphasisDelay > 0 && --ppu2001EmphasisDelay == 0)
            {
                byte v = ppu2001EmphasisPending;
                ppuEmphasis = (byte)((v >> 5) & 0x7);
                if (Region != RegionType.NTSC)
                    ppuEmphasis = (byte)((ppuEmphasis & 0x4) | ((ppuEmphasis & 1) << 1) | ((ppuEmphasis >> 1) & 1));
            }

            // Open bus decay
            if (--open_bus_decay_timer == 0)
            {
                open_bus_decay_timer = 77777;
                openbus = 0;
            }

            // renderingEnabled uses _Instant flags (Tier 1) for core PPU state
            // (odd frame skip, vram increment, tile fetch control, etc.)
            renderingEnabled = ShowBackGround_Instant || ShowSprites_Instant;
            cx = ppu_cycles_x;

            // Sprite 0 hit detection moved to CalculatePixel (TriCNES model: pixel-based, in full step)
        }

        // ★ REGION: shared rendering body — PRE_RENDER_LINE passed as hardcoded literal from each version
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ppu_step_rendering(int cx, bool renderingEnabled, int PRE_RENDER_LINE)
        {
            // TriCNES line 1674: address bus = v when rendering disabled — ALL scanlines
            // Uses Tier 2 delayed flags (TriCNES: PPU_Mask_ShowBackground/ShowSprites)
            if (!ShowBackGround && !ShowSprites)
            {
                ppuAddressBus = vram_addr;
                ppuChrFetchA12 = (vram_addr >> 12) & 1;
            }

            // Deferred commit: CXinc from previous phase 7 (TriCNES: PPU_Commit_PatternHighFetch)
            // TriCNES line 1727: CommitShiftRegistersAndBitPlanes runs OUTSIDE scanline gate
            if (commitCXinc) { commitCXinc = false; CXinc(); }

            if (scanline < 240 || scanline == PRE_RENDER_LINE) // ★ REGION
            {
                if (ppuRenderingEnabled)
                    ppu_rendering_tick(cx, PRE_RENDER_LINE); // ★ REGION

                // Alignment-dependent eval delay update (TriCNES: lines 1652-1658)
                // Non-phase-3: update delayed flags BEFORE sprite eval (1 PPU cycle delay)
                if ((mcCpuClock & 3) != 3)
                {
                    ShowBG_EvalDelay = ShowBackGround;
                    ShowSpr_EvalDelay = ShowSprites;
                }

                // Per-dot sprite evaluation (visible + pre-render scanlines)
                // TriCNES: clear phase uses DELAYED flags, eval phase uses INSTANT flags
                if (AccuracyOptA)
                {
                    bool evalScanline = (scanline >= 0 && scanline < 240) || scanline == PRE_RENDER_LINE;
                    bool ro = scanline == preRenderLine; // SpriteEval_ReadOnly_PreRenderLine
                    // Eval uses PPU_Dot (= ppu_cycles_x) for boundaries — independent of cx
                    int evalDot = ppu_cycles_x; // = PPU_Dot (TriCNES: post-increment)

                    // ── Dots 0-64: clear secondary OAM — DELAYED flags gate (TriCNES line 2510/2541) ──
                    if (evalScanline && evalDot >= 0 && evalDot <= 64 && (ShowBG_EvalDelay || ShowSpr_EvalDelay))
                    {
                        // Dot 1: reset eval state (TriCNES line 2520-2526)
                        if (evalDot == 1)
                        {
                            evalOam2Addr = 0;
                            evalOam2Full = false;
                            evalTick = 0;
                            evalOamOverflowed = false;
                        }

                        if ((evalDot & 1) != 0) // odd (TriCNES: PPU_Dot & 1 == 1)
                        {
                            oamCopyBuffer = ro ? secondaryOAM[evalOam2Addr] : (byte)0xFF;
                        }
                        else if (evalDot > 0) // even > 0: write + advance
                        {
                            if (!ro)
                                secondaryOAM[evalOam2Addr] = oamCopyBuffer;
                            evalOam2Addr++;
                            evalOam2Addr &= 0x1F;
                        }
                    }

                    // ── Dot 65: init OUTSIDE rendering gate (TriCNES line 2585-2588) ──
                    if (evalScanline && evalDot == 65)
                    {
                        evalOam2Addr = 0;
                        nineObjectsOnLine = false;
                    }

                    // ── Dots 65-256: evaluation — INSTANT flags gate (TriCNES line 2590) ──
                    if (evalScanline && evalDot >= 65 && evalDot <= 256 && (ShowBackGround_Instant || ShowSprites_Instant))
                    {
                        if (evalDot == 65)
                        {
                            sprite0_eval_addr = spr_ram_add;
                            SpriteEvalInit();
                            SpriteEvalTick();
                        }
                        else
                        {
                            SpriteEvalTick();
                            if (evalDot == 256) SpriteEvalEnd();
                        }
                    }
                    // Pre-render line: save sprite0_eval_addr at dot 65 even if rendering off
                    else if (ro && evalDot == 65 && ppuRenderingEnabled)
                    {
                        sprite0_eval_addr = spr_ram_add;
                    }
                }

                // Phase-3: update delayed flags AFTER sprite eval (2 PPU cycle delay)
                // (TriCNES: lines 1667-1673)
                if ((mcCpuClock & 3) == 3)
                {
                    ShowBG_EvalDelay = ShowBackGround;
                    ShowSpr_EvalDelay = ShowSprites;
                }

                if (scanline >= 0 && scanline < 240)
                {
                    // At start of each visible scanline: always zero Buffer_BG_array to prevent
                    // stale data from prior frames causing incorrect sprite priority decisions.
                    if (cx == 1) // PPU_Dot=1: first dot of new scanline
                    {
                        int scanOff = scanline << 8;
                        int* bgp = Buffer_BG_array + scanOff;
                        for (int* bge = bgp + 256; bgp < bge; bgp++) *bgp = 0;
                        // Always fill backdrop: per-dot rendering overwrites active BG pixels,
                        // but dots where rendering is disabled mid-scanline ($2001 toggle)
                        // must show backdrop, not stale data from the previous frame.
                        {
                            uint bgColor = NesColors[ppu_ram[0x3f00] & 0x3f];
                            uint* sp = ScreenBuf1x + scanOff;
                            for (uint* se = sp + 256; sp < se; sp++) *sp = bgColor;
                            if (AnalogEnabled)
                            {
                                byte bgIdx = (byte)(ppu_ram[0x3f00] & 0x3f);
                                for (int i = 0; i < 256; i++) ntscScanBuf[i] = bgIdx;
                            }
                        }
                        PrecomputeOverflow();
                    }

                    // Per-cycle sprite overflow flag
                    if (spriteOverflowCycle >= 0 && cx == spriteOverflowCycle)
                        isSpriteOverflow = true;

                }

                // Dot 257: initialize per-dot sprite shift register state
                // Sprite tiles are fetched at dots 257-320 into shift registers,
                // X counters set at dot 339, rendered per-dot at dots 1-256 next scanline.
                if (scanline >= 0 && scanline < 240 && cx == 257)
                {
                    if (mmc5Ref != null) mmc5Ref.PreSpriteRender();
                    sprSlotCount = evalSpriteCount;
                    sprZeroInSlots = evalSprite0Visible;
                }
                // Pre-render line: now runs evaluation (like TriCNES), so use eval results
                else if (scanline == PRE_RENDER_LINE && cx == 257) // ★ REGION
                {
                    sprSlotCount = evalSpriteCount;
                    sprZeroInSlots = evalSprite0Visible;
                }

                // Pre-render line: compute pre-render sprite data at dot 257
                if (scanline == PRE_RENDER_LINE && cx == 257 && ppuRenderingEnabled) // ★ REGION
                    PrecomputePreRenderSprites();

            }

            // 3-dot pipeline shift (TriCNES line 1724: runs EVERY dot, ALL scanlines — OUTSIDE any gate)
            prevPrevPrevDotColor = prevPrevDotColor; prevPrevDotColor = prevDotColor; prevDotColor = dotColor;
            prevPrevPrevDotPalIdx = prevPrevDotPalIdx; prevPrevDotPalIdx = prevDotPalIdx; prevDotPalIdx = dotPalIdx;

            // ── TriCNES CalculatePixel + UpdateSpriteShiftRegisters + DrawToScreen ──
            // Runs every dot on visible scanlines. BG/sprite pixels read PRE-shift/PRE-decrement,
            // then sprite counters decremented and shift registers shifted AFTER (TriCNES order).
            if (scanline >= 0 && scanline < 240)
            {
                // TriCNES: if (PPU_Dot > 0 && PPU_Dot <= 257) — CalculatePixel + UpdateSprite + DrawToScreen
                if (cx > 0 && cx <= 257) // outer gate for ALL pixel operations
                {
                    byte backdropIdx = (byte)(ppu_ram[0x3f00] & 0x3f);
                    uint compositeColor = NesColors[backdropIdx];
                    byte compositePalIdx = backdropIdx;
                    int bgColor = 0;
                    int bgPalette = 0;

                    // BG pixel: TriCNES PPU_Dot <= 256 && (PPU_Dot > 8 || mask)
                    if (cx <= 256 && ShowBackGround && (cx > 8 || ShowBgLeft8))
                    {
                        int bit = 15 - FineX;
                        int col0 = (renderLow >> bit) & 1;
                        int col1 = (renderHigh >> bit) & 1;
                        bgColor = (col1 << 1) | col0;
                        bgPalette = (bit >= 8) ? bg_attr_p3 : bg_attr_p2;
                        if (bgColor == 0) bgPalette = 0;
                    }

                    // Sprite pixel: TriCNES PPU_Dot <= 256 && (PPU_Dot > 8 || mask)
                    int sprColor = 0, sprPalette = 0, sprSlot = -1;
                    bool sprPriority = false;
                    if (cx <= 256 && ShowSprites && (cx > 8 || ShowSprLeft8))
                    {
                        for (int s = 0; s < 8; s++)
                        {
                            if (sprXCounter[s] == 0 || skippedPreRenderDot341)
                            {
                                int px = ((sprShiftH[s] >> 7) << 1) | (sprShiftL[s] >> 7);
                                if (px != 0 && sprColor == 0)
                                {
                                    sprColor = px;
                                    sprPalette = (sprFetchAttr[s] & 3) | 4;
                                    sprPriority = ((sprFetchAttr[s] >> 5) & 1) == 0;
                                    sprSlot = s;
                                }
                            }
                        }

                        // Sprite 0 hit: TriCNES PPU_Dot > 8 && PPU_Dot < 256
                        if (canDetectSprite0Hit && sprSlot == 0 && sprZeroInSlots
                            && ShowBackGround && ShowSprites
                            && bgColor != 0 && sprColor != 0)
                        {
                            if ((ShowSprLeft8 || cx > 8) && cx < 256)
                            {
                                pendingSprite0Hit = true;
                                canDetectSprite0Hit = false;
                            }
                        }
                        if (sprColor != 0 && ShowSprites)
                        {
                            if (bgColor == 0 || sprPriority)
                            {
                                bgColor = sprColor;
                                bgPalette = sprPalette;
                            }
                        }
                    }

                    // Final color: PPU_Dot <= 256
                    if ((ShowBackGround || ShowSprites) && cx <= 256)
                    {
                        int palAddr = (bgPalette << 2) | bgColor;
                        if (bgColor == 0) palAddr = 0;
                        compositeColor = NesColors[ppu_ram[0x3f00 + palAddr] & 0x3f];
                        compositePalIdx = (byte)(ppu_ram[0x3f00 + palAddr] & 0x3f);
                    }
                    else if (cx <= 256)
                    {
                        if ((vram_addr & 0x3F1F) >= 0x3F00)
                        {
                            int palAddr = vram_addr & 0x1F;
                            if ((palAddr & 3) == 0) palAddr &= 0x0F;
                            compositeColor = NesColors[ppu_ram[0x3f00 + palAddr] & 0x3f];
                            compositePalIdx = (byte)(ppu_ram[0x3f00 + palAddr] & 0x3f);
                        }
                    }

                    dotColor = compositeColor;
                    dotPalIdx = compositePalIdx;
                } // end CalculatePixel inner (cx <= 257 visible+border)

                // UpdateSpriteShiftRegisters (TriCNES line 3718: PPU_Dot <= 256)
                // Must match TriCNES outer gate: PPU_Dot > 0 (excludes dot 0 after scanline wrap)
                if (cx > 0 && cx <= 256)
                {
                    for (int s = 0; s < 8; s++)
                    {
                        if (sprXCounter[s] > 0 && !skippedPreRenderDot341)
                        {
                            sprXCounter[s]--;
                        }
                        else
                        {
                            if (ShowSprites || ShowBackGround)
                            {
                                sprShiftL[s] <<= 1;
                                sprShiftH[s] <<= 1;
                            }
                        }
                    }
                }

                // DrawToScreen: TriCNES PPU_Dot > 3 && PPU_Dot <= 259
                if (cx >= 4 && cx <= 259)
                {
                    int pos = (scanline << 8) + (cx - 4);
                    ScreenBuf1x[pos] = prevPrevPrevDotColor;
                    if (AnalogEnabled) ntscScanBuf[cx - 4] = prevPrevPrevDotPalIdx;
                }

                if (AnalogEnabled && cx == 260)
                    DecodeScanline(scanline, ntscScanBuf, ppuEmphasis);
            } // end outer gate (cx > 0 && cx <= 257)

            if (scanline == 240 && cx == 1)
            {
                RenderScreen();
                frame_count++;
                if (AnalogEnabled) { Ntsc_SetFrameCount(frame_count); Crt_SetFrameCount(frame_count); }
            }
            ppuRenderingEnabled = renderingEnabled;
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

        // Precomputed packed scanline event constants (set by ApplyRegionProfile)
        static int L_VBL_START;     // (nmiTriggerLine << 9) | 1
        static int L_SPRITE_RESET;  // (preRenderLine << 9) | 1
        static int L_VBL_END;       // (preRenderLine << 9) | 2

        // ═══════════════════════════════════════════════════════════════
        // Unified PPU step — region differences via precomputed parameters:
        //   preRenderLine, nmiTriggerLine, totalScanlines (set by ApplyRegionProfile)
        //   NTSC odd frame skip (always enabled — PAL/Dendy stripped)
        // ═══════════════════════════════════════════════════════════════
        static void ppu_step()
        {
            int cx; bool re;
            ppu_step_common(out cx, out re);

            // TriCNES order: deferred → scroll → dot++ → WRAP → events → VBL → mapper → A12_Prev → rendering
            ppu_cycles_x = ++cx;

            // Scanline wrap — BEFORE events/mapper (TriCNES: PPU_Dot > 340 → wrap)
            if (cx == 341)
            {
                if (++scanline == totalScanlines)
                { scanline = 0; if (ShowBackGround_Instant || ShowSprites_Instant) ProcessOamCorruption(); }
                ppu_cycles_x = cx = 0;
            }

            // ── Events (TriCNES lines 1532-1606) ──
            if (cx <= 2)
            {
                int L = (scanline << 9) | cx;
                if (L == L_VBL_START)
                    pendingVblank = true;
                else if (L == L_SPRITE_RESET)
                    { isSprite0hit = isSpriteOverflow = false; pendingSprite0Hit = false; pendingSprite0Hit2 = false; canDetectSprite0Hit = true; }
                else if (L == L_VBL_END)
                    isVblank = false;
            }
            // TriCNES line 1582-1584: OddFrame toggle at scanline 260 dot 340
            if (scanline == 260 && cx == 340)
                oddSwap = !oddSwap;

            // ── VBL latch (TriCNES lines 1608-1616) ──
            ppuVSET_Latch1 = !ppuVSET;
            if (ppuVSET && !ppuVSET_Latch2)
                isVblank = true;
            if (ppu2002ReadPending)
            {
                ppu2002ReadPending = false;
                isVblank = false;
            }

            // Sprite overflow delayed (TriCNES line 1619)
            isSpriteOverflow_Delayed = isSpriteOverflow;

            // Mapper (TriCNES line 1627)
            MapperObj.PpuClock();

            // A12_Prev (TriCNES line 1628)
            ppuA12Prev = (ppuAddressBus & 0x1000) != 0;

            // ── Odd frame skip (TriCNES lines 1629-1637, AFTER mapper, BEFORE rendering) ──
            // Uses oddSwap (toggled at SL260), NOT toggled here
            // TriCNES uses delayed flags (PPU_Mask_ShowBackground/Sprites), not Instant
            if (oddSwap && (ShowBackGround || ShowSprites)
                && scanline == preRenderLine && cx == 340)
            {
                if (mmc5Ref != null)
                    mmc5Ref.NotifyVramRead(0x2000 | (vram_addr & 0x0FFF));
                scanline = 0;
                ppu_cycles_x = cx = 0;
                skippedPreRenderDot341 = true;
            }
            // TriCNES line 1640-1642
            if (skippedPreRenderDot341 && scanline == 0 && cx == 2)
                skippedPreRenderDot341 = false;

            // Rendering — cx = PPU_Dot (direct TriCNES match)
            ppu_step_rendering(cx, re, preRenderLine);

            // Scanline wrap moved to before events (TriCNES order)
        }

        #endregion

        static int open_bus_decay_timer = 77777;

        // Pre-computed sprite 0 data for per-pixel hit detection during BG rendering
        static bool sprite0_on_line;
        static int sprite0_line_x;
        static byte sprite0_tile_low, sprite0_tile_high;
        static bool sprite0_flip_x;
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
        static bool spriteInRange;                  // TriCNES: PPU_OAMEvaluationObjectInRange
        static bool sprite0Added;                   // TriCNES: PPU_NextScanlineContainsSpriteZero
        static bool nineObjectsOnLine;              // TriCNES: NineObjectsOnThisScanline
        static int evalSpriteCount;                 // Number of sprites found (0-8)
        // Legacy aliases for code that still references old names
        static byte spriteEvalAddrH { get { return (byte)(evalOamAddr >> 2); } }
        static byte spriteEvalAddrL { get { return (byte)(evalOamAddr & 3); } }
        static byte secOAMAddr { get { return evalOam2Addr; } set { evalOam2Addr = value; } }
        static bool oamCopyDone { get { return evalOamOverflowed; } set { evalOamOverflowed = value; } }
        static byte overflowBugCounter;             // For sprite overflow hardware bug
        static bool evalSprite0Visible;             // Sprite 0 found in secondary OAM

        // Pre-render line sprite data (loaded at pre-render dot 257 for scanline 0)
        static bool prerender_sprite0_valid;
        static int prerender_sprite0_x;
        static byte prerender_sprite0_tile_low, prerender_sprite0_tile_high;
        static bool prerender_sprite0_flip_x;

        // Pre-computed sprite overflow cycle for cycle-accurate overflow flag timing
        static int spriteOverflowCycle;

        // OAM corruption: mark which row gets corrupted when rendering is disabled mid-scanline
        // 6.5: TriCNES OAM corruption model — uses OAM2Address as corruption index
        static void SetOamCorruptionFlags()
        {
            // TriCNES: PPU_OAMCorruptionIndex = OAM2Address
            // Record which row to corrupt based on current sprite evaluation address
            oamCorruptIndex = evalOam2Addr;
        }

        // OAM corruption: copy first 8 bytes of OAM over the corrupted row (TriCNES CorruptOAM)
        static unsafe void ProcessOamCorruption()
        {
            int idx = oamCorruptIndex;
            if (idx >= 0x20) idx = 0; // TriCNES: wrap at 32
            if (idx > 0)
            {
                // Copy row 0 (8 bytes) to corrupted row
                for (int i = 0; i < 8; i++)
                    spr_ram[idx * 8 + i] = spr_ram[i];
                // Also corrupt secondary OAM (TriCNES: OAM2[index] = OAM2[0])
                secondaryOAM[idx] = secondaryOAM[0];
            }
        }

        // Pre-compute sprite 0 tile data for the current scanline so hit detection
        // can happen per-pixel inside RenderBGTile() at the correct PPU cycle.
        static void PrecomputeSprite0Line()
        {
            sprite0_on_line = false;
            if (isSprite0hit || pendingSprite0Hit) return;
            if (!ShowBackGround && !ShowSprites) return;

            // Sprite 0 is the first sprite processed during evaluation on the PREVIOUS
            // scanline. Use the saved OAMADDR from that evaluation (dot 65).
            // For misaligned OAM, bytes wrap: addrH increments when addrL wraps past 3.
            int addrH = (sprite0_eval_addr >> 2) & 0x3F;
            int addrL = sprite0_eval_addr & 0x03;

            // Read 4 bytes as hardware does: Y, tile, attr, X — with addrL wrapping
            byte sprY    = spr_ram[(byte)(addrH * 4 + addrL)]; addrL++; if (addrL >= 4) { addrH = (addrH + 1) & 0x3F; addrL = 0; }
            byte sprTile = spr_ram[(byte)(addrH * 4 + addrL)]; addrL++; if (addrL >= 4) { addrH = (addrH + 1) & 0x3F; addrL = 0; }
            byte sprAttr = spr_ram[(byte)(addrH * 4 + addrL)]; addrL++; if (addrL >= 4) { addrH = (addrH + 1) & 0x3F; addrL = 0; }
            byte sprX    = spr_ram[(byte)(addrH * 4 + addrL)];

            int y_loc = sprY + 1; // NES hardware: sprites display at OAM_Y + 1
            // Use latched sprite size from dot 261 of previous scanline's HBlank.
            // This matches real hardware where sprite 0's CHR fetch uses the size
            // at fetch time, not the value at dot 0 of the next scanline.
            bool sprSize16 = spriteSizeLatchedForFetch;
            int height = sprSize16 ? 15 : 7;
            if (scanline < y_loc || scanline - y_loc > height)
            {
                // Scanline 0: use pre-render line sprite 0 data if available.
                if (scanline == 0 && prerender_sprite0_valid)
                {
                    sprite0_on_line = true;
                    sprite0_line_x = prerender_sprite0_x;
                    sprite0_tile_low = prerender_sprite0_tile_low;
                    sprite0_tile_high = prerender_sprite0_tile_high;
                    sprite0_flip_x = prerender_sprite0_flip_x;
                }
                return;
            }

            sprite0_on_line = true;
            sprite0_line_x = sprX;

            sprite0_flip_x = (sprAttr & 0x40) != 0;
            int offset, tile_th_t, line, line_t;
            byte tile_th;

            if (sprSize16)
            {
                tile_th = (byte)(sprTile & 0xfe);
                offset = (sprTile & 1) != 0 ? 256 : 0;
            }
            else
            {
                tile_th = sprTile;
                offset = SpPatternTableAddr >> 4;
            }

            if (scanline <= y_loc + 7)
            {
                tile_th_t = tile_th + offset;
                line = scanline - y_loc;
            }
            else
            {
                tile_th_t = tile_th + offset + 1;
                line = scanline - y_loc - 8;
            }

            if ((sprAttr & 0x80) != 0)
            {
                line_t = 7 - line;
                if (sprSize16) tile_th_t ^= 1;
            }
            else line_t = line;

            { int a = (tile_th_t << 4) | (line_t + 8); sprite0_tile_high = chrBankPtrs[(a >> 10) & 7][a & 0x3FF]; }
            { int a = (tile_th_t << 4) | line_t;       sprite0_tile_low  = chrBankPtrs[(a >> 10) & 7][a & 0x3FF]; }
        }

        // Pre-compute the PPU cycle at which sprite overflow flag should be set.
        // Simulates NES sprite evaluation timing (dots 65-256) with the hardware
        // overflow bug: after finding 8 sprites, byte offset m cycles 0→1→2→3,
        // reading tile/attr/X bytes as Y coordinates.
        static void PrecomputeOverflow()
        {
            spriteOverflowCycle = -1;
            if (!ShowBackGround && !ShowSprites) return;

            int height = Spritesize8x16 ? 15 : 7;
            int evalCycle = 66; // PPU_Dot based (was 65 with old cx=PPU_Dot-1)
            int foundCount = 0;
            int m = 0; // byte offset for overflow bug

            for (int n = 0; n < 64 && evalCycle <= 256; n++)
            {
                if (foundCount < 8)
                {
                    // Normal evaluation: always read byte 0 (Y)
                    int oam_y = spr_ram[n << 2];
                    if (scanline >= oam_y && scanline - oam_y <= height)
                    {
                        foundCount++;
                        evalCycle += 8; // in-range: 2 read + 6 copy
                    }
                    else
                    {
                        evalCycle += 2; // not in range
                    }
                }
                else
                {
                    // Overflow bug evaluation: read byte m (cycles through 0,1,2,3)
                    int oam_y = spr_ram[(n << 2) + m];
                    if (scanline >= oam_y && scanline - oam_y <= height)
                    {
                        spriteOverflowCycle = evalCycle;
                        return;
                    }
                    m = (m + 1) & 3; // bug: increment byte offset on miss
                    evalCycle += 2;
                }
            }
        }

        // Initialize sprite evaluation state at dot 65 of visible scanlines
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SpriteEvalInit()
        {
            sprite0Added = false;
            spriteInRange = false;
            evalOam2Addr = 0;           // TriCNES: OAM2Address = 0 at dot 65
            evalOam2Full = false;       // TriCNES: SecondaryOAMFull = false
            evalTick = 0;              // TriCNES: SpriteEvaluationTick = 0
            overflowBugCounter = 0;
            evalOamOverflowed = false;  // TriCNES: OAMAddressOverflowedDuringSpriteEvaluation = false
            nineObjectsOnLine = false;  // TriCNES: NineObjectsOnThisScanline = false
            // evalOamAddr IS spr_ram_add (alias) — no init needed, PPUOAMAddress is the register itself
        }

        // Per-dot sprite evaluation: odd dots read, even dots write/check
        // TriCNES uses (PPU_Dot & 1)==1 for odd. AprNes ppu_cycles_x = cx+1 (post-increment),
        // so (ppu_cycles_x & 1)==0 aligns with TriCNES odd dots.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SpriteEvalTick()
        {
            bool isOdd = (ppu_cycles_x & 1) != 0; // eval uses ppu_cycles_x = PPU_Dot, direct parity match

            if (isOdd)
            {
                // Odd cycle: read from primary OAM (TriCNES line 2595)
                oamCopyBuffer = spr_ram[evalOamAddr];
                if ((evalOamAddr & 3) == 2) oamCopyBuffer &= 0xE7; // TriCNES: bits 3-4 masked
            }
            else
            {
                // Even cycle: write/check
                SpriteEvalWrite();
            }
        }

        // TriCNES _EmulatePPU lines 2614-2785: even cycle sprite evaluation
        // Full port with SpriteEval_ReadOnly_PreRenderLine guards and tick 3 pseudo range check
        static void SpriteEvalWrite()
        {
            int height = Spritesize8x16 ? 16 : 8;
            int evalSL = scanline & 0xFF; // TriCNES: (PPU_Scanline & 0xFF)
            bool ro = scanline == preRenderLine; // TriCNES: SpriteEval_ReadOnly_PreRenderLine

            if (!evalOamOverflowed) // TriCNES line 2620
            {
                byte preIncAddr = evalOamAddr; // TriCNES line 2622

                // Write to SecOAM if not full AND not read-only (TriCNES line 2623)
                if (!evalOam2Full && !ro)
                    secondaryOAM[evalOam2Addr] = oamCopyBuffer;

                // Capture OAM2READ after write (TriCNES line 2627)
                byte oam2Read = secondaryOAM[evalOam2Addr];

                if (evalTick == 0) // tick 0: Y byte (TriCNES line 2628)
                {
                    if (!nineObjectsOnLine && !ro
                        && evalSL >= oamCopyBuffer && evalSL < oamCopyBuffer + height)
                    {
                        // In range (TriCNES line 2633)
                        if (!evalOam2Full)
                        {
                            if (!ro) evalOamAddr++;       // line 2641
                            evalOam2Addr++;               // line 2643
                            evalOam2Addr &= 0x1F;
                            if (evalOam2Addr == 0) evalOam2Full = true;
                        }
                        else
                        {
                            // 9th+ sprite (TriCNES lines 2661-2668)
                            nineObjectsOnLine = true;
                            evalOamAddr++;
                        }
                        // Sprite 0 at dot 66 (TriCNES line 2655 — inside !evalOam2Full branch)
                        if (ppu_cycles_x == 66 && !evalOam2Full) sprite0Added = true;
                        if (!ro) evalTick++; // line 2671
                    }
                    else
                    {
                        // Not in range (TriCNES line 2674)
                        if (ppu_cycles_x == 66) sprite0Added = false;

                        if (!ro)
                        {
                            if (evalOam2Full && !nineObjectsOnLine)
                            {
                                // Overflow bug (TriCNES lines 2683-2693)
                                if ((evalOamAddr & 0x3) == 3)
                                    evalOamAddr++;
                                else
                                {
                                    evalOamAddr += 4;
                                    evalOamAddr++;
                                }
                            }
                            else
                            {
                                // Normal skip (TriCNES lines 2695-2698)
                                evalOamAddr += 4;
                                evalOamAddr &= 0xFC;
                            }
                        }
                    }
                }
                else // ticks 1, 2, 3 (TriCNES line 2703)
                {
                    if (evalTick == 3) // tick 3: X byte (TriCNES line 2705)
                    {
                        // Pseudo range check on X byte (TriCNES lines 2710-2744)
                        bool xInRange = (evalSL - oamCopyBuffer >= 0) && (evalSL - oamCopyBuffer < height);
                        if (xInRange)
                        {
                            if (!evalOam2Full)
                            {
                                if (!ro) evalOamAddr++; // line 2718
                            }
                            else
                            {
                                if (!ro) evalOamAddr += 4; // line 2725 (TriCNES code does +=4)
                            }
                        }
                        else
                        {
                            if (!evalOam2Full)
                            {
                                if (!ro) { evalOamAddr++; evalOamAddr &= 0xFC; } // lines 2736-2737
                            }
                            else
                            {
                                evalOamAddr++; evalOamAddr &= 0xFC; // lines 2742-2743 (no ro guard!)
                            }
                        }
                    }
                    else // ticks 1, 2 (TriCNES line 2747-2752)
                    {
                        if (!ro) evalOamAddr++;
                    }

                    evalTick = (byte)((evalTick + 1) & 3); // line 2754-2755

                    if (!evalOam2Full && !ro) // line 2756
                    {
                        evalOam2Addr++;       // line 2758
                        evalOam2Addr &= 0x1F; // line 2759
                        if (evalOam2Addr == 0) evalOam2Full = true;
                    }
                }

                // Detect overflow (TriCNES lines 2768-2770)
                if (evalOamAddr < preIncAddr && evalOamAddr < 4)
                    evalOamOverflowed = true;

                // PPU_OAMLatch = OAM2READ (TriCNES line 2772)
                oamCopyBuffer = oam2Read;
            }
            else // OAMAddressOverflowed (TriCNES line 2774)
            {
                if (!ro)
                {
                    evalOamAddr += 4;
                    evalOamAddr &= 0xFC;
                }
                oamCopyBuffer = secondaryOAM[evalOam2Addr]; // line 2785
            }

            // evalOamAddr IS spr_ram_add (alias) — no sync needed
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

        // Pre-render line: check secondary OAM entries against (261 & 255) = 5
        // and store sprite 0 data for scanline 0 rendering
        static void PrecomputePreRenderSprites()
        {
            prerender_sprite0_valid = false;
            int effectiveScanline = preRenderLine & 255; // NTSC: 261&255=5, PAL: 311&255=55
            int height = Spritesize8x16 ? 16 : 8;

            // Check first entry in secondary OAM (potential sprite 0)
            byte sprY = secondaryOAM[0];
            if (sprY >= 240) return; // $FF or invalid

            if (effectiveScanline >= sprY && effectiveScanline < sprY + height)
            {
                byte sprTile = secondaryOAM[1];
                byte sprAttr = secondaryOAM[2];
                byte sprX = secondaryOAM[3];

                prerender_sprite0_valid = true;
                prerender_sprite0_x = sprX;
                prerender_sprite0_flip_x = (sprAttr & 0x40) != 0;

                int line = effectiveScanline - sprY;
                int offset, tile_th_t, line_t;
                byte tile_th;

                if (Spritesize8x16)
                {
                    tile_th = (byte)(sprTile & 0xfe);
                    offset = (sprTile & 1) != 0 ? 256 : 0;
                }
                else
                {
                    tile_th = sprTile;
                    offset = SpPatternTableAddr >> 4;
                }

                if (line <= 7)
                {
                    tile_th_t = tile_th + offset;
                }
                else
                {
                    tile_th_t = tile_th + offset + 1;
                    line -= 8;
                }

                if ((sprAttr & 0x80) != 0)
                {
                    line_t = 7 - line;
                    if (Spritesize8x16) tile_th_t ^= 1;
                }
                else line_t = line;

                { int a = (tile_th_t << 4) | (line_t + 8); prerender_sprite0_tile_high = chrBankPtrs[(a >> 10) & 7][a & 0x3FF]; }
                { int a = (tile_th_t << 4) | line_t;       prerender_sprite0_tile_low  = chrBankPtrs[(a >> 10) & 7][a & 0x3FF]; }
            }
        }

        // ── RenderSpritesLine 優化摘要 ──
        // Pass 1: unsigned subtraction 範圍檢查取代雙分支; 找到 8 個後 break
        // Pass 2: long* 批次清零 sprSet (32×8=256 bytes); shift 取像素取代 mask+乘法;
        //         8x16 tile 選擇用 line>>3 取代 if 分支; priority 直接位移取值
        // Pass 3: long* 8-byte block-scan 跳過全空區域 (sprite 通常只佔少數像素)
        static unsafe void RenderSpritesLine_Batch()
        {
            // Pass 1: scan OAM 0→63, pick first 8 sprites visible on this scanline.
            // NES hardware only performs sprite evaluation when rendering is enabled.
            // Overflow detection is handled by PrecomputeOverflow() at cycle-accurate timing.
            if (!(ShowBackGround_Instant || ShowSprites_Instant))
            {
                if (AnalogEnabled) DecodeScanline(scanline, ntscScanBuf, ppuEmphasis);
                return;
            }

            int* sel = stackalloc int[8];
            int selCount = 0;
            int height = Spritesize8x16 ? 15 : 7;

            for (int oam_th = 0; oam_th < 64; oam_th++)
            {
                // Selection uses Y+1 (sprites display one scanline later).
                // Unsigned subtraction trick: (uint)(a - b) <= (uint)h
                // is equivalent to (a >= b && a - b <= h) but avoids two branches.
                int render_y = spr_ram[oam_th << 2] + 1;
                if ((uint)(scanline - render_y) <= (uint)height)
                {
                    if (selCount < 8) sel[selCount++] = oam_th;
                    else break; // hardware stops after 8 sprites
                }
            }

            if (!ShowSprites || selCount == 0)
            {
                // Analog mode: must decode every visible scanline even without sprites
                if (AnalogEnabled) DecodeScanline(scanline, ntscScanBuf, ppuEmphasis);
                return;
            }

            // Use static per-dot sprite buffers (compositing moved to ppu_half_step)
            // Clear sprLineSet with long* writes: 32 × 8 bytes = 256 bytes
            long* pClear = (long*)sprLineSet;
            for (int i = 0; i < 32; i++) pClear[i] = 0;

            // Pass 2: evaluate sprites in reverse OAM order so lower-index sprites overwrite higher,
            // making the lowest-index sprite the final winner at each pixel.
            for (int si = selCount - 1; si >= 0; si--)
            {
                int oam_addr = sel[si] << 2;
                int y_loc = spr_ram[oam_addr] + 1; // NES hardware: sprites display at OAM_Y + 1
                byte tile_th = spr_ram[oam_addr | 1];
                byte sprite_attr = spr_ram[oam_addr | 2];
                byte x_loc = spr_ram[oam_addr | 3];

                int tile_th_t, line;
                if (Spritesize8x16)
                {
                    int offset = (tile_th & 1) << 8; // bit 0 selects pattern bank (0 or 0x100)
                    tile_th &= 0xFE;
                    line = scanline - y_loc;
                    tile_th_t = tile_th + offset + (line >> 3); // line 0~7 → +0 (top), 8~15 → +1 (bottom)
                    line &= 7;
                }
                else
                {
                    tile_th_t = tile_th + (SpPatternTableAddr >> 4);
                    line = scanline - y_loc;
                }

                // Vertical flip: reverse line within tile + swap top/bottom for 8x16
                if ((sprite_attr & 0x80) != 0)
                {
                    line = 7 - line;
                    if (Spritesize8x16) tile_th_t ^= 1; // swap top ↔ bottom tile
                }

                int addr_low = (tile_th_t << 4) | line;
                byte tile_lbyte = chrBankPtrs[(addr_low >> 10) & 7][addr_low & 0x3FF];
                byte tile_hbyte = chrBankPtrs[((addr_low + 8) >> 10) & 7][(addr_low + 8) & 0x3FF];
                bool flip_x = (sprite_attr & 0x40) != 0;
                int palBase = 0x3F10 + ((sprite_attr & 3) << 2);
                byte priority = (byte)((sprite_attr & 0x20) >> 5); // 0 = front, 1 = behind BG

                for (int loc = 0; loc < 8; loc++)
                {
                    int sx = x_loc + loc;
                    if (sx > 255) break; // past right edge — remaining pixels also out of bounds
                    if (!ShowSprLeft8 && sx < 8) continue;

                    // Extract 2-bit pixel from tile planes using shift (branchless, no mask multiply)
                    int shift = flip_x ? loc : (7 - loc);
                    int p = ((tile_hbyte >> shift) & 1) << 1 | ((tile_lbyte >> shift) & 1);
                    if (p == 0) continue;

                    // Record as winner at this column (lower OAM index will overwrite later)
                    byte rawPal = (byte)(ppu_ram[palBase | p] & 0x3F);
                    sprLineSet[sx]    = 1;
                    sprLinePri[sx]    = priority;
                    sprLineBuf[sx]    = NesColors[rawPal];
                    sprLinePalIdx[sx] = rawPal;
                }
            }

            // Batch compositing (restored — per-dot requires buffer before dot 0, not feasible with MMC3)
            int scanOff = scanline << 8;
            long* pSprSetLong = (long*)sprLineSet;
            for (int b = 0; b < 32; b++)
            {
                if (pSprSetLong[b] == 0) continue;
                int bx = b << 3;
                for (int i = 0; i < 8; i++)
                {
                    int sx = bx + i;
                    if (sprLineSet[sx] == 0) continue;
                    int loc = scanOff + sx;
                    if (!ShowBackGround || Buffer_BG_array[loc] == 0 || sprLinePri[sx] == 0)
                    {
                        ScreenBuf1x[loc] = sprLineBuf[sx];
                        if (AnalogEnabled) ntscScanBuf[sx] = sprLinePalIdx[sx];
                    }
                }
            }

            if (AnalogEnabled)
                DecodeScanline(scanline, ntscScanBuf, ppuEmphasis);
        }

        static public bool screen_lock = false;
        static public volatile bool emuWaiting = false;
        static void RenderScreen()
        {
            if (AnalogEnabled && analogRenderThreadRunning)
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
            else if (AnalogEnabled)
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
                // === 數位模式同步 path / headless ===
                screen_lock = true;
                VideoOutput?.Invoke(null, null);
                screen_lock = false;
                emuWaiting = true;
                _event.WaitOne();
                emuWaiting = false;
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static byte ppu_r_2002()
        {
            // ── Start of read: VBL flag sampled immediately ──
            bool vblFlag = isVblank;

            // VBL suppression at exact VBL set dot (sl=nmiTriggerLine, cx=1):
            // PPU already set pendingVblank at this dot — clear it before half-step promotes it.
            if (scanline == nmiTriggerLine && ppu_cycles_x == 1)
            {
                pendingVblank = false;  // Cancel pending VBL promotion
                vblFlag = false;        // Return VBL=0 to CPU
            }

            // TriCNES: deferred VBL clear via PPU_Read2002 flag (processed in ppu_step)
            ppu2002ReadPending = true;

            // ── EmulateUntilEndOfRead: advance 7 master clocks (~1.75 PPU dots) ──
            // TriCNES: PPU advances mid-read so sprite flags reflect end-of-read state
            for (int i = 0; i < 7; i++)
                MasterClockTick();

            // ── End of read: sprite flags sampled after PPU advancement ──
            openbus = (byte)((vblFlag ? 0x80 : 0) | ((isSprite0hit_Delayed) ? 0x40 : 0) | ((isSpriteOverflow_Delayed) ? 0x20 : 0) | (openbus & 0x1f));

            vram_latch = false;
            return openbus;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static byte ppu_r_2007()
        {
            // Back-to-back $2007 access: if SM still running, return openbus
            if (ppu2007SM < 9)
                return openbus;

            int addr = vram_addr & 0x3FFF;
            byte result;

            if (addr >= 0x3F00)
                result = (byte)((openbus & 0xC0) | (PpuBusRead(addr) & 0x3F));
            else
                result = ppu_2007_buffer;

            // Fully deferred: buffer update at state 1/4, increment at state 4
            ppu2007SM_addr = vram_addr;
            ppu2007SM_isRead = true;
            ppu2007SM_readDelayed = true;
            ppu2007SM = 0;
            ppu2007SM_bufferLate = ((mcCpuClock & 3) <= 1); // TriCNES: uses CPUClock phase

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
            ppu2000UpdateDelay = ((mcPpuClock & 3) <= 1) ? 2 : 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ppu_w_2001(byte value)
        {
            openbus = value;

            // Tier 1: Instant flags — take effect immediately
            ShowBackGround_Instant = (value & 0x08) != 0;
            ShowSprites_Instant    = (value & 0x10) != 0;

            // P4-1: OAM corruption with per-alignment suppression (TriCNES model)
            bool newRenderingInstant = ShowBackGround_Instant || ShowSprites_Instant;
            if (prevRenderingEnabled != newRenderingInstant)
            {
                bool outsideVblank = scanline >= 0 && (scanline < 240 || scanline == preRenderLine);
                if (outsideVblank)
                {
                    if (!newRenderingInstant)
                    {
                        // Disabling rendering — mark rows for corruption
                        SetOamCorruptionFlags();
                        oamCorruptPending = true;

                        // P4-2: Palette corruption when disabling during first 2 dots of NT fetch
                        if ((ppu_cycles_x & 7) < 2 && ppu_cycles_x <= 250)
                        {
                            if ((vram_addr & 0x3FFF) >= 0x3C00)
                                paletteCorruptFromDisable = true;
                        }
                    }
                    else
                    {
                        // Re-enabling rendering — apply corruption with alignment gate
                        // TriCNES: alignment 1,2 suppress corruption on re-enable
                        int alignment = mcCpuClock & 3;
                        if (oamCorruptPending && (alignment == 1 || alignment == 2))
                            oamCorruptSuppressed = true;

                        if (!oamCorruptSuppressed)
                            ProcessOamCorruption();
                        oamCorruptPending = false;
                        oamCorruptSuppressed = false;

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
            // TriCNES: latch NOT flipped here — deferred to delay handler (line 1302)
            // TriCNES: alignment 0,1,3=1cycle; alignment 2=2cycles
            ppu2005UpdateDelay = ((mcPpuClock & 3) == 2) ? 2 : 1; // TriCNES: phase 2=2, others=1
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ppu_w_2007(byte value)
        {
            openbus = value;
            open_bus_decay_timer = 77777;
            ppu2007SM_writeValue = value;

            // P3-3: TriCNES $2007 write model (lines 9675-9717)
            // TriCNES checks SM==3||6 (single-tick). AprNes double-ticks → SM==6 = 1 CPU cycle.
            if (ppu2007SM == 6) // consecutive access (1 CPU cycle with double-tick)
            {
                ushort addr = (ushort)(vram_addr & 0x3FFF);
                ppu2007SM_mysteryAddr = (ushort)((addr & 0xFF00) | value);

                if (!ppu2007SM_isRead)
                    ppu2007SM_performMysteryWrite = true;
                else
                    ppu2007SM_interruptedReadToWrite = true;
            }
            else
            {
                ppu2007SM_normalWriteBehavior = true;
            }

            if (ppu2007SM != 6)
            {
                if (ppu2007SM >= 9)
                    ppu2007SM = 3;
                else
                    ppu2007SM = 0;
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
