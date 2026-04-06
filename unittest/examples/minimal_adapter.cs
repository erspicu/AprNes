using System;
using NesTestFramework;

/// <summary>
/// Minimal IEmulatorCore skeleton.
/// Replace TODO comments with your emulator's API calls.
/// </summary>
public class MyEmulatorAdapter : IEmulatorCore
{
    public void SetRegion(NesRegion region)
    {
        // TODO: Set your emulator's TV region (NTSC/PAL/Dendy)
    }

    public bool LoadRom(byte[] romData)
    {
        // TODO: Parse iNES header + load PRG/CHR into your emulator
        // Return true on success, false on failure
        return false;
    }

    public void RunOneFrame()
    {
        // TODO: Advance your emulator by exactly one frame (VBlank-to-VBlank)
        // This should be synchronous — return only after the frame completes
    }

    public void SoftReset()
    {
        // TODO: Trigger a soft reset (like pressing the NES Reset button)
    }

    public byte ReadCpuMemory(ushort address)
    {
        // TODO: Read a byte from CPU address space ($0000-$FFFF)
        // Must reflect current state (including mapper bank switching)
        return 0;
    }

    public void GetNametable0(byte[] buffer)
    {
        // TODO: Copy 960 bytes of nametable 0 (PPU $2000-$23BF) into buffer
        // These are tile indices, not rendered pixels
    }

    public void GetScreenPixels(uint[] buffer)
    {
        // TODO: Copy 256x240 pixels as 0xAARRGGBB into buffer
        // This is the fully rendered frame (BG + sprites + emphasis)
    }

    public void SetP1Buttons(bool a, bool b, bool select, bool start,
                              bool up, bool down, bool left, bool right)
    {
        // TODO: Set Player 1 controller button states
    }

    public void Dispose()
    {
        // TODO: Clean up emulator resources
    }
}
