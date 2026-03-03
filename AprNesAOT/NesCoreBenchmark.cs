// NesCoreBenchmark – P/Invoke wrapper for NesCoreNative.dll (Native AOT build)
// Used by AprNesAOT to compare JIT (.NET 8) vs AOT (NesCoreNative.dll) performance.
//
// To produce NesCoreNative.dll:
//   cd ..\NesCoreNative && dotnet publish -r win-x64 -c Release -o bin\publish
//   Copy bin\publish\NesCoreNative.dll alongside AprNesAOT.exe

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace AprNes
{
    public static unsafe class NesCoreBenchmark
    {
        const string DllName = "NesCoreNative.dll";

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "nescore_init")]
        static extern int aot_init(byte* romData, int len);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "nescore_benchmark")]
        static extern int aot_benchmark(int seconds);

        /// <summary>Returns true if NesCoreNative.dll is present next to the executable.</summary>
        public static bool IsAvailable()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DllName);
            return File.Exists(path);
        }

        /// <summary>
        /// Initialise the AOT DLL with romBytes, then run at max speed for
        /// <paramref name="seconds"/> seconds. Returns frame count, or -1 on failure.
        /// This call blocks the calling thread for the duration of the benchmark.
        /// </summary>
        public static int RunAotBenchmark(byte[] romBytes, int seconds)
        {
            try
            {
                fixed (byte* p = romBytes)
                {
                    if (aot_init(p, romBytes.Length) == 0) return -1;
                    return aot_benchmark(seconds);
                }
            }
            catch
            {
                return -1;
            }
        }
    }
}
