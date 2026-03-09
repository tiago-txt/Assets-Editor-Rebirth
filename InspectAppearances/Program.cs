using System.Text.Json;
using Tibia.Protobuf.Appearances;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: InspectAppearances <things-dir|appearances-file>");
    return 1;
}

string inputPath = args[0];
string appearancesPath;
string? catalogPath = null;
if (Directory.Exists(inputPath))
{
    appearancesPath = Path.Combine(inputPath, "appearances.dat");
    catalogPath = Path.Combine(inputPath, "catalog-content.json");
}
else
{
    appearancesPath = inputPath;
}

if (!File.Exists(appearancesPath))
{
    Console.Error.WriteLine($"appearances file not found: {appearancesPath}");
    return 2;
}

using var stream = File.OpenRead(appearancesPath);
var appearances = Appearances.Parser.ParseFrom(stream);
var catalogs = catalogPath != null && File.Exists(catalogPath)
    ? LoadCatalogs(catalogPath)
    : new List<CatalogEntry>();

InspectObject(appearances, catalogs, 1722);
Console.WriteLine();
InspectMissile(appearances, catalogs, 58);

return 0;

static void InspectObject(Appearances appearances, List<CatalogEntry> catalogs, uint id)
{
    var appearance = appearances.Object.FirstOrDefault(x => x.Id == id);
    InspectAppearance("item", appearance, catalogs, id);
}

static void InspectMissile(Appearances appearances, List<CatalogEntry> catalogs, uint id)
{
    var appearance = appearances.Missile.FirstOrDefault(x => x.Id == id);
    InspectAppearance("missile", appearance, catalogs, id);
}

static void InspectAppearance(string label, Appearance? appearance, List<CatalogEntry> catalogs, uint id)
{
    if (appearance == null)
    {
        Console.WriteLine($"{label} {id}: not found");
        return;
    }

    Console.WriteLine($"{label} {id}: found");
    Console.WriteLine($"name: {appearance.Name}");
    Console.WriteLine($"frameGroups: {appearance.FrameGroup.Count}");

    for (int fgIndex = 0; fgIndex < appearance.FrameGroup.Count; fgIndex++)
    {
        var fg = appearance.FrameGroup[fgIndex];
        var si = fg.SpriteInfo;
        if (si == null)
        {
            Console.WriteLine($"  fg {fgIndex}: no SpriteInfo");
            continue;
        }

        var ids = si.SpriteId.ToList();
        int zeroCount = ids.Count(x => x == 0);
        int missingCatalog = catalogs.Count == 0
            ? 0
            : ids.Count(x => x != 0 && !catalogs.Any(c => x >= c.FirstSpriteId && x <= c.LastSpriteId));

        Console.WriteLine(
            $"  fg {fgIndex}: layers={si.Layers}, pw={si.PatternWidth}, ph={si.PatternHeight}, pz={si.PatternDepth}, frames={si.PatternFrames}, count={ids.Count}, zeros={zeroCount}, missingCatalog={missingCatalog}");

        if (ids.Count > 0)
        {
            Console.WriteLine($"    first ids: {string.Join(", ", ids.Take(20))}");
        }

        Console.WriteLine($"    spriteData count: {appearance.SpriteData.Count}");

        foreach (var badId in ids.Where(x => x != 0 && catalogs.Count > 0 && !catalogs.Any(c => x >= c.FirstSpriteId && x <= c.LastSpriteId)).Distinct().Take(20))
        {
            Console.WriteLine($"    id outside catalog: {badId}");
        }
    }
}

static List<CatalogEntry> LoadCatalogs(string path)
{
    using var stream = File.OpenRead(path);
    using var doc = JsonDocument.Parse(stream);
    var result = new List<CatalogEntry>();

    foreach (var element in doc.RootElement.EnumerateArray())
    {
        if (!element.TryGetProperty("type", out var typeProp) || typeProp.GetString() != "sprite")
            continue;

        int first = element.TryGetProperty("firstspriteid", out var firstProp)
            ? firstProp.GetInt32()
            : element.TryGetProperty("FirstSpriteid", out var firstAlt)
                ? firstAlt.GetInt32()
                : -1;
        int last = element.TryGetProperty("lastspriteid", out var lastProp)
            ? lastProp.GetInt32()
            : element.TryGetProperty("LastSpriteid", out var lastAlt)
                ? lastAlt.GetInt32()
                : -1;

        if (first >= 0 && last >= first)
        {
            result.Add(new CatalogEntry(first, last));
        }
    }

    return result;
}

internal readonly record struct CatalogEntry(int FirstSpriteId, int LastSpriteId);
