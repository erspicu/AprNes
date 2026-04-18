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
    enum Cipher { M3, M4, Lorenz, T52e }

    sealed class Request
    {
        public Cipher Cipher;
        public byte[] Ciphertext = Array.Empty<byte>();
        public EnigmaM3 M3Parts;
        public EnigmaM4 M4Parts;
        public byte[][] LorenzChiPins = Array.Empty<byte[]>();
        public byte[][] T52ePins = Array.Empty<byte[]>();
        public int[] T52eSwitchMap = Array.Empty<int>();
        public int[] T52eKnownStart = Array.Empty<int>();
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
    volatile TaskCompletionSource<CrackResultT52e>? _pendingT52eTcs;

    /// <summary>Schedule a T52e reduced-keyspace crack (24 M candidates).</summary>
    public Task<CrackResultT52e> RunGpuT52eAsync(
        byte[] ciphertext, byte[][] pins, int[] switchMap, int[] knownStart, CrackScope scope)
    {
        var req = new Request
        {
            Cipher = Cipher.T52e,
            Ciphertext = ciphertext,
            T52ePins = pins,
            T52eSwitchMap = switchMap,
            T52eKnownStart = knownStart,
            Scope = scope,
        };
        _pending = req;
        _pendingT52eTcs = new TaskCompletionSource<CrackResultT52e>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        InvalidateVisual();
        return _pendingT52eTcs.Task;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var req = _pending;
        if (req == null) return;
        var lorenzTcs = _pendingLorenzTcs;
        var t52eTcs = _pendingT52eTcs;
        context.Custom(new BenchDrawOp(
            new Rect(Bounds.Size), req,
            () => { _pending = null; _pendingLorenzTcs = null; _pendingT52eTcs = null; },
            lorenzTcs, t52eTcs));
    }

    sealed class BenchDrawOp : ICustomDrawOperation
    {
        readonly Request _req;
        readonly Action _clear;
        readonly TaskCompletionSource<CrackResultLorenz>? _lorenzTcs;
        readonly TaskCompletionSource<CrackResultT52e>? _t52eTcs;
        public Rect Bounds { get; }

        public BenchDrawOp(Rect bounds, Request req, Action clear,
                           TaskCompletionSource<CrackResultLorenz>? lorenzTcs,
                           TaskCompletionSource<CrackResultT52e>? t52eTcs)
        {
            Bounds = bounds;
            _req = req;
            _clear = clear;
            _lorenzTcs = lorenzTcs;
            _t52eTcs = t52eTcs;
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
                    var lorenzResult = new GpuCrackerLorenz(gr).Crack(
                        _req.Ciphertext, _req.LorenzChiPins, _req.Scope);
                    _lorenzTcs?.TrySetResult(lorenzResult);
                    return;
                }

                if (_req.Cipher == Cipher.T52e)
                {
                    var t52eResult = new GpuCrackerT52e(gr).Crack(
                        _req.Ciphertext, _req.T52ePins, _req.T52eSwitchMap,
                        _req.T52eKnownStart, _req.Scope);
                    _t52eTcs?.TrySetResult(t52eResult);
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
                else if (_req.Cipher == Cipher.T52e)
                    _t52eTcs?.TrySetException(ex);
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
