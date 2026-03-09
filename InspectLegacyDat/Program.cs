using Assets_Editor;
using AppearanceMessage = Tibia.Protobuf.Appearances.Appearance;
using Tibia.Protobuf.Appearances;

if (args.Length < 2)
{
    Console.WriteLine("Usage: InspectLegacyDat <Tibia.dat path> <version>");
    return 1;
}

string datPath = args[0];
int version = int.Parse(args[1]);

var datStructure = new DatStructure();
var versionInfo = datStructure.GetVersionInfo(version);

var presets = new[]
{
    new PresetSettings { Extended = false, FrameDurations = false, FrameGroups = false },
    new PresetSettings { Extended = true, FrameDurations = false, FrameGroups = false },
    new PresetSettings { Extended = true, FrameDurations = true, FrameGroups = false },
    new PresetSettings { Extended = true, FrameDurations = true, FrameGroups = true },
    new PresetSettings { Extended = false, FrameDurations = true, FrameGroups = false },
    new PresetSettings { Extended = false, FrameDurations = true, FrameGroups = true },
    new PresetSettings { Extended = true, FrameDurations = false, FrameGroups = true },
    new PresetSettings { Extended = false, FrameDurations = false, FrameGroups = true },
};

foreach (var preset in presets)
{
    try
    {
        using var stream = File.OpenRead(datPath);
        using var reader = new BinaryReader(stream);
        var info = DatStructure.ReadAppearanceInfo(reader);
        var appearances = new Appearances();

        for (int i = 0; i < info.OutfitCount; i++)
            appearances.Outfit.Add(DatStructure.ReadAppearance(reader, APPEARANCE_TYPE.AppearanceOutfit, versionInfo, preset));
        for (int i = 0; i < info.ObjectCount; i++)
            appearances.Object.Add(DatStructure.ReadAppearance(reader, APPEARANCE_TYPE.AppearanceObject, versionInfo, preset));
        for (int i = 0; i < info.EffectCount; i++)
            appearances.Effect.Add(DatStructure.ReadAppearance(reader, APPEARANCE_TYPE.AppearanceEffect, versionInfo, preset));
        for (int i = 0; i < info.MissileCount; i++)
            appearances.Missile.Add(DatStructure.ReadAppearance(reader, APPEARANCE_TYPE.AppearanceMissile, versionInfo, preset));

        Console.WriteLine($"Preset ok: ext={preset.Extended}, dur={preset.FrameDurations}, grp={preset.FrameGroups}");
        Dump("item", appearances.Object.ElementAtOrDefault(1722 - 100), 1722);
        Dump("missile", appearances.Missile.ElementAtOrDefault(58 - 1), 58);
        Console.WriteLine();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Preset fail: ext={preset.Extended}, dur={preset.FrameDurations}, grp={preset.FrameGroups} -> {ex.Message}");
    }
}

return 0;

static void Dump(string label, AppearanceMessage appearance, int displayId)
{
    if (appearance == null)
    {
        Console.WriteLine($"{label} {displayId}: not found");
        return;
    }

    Console.WriteLine($"{label} {displayId}: frameGroups={appearance.FrameGroup.Count}");
    for (int i = 0; i < appearance.FrameGroup.Count; i++)
    {
        var si = appearance.FrameGroup[i].SpriteInfo;
        Console.WriteLine($"  fg {i}: pw={si.PatternWidth}, ph={si.PatternHeight}, pl={si.PatternLayers}, px={si.PatternX}, py={si.PatternY}, pz={si.PatternZ}, pf={si.PatternFrames}, count={si.SpriteId.Count}, zeros={si.SpriteId.Count(x => x == 0)}");
        Console.WriteLine($"    first ids: {string.Join(", ", si.SpriteId.Take(20))}");
    }
}

namespace Assets_Editor
{
    public class PresetSettings
    {
        public bool Extended { get; set; }
        public bool FrameDurations { get; set; }
        public bool FrameGroups { get; set; }
    }

    public static class ErrorManager
    {
        public static void ShowWarning(string message)
        {
            Console.Error.WriteLine(message);
        }
    }

    public static class ObdDecoder
    {
        public static void SetDatStructure(VersionInfo versionInfo)
        {
        }
    }
}
