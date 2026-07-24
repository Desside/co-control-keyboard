using CoControl.Input;
using Xunit;

namespace CoControl.UnitTests;

public class RemapTests
{
    [Fact]
    public void RemapEntry_RoundTrips()
    {
        var entry = RemapEntry.Combo(Modifiers.LeftCtrl | Modifiers.LeftShift, KeyCode.Escape);
        var buf = new byte[4];
        entry.WriteTo(buf);
        Assert.Equal(entry, RemapEntry.ReadFrom(buf));
    }

    [Fact]
    public void KeyRemapper_SerializesTo352Bytes()
    {
        var remapper = new KeyRemapper();
        Assert.Equal(352, KeyRemapper.TableSize);
        Assert.Equal(352, remapper.Serialize().Length);
    }

    [Fact]
    public void KeyRemapper_RoundTrips()
    {
        var remapper = new KeyRemapper();
        remapper[0] = RemapEntry.Key(KeyCode.CapsLock);          // ESC → CapsLock
        remapper[40] = RemapEntry.Disabled;
        remapper[87] = RemapEntry.Macro(3);

        var restored = KeyRemapper.Deserialize(remapper.Serialize());

        Assert.Equal(RemapEntry.Key(KeyCode.CapsLock), restored[0]);
        Assert.Equal(RemapEntry.Disabled, restored[40]);
        Assert.Equal(RemapEntry.Macro(3), restored[87]);
        Assert.Equal(RemapEntry.Default, restored[1]); // untouched key
    }

    [Fact]
    public void KeyRemapper_RejectsBadIndex()
    {
        var remapper = new KeyRemapper();
        Assert.Throws<ArgumentOutOfRangeException>(() => remapper[88]);
        Assert.Throws<ArgumentOutOfRangeException>(() => remapper[-1] = RemapEntry.Default);
    }

    [Fact]
    public void MacroEntry_RejectsBadSlot()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RemapEntry.Macro(FlashLayout.MacroSlotCount));
    }
}

public class MacroTests
{
    [Fact]
    public void Macro_RoundTrips()
    {
        var macro = new Macro { RepeatCount = 2 };
        macro.Add(MacroEvent.Down(KeyCode.LeftCtrl))
             .Add(MacroEvent.Down(KeyCode.C))
             .Add(MacroEvent.Wait(50))
             .Add(MacroEvent.Up(KeyCode.C))
             .Add(MacroEvent.Up(KeyCode.LeftCtrl));

        var restored = Macro.Deserialize(macro.Serialize());

        Assert.Equal(2, restored.RepeatCount);
        Assert.Equal(5, restored.Events.Count);
        Assert.Equal(MacroEvent.Wait(50), restored.Events[2]);
        Assert.Equal(MacroEvent.Up(KeyCode.LeftCtrl), restored.Events[4]);
    }

    [Fact]
    public void Tap_EmitsDownDelayUp()
    {
        var macro = new Macro().Tap(KeyCode.A, 10);
        Assert.Equal(3, macro.Events.Count);
        Assert.Equal(MacroEvent.Down(KeyCode.A), macro.Events[0]);
        Assert.Equal(MacroEvent.Wait(10), macro.Events[1]);
        Assert.Equal(MacroEvent.Up(KeyCode.A), macro.Events[2]);
    }

    [Fact]
    public void Type_MapsCharactersToKeys()
    {
        var macro = new Macro().Type("GG", 0);
        Assert.Equal(4, macro.Events.Count); // 2 × (down+up), no delays
        Assert.Equal(MacroEvent.Down(KeyCode.G), macro.Events[0]);
    }

    [Fact]
    public void Type_RejectsUnsupportedCharacters()
    {
        Assert.Throws<ArgumentException>(() => new Macro().Type("привет"));
    }

    [Fact]
    public void Macro_EnforcesSlotCapacity()
    {
        var macro = new Macro();
        for (int i = 0; i < Macro.MaxEvents; i++)
            macro.Add(MacroEvent.Wait(1));
        Assert.Throws<InvalidOperationException>(() => macro.Add(MacroEvent.Wait(1)));
        Assert.True(macro.Serialize().Length <= FlashLayout.MacroSlotSize);
    }

    [Fact]
    public void Deserialize_RejectsTruncatedData()
    {
        var data = new Macro().Tap(KeyCode.A).Serialize();
        Assert.Throws<ArgumentException>(() => Macro.Deserialize(data.AsSpan(0, data.Length - 1)));
    }
}
