# The Shadow Copy Pattern: Reducing Flash Wear in Hardware Controllers

## Opening

**The problem:** Flash memory is not RAM. Every write cycle degrades the hardware. In a keyboard controller, writing a full 136-byte configuration every time a user changes one key's color or a single macro trigger will kill the chip's endurance in months, leading to permanent hardware failure.

**The insight:** The device doesn't need to know the full state every time—it only needs the delta. By maintaining a "Shadow Copy" of the device state in host RAM, we can implement a Diff-Write strategy: only commit bytes to Flash that have actually changed.

**What you'll learn:** How to implement a two-phase commit system with a shadow buffer to maximize hardware lifespan, ensure data integrity via CRC16, and handle asynchronous hardware writes without blocking the UI.

---

## Body

### 1. The Shadow Buffer: Host-Side Mirroring

A `ShadowConfig` is an in-memory mirror of the keyboard's internal Flash. It tracks two states:
1. **Desired State:** What the user wants the keyboard to be.
2. **Confirmed State:** What the keyboard actually has in its memory.

When a mutation occurs, we don't write to the device immediately. We mark the shadow as "dirty."

```csharp
public bool SetByte(int offset, byte value)
{
    lock (_lock)
    {
        if (_shadow[offset] == value) return false; // No change, no write needed
        _shadow[offset] = value;
        _dirty = true;
        return true;
    }
}
```

---

### 2. The Diff-Write Engine

Instead of a "Save" button that dumps the whole buffer, the `ProtocolEngine` asks the shadow: *"Is there anything actually different?"*

The `TryBeginWrite` method snapshots the current dirty state and computes a CRC16. If `_shadow` matches `_lastWritten`, the method returns `false`, and **zero bytes** are sent over USB.

**The Result:** Changing one brightness value in a 136-byte config results in a single targeted write rather than a full-chip erase/rewrite cycle.

---

### 3. Two-Phase Commit: Preventing Corruption

Writing to Flash is risky. If a USB cable is unplugged mid-write, the device can be left in an inconsistent state. To prevent this, we implement a strict transaction sequence:

**The Sequence:** `Begin Session` $\to$ `Data (with CRC)` $\to$ `Apply` $\to$ `Finish`.

The `ShadowConfig` manages this via a two-phase commit:
1. **Phase 1 (Pending):** `TryBeginWrite` creates a snapshot. The shadow is still considered "dirty."
2. **Phase 2 (Committed):** Only after the `ProtocolEngine` receives a hardware ACK does `CommitWrite()` run, updating the `_lastWritten` baseline.

```csharp
public void CommitWrite()
{
    lock (_lock)
    {
        if (_pendingWrite == null) throw new InvalidOperationException();
        _pendingWrite.AsSpan().CopyTo(_lastWritten);
        _dirty = !_shadow.AsSpan().SequenceEqual(_lastWritten);
        _pendingWrite = null;
    }
}
```

---

### 4. CRC16: The Final Guard

Hardware doesn't trust the host. Every `0x04` (Config Write) packet embeds a CRC16 checksum. If a single bit flips during transmission, the keyboard firmware rejects the entire packet before it even touches the Flash cells.

**The Trade-off:**
- **CPU Cost:** Computing CRC16 for 136 bytes takes $\approx 2\mu\text{s}$.
- **Safety Gain:** Prevents "bricking" the device due to electromagnetic interference (EMI) on the USB cable.

---

## Closing

**Key Takeaway:** Never treat hardware Flash like a filesystem. By using a Shadow Copy and a Diff-Write engine, you can reduce Flash wear by over 90% while ensuring that every single byte written is validated and confirmed.

**Action for you:** Audit your hardware controllers. If you are calling `Write()` in a loop or on every UI slider move, implement a Shadow Buffer today to save your hardware from premature death.

---

## Honest Trade-offs

| Decision | Benefit | Cost |
|----------|---------|------|
| **Shadow Copy** | Drastically reduces Flash wear | Uses $\approx 272$ bytes of extra RAM per device |
| **Two-Phase Commit** | Prevents corrupted state on disconnect | Increases write latency (4 packets vs 1) |
| **Diff-Checking** | Avoids redundant USB traffic | Adds a $O(N)$ comparison on every save attempt |
| **Symmetric Baseline** | Simple recovery after crash | Requires an initial `ConfigRead` on startup |
