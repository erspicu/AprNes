using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using SkiaSharp;
using EnigmaBenchmark.Core;
using EnigmaBenchmark.Crackers;

namespace EnigmaBenchmarkAvalonia;

/// <summary>
/// Hosts the GPU cracker run. Avalonia only exposes the D3D11 GrContext inside
/// a render callback via ISkiaSharpApiLease, so we:
///   1. Stash cracker inputs + a TaskCompletionSource
///   2. Trigger InvalidateVisual() to schedule a render frame
///   3. In the DrawOp, lease the context, run GpuCracker once, publish result
///   4. Resolve the TCS so the caller can await the GPU result
///
/// UI freezes for the duration of the GPU cracker run because the lease is
/// synchronous within Render(). On real GPU hardware that's typically 1-3s,
/// which is acceptable for a benchmark tool.
/// </summary>
public class BenchmarkControl : Control
{
    sealed class Request
    {
        public byte[] Ciphertext = Array.Empty<byte>();
        public EnigmaM3 FixedParts;
        public CrackScope Scope;
        // RunContinuationsAsynchronously: without this, TrySetResult would
        // fire the await continuation synchronously on the render thread,
        // and any Avalonia UI update downstream would cross-thread-throw.
        public TaskCompletionSource<CrackResult> Tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    volatile Request? _pending;

    /// <summary>
    /// Schedule a GPU cracker run. Must be called from the UI thread (posts
    /// an invalidate). The returned task completes on the render thread once
    /// the DrawOp finishes.
    /// </summary>
    public Task<CrackResult> RunGpuAsync(byte[] ciphertext, EnigmaM3 fixedParts, CrackScope scope)
    {
        var req = new Request
        {
            Ciphertext = ciphertext,
            FixedParts = fixedParts,
            Scope      = scope,
        };
        _pending = req;
        InvalidateVisual();
        return req.Tcs.Task;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var req = _pending;
        if (req == null) return;

        context.Custom(new BenchDrawOp(new Rect(Bounds.Size), req, () => _pending = null));
    }

    sealed class BenchDrawOp : ICustomDrawOperation
    {
        readonly Request _req;
        readonly Action _clear;

        public Rect Bounds { get; }

        public BenchDrawOp(Rect bounds, Request req, Action clear)
        {
            Bounds = bounds;
            _req = req;
            _clear = clear;
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
                var gr = lease.GrContext;   // may still be null if not a GPU backend

                // Diagnostic breadcrumbs so we can see whether the lease
                // actually gave us a real GPU context. Result flows back via
                // the exception channel if anything is null/wrong.
                Console.WriteLine($"[BenchCtl] GrContext={(gr != null ? "non-null" : "NULL")} "
                                + $"Backend={(gr != null ? gr.Backend.ToString() : "n/a")}");

                var cracker = new GpuCracker(gr);
                var result = cracker.Crack(_req.Ciphertext, _req.FixedParts, _req.Scope);
                _req.Tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
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
