using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CoControl.HAL;
using CoControl.Protocol;
using CoControl.State;

namespace CoControl.Service;

/// <summary>
/// Delegate for mutating a 136-byte config buffer.
/// </summary>
public delegate void ConfigMutator(byte[] config);

/// <summary>
/// Serializes all outbound HID packets to prevent USB saturation.
/// Enforces Begin → Data → Apply → Finish sequence for Flash writes.
/// Every enqueued command completes its Task, so errors always propagate.
/// </summary>
public sealed class ProtocolEngine : IDisposable
{
    /// <summary>Delay between steps of the Flash write sequence (spec: ~40ms).</summary>
    private static readonly TimeSpan FlashStepDelay = TimeSpan.FromMilliseconds(40);
    /// <summary>Delay before reading a response Feature Report.</summary>
    private static readonly TimeSpan ResponseDelay = TimeSpan.FromMilliseconds(2);

    private readonly IHidDevice _device;
    private readonly ShadowConfig _shadow;
    private readonly ConcurrentQueue<QueuedCommand> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;
    private bool _disposed;

    public ProtocolEngine(IHidDevice device, ShadowConfig shadow)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _shadow = shadow ?? throw new ArgumentNullException(nameof(shadow));
        _worker = Task.Run(ProcessQueueAsync);
    }

    /// <summary>
    /// Reads current config from device (CMD 0x84) and loads into shadow.
    /// </summary>
    public async Task RefreshShadowAsync()
    {
        var readPkt = PacketBuilder.BuildConfigRead();
        var resp = await SendWithResponseAsync(readPkt, HidCommand.ConfigRead).ConfigureAwait(false);
        var config = PacketParser.ParseConfigRead(resp);
        _shadow.SyncFromDevice(config);
    }

    /// <summary>
    /// Sends Model Query (CMD 0x82) for device validation.
    /// </summary>
    public async Task<(byte Model, byte SubModel)> QueryModelAsync()
    {
        var query = PacketBuilder.BuildModelQuery();
        var resp = await SendWithResponseAsync(query, HidCommand.ModelQuery).ConfigureAwait(false);
        return PacketParser.ParseModelQuery(resp);
    }

    /// <summary>
    /// Sets per-key RGB (Planar, CMD 0x06). No Flash write.
    /// The returned task completes when the packet has been sent.
    /// </summary>
    public Task SetPerKeyRgbAsync(ReadOnlySpan<byte> red126, ReadOnlySpan<byte> green126, ReadOnlySpan<byte> blue126)
    {
        var pkt = PacketBuilder.BuildPerKeyRgb(red126, green126, blue126);
        return Enqueue(new QueuedCommand(pkt, CommandType.Simple));
    }

    /// <summary>
    /// Sets Direct Mode RGB (CMD 0x08) for real-time animation. Bypasses Flash.
    /// </summary>
    public Task SetDirectModeAsync(ReadOnlySpan<byte> rgb366)
    {
        var pkt = PacketBuilder.BuildDirectMode(rgb366);
        return Enqueue(new QueuedCommand(pkt, CommandType.Simple));
    }

    /// <summary>
    /// Modifies config in shadow and performs a Diff-Write (CMD 0x04) if changed.
    /// The shadow baseline is committed only after the device accepts the
    /// full Begin → Data → Apply → Finish sequence.
    /// Returns false if the mutation produced no changes.
    /// </summary>
    public async Task<bool> UpdateConfigAsync(ConfigMutator mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);

        // Mutate through ShadowConfig so dirty tracking cannot be bypassed.
        _shadow.Mutate(cfg => mutator(cfg));

        if (!_shadow.TryBeginWrite(out var config136, out ushort crc))
            return false;

        var pkt = PacketBuilder.BuildConfigWrite(config136, crc);
        try
        {
            await Enqueue(new QueuedCommand(pkt, CommandType.FlashWrite)).ConfigureAwait(false);
            _shadow.CommitWrite();
            return true;
        }
        catch
        {
            _shadow.AbortWrite();
            throw;
        }
    }

    /// <summary>
    /// Writes a raw Flash page (remap table / macro storage) with CRC guard
    /// and the full Begin → Data → Apply → Finish sequence.
    /// </summary>
    public Task WriteFlashPageAsync(byte page, ReadOnlyMemory<byte> data)
    {
        ushort crc = Crc16.Compute(data.Span);
        var pkt = PacketBuilder.BuildFlashPageWrite(page, data.Span, crc);
        return Enqueue(new QueuedCommand(pkt, CommandType.FlashWrite));
    }

    /// <summary>
    /// Low-level: enqueue a packet and read back the device response.
    /// </summary>
    public async Task<byte[]> SendWithResponseAsync(byte[] packet, HidCommand expectedAck)
    {
        var cmd = new QueuedCommand(packet, CommandType.RequestResponse, expectedAck);
        EnqueueCore(cmd);
        var resp = await cmd.Completion.Task.ConfigureAwait(false);
        return resp ?? throw new IOException("No response");
    }

    private Task Enqueue(QueuedCommand cmd)
    {
        EnqueueCore(cmd);
        return cmd.Completion.Task;
    }

    private void EnqueueCore(QueuedCommand cmd)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _queue.Enqueue(cmd);
        _signal.Release();
    }

    private async Task ProcessQueueAsync()
    {
        var token = _cts.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                await _signal.WaitAsync(token).ConfigureAwait(false);
                if (!_queue.TryDequeue(out var cmd)) continue;

                try
                {
                    switch (cmd.Type)
                    {
                        case CommandType.Simple:
                            _device.SetFeature(cmd.Packet);
                            cmd.Completion.TrySetResult(null);
                            break;

                        case CommandType.RequestResponse:
                            _device.SetFeature(cmd.Packet);
                            await Task.Delay(ResponseDelay, token).ConfigureAwait(false);
                            // Report ID is always 0x06 — never the command opcode.
                            var resp = _device.GetFeature();
                            if (!PacketParser.IsAck(resp, cmd.ExpectedAck))
                                cmd.Completion.TrySetException(
                                    new IOException($"Unexpected response header for {cmd.ExpectedAck} (got 0x{resp[1]:X2})"));
                            else
                                cmd.Completion.TrySetResult(resp);
                            break;

                        case CommandType.FlashWrite:
                            // Full sequence: Begin → Data → Apply → Finish.
                            _device.SetFeature(PacketBuilder.BuildBeginSession());
                            await Task.Delay(FlashStepDelay, token).ConfigureAwait(false);
                            _device.SetFeature(cmd.Packet);
                            await Task.Delay(FlashStepDelay, token).ConfigureAwait(false);
                            _device.SetFeature(PacketBuilder.BuildApply());
                            await Task.Delay(FlashStepDelay, token).ConfigureAwait(false);
                            _device.SetFeature(PacketBuilder.BuildFinish());
                            cmd.Completion.TrySetResult(null);
                            break;
                    }
                }
                catch (OperationCanceledException)
                {
                    cmd.Completion.TrySetCanceled();
                    throw;
                }
                catch (Exception ex)
                {
                    cmd.Completion.TrySetException(ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown: fail everything still queued.
            while (_queue.TryDequeue(out var pending))
                pending.Completion.TrySetCanceled();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        try { _worker.Wait(TimeSpan.FromSeconds(2)); } catch { /* worker faulted on shutdown */ }
        _cts.Dispose();
        _signal.Dispose();
    }

    private enum CommandType { Simple, RequestResponse, FlashWrite }

    private sealed class QueuedCommand
    {
        public byte[] Packet { get; }
        public CommandType Type { get; }
        public HidCommand ExpectedAck { get; }
        public TaskCompletionSource<byte[]?> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public QueuedCommand(byte[] packet, CommandType type, HidCommand expectedAck = 0)
        {
            Packet = packet; Type = type; ExpectedAck = expectedAck;
        }
    }
}
