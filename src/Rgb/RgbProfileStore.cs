using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CoControl.Rgb;

/// <summary>
/// A named per-key RGB profile (88 keys × RGB, interleaved).
/// </summary>
public sealed class RgbProfile
{
    public string Name { get; set; } = "Untitled";
    public int Version { get; set; } = 1;
    public byte[] Rgb88x3 { get; set; } = new byte[88 * 3];

    public void Validate()
    {
        if (Rgb88x3 is not { Length: 88 * 3 })
            throw new InvalidDataException($"Profile '{Name}': Rgb88x3 must be {88 * 3} bytes");
    }
}

/// <summary>
/// Persists RGB profiles as JSON files in %LocalAppData%\CoControl\profiles.
/// </summary>
public sealed class RgbProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _dir;

    public RgbProfileStore(string? directory = null)
    {
        _dir = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CoControl", "profiles");
    }

    public IReadOnlyList<string> List()
    {
        if (!Directory.Exists(_dir)) return Array.Empty<string>();
        return Directory.EnumerateFiles(_dir, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n != null)
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void Save(RgbProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        Directory.CreateDirectory(_dir);
        string path = PathFor(profile.Name);
        // Write-then-rename for atomicity: a crash mid-write must not corrupt the profile.
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(profile, JsonOptions));
        File.Move(tmp, path, overwrite: true);
    }

    public RgbProfile Load(string name)
    {
        string path = PathFor(name);
        if (!File.Exists(path)) throw new FileNotFoundException($"Profile '{name}' not found", path);
        var profile = JsonSerializer.Deserialize<RgbProfile>(File.ReadAllText(path))
            ?? throw new InvalidDataException($"Profile '{name}' is corrupt");
        profile.Validate();
        return profile;
    }

    public bool Delete(string name)
    {
        string path = PathFor(name);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    private string PathFor(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return Path.Combine(_dir, name + ".json");
    }
}
