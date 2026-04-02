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
            if (Region == RegionType.PAL)
            {
                // Generate PAL palette from 2C07 voltage levels + YUV decoding
                generatePaletteFromVoltages(
                    new float[] { -0.117f, 0.000f, 0.223f, 0.490f }, // lo levels
                    new float[] {  0.306f, 0.543f, 0.741f, 1.000f }, // hi levels
                    true); // YUV decoding
            }
            else
            {
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
        static byte ppu_2007_buffer = 0, ppu_2007_temp = 0;
        // $2007 read cooldown (suppresses rapid consecutive reads, e.g. double_2007_read test)
        static int ppu2007ReadCooldown = 0;

        // $2007 state machine (TriCNES model: PPU_Data_StateMachine) — PLACEHOLDER
        // Full implementation requires MEM.cs lambda refactor
        static int ppu2007SM = 9; // 9 = idle
        static bool ppu2007SM_isRead = false;
        static byte ppu2007SM_writeValue = 0;
        static bool ppu2007SM_bufferLate = false;

        // $2000 delayed control update (TriCNES: PPU_Update2000Delay, 1-2 PPU cycles)
        // NMI enable is immediate; pattern table/sprite size delayed
        static int ppu2000UpdateDelay = 0;
        static byte ppu2000PendingValue = 0;

        // $2001 delayed mask update (TriCNES: PPU_Update2001Delay, 2-3 PPU cycles)
        // _Instant flags set immediately; ShowBackGround/ShowSprites applied after delay
        static int ppu2001UpdateDelay = 0;
        static byte ppu2001PendingValue = 0;

        // $2005 delayed scroll update (TriCNES model: 1-2 PPU dots after CPU write)
        static int ppu2005UpdateDelay = 0;
        static byte ppu2005PendingValue = 0;
        static bool ppu2005PendingIsSecond = false; // true = second write (Y scroll)

        // $2006 delayed t→v copy (TriCNES model: 3 PPU dots after CPU write)
        // Real hardware doesn't update vram_addr immediately on the second $2006 write;
        // there's a ~4-5 PPU dot delay depending on CPU/PPU alignment.
        static int ppu2006UpdateDelay = 0;
        static int ppu2006PendingAddr = 0;
        static byte* spr_ram;
        static public byte* ppu_ram;

        // OAM corruption: when rendering is disabled mid-scanline, the PPU's internal
        // secondary OAM addressing glitches and marks rows for corruption.
        // When rendering re-enables (or at next frame start), first 8 bytes of OAM
        // are copied over each marked row.
        static byte* corruptOamRow; // 32 bytes, 0=clean 1=corrupt
        static bool prevRenderingEnabled = false;
        static public uint* ScreenBuf1x;
        static uint* NesColors; //, targetSize;
        static int* Buffer_BG_array;
        static uint* palCacheR; // 4 pre-computed palette colors for renderAttr
        static uint* palCacheN; // 4 pre-computed palette colors for nextAttr
        static byte spr_ram_add = 0;

        static bool oddSwap = false;
        static bool ppuRenderingEnabled = false; // Delayed rendering enable (Mesen2 model: updated at end of PPU dot)
        static bool nmi_output_prev = false;  // NMI edge detection: previous NMI output level
        static long nmi_delay_cycle = -1;     // CPU cycle that detected NMI edge (-1 = inactive)
                                              // Promotes to nmi_pending when cpuCycleCount > nmi_delay_cycle
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

        // ---- BG shift registers (16-bit, two tiles: high=current, low=next) ----
        static ushort lowshift = 0, highshift = 0;

        // ---- Pre-reload latch for per-dot pixel output (half-step) ----
        // At phase 7, main shift registers are reloaded AFTER tile fetch.
        // Half-step needs PRE-reload data for the last pixel of the tile group.
        static ushort halfStepLow = 0, halfStepHigh = 0;
        static byte halfStepAttrCurrent = 0, halfStepAttrNext = 0;
        static bool halfStepPhase7 = false;

        // ---- Per-dot shifted BG registers for sprite 0 hit (serial in: 0=low, 1=high) ----
        static ushort lowshift_s0 = 0, highshift_s0 = 0;

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
        static void ppu_rendering_tick(int cx, int preRenderLn)
        {
            if (cx < 256 || (cx >= 320 && cx < 336))
            {
                // MMC5 CHR A/B: ensure correct set at BG tile boundaries.
                // Dot 0: initialize for this scanline (handles vblank→render transition).
                // Dot 320: switch back to BG after sprite fetches.
                if ((cx == 0 || cx == 320) && chrABAutoSwitch)
                {
                    byte*[] src = Spritesize8x16 ? (chrBGUseASet ? chrBankPtrsA : chrBankPtrsB) : chrBankPtrsA;
                    for (int i = 0; i < 8; i++) chrBankPtrs[i] = src[i];
                }
                int phase = cx & 7;
                if (phase == 0) {
                    ioaddr = 0x2000 | (vram_addr & 0x0FFF);
                    if (mapperA12IsMmc3) NotifyMapperA12(ioaddr);  // NT addr, A12=0
                } else if (phase == 1) {
                    if (ntChrOverrideEnabled)
                        NTVal = ntBankPtrs[(ioaddr >> 10) & 3][ioaddr & 0x3FF];
                    else
                        NTVal = ppu_ram[CIRAMAddr(ioaddr)];
                    if (extAttrEnabled) extAttrNTOffset = (ushort)(ioaddr & 0x3FF);
                    if (mmc5Ref != null) mmc5Ref.NotifyVramRead(ioaddr);
                } else if (phase == 2) {
                    ioaddr = 0x23C0 | (vram_addr & 0x0C00) | ((vram_addr >> 4) & 0x38) | ((vram_addr >> 2) & 0x07);
                } else if (phase == 3) {
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
                    if (mmc5Ref != null) mmc5Ref.NotifyVramRead(ioaddr);
                } else if (phase == 4) {
                    if (extAttrEnabled && extAttrChrSize > 0)
                        ioaddr = (extAttrChrBank << 12) | (NTVal << 4) | ((vram_addr >> 12) & 7);
                    else
                        ioaddr = BgPatternTableAddr | (NTVal << 4) | ((vram_addr >> 12) & 7);
                    if (mapperNeedsA12) NotifyMapperA12(ioaddr);  // CHR low addr
                } else if (phase == 5) {
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
                    if (mapperNeedsA12 && !mapperA12IsMmc3) NotifyMapperA12(ioaddr);  // MMC2/MMC4: CHR high triggers latch
                } else {
                    if (extAttrEnabled && extAttrChrSize > 0)
                        highTile = extAttrCHR[ioaddr % extAttrChrSize];
                    else
                        highTile = chrBankPtrs[(ioaddr >> 10) & 7][ioaddr & 0x3FF];
                    if (mmc5Ref != null) mmc5Ref.NotifyVramRead(ioaddr);
                    // Save pre-reload state for half-step pixel output at phase 7
                    if (scanline < 240 && cx < 256)
                    {
                        halfStepLow = lowshift;
                        halfStepHigh = highshift;
                        halfStepAttrCurrent = bg_attr_p3;
                        halfStepAttrNext = bg_attr_p2;
                        halfStepPhase7 = true;
                    }
                    // Palette cache update (for compatibility)
                    if (scanline < 240 && cx < 256 && ppuRenderingEnabled)
                        RenderBGTile(cx);
                    // Load shift registers (high = old-low = previous tile, low = new tile)
                    lowshift  = (ushort)((lowshift  << 8) | lowTile);
                    highshift = (ushort)((highshift << 8) | highTile);
                    // Sync per-dot shadow registers: load new tile into low byte
                    // (the per-dot shifting already shifted the old data up by 8 bits)
                    lowshift_s0  = (ushort)((lowshift_s0  & 0xFF00) | lowTile);
                    highshift_s0 = (ushort)((highshift_s0 & 0xFF00) | highTile);
                    CXinc();
                }
            }
            else if (cx == 256)
            {
                Yinc();
            }
            else if (cx >= 257 && cx < 320)
            {
                if (cx == 257)
                {
                    CopyHoriV(); spr_ram_add = 0;
                    // MMC5 CHR A/B: switch to A set (sprites) at dot 257 (only in 8x16 mode)
                    if (chrABAutoSwitch && Spritesize8x16)
                        for (int i = 0; i < 8; i++) chrBankPtrs[i] = chrBankPtrsA[i];
                }

                // Latch sprite size at dot 261 (sprite 0 CHR low fetch address).
                // On real hardware, the Spritesize8x16 value at CHR fetch time determines
                // tile addressing. Mid-HBlank $2000 writes after this dot won't affect
                // the current scanline's sprite 0 tile data.
                if (cx == 261) spriteSizeLatchedForFetch = Spritesize8x16;

                if (mapperA12IsMmc3)
                {
                    int phase = (cx - 257) & 7;
                    if (phase == 0) NotifyMapperA12(0x2000);                // garbage NT addr, A12=0
                    else if (phase == 3) NotifyMapperA12(SpPatternTableAddr); // sprite CHR addr (pre-output)
                }
                else if (mapperNeedsA12 && !mapperA12IsMmc3)
                {
                    // MMC2/MMC4: per-sprite CHR high address for right-latch detection
                    int phase9 = (cx - 257) & 7;
                    int slot9  = (cx - 257) >> 3;
                    if (phase9 == 0) NotifyMapperA12(0x2000);
                    else if (phase9 == 5 && slot9 < 8)
                    {
                        byte tileNum = secondaryOAM[slot9 * 4 + 1];
                        NotifyMapperA12(SpPatternTableAddr | (tileNum << 4) | 8);
                    }
                }
                // MMC5: per-fetch VRAM read notifications for sprite phase
                if (mmc5Ref != null)
                {
                    int phaseM5 = (cx - 257) & 7;
                    if (phaseM5 == 1) mmc5Ref.NotifyVramRead(0x2000);        // garbage NT
                    else if (phaseM5 == 3) mmc5Ref.NotifyVramRead(0x23C0);   // garbage AT
                    else if (phaseM5 == 5) mmc5Ref.NotifyVramRead(SpPatternTableAddr);       // CHR low
                    else if (phaseM5 == 7) mmc5Ref.NotifyVramRead(SpPatternTableAddr | 8);   // CHR high
                }
            }

            // Dot 321: latch secondary OAM[0] into oamCopyBuffer for dots 321-340
            if (cx == 321 && scanline < 240 && (ShowBackGround_Instant || ShowSprites_Instant))
                oamCopyBuffer = secondaryOAM[0];

            // Pre-render scanline: continuous vert(v) = vert(t) copy at cycles 280-304
            if (scanline == preRenderLn && cx >= 280 && cx <= 304)
            {
                vram_addr = (vram_addr & ~0x7BE0) | (vram_addr_internal & 0x7BE0);
            }

            // Garbage NT fetches at dots 336-339: notify A12=0 to create falling edge
            // after BG prefetch CHR (A12=1 for BG=$1000), needed for scanline-boundary timing
            if (mapperNeedsA12 && cx == 336)
                NotifyMapperA12(0x2000);

            // MMC5: garbage NT fetches at dots 337 and 339 (same NT address as first tile)
            // These create the 3-consecutive-identical-read pattern for scanline detection
            if (mmc5Ref != null && (cx == 337 || cx == 339))
                mmc5Ref.NotifyVramRead(0x2000 | (vram_addr & 0x0FFF));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void NotifyMapperA12(int address)
        {
            MapperObj.NotifyA12(address, scanline * 341 + ppu_cycles_x);
        }

        // ── Half-step: runs AFTER each full ppu_step (mid-dot) ──
        // TriCNES model: _EmulateHalfPPU() at PPUClock==2
        // Handles: per-dot pixel output from shift registers, fine-grained register delays
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ppu_half_step()
        {
            // VBL half-dot latch: promote pending → actual (TriCNES: PPU_PendingVBlank → PPUStatus_VBlank)
            if (pendingVblank)
            {
                pendingVblank = false;
                isVblank = true;
            }

            // Sprite 0 hit half-dot latch: promote pending → actual
            if (pendingSprite0Hit)
            {
                pendingSprite0Hit = false;
                isSprite0hit = true;
            }

            int cx = ppu_cycles_x - 1; // cx is the dot we just completed in ppu_step (already incremented)
            if (cx < 0 || cx >= 256 || scanline < 0 || scanline >= 240)
                return;

            // Per-dot BG pixel output using main shift registers
            // Phase 7 uses pre-reload latch (saved before shift register reload in full-step)
            if (ppuRenderingEnabled && ShowBackGround)
            {
                ushort ls, hs;
                byte attrCur, attrNxt;
                if (halfStepPhase7)
                {
                    ls = halfStepLow;
                    hs = halfStepHigh;
                    attrCur = halfStepAttrCurrent;
                    attrNxt = halfStepAttrNext;
                    halfStepPhase7 = false;
                }
                else
                {
                    ls = lowshift;
                    hs = highshift;
                    attrCur = bg_attr_p3;
                    attrNxt = bg_attr_p2;
                }

                int dotInGroup = cx & 7;
                int bit = 15 - dotInGroup - FineX;
                int bgPixel = ((ls >> bit) & 1) | (((hs >> bit) & 1) << 1);
                byte attrUse = (bit >= 8) ? attrCur : attrNxt;

                bool masked = !ShowBgLeft8 && cx < 8;
                int slot = (scanline << 8) + cx;
                Buffer_BG_array[slot] = masked ? 0 : bgPixel;

                int baseAddr = 0x3f00 | (attrUse << 2);
                uint bgColor = NesColors[ppu_ram[0x3f00] & 0x3f];
                uint color = (masked || bgPixel == 0)
                    ? bgColor
                    : NesColors[ppu_ram[baseAddr + bgPixel] & 0x3f];
                ScreenBuf1x[slot] = color;

                // NTSC analog: write palette index
                if (AnalogEnabled)
                {
                    ntscScanBuf[cx] = (masked || bgPixel == 0)
                        ? (byte)(ppu_ram[0x3f00] & 0x3f)
                        : (byte)(ppu_ram[baseAddr + bgPixel] & 0x3f);
                }

                // Per-dot sprite compositing (replaces batch Pass 3 in RenderSpritesLine)
                if (sprLineReady && sprLineSet[cx] != 0 && ShowSprites)
                {
                    // Sprite visible if: BG disabled, OR BG pixel transparent, OR sprite is front-priority
                    if (!ShowBackGround || Buffer_BG_array[slot] == 0 || sprLinePri[cx] == 0)
                    {
                        ScreenBuf1x[slot] = sprLineBuf[cx];
                        if (AnalogEnabled) ntscScanBuf[cx] = sprLinePalIdx[cx];
                    }
                }
            }
            else
                halfStepPhase7 = false; // discard latch if rendering disabled

            // End of visible scanline: trigger NTSC decode and reset sprite buffer
            if (cx == 255)
            {
                if (AnalogEnabled)
                    DecodeScanline(scanline, ntscScanBuf, ppuEmphasis);
                sprLineReady = false;
            }
        }

        // ── Region-specialized PPU step functions ──
        // Common logic extracted into ppu_step_common + ppu_step_rendering (AggressiveInlining).
        // Region-specific tail (VBL trigger, flag clear, dot skip, scanline wrap) hardcoded per version.
        // Search "★ REGION" for all difference points.

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ppu_step_common(out int cx, out bool renderingEnabled)
        {
            // $2007 read cooldown
            if (ppu2007ReadCooldown > 0) ppu2007ReadCooldown--;

            // $2007 state machine — PLACEHOLDER (requires MEM.cs lambda refactor)

            // $2006 delayed t→v copy
            if (ppu2006UpdateDelay > 0 && --ppu2006UpdateDelay == 0)
            {
                vram_addr = ppu2006PendingAddr;
                if (mapperNeedsA12) NotifyMapperA12(vram_addr);
            }

            // $2005 delayed scroll update
            if (ppu2005UpdateDelay > 0 && --ppu2005UpdateDelay == 0)
            {
                byte v = ppu2005PendingValue;
                if (ppu2005PendingIsSecond)
                {
                    scrol_y = v & 7;
                    vram_addr_internal = (vram_addr_internal & 0x0C1F) | ((v & 0x7) << 12) | ((v & 0xF8) << 2);
                }
                else
                {
                    vram_addr_internal = (vram_addr_internal & 0x7fe0) | ((v & 0xf8) >> 3);
                    FineX = v & 0x07;
                }
            }

            // $2000 delayed control update (pattern table, sprite size)
            if (ppu2000UpdateDelay > 0 && --ppu2000UpdateDelay == 0)
            {
                SpPatternTableAddr = ((ppu2000PendingValue & 8) > 0) ? 0x1000 : 0;
                BgPatternTableAddr = ((ppu2000PendingValue & 0x10) > 0) ? 0x1000 : 0;
                Spritesize8x16 = ((ppu2000PendingValue & 0x20) > 0);
            }

            // $2001 delayed mask update (Tier 2: ShowBackGround/ShowSprites)
            if (ppu2001UpdateDelay > 0 && --ppu2001UpdateDelay == 0)
            {
                ShowBgLeft8    = (ppu2001PendingValue & 0x02) != 0;
                ShowSprLeft8   = (ppu2001PendingValue & 0x04) != 0;
                ShowBackGround = (ppu2001PendingValue & 0x08) != 0;
                ShowSprites    = (ppu2001PendingValue & 0x10) != 0;
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

            // At dot 0 of visible scanlines: precompute sprite 0 data for hit detection.
            // Must run BEFORE the hit check so sprite0_on_line is valid at dot 0.
            // On real hardware, sprite evaluation happens during the previous scanline.
            if (scanline >= 0 && scanline < 240 && cx == 0)
                PrecomputeSprite0Line();

            // Per-pixel sprite 0 hit detection using per-dot shifted shadow registers.
            // Condition order: cheapest short-circuits first.
            // sprite0_on_line / !isSprite0hit eliminate most scanlines instantly.
            // Range check (cx in [sprite0_line_x, +8)) narrows to 8 dots from 256,
            // avoiding the inner sprCol calculation for the other 248 dots per scanline.
            if (sprite0_on_line && !isSprite0hit && !pendingSprite0Hit
                && cx >= sprite0_line_x && cx < sprite0_line_x + 8
                && cx < 256
                && scanline >= 0 && scanline < 240
                && ShowBackGround && ShowSprites)
            {
                bool inLeft8 = cx < 8;
                if (!(!ShowBgLeft8 && inLeft8) && !(!ShowSprLeft8 && inLeft8) && cx != 255)
                {
                    int sprCol = cx - sprite0_line_x;
                    int bit = 15 - FineX;
                    int bgPixel = ((lowshift_s0 >> bit) & 1) | (((highshift_s0 >> bit) & 1) << 1);
                    if (bgPixel != 0)
                    {
                        int loc_t = sprite0_flip_x ? (7 - sprCol) : sprCol;
                        int mask = 1 << (7 - loc_t);
                        int sprPx = (((sprite0_tile_high & mask) << 1) + (sprite0_tile_low & mask)) >> (7 - loc_t);
                        if (sprPx != 0)
                            pendingSprite0Hit = true; // promoted in half-step
                    }
                }
            }
        }

        // ★ REGION: shared rendering body — PRE_RENDER_LINE passed as hardcoded literal from each version
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ppu_step_rendering(int cx, bool renderingEnabled, int PRE_RENDER_LINE)
        {
            if (ppuRenderingEnabled)
            {
                if ((scanline >= 0 && scanline < 240 && cx < 256)
                    || ((scanline < 240 || scanline == PRE_RENDER_LINE) // ★ REGION
                        && cx >= 320 && cx < 336))
                {
                    lowshift_s0 <<= 1;
                    highshift_s0 = (ushort)((highshift_s0 << 1) | 1);
                }
            }

            if (scanline < 240 || scanline == PRE_RENDER_LINE) // ★ REGION
            {
                if (ppuRenderingEnabled)
                    ppu_rendering_tick(cx, PRE_RENDER_LINE); // ★ REGION

                // Per-dot sprite evaluation (visible scanlines only)
                if (AccuracyOptA)
                {
                    if (scanline >= 0 && scanline < 240 && ppuRenderingEnabled)
                    {
                        // Dots 1-64: clear secondary OAM (write $FF, 2 dots per byte)
                        if (cx >= 1 && cx <= 64)
                        {
                            oamCopyBuffer = 0xFF;
                            if ((cx & 1) == 0)
                                secondaryOAM[(cx >> 1) - 1] = 0xFF;
                        }
                        // Dot 65: initialize evaluation FSM
                        else if (cx == 65)
                        {
                            sprite0_eval_addr = spr_ram_add;
                            SpriteEvalInit();
                            SpriteEvalTick();
                        }
                        // Dots 66-256: per-dot evaluation
                        else if (cx >= 66 && cx <= 256)
                        {
                            SpriteEvalTick();
                            if (cx == 256) SpriteEvalEnd();
                        }
                    }
                    // Pre-render line: save sprite0_eval_addr at dot 65
                    else if (scanline == PRE_RENDER_LINE && cx == 65 && ppuRenderingEnabled) // ★ REGION
                    {
                        sprite0_eval_addr = spr_ram_add;
                    }
                }

                if (scanline >= 0 && scanline < 240)
                {
                    // At start of each visible scanline: always zero Buffer_BG_array to prevent
                    // stale data from prior frames causing incorrect sprite priority decisions.
                    if (cx == 0)
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
                        // Pre-fill sprite pixel buffer for per-dot compositing in half-step
                        if (mmc5Ref != null) mmc5Ref.PreSpriteRender();
                        RenderSpritesLine();
                    }

                    // Per-cycle sprite overflow flag
                    if (spriteOverflowCycle >= 0 && cx == spriteOverflowCycle)
                        isSpriteOverflow = true;

                }

                // Pre-render line: compute pre-render sprite data at dot 257
                if (scanline == PRE_RENDER_LINE && cx == 257 && ppuRenderingEnabled) // ★ REGION
                    PrecomputePreRenderSprites();

            }

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

        // NTSC: nmiTriggerLine=241, preRenderLine=261
        private const int L_NTSC_VBL_START    = 0x1E201; // 241<<9|1  VBlank set + NMI
        private const int L_NTSC_SPRITE_RESET = 0x20A01; // 261<<9|1  clear sprite0hit/overflow
        private const int L_NTSC_VBL_END      = 0x20A02; // 261<<9|2  VBlank clear

        // PAL: nmiTriggerLine=241, preRenderLine=311
        private const int L_PAL_VBL_START     = 0x1E201; // 241<<9|1  (same as NTSC)
        private const int L_PAL_SPRITE_RESET  = 0x26E01; // 311<<9|1
        private const int L_PAL_VBL_END       = 0x26E02; // 311<<9|2

        // Dendy: nmiTriggerLine=291, preRenderLine=311
        private const int L_DENDY_VBL_START   = 0x24601; // 291<<9|1
        private const int L_DENDY_SPRITE_RESET= 0x26E01; // 311<<9|1  (same as PAL)
        private const int L_DENDY_VBL_END     = 0x26E02; // 311<<9|2  (same as PAL)

        // ═══════════════════════════════════════════════════════════════
        // NTSC: preRenderLine=261, nmiTriggerLine=241, totalScanlines=262, has dot skip
        // ═══════════════════════════════════════════════════════════════
        static void ppu_step_ntsc()
        {
            int cx; bool re;
            ppu_step_common(out cx, out re);
            ppu_step_rendering(cx, re, 261);
            ppu_cycles_x = ++cx;

            // ★ Scanline event guard: cx<=2 時才可能觸發 VBL/Sprite/VBL-end 事件
            //   339/341 dots 完全跳過，僅 dot 1 & 2 進入內部判定
            if (cx <= 2)
            {
                int L = (scanline << 9) | cx;
                if (L == L_NTSC_VBL_START)         // scanline 241, dot 1
                { if (!SuppressVbl) pendingVblank = true; SuppressVbl = false; }
                else if (L == L_NTSC_SPRITE_RESET) // scanline 261, dot 1
                    { isSprite0hit = isSpriteOverflow = false; pendingSprite0Hit = false; }
                else if (L == L_NTSC_VBL_END)      // scanline 261, dot 2
                    isVblank = false;
            }

            // NTSC odd frame dot skip (pre-render line 261, dot 339)
            if (scanline == 261 && cx == 339)
            {
                oddSwap = !oddSwap;
                if (!oddSwap && (ShowBackGround_Instant || ShowSprites_Instant))
                {
                    if (mmc5Ref != null)
                        mmc5Ref.NotifyVramRead(0x2000 | (vram_addr & 0x0FFF));
                    ppu_cycles_x = ++cx;
                }
            }
            if (cx == 341)
            {
                if (++scanline == 262)
                { scanline = 0; if (ShowBackGround_Instant || ShowSprites_Instant) ProcessOamCorruption(); }
                ppu_cycles_x = 0;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // PAL: preRenderLine=311, nmiTriggerLine=241, totalScanlines=312, no dot skip
        // ═══════════════════════════════════════════════════════════════
        static void ppu_step_pal()
        {
            int cx; bool re;
            ppu_step_common(out cx, out re);
            ppu_step_rendering(cx, re, 311);
            ppu_cycles_x = ++cx;

            if (cx <= 2)
            {
                int L = (scanline << 9) | cx;
                if (L == L_PAL_VBL_START)         // scanline 241, dot 1
                { if (!SuppressVbl) pendingVblank = true; SuppressVbl = false; }
                else if (L == L_PAL_SPRITE_RESET) // scanline 311, dot 1
                    { isSprite0hit = isSpriteOverflow = false; pendingSprite0Hit = false; }
                else if (L == L_PAL_VBL_END)      // scanline 311, dot 2
                    isVblank = false;
            }
            // PAL — no dot skip
            if (cx == 341)
            {
                if (++scanline == 312)
                { scanline = 0; if (ShowBackGround_Instant || ShowSprites_Instant) ProcessOamCorruption(); }
                ppu_cycles_x = 0;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Dendy: preRenderLine=311, nmiTriggerLine=291, totalScanlines=312, no dot skip
        // ═══════════════════════════════════════════════════════════════
        static void ppu_step_dendy()
        {
            int cx; bool re;
            ppu_step_common(out cx, out re);
            ppu_step_rendering(cx, re, 311);
            ppu_cycles_x = ++cx;

            if (cx <= 2)
            {
                int L = (scanline << 9) | cx;
                if (L == L_DENDY_VBL_START)         // scanline 291, dot 1
                { if (!SuppressVbl) pendingVblank = true; SuppressVbl = false; }
                else if (L == L_DENDY_SPRITE_RESET) // scanline 311, dot 1
                    { isSprite0hit = isSpriteOverflow = false; pendingSprite0Hit = false; }
                else if (L == L_DENDY_VBL_END)      // scanline 311, dot 2
                    isVblank = false;
            }
            // Dendy — no dot skip
            if (cx == 341)
            {
                if (++scanline == 312)
                { scanline = 0; if (ShowBackGround_Instant || ShowSprites_Instant) ProcessOamCorruption(); }
                ppu_cycles_x = 0;
            }
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
        static byte oamCopyBuffer;                  // Last byte read during evaluation ($2004 returns this)
        static byte spriteEvalAddrH;                // Primary OAM sprite index (0-63)
        static byte spriteEvalAddrL;                // Byte offset within sprite (0-3)
        static byte secOAMAddr;                     // Write position in secondary OAM (0-31)
        static bool spriteInRange;                  // Current sprite Y is in range
        static bool sprite0Added;                   // First evaluated sprite was in range
        static bool oamCopyDone;                    // Evaluation wrapped around all 64 sprites
        static byte overflowBugCounter;             // For sprite overflow hardware bug
        static int evalSpriteCount;                 // Number of sprites found (0-8)
        static bool evalSprite0Visible;             // Sprite 0 found in secondary OAM

        // Pre-render line sprite data (loaded at pre-render dot 257 for scanline 0)
        static bool prerender_sprite0_valid;
        static int prerender_sprite0_x;
        static byte prerender_sprite0_tile_low, prerender_sprite0_tile_high;
        static bool prerender_sprite0_flip_x;

        // Pre-computed sprite overflow cycle for cycle-accurate overflow flag timing
        static int spriteOverflowCycle;

        // OAM corruption: mark which row gets corrupted when rendering is disabled mid-scanline
        static void SetOamCorruptionFlags()
        {
            if (ppu_cycles_x >= 0 && ppu_cycles_x < 64)
            {
                // Secondary OAM clear phase: every 2 dots shifts corruption down 1 row
                corruptOamRow[ppu_cycles_x >> 1] = 1;
            }
            else if (ppu_cycles_x >= 256 && ppu_cycles_x < 320)
            {
                // Sprite tile fetch phase: 8-dot segments
                int rel = ppu_cycles_x - 256;
                int baseIdx = rel >> 3;
                int offset = rel & 0x07;
                if (offset > 3) offset = 3;
                corruptOamRow[baseIdx * 4 + offset] = 1;
            }
        }

        // OAM corruption: copy first 8 bytes of OAM over each marked row
        // Optimized: single long (8-byte) read/write replaces inner byte loop
        static unsafe void ProcessOamCorruption()
        {
            long sourcePattern = *(long*)spr_ram; // row 0: first 8 bytes
            for (int i = 1; i < 32; i++)
            {
                if (corruptOamRow[i] != 0)
                {
                    ((long*)spr_ram)[i] = sourcePattern;
                    corruptOamRow[i] = 0;
                }
            }
            corruptOamRow[0] = 0;
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
            int evalCycle = 65;
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
            secOAMAddr = 0;
            overflowBugCounter = 0;
            oamCopyDone = false;
            spriteEvalAddrH = (byte)((spr_ram_add >> 2) & 0x3F);
            spriteEvalAddrL = (byte)(spr_ram_add & 0x03);
        }

        // Per-dot sprite evaluation: odd dots read, even dots write/check
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SpriteEvalTick()
        {
            bool isOdd = (ppu_cycles_x & 1) != 0;

            if (isOdd)
            {
                // Odd cycle: read from primary OAM
                oamCopyBuffer = spr_ram[(byte)(spriteEvalAddrH * 4 + spriteEvalAddrL)];
                // Attribute byte bits 2-4 don't exist in hardware; masked on internal bus
                if (spriteEvalAddrL == 2) oamCopyBuffer &= 0xE3;
            }
            else
            {
                // Even cycle: write/check
                SpriteEvalWrite();
            }
        }

        static void SpriteEvalWrite()
        {
            int height = Spritesize8x16 ? 16 : 8;

            if (secOAMAddr >= 0x20)
            {
                // Secondary OAM full
                oamCopyBuffer = secondaryOAM[secOAMAddr & 0x1F]; // read instead of write

                if (oamCopyDone)
                {
                    // Already found 8+ sprites, just advance
                    spriteEvalAddrH = (byte)((spriteEvalAddrH + 1) & 0x3F);
                    spriteEvalAddrL = 0;
                }
                else if (spriteInRange)
                {
                    // Found 9th+ sprite: overflow flag set by PrecomputeOverflow() at exact cycle
                    spriteEvalAddrL++;
                    if (spriteEvalAddrL >= 4)
                    {
                        spriteEvalAddrH = (byte)((spriteEvalAddrH + 1) & 0x3F);
                        spriteEvalAddrL = 0;
                    }
                    if (overflowBugCounter == 0)
                        overflowBugCounter = 3;
                    else if (--overflowBugCounter == 0)
                    {
                        oamCopyDone = true;
                        spriteEvalAddrL = 0;
                    }
                    spriteInRange = false; // reset for next check
                }
                else
                {
                    // Check in-range for overflow bug (reads wrong byte offset)
                    if (scanline >= oamCopyBuffer && scanline < oamCopyBuffer + height)
                        spriteInRange = true;
                    // Advance both H and L (hardware bug: L increments on miss)
                    spriteEvalAddrH = (byte)((spriteEvalAddrH + 1) & 0x3F);
                    spriteEvalAddrL = (byte)((spriteEvalAddrL + 1) & 0x03);
                    if (spriteEvalAddrH == 0) oamCopyDone = true;
                }
            }
            else
            {
                // Check in-range if not already tracking
                if (!spriteInRange)
                {
                    if (scanline >= oamCopyBuffer && scanline < oamCopyBuffer + height)
                        spriteInRange = !oamCopyDone;
                }

                if (oamCopyDone)
                {
                    // All 64 sprites checked, fewer than 8 in range:
                    // even dots read from secondary OAM into oamCopyBuffer (no write)
                    oamCopyBuffer = secondaryOAM[secOAMAddr];
                    // Still advance through primary OAM (hardware continues cycling)
                    spriteEvalAddrH = (byte)((spriteEvalAddrH + 1) & 0x3F);
                    spriteEvalAddrL = 0;
                }
                else
                {
                    // Write to secondary OAM (attribute already masked at read time)
                    secondaryOAM[secOAMAddr] = oamCopyBuffer;

                    if (spriteInRange)
                    {
                        // First in-range sprite at the very first evaluation is sprite 0
                        if (ppu_cycles_x == 66) sprite0Added = true;

                        spriteEvalAddrL++;
                        secOAMAddr++;

                        if (spriteEvalAddrL >= 4)
                        {
                            spriteEvalAddrH = (byte)((spriteEvalAddrH + 1) & 0x3F);
                            spriteEvalAddrL = 0;
                            if (spriteEvalAddrH == 0) oamCopyDone = true;
                        }

                        if ((secOAMAddr & 0x03) == 0)
                        {
                            // Done copying 4 bytes of this sprite
                            spriteInRange = false;
                        }
                    }
                    else
                    {
                        // Not in range: skip to next sprite
                        spriteEvalAddrH = (byte)((spriteEvalAddrH + 1) & 0x3F);
                        spriteEvalAddrL = 0;
                        if (spriteEvalAddrH == 0) oamCopyDone = true;
                    }
                }
            }

            // Update primary OAM read address (for $2003 visibility)
            spr_ram_add = (byte)((spriteEvalAddrL & 0x03) | (spriteEvalAddrH << 2));
        }

        // Finalize evaluation at dot 256
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SpriteEvalEnd()
        {
            evalSprite0Visible = sprite0Added;
            evalSpriteCount = (secOAMAddr + 3) >> 2;
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
        static unsafe void RenderSpritesLine()
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

            // Compositing moved to ppu_half_step (per-dot)
            sprLineReady = true;

            // NTSC analog: still need to decode after all pixels are composited.
            // This will be triggered at end-of-scanline in half-step or at cx==256.
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

        static bool SuppressVbl = false;
        static bool pendingVblank = false; // half-dot VBL latch (TriCNES: PPU_PendingVBlank)
        static bool pendingSprite0Hit = false; // half-dot sprite 0 hit latch (TriCNES: PPUStatus_PendingSpriteZeroHit)

        // Per-dot sprite compositing buffers (filled at cx==257, consumed per-dot in half-step)
        static uint* sprLineBuf;     // 256 entries: winning sprite color (NesColors[])
        static byte* sprLinePri;     // 256 entries: priority (0=front, 1=behind BG)
        static byte* sprLineSet;     // 256 entries: 1=sprite pixel present, 0=empty
        static byte* sprLinePalIdx;  // 256 entries: raw palette index (for NTSC analog)
        static bool sprLineReady = false; // true after RenderSpritesLine fills buffers
        //ref http://wiki.nesdev.com/w/index.php/PPU_scrolling
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static byte ppu_r_2002()
        {
            bool vblFlag = isVblank;

            // VBL suppression at exact VBL set dot (sl=nmiTriggerLine, cx=1):
            // VBL was just set by tick; suppress flag read and NMI
            if (scanline == nmiTriggerLine && ppu_cycles_x == 1)
            {
                vblFlag = false;
            }

            openbus = (byte)((vblFlag ? 0x80 : 0) | ((isSprite0hit) ? 0x40 : 0) | ((isSpriteOverflow) ? 0x20 : 0) | (openbus & 0x1f));

            isVblank = false;
            nmi_delay_cycle = -1;      // Cancel not-yet-promoted NMI (same-cycle $2002 read)
            nmi_output_prev = false;   // Reset edge state to prevent false rising edge on next tick
            // Note: nmi_pending is NOT cleared — once promoted, $2002 can't cancel it
            vram_latch = false;
            return openbus;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static byte ppu_r_2007()
        {
            if (ppu2007ReadCooldown > 0)
                return openbus; // suppress rapid consecutive $2007 reads
            byte result = ppu_read_fun[vram_addr](vram_addr);
            ppu2007ReadCooldown = 6;
            return result;
        }

        static byte openbus;
        static public byte cpubus;  // CPU data bus value (last byte read/written by CPU)

        static void ppu_w_2000(byte value)
        {
            openbus = value;

            // Immediate: nametable bits (for t register) and NMI enable
            vram_addr_internal = (ushort)((vram_addr_internal & 0x73ff) | ((value & 3) << 10));
            BaseNameTableAddr = 0x2000 | ((value & 3) << 10);
            VramaddrIncrement = ((value & 4) > 0) ? 32 : 1;
            NMIable = ((value & 0x80) > 0);

            // NMI edge detection (immediate — critical for NMI timing)
            bool nmi_output = isVblank && NMIable;
            if (!nmi_output && nmi_output_prev)
            {
                nmi_delay_cycle = -1;
                nmi_output_prev = false;
            }

            // Delayed: pattern table addresses, sprite size
            // TriCNES: alignment 0,1=2cycles; alignment 2,3=1cycle
            ppu2000UpdateDelay = (ppu_cycles_x % 3 == 2) ? 1 : 2;
            ppu2000PendingValue = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ppu_w_2001(byte value)
        {
            openbus = value;

            // Tier 1: Instant flags — take effect immediately
            ShowBackGround_Instant = (value & 0x08) != 0;
            ShowSprites_Instant    = (value & 0x10) != 0;
            ppuEmphasis = (byte)((value >> 5) & 0x7);
            if (Region != RegionType.NTSC)
                ppuEmphasis = (byte)((ppuEmphasis & 0x4) | ((ppuEmphasis & 1) << 1) | ((ppuEmphasis >> 1) & 1));

            // OAM corruption uses instant flags
            bool newRenderingInstant = ShowBackGround_Instant || ShowSprites_Instant;
            if (prevRenderingEnabled != newRenderingInstant && scanline >= 0 && scanline < 240)
            {
                if (newRenderingInstant)
                {
                    ProcessOamCorruption();
                    if (!sprite0_on_line && !isSprite0hit && ppu_cycles_x > 0)
                    {
                        PrecomputeSprite0Line();
                        if (sprite0_on_line)
                            sprite0_line_x = ppu_cycles_x;
                    }
                }
                else
                    SetOamCorruptionFlags();
            }
            prevRenderingEnabled = newRenderingInstant;

            // Tier 2: Delayed flags — applied after 2-3 PPU cycles
            // TriCNES: alignment 0,1,3=2cycles; alignment 2=3cycles
            ppu2001UpdateDelay = (ppu_cycles_x % 3 == 2) ? 3 : 2;
            ppu2001PendingValue = value;
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
                spr_ram[spr_ram_add++] = value;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static byte ppu_r_2004()
        {
            byte val;
            bool renderingOn = ShowBackGround_Instant || ShowSprites_Instant;
            if (scanline >= 0 && scanline < 240 && renderingOn)
            {
                // Timing offset: tick() processes 3 PPU dots BEFORE the bus read,
                // so at cx=N the last processed dot was N-1. Shift ranges by +1.
                if (ppu_cycles_x >= 2 && ppu_cycles_x <= 65)
                {
                    // Secondary OAM clear phase (hardware dots 1-64): always $FF
                    val = 0xFF;
                }
                else if (ppu_cycles_x >= 66 && ppu_cycles_x <= 257)
                {
                    // Sprite evaluation phase (hardware dots 65-256): last byte read from primary OAM
                    val = oamCopyBuffer;
                }
                else if (ppu_cycles_x >= 258 && ppu_cycles_x <= 321)
                {
                    // Sprite tile fetch (hardware dots 257-320): reads from secondary OAM
                    // Pattern per 8-dot group: Y, tile, attr, X, X, X, X, X
                    int offset = ppu_cycles_x - 258;
                    int spriteIdx = offset >> 3;
                    int step = offset & 0x07;
                    int byteIdx = step > 3 ? 3 : step;
                    val = secondaryOAM[spriteIdx * 4 + byteIdx];
                }
                else
                {
                    // Dots 0-1 and 322-340: return oamCopyBuffer
                    val = oamCopyBuffer;
                }
            }
            else
            {
                val = spr_ram[spr_ram_add];
                if ((spr_ram_add & 3) == 2) val &= 0xE3; // mask unimplemented bits of attribute byte
            }
            open_bus_decay_timer = 77777;
            return openbus = val;
        }

        static void ppu_w_2005(byte value) //ok
        {
            openbus = value;
            // Delayed scroll update (TriCNES: PPU_Update2005Delay = 1-2 cycles)
            ppu2005PendingValue = value;
            ppu2005PendingIsSecond = vram_latch;
            // TriCNES: alignment 0,1,3=1cycle; alignment 2=2cycles
            ppu2005UpdateDelay = (ppu_cycles_x % 3 == 2) ? 2 : 1;
            vram_latch = !vram_latch;
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
                ppu2006UpdateDelay = (ppu_cycles_x % 3 == 2) ? 5 : 4;
            }
            vram_latch = !vram_latch;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ppu_w_2007(byte value)
        {
            // Existing lambda handles write + increment
            open_bus_decay_timer = 77777;
            ppu_write_fun[vram_addr](value);
        }

        static void ppu_w_4014(byte value)//DMA , fixex 2017.01.16 pass sprite_ram test
        {
            // Set OAM DMA flags — deferred to next read cycle via ProcessPendingDma()
            spriteDmaTransfer = true;
            spriteDmaOffset = value;
            dmaNeedHalt = true;
        }
    }
}
