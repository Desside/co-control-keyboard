# Taming the USB Bus: Command Queues and State Machines in HID Drivers

## Opening

**The problem:** HID devices are not high-speed network interfaces; they are fragile endpoints. Sending a burst of "SetFeature" packets to a keyboard often results in "buffer overflow" errors, dropped commands, or—worst case—a firmware hang that requires a physical unplug to fix.

**The insight:** To ensure 100% reliability, you cannot treat hardware I/O as a standard asynchronous task. You must implement a **Serialized Command Queue** and a **Strict State Machine** that treats a sequence of packets as a single atomic transaction.

**What you'll learn:** How to build a producer-consumer queue in .NET 8 that prevents USB saturation and how to enforce a multi-step protocol sequence (`Begin` $\to$ `Data` $\to$ `Apply` $\to$ `Finish`) so that hardware never enters an undefined state.

---

## Body

### 1. The USB Saturation Problem

If you call `HidD_SetFeature` in a tight loop (e.g., updating 88 LEDs), the Windows HID driver may accept the packets, but the device's internal buffer will overflow. This leads to non-deterministic failures where some LEDs update and others don't.

**The Solution: The Single-Worker Queue.**
Instead of calling the API directly, the `ProtocolEngine` enqueues a `QueuedCommand` and signals a single background worker. This ensures that only one packet is "in flight" at a time.

```csharp
private readonly ConcurrentQueue<QueuedCommand> _queue = new();
private readonly SemaphoreSlim _signal = new(0);

private async Task ProcessQueueAsync()
{
    while (!token.IsCancellationRequested)
    {
        await _signal.WaitAsync(token); // Wait for work
        if (!_queue.TryDequeue(out var cmd)) continue;
        
        // Execute the command based on its type (Simple, RequestResponse, or FlashWrite)
        await ExecuteCommandAsync(cmd); 
    }
}
```

---

### 2. The Flash Write State Machine

A single byte change in the keyboard's config isn't just one packet. It is a **transaction**. If you send a "Data" packet without a "Begin" packet, the firmware ignores it. If you send "Begin" and "Data" but forget "Apply", the changes are never committed to Flash.

**The Sequence:**
1. `BuildBeginSession()` $\to$ Tells MCU to prepare for writing.
2. `BuildFlashPageWrite()` $\to$ Sends the actual payload + CRC.
3. `BuildApply()` $\to$ Tells MCU to commit the buffer to physical cells.
4. `BuildFinish()` $\to$ Closes the transaction.

**Implementation via Command Types:**
We define a `CommandType.FlashWrite`. When the worker encounters this type, it doesn't just send one packet; it orchestrates the entire sequence with mandatory delays to match the hardware's DMA timing.

```csharp
case CommandType.FlashWrite:
    _device.SetFeature(PacketBuilder.BuildBeginSession());
    await Task.Delay(FlashStepDelay, token); // ~40ms: Hardware needs time to prep
    _device.SetFeature(cmd.Packet);
    await Task.Delay(FlashStepDelay, token);
    _device.SetFeature(PacketBuilder.BuildApply());
    await Task.Delay(FlashStepDelay, token);
    _device.SetFeature(PacketBuilder.BuildFinish());
    cmd.Completion.TrySetResult(null);
    break;
```

---

### 3. Request-Response Synchronization

Some commands are "Fire and Forget" (Simple), but others are "Requests" (like `ConfigRead` or `ModelQuery`) that require a response.

In an asynchronous environment, you can't just call `GetFeature()` immediately after `SetFeature()`, because the hardware may still be processing the request.

**The Sync Pattern:**
We use `TaskCompletionSource<byte[]>` to bridge the gap between the synchronous queue worker and the asynchronous caller.

```csharp
public async Task<byte[]> SendWithResponseAsync(byte[] packet, HidCommand expectedAck)
{
    var cmd = new QueuedCommand(packet, CommandType.RequestResponse, expectedAck);
    EnqueueCore(cmd);
    // The worker will call cmd.Completion.TrySetResult(resp) once the packet arrives
    return await cmd.Completion.Task; 
}
```

---

### 4. Error Propagation in a Background Thread

When a command fails in a background worker, you cannot simply throw an exception, or the worker thread will die and the app will stop responding.

**The Solution:** We wrap the execution in a `try-catch` and propagate the exception back to the original caller via the `TaskCompletionSource`.

```csharp
try {
    await ExecuteCommandAsync(cmd);
}
catch (Exception ex) {
    cmd.Completion.TrySetException(ex); // Caller gets the IOException
}
```

---

## Closing

**Key Takeaway:** Hardware stability is achieved by restricting concurrency. By moving from a "Call-and-Response" model to a "Serialized Queue" model, we eliminate USB saturation and ensure that complex multi-packet transactions are always atomic.

**Action for you:** If your hardware driver is "flaky" or occasionally misses commands, stop calling the API from your UI/Service threads. Implement a single-worker queue and a state-aware transaction manager.

---

## Honest Trade-offs

| Decision | Benefit | Cost |
|----------|---------|------|
| **Single Worker Thread** | Guaranteed packet order; no USB saturation | Commands are processed sequentially (no parallel writes) |
| **Mandatory Flash Delays** | 100% write reliability | Flash writes are slow ($\approx 160\text{ms}$ per page) |
| **TCS for Sync** | Clean async/await API for the caller | Slightly more memory overhead per command |
| **Strict State Machine** | Prevents Flash corruption | A single failed packet fails the entire transaction |
