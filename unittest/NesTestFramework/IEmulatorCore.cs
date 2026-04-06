using System;

namespace NesTestFramework
{
    public enum NesRegion { NTSC, PAL, Dendy }

    public enum NesButton { A = 0, B = 1, Select = 2, Start = 3, Up = 4, Down = 5, Left = 6, Right = 7 }

    /// <summary>
    /// Interface that any NES emulator must implement to use the test framework.
    /// All methods are called from the test runner's thread.
    /// </summary>
    public interface IEmulatorCore : IDisposable
    {
        /// <summary>Set the TV region before loading a ROM.</summary>
        void SetRegion(NesRegion region);

        /// <summary>Load a ROM from raw bytes. Returns true on success.</summary>
        bool LoadRom(byte[] romData);

        /// <summary>Advance emulation by exactly one frame (one VBlank-to-VBlank).</summary>
        void RunOneFrame();

        /// <summary>Trigger a soft reset (equivalent to pressing the Reset button).</summary>
        void SoftReset();

        /// <summary>
        /// Read a byte from the CPU address space.
        /// Must support at least $0000-$FFFF range.
        /// Used for $6000 blargg protocol and $F0 fallback.
        /// </summary>
        byte ReadCpuMemory(ushort address);

        /// <summary>
        /// Copy nametable 0 data (960 bytes: 30 rows x 32 columns) into the buffer.
        /// Data comes from PPU VRAM at $2000-$23BF (before attribute table).
        /// Buffer is pre-allocated to at least 960 bytes.
        /// </summary>
        void GetNametable0(byte[] buffer);

        /// <summary>
        /// Copy current screen pixels into the buffer as 0xAARRGGBB uint values.
        /// Buffer is pre-allocated to at least 256*240 = 61440 entries.
        /// </summary>
        void GetScreenPixels(uint[] buffer);

        /// <summary>
        /// Set Player 1 button states. Called before each RunOneFrame().
        /// </summary>
        void SetP1Buttons(bool a, bool b, bool select, bool start,
                          bool up, bool down, bool left, bool right);
    }
}
