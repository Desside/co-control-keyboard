using CoControl.Rgb;
using Xunit;

namespace CoControl.UnitTests;

public class PlanarRgbConverterTests
{
    [Fact]
    public void ToPlanar_MapsEscToLed0()
    {
        var rgb = new byte[88 * 3];
        rgb[0] = 0xFF; rgb[1] = 0x80; rgb[2] = 0x40; // ESC (key 0)

        var r = new byte[126]; var g = new byte[126]; var b = new byte[126];
        PlanarRgbConverter.ToPlanar(rgb, r, g, b);

        Assert.Equal(0xFF, r[0]);
        Assert.Equal(0x80, g[0]);
        Assert.Equal(0x40, b[0]);
    }

    [Fact]
    public void ToPlanar_SolidColor_RoundTripsForAllKeys()
    {
        var rgb = new byte[88 * 3];
        for (int i = 0; i < 88; i++)
        {
            rgb[i * 3] = 0x12; rgb[i * 3 + 1] = 0x34; rgb[i * 3 + 2] = 0x56;
        }

        var r = new byte[126]; var g = new byte[126]; var b = new byte[126];
        PlanarRgbConverter.ToPlanar(rgb, r, g, b);

        var back = new byte[88 * 3];
        PlanarRgbConverter.FromPlanar(r, g, b, back);

        Assert.Equal(rgb, back);
    }

    [Fact]
    public void ToPlanar_RejectsWrongSizes()
    {
        Assert.Throws<ArgumentException>(() =>
            PlanarRgbConverter.ToPlanar(new byte[100], new byte[126], new byte[126], new byte[126]));
        Assert.Throws<ArgumentException>(() =>
            PlanarRgbConverter.ToPlanar(new byte[264], new byte[125], new byte[126], new byte[126]));
    }

    [Fact]
    public void FillSolid_FillsAllLeds()
    {
        var r = new byte[126]; var g = new byte[126]; var b = new byte[126];
        PlanarRgbConverter.FillSolid(1, 2, 3, r, g, b);
        Assert.All(r, v => Assert.Equal(1, v));
        Assert.All(g, v => Assert.Equal(2, v));
        Assert.All(b, v => Assert.Equal(3, v));
    }
}

public class RgbProfileStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"cocontrol-profiles-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void SaveLoad_RoundTrips()
    {
        var store = new RgbProfileStore(_dir);
        var profile = new RgbProfile { Name = "Gaming" };
        profile.Rgb88x3[0] = 0xFF;

        store.Save(profile);
        var loaded = store.Load("Gaming");

        Assert.Equal("Gaming", loaded.Name);
        Assert.Equal(0xFF, loaded.Rgb88x3[0]);
    }

    [Fact]
    public void List_ReturnsSavedProfiles()
    {
        var store = new RgbProfileStore(_dir);
        store.Save(new RgbProfile { Name = "B" });
        store.Save(new RgbProfile { Name = "A" });

        Assert.Equal(new[] { "A", "B" }, store.List());
    }

    [Fact]
    public void Delete_RemovesProfile()
    {
        var store = new RgbProfileStore(_dir);
        store.Save(new RgbProfile { Name = "Temp" });
        Assert.True(store.Delete("Temp"));
        Assert.False(store.Delete("Temp"));
        Assert.Empty(store.List());
    }

    [Fact]
    public void Save_SanitizesFileName()
    {
        var store = new RgbProfileStore(_dir);
        store.Save(new RgbProfile { Name = "a/b:c" });
        Assert.Single(store.List());
    }

    [Fact]
    public void Save_RejectsInvalidBuffer()
    {
        var store = new RgbProfileStore(_dir);
        var bad = new RgbProfile { Name = "Bad", Rgb88x3 = new byte[10] };
        Assert.Throws<InvalidDataException>(() => store.Save(bad));
    }
}
