using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
using EnigmaBenchmark.Core;
using EnigmaBenchmark.Crackers;

namespace EnigmaBenchmarkAvalonia;

/// <summary>
/// Hosts the GPU cracker run. Avalonia only exposes the GrContext inside a
/// render callback via ISkiaSharpApiLease, so we:
///   1. Stash cracker inputs + a TaskCompletionSource
///   2. Trigger InvalidateVisual() to schedule a render frame
///   3. In the DrawOp, lease the context, run the right cracker, publish result
///   4. Resolve the TCS so the caller can await
///
/// One BenchmarkControl handles both M3 and M4 runs — `_pending` is a discriminated
/// union of request shapes so the DrawOp can pick the correct cracker.
/// </summary>
public class BenchmarkControl : Control
{
    enum Cipher { M3, M4, Lorenz }

    sealed class Request
    {
        public Cipher Cipher;
        public byte[] Ciphertext = Array.Empty<byte>();
        public EnigmaM3 M3Parts;
        public EnigmaM4 M4Parts;
        public byte[][] LorenzChiPins = Array.Empty<byte[]>();
        public CrackScope Scope;
        public TaskCompletionSource<CrackResult> Tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    volatile Request? _pending;

    public Task<CrackResult> RunGpuAsync(byte[] ciphertext, EnigmaM3 fixedParts, CrackScope scope)
    {
        var req = new Request
        {
            Cipher = Cipher.M3,
            Ciphertext = ciphertext,
            M3Parts = fixedParts,
            Scope = scope,
        };
        _pending = req;
        InvalidateVisual();
        return req.Tcs.Task;
    }

    public Task<CrackResult> RunGpuM4Async(byte[] ciphertext, EnigmaM4 fixedParts, CrackScope scope)
    {
        var req = new Request
        {
            Cipher = Cipher.M4,
            Ciphertext = ciphertext,
            M4Parts = fixedParts,
            Scope = scope,
        };
        _pending = req;
        InvalidateVisual();
        return req.Tcs.Task;
    }

    /// <summary>
    /// Schedule a Lorenz chi-only crack. Returns the GENERIC CrackResult shape
    /// (PL/PM/PR fields carry s0/s1/s2, RR/RM/RL carry s3/s4/ic — sloppy but
    /// keeps the public await contract uniform with M3/M4).
    /// </summary>
    public Task<CrackResultLorenz> RunGpuLorenzAsync(byte[] ciphertext, byte[][] chiPins, CrackScope scope)
    {
        var req = new Request
        {
            Cipher = Cipher.Lorenz,
            Ciphertext = ciphertext,
            LorenzChiPins = chiPins,
            Scope = scope,
        };
        _pending = req;
        _pendingLorenzTcs = new TaskCompletionSource<CrackResultLorenz>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        InvalidateVisual();
        return _pendingLorenzTcs.Task;
    }

    volatile TaskCompletionSource<CrackResultLorenz>? _pendingLorenzTcs;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var req = _pending;
        if (req == null) return;
        var lorenzTcs = _pendingLorenzTcs;
        context.Custom(new BenchDrawOp(
            new Rect(Bounds.Size), req,
            () => { _pending = null; _pendingLorenzTcs = null; },
            lorenzTcs));
    }

    sealed class BenchDrawOp : ICustomDrawOperation
    {
        readonly Request _req;
        readonly Action _clear;
        readonly TaskCompletionSource<CrackResultLorenz>? _lorenzTcs;
        public Rect Bounds { get; }

        public BenchDrawOp(Rect bounds, Request req, Action clear,
                           TaskCompletionSource<CrackResultLorenz>? lorenzTcs)
        {
            Bounds = bounds;
            _req = req;
            _clear = clear;
            _lorenzTcs = lorenzTcs;
        }

        public void Render(ImmediateDrawingContext context)
        {
            try
            {
                var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
                if (leaseFeature == null)
                {
                    _req.Tcs.TrySetException(new InvalidOperationException(
                        "ISkiaSharpApiLeaseFeature unavailable — Avalonia Skia backend missing"));
                    return;
                }

                using var lease = leaseFeature.Lease();
                var gr = lease.GrContext;

                Console.WriteLine($"[BenchCtl] Cipher={_req.Cipher} "
                                + $"GrContext={(gr != null ? "non-null" : "NULL")} "
                                + $"Backend={(gr != null ? gr.Backend.ToString() : "n/a")}");

                if (_req.Cipher == Cipher.Lorenz)
                {
                    // Lorenz uses a different result type — dispatch through
                    // the parallel TCS rather than the generic Tcs.
                    var lorenzResult = new GpuCrackerLorenz(gr).Crack(
                        _req.Ciphertext, _req.LorenzChiPins, _req.Scope);
                    _lorenzTcs?.TrySetResult(lorenzResult);
                    return;
                }

                CrackResult result;
                if (_req.Cipher == Cipher.M3)
                    result = new GpuCracker(gr).Crack(_req.Ciphertext, _req.M3Parts, _req.Scope);
                else
                    result = new GpuCrackerM4(gr).Crack(_req.Ciphertext, _req.M4Parts, _req.Scope);

                _req.Tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                if (_req.Cipher == Cipher.Lorenz)
                    _lorenzTcs?.TrySetException(ex);
                else
                    _req.Tcs.TrySetException(ex);
            }
            finally
            {
                _clear();
            }
        }

        public void Dispose() { }
        public bool Equals(ICustomDrawOperation? other) => false;
        public bool HitTest(Point p) => false;
    }
}
