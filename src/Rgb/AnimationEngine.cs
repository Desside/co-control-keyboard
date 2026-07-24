using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CoControl.Service;

namespace CoControl.Rgb;

/// <summary>
/// A host-side animation: renders a frame for a given time.
/// </summary>
public interface IAnimation
{
    /// <summary>Fills <paramref name="rgb88x3"/> (88 keys × RGB interleaved) for time <paramref name="timeSec"/>.</summary>
    void Render(float timeSec, Span<byte> rgb88x3);
}

/// <summary>
/// Direct Mode (CMD 0x08) frame loop at 30–60 Hz.
/// While an animation plays, each frame doubles as keepalive.
/// While paused, the last frame is re-sent periodically so the firmware
/// does not revert to its internal effect.
/// </summary>
public sealed class AnimationEngine : IAsyncDisposable
{
    public const int MinFps = 30;
    public const int MaxFps = 60;

    private static readonly TimeSpan KeepaliveInterval = TimeSpan.FromMilliseconds(900);

    private readonly ProtocolEngine _engine;
    private readonly FrameBuffer _fb = new();
    private readonly object _lock = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    private IAnimation? _animation;
    private volatile bool _paused;
    private readonly TimeSpan _framePeriod;

    /// <summary>Last error raised by the frame loop (loop keeps running).</summary>
    public Exception? LastError { get; private set; }

    /// <summary>Total frames successfully sent to the device.</summary>
    public long FramesSent { get; private set; }

    public AnimationEngine(ProtocolEngine engine, int fps = 45)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _framePeriod = TimeSpan.FromSeconds(1.0 / Math.Clamp(fps, MinFps, MaxFps));
    }

    public bool IsRunning => _loop is { IsCompleted: false };
    public bool IsPaused => _paused;

    /// <summary>Starts (or switches to) an animation.</summary>
    public void Play(IAnimation animation)
    {
        ArgumentNullException.ThrowIfNull(animation);
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_cts.IsCancellationRequested, this);
            _animation = animation;
            _paused = false;
            _loop ??= Task.Run(LoopAsync);
        }
    }

    /// <summary>Freezes on the current frame; keepalive keeps Direct Mode alive.</summary>
    public void Pause() => _paused = true;

    public void Resume() => _paused = false;

    /// <summary>
    /// Renders and sends exactly one frame. Used by the loop and by tests.
    /// </summary>
    internal async Task RenderFrameAsync(float timeSec)
    {
        IAnimation? anim;
        lock (_lock) anim = _animation;
        if (anim == null) return;

        anim.Render(timeSec, _fb.NextRgb);
        PlanarRgbConverter.ToDirectMode(_fb.NextRgb, _fb.DirectMode);
        await _engine.SetDirectModeAsync(_fb.DirectMode).ConfigureAwait(false);
        _fb.Swap();
    }

    /// <summary>Re-sends the last rendered frame (keepalive).</summary>
    internal Task SendKeepaliveAsync() => _engine.SetDirectModeAsync(_fb.DirectMode);

    private async Task LoopAsync()
    {
        var token = _cts.Token;
        var clock = Stopwatch.StartNew();
        var lastKeepalive = TimeSpan.Zero;

        try
        {
            while (!token.IsCancellationRequested)
            {
                var frameStart = clock.Elapsed;

                try
                {
                    if (!_paused)
                    {
                        await RenderFrameAsync((float)frameStart.TotalSeconds).ConfigureAwait(false);
                        FramesSent++;
                        lastKeepalive = frameStart;
                    }
                    else if (frameStart - lastKeepalive >= KeepaliveInterval)
                    {
                        await SendKeepaliveAsync().ConfigureAwait(false);
                        lastKeepalive = frameStart;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    // Transient USB error must not silently kill the loop.
                    LastError = ex;
                }

                var elapsed = clock.Elapsed - frameStart;
                var remaining = _framePeriod - elapsed;
                if (remaining > TimeSpan.Zero)
                    await Task.Delay(remaining, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_loop != null)
        {
            try { await _loop.ConfigureAwait(false); } catch { /* shutdown */ }
        }
        _cts.Dispose();
    }
}
