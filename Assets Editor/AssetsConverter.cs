using Google.Protobuf;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Tibia.Protobuf.Appearances;

namespace Assets_Editor;

public static class AssetsConverter
{
    private sealed class LowercaseContractResolver : DefaultContractResolver
    {
        protected override string ResolvePropertyName(string propertyName)
        {
            return propertyName.ToLower();
        }
    }

    /// <summary>Progress report: (percent 0-100, message).</summary>
    public static void ConvertLegacyToAssets(string outputPath, Appearances appearances, SpriteStorage spriteStorage, IProgress<(int percent, string message)> progress = null)
    {
        if (outputPath == null) throw new ArgumentNullException(nameof(outputPath));
        if (appearances == null) throw new ArgumentNullException(nameof(appearances));
        if (spriteStorage == null) throw new ArgumentNullException(nameof(spriteStorage));

        void Report(int percent, string message) => progress?.Report((percent, message));

        Directory.CreateDirectory(outputPath);

        Report(2, "Normalizing appearances...");
        // 1. Normalize bounding from first tile
        AppearanceNormalizer.NormalizeForAssets(appearances, spriteStorage);

        Report(10, "Merging multi-tile sprites...");
        // 2. Merge multi-tile into composed bitmaps (Beast-style)
        Dictionary<int, Bitmap> composedSprites = MergeMultiTileSprites(appearances, spriteStorage);

        // 3. Optional pad to 192 with bottom-right anchor (applied inside merge; composedSprites already final)

        Report(18, "Applying bounding boxes...");
        // 4. Bounding pass for merged frame groups
        ApplyBoundingForComposed(appearances, composedSprites);

        Report(22, "Remapping pattern fields...");
        // 4.5. Remap pattern fields so DatEditor.GetSpriteIndex uses correct multipliers (direction vs frame).
        RemapPatternForAssetsIndex(appearances);

        // 5. Max sprite ID (includes new composed IDs)
        int maxSpriteIdUsed = GetMaxSpriteIdUsed(appearances);
        if (spriteStorage.SprLists != null)
        {
            int maxStored = spriteStorage.SprLists.Keys.DefaultIfEmpty(-1).Max();
            maxSpriteIdUsed = Math.Max(maxSpriteIdUsed, maxStored);
        }
        foreach (int id in composedSprites.Keys)
            maxSpriteIdUsed = Math.Max(maxSpriteIdUsed, id);

        // 5.5. Ensure no SpriteId = 0 remains; replace zeros by a shared transparent sprite id.
        FixZeroSpriteIds(appearances, composedSprites, ref maxSpriteIdUsed);

        Report(28, "Writing appearances.dat...");
        // 6. Write appearances first
        string appearancesFileName = WriteAppearances(outputPath, appearances);

        // 7. Generate sheets (32x32 then 64/96/192) with stable filenames
        List<MainWindow.Catalog> catalogEntries = [];
        catalogEntries.Add(new MainWindow.Catalog
        {
            Type = "appearances",
            File = Path.GetFileName(appearancesFileName),
            SpriteType = int.MinValue,
            FirstSpriteid = int.MinValue,
            LastSpriteid = int.MinValue,
            Area = 0,
            Version = 0
        });

        catalogEntries.AddRange(GenerateSpriteSheets(outputPath, spriteStorage, maxSpriteIdUsed, composedSprites, progress));

        foreach (Bitmap bmp in composedSprites.Values)
            bmp?.Dispose();

        catalogEntries.Add(new MainWindow.Catalog
        {
            Type = "staticdata",
            File = "staticdata.dat",
            SpriteType = int.MinValue,
            FirstSpriteid = int.MinValue,
            LastSpriteid = int.MinValue,
            Area = 0,
            Version = 0
        });

        catalogEntries.Add(new MainWindow.Catalog
        {
            Type = "staticmapdata",
            File = "staticmapdata.dat",
            SpriteType = int.MinValue,
            FirstSpriteid = int.MinValue,
            LastSpriteid = int.MinValue,
            Area = 0,
            Version = 0
        });

        catalogEntries.Add(new MainWindow.Catalog
        {
            Type = "map",
            File = "map.dat",
            SpriteType = int.MinValue,
            FirstSpriteid = int.MinValue,
            LastSpriteid = int.MinValue,
            Area = 0,
            Version = 0
        });

        Report(92, "Writing placeholder data files...");
        WritePlaceholderDataFiles(outputPath);

        Report(95, "Writing catalog...");
        WriteCatalog(outputPath, catalogEntries);
        Report(100, "Done.");
    }

    /// <summary>Creates staticdata.dat, staticmapdata.dat and map.dat so OTClient finds them when loading the catalog.</summary>
    private static void WritePlaceholderDataFiles(string outputPath)
    {
        string[] placeholderFiles = { "staticdata.dat", "staticmapdata.dat", "map.dat" };
        foreach (string name in placeholderFiles)
        {
            string path = Path.Combine(outputPath, name);
            if (!File.Exists(path))
            {
                using (File.Create(path)) { }
            }
        }
    }

    private static (int w, int h) GetSpriteDimensions(SpriteStorage spriteStorage, Dictionary<int, Bitmap> composedSprites, int id)
    {
        if (composedSprites != null && composedSprites.TryGetValue(id, out Bitmap bmp) && bmp != null)
            return (bmp.Width, bmp.Height);
        try
        {
            MemoryStream stream = spriteStorage.getSpriteStream((uint)id);
            if (stream != null && stream.Length > 0)
            {
                stream.Position = 0;
                using Image img = Image.FromStream(stream, false, false);
                return (img.Width, img.Height);
            }
        }
        catch { }
        return (Sprite.DefaultSize, Sprite.DefaultSize);
    }

    /// <summary>Must match DatEditor.SpriteSizes indices so the loader slices sheets correctly (e.g. 96x96 = index 14, not 4).</summary>
    private static int GetSpriteType(int w, int h)
    {
        if (w == 32 && h == 32) return 0;
        if (w == 32 && h == 64) return 1;
        if (w == 64 && h == 32) return 2;
        if (w == 64 && h == 64) return 3;
        if (w == 96 && h == 96) return 14;  // DatEditor SpriteSizes[14] = (96,96); [4] is (32,96)
        if (w == 192 && h == 192) return 28; // DatEditor SpriteSizes[28] = (192,192)
        if (w == 384 && h == 384) return 35; // DatEditor SpriteSizes[35] = (384,384) — used for 8x8 (256) padded
        return 0; // fallback
    }

    /// <summary>Standard sizes for padding (bottom-right anchor). 384 so 8x8 (256) pads to 384x384.</summary>
    private static readonly int[] StandardSizes = { 64, 96, 192, 384 };

    private static int RoundUpToStandard(int size)
    {
        foreach (int s in StandardSizes)
            if (size <= s) return s;
        return 384;
    }

    /// <summary>Pad composed bitmap to next standard size (64/96/192) with content at bottom-right.</summary>
    private static Bitmap PadToStandardBottomRight(Bitmap composed)
    {
        int w = composed.Width;
        int h = composed.Height;
        int s = Math.Max(RoundUpToStandard(w), RoundUpToStandard(h));
        if (w == s && h == s) return composed;
        var padded = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(padded))
        {
            g.Clear(Color.Transparent);
            g.DrawImage(composed, s - w, s - h, w, h);
        }
        composed.Dispose();
        return padded;
    }

    /// <summary>Pad to standard size with content centered (for strips like 32×96 so they become 96×96 centered).</summary>
    private static Bitmap PadToStandardCentered(Bitmap composed)
    {
        int w = composed.Width;
        int h = composed.Height;
        int s = Math.Max(RoundUpToStandard(w), RoundUpToStandard(h));
        if (w == s && h == s) return composed;
        var padded = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(padded))
        {
            g.Clear(Color.Transparent);
            int x = (s - w) / 2;
            int y = (s - h) / 2;
            g.DrawImage(composed, x, y, w, h);
        }
        composed.Dispose();
        return padded;
    }

    /// <summary>Beast-style merge: multi-tile frame groups become one bitmap per pattern. Tries multiple index orders.</summary>
    private static Dictionary<int, Bitmap> MergeMultiTileSprites(Appearances appearances, SpriteStorage spriteStorage)
    {
        var composedSprites = new Dictionary<int, Bitmap>();
        if (appearances == null || spriteStorage?.SprLists == null) return composedSprites;

        int nextId = Math.Max(GetMaxSpriteIdUsed(appearances), spriteStorage.SprLists.Keys.DefaultIfEmpty(-1).Max()) + 1;
        const int tileSize = 32;

        foreach (var list in new[] { appearances.Outfit, appearances.Object, appearances.Effect, appearances.Missile })
        {
            if (list == null) continue;
            foreach (var appearance in list)
            {
                if (appearance.FrameGroup == null) continue;
                foreach (var frameGroup in appearance.FrameGroup)
                {
                    var si = frameGroup.SpriteInfo;
                    if (si?.SpriteId == null || si.SpriteId.Count == 0) continue;

                    int pw = (int)si.PatternWidth;
                    int ph = (int)si.PatternHeight;
                    if (pw * ph <= 1) continue;

                    int pl = (int)(si.PatternLayers != 0 ? si.PatternLayers : (si.Layers != 0 ? si.Layers : 1));
                    int px = (int)(si.PatternX != 0 ? si.PatternX : 1);
                    int py = (int)(si.PatternY != 0 ? si.PatternY : 1);
                    int pz = (int)(si.PatternZ != 0 ? si.PatternZ : 1);
                    int pf = (int)(si.PatternFrames != 0 ? si.PatternFrames : 1);

                    if (pz != 1) continue; // mount (patternZ) not handled in merge
                    // Merge multi-tile frames when there are multiple layers (blend layers, addons layers)
                    // so that a 64x64/96x96 outfit correctly becomes large bitmaps per layer.
                    if (pw * ph > 256 || pf > 16 || px * py > 16) continue;

                    int T = pw * ph;
                    int N = px * py * pf * pl;

                    // Only merge 9 IDs into 3×3 when it is actually a 3×3 tile grid (not e.g. 9 missile directions).
                    if (pw == 3 && ph == 3 && si.SpriteId.Count == 9 && N == 1)
                    {
                        const int gridSize = 3;
                        int canvasSide = gridSize * tileSize;
                        Bitmap canvas = new(canvasSide, canvasSide, PixelFormat.Format32bppArgb);
                        int drawn = 0;
                        for (int order = 0; order < 2; order++)
                        {
                            using (var g = Graphics.FromImage(canvas))
                                g.Clear(Color.Transparent);
                            drawn = 0;
                            for (int row = 0; row < gridSize; row++)
                            {
                                for (int col = 0; col < gridSize; col++)
                                {
                                    int idx = order == 0 ? (row * gridSize + col) : (col * gridSize + row);
                                    if (idx >= si.SpriteId.Count) continue;
                                    uint sid = si.SpriteId[idx];
                                    if (sid == 0) continue;
                                    try
                                    {
                                        MemoryStream stream = spriteStorage.getSpriteStream(sid);
                                        stream.Position = 0;
                                        using Bitmap tile = new(stream);
                                        int destX = col * tileSize;
                                        int destY = (gridSize - 1 - row) * tileSize;
                                        using (var gg = Graphics.FromImage(canvas))
                                            gg.DrawImage(tile, destX, destY, tileSize, tileSize);
                                        drawn++;
                                    }
                                    catch { }
                                }
                            }
                            if (drawn == 9) break;
                        }

                        if (drawn > 0)
                        {
                            composedSprites[nextId] = canvas;
                            si.SpriteId.Clear();
                            si.SpriteId.Add((uint)nextId);
                            SetSingleSpritePatternAndAnimation(si);
                            si.PatternWidth = 1;
                            si.PatternHeight = 1;
                            si.PatternSize = (uint)RoundUpToStandard(canvasSide);
                            nextId++;
                        }

                        continue;
                    }

                    // Multiple 1×3 columns combined into one 3×3 (96×96): e.g. 3 columns × 3 rows = 9 IDs.
                    // Try row-major (row 0: 0,1,2; row 1: 3,4,5; row 2: 6,7,8) then column-major.
                    if (pw == 1 && ph == 3 && N == 3 && si.SpriteId.Count == 9)
                    {
                        int gridCols = 3;
                        int gridRows = 3;
                        int canvasW = gridCols * tileSize;
                        int canvasH = gridRows * tileSize;
                        Bitmap canvas = new(canvasW, canvasH, PixelFormat.Format32bppArgb);
                        using (var g = Graphics.FromImage(canvas))
                            g.Clear(Color.Transparent);

                        bool wroteAny = false;
                        for (int row = 0; row < gridRows; row++)
                        {
                            for (int col = 0; col < gridCols; col++)
                            {
                                int idx = row * gridCols + col;
                                if (idx >= si.SpriteId.Count) continue;
                                uint sid = si.SpriteId[idx];
                                if (sid == 0) continue;
                                try
                                {
                                    MemoryStream stream = spriteStorage.getSpriteStream(sid);
                                    stream.Position = 0;
                                    using Bitmap tile = new(stream);
                                    int destX = col * tileSize;
                                    int destY = (gridRows - 1 - row) * tileSize;
                                    using (var gg = Graphics.FromImage(canvas))
                                        gg.DrawImage(tile, destX, destY, tileSize, tileSize);
                                    wroteAny = true;
                                }
                                catch { }
                            }
                        }

                        if (wroteAny)
                        {
                            composedSprites[nextId] = canvas;
                            si.SpriteId.Clear();
                            si.SpriteId.Add((uint)nextId);
                            SetSingleSpritePatternAndAnimation(si);
                            si.PatternWidth = 1;
                            si.PatternHeight = 1;
                            si.PatternSize = (uint)RoundUpToStandard(Math.Max(canvasW, canvasH));
                            nextId++;
                        }

                        continue;
                    }

                    // Simple grid case: single pattern (no directions/frames) where spriteId is exactly a pw*ph grid.
                    if (N == 1 && si.SpriteId.Count == T && px == 1 && py == 1 && pf == 1)
                    {
                        int canvasW = pw * tileSize;
                        int canvasH = ph * tileSize;
                        Bitmap canvas = new(canvasW, canvasH, PixelFormat.Format32bppArgb);
                        using (var g = Graphics.FromImage(canvas))
                            g.Clear(Color.Transparent);

                        bool wroteAny = false;
                        for (int h = 0; h < ph; h++)
                        {
                            for (int wTile = 0; wTile < pw; wTile++)
                            {
                                int idx = h * pw + wTile;
                                if (idx < 0 || idx >= si.SpriteId.Count) continue;
                                uint sid = si.SpriteId[idx];
                                if (sid == 0) continue;
                                try
                                {
                                    MemoryStream stream = spriteStorage.getSpriteStream(sid);
                                    stream.Position = 0;
                                    using Bitmap tile = new(stream);
                                    int destX = (pw - 1 - wTile) * tileSize;
                                    int destY = (ph - 1 - h) * tileSize;
                                    using (var gg = Graphics.FromImage(canvas))
                                        gg.DrawImage(tile, destX, destY, tileSize, tileSize);
                                    wroteAny = true;
                                }
                                catch { }
                            }
                        }

                        if (wroteAny)
                        {
                            // Use centered padding for strips (1×3 or 3×1) so 32×96 becomes 96×96 with content centered.
                            bool isStrip = (pw == 1 && ph >= 2) || (ph == 1 && pw >= 2);
                            canvas = isStrip ? PadToStandardCentered(canvas) : PadToStandardBottomRight(canvas);
                            composedSprites[nextId] = canvas;
                            si.SpriteId.Clear();
                            si.SpriteId.Add((uint)nextId);
                            SetSingleSpritePatternAndAnimation(si);
                            si.PatternWidth = 1;
                            si.PatternHeight = 1;
                            int mergedSize = Math.Max(pw, ph) * tileSize;
                            si.PatternSize = (uint)RoundUpToStandard(mergedSize);
                            nextId++;
                        }

                        continue;
                    }

                    if (si.SpriteId.Count != T * N) continue;

                    // Choose best order for this specific appearance instead of per-frame, to ensure consistency between layers/directions.
                    int bestOrder = 0;
                    bool foundAnyOrder = false;
                    for (int o = 0; o < 4 && !foundAnyOrder; o++)
                    {
                        for (int pCheck = 0; pCheck < N && !foundAnyOrder; pCheck++)
                        {
                            int lC = pCheck % pl;
                            int restC = (pCheck / pl) % (px * py);
                            int xC = restC % px;
                            int yC = restC / px;
                            int fC = pCheck / (pl * px * py);
                            for (int h = 0; h < ph; h++)
                            {
                                for (int w = 0; w < pw; w++)
                                {
                                    int idx = GetMergeIndex(o, fC, xC, yC, lC, h, w, pw, ph, px, py, pl, pf);
                                    if (idx >= 0 && idx < si.SpriteId.Count && si.SpriteId[idx] != 0)
                                    {
                                        bestOrder = o;
                                        foundAnyOrder = true;
                                        break;
                                    }
                                }
                                if (foundAnyOrder) break;
                            }
                        }
                    }

                    var newIds = new List<uint>();
                    for (int p = 0; p < N; p++)
                    {
                        int layer = p % pl;
                        int rest = (p / pl) % (px * py);
                        int xpat = rest % px;
                        int ypat = rest / px;
                        int f = p / (pl * px * py);

                        int canvasW = pw * tileSize;
                        int canvasH = ph * tileSize;
                        Bitmap canvas = new(canvasW, canvasH, PixelFormat.Format32bppArgb);
                        using (var g = Graphics.FromImage(canvas))
                            g.Clear(Color.Transparent);

                        bool wroteAny = false;
                        for (int h = 0; h < ph; h++)
                        {
                            for (int w = 0; w < pw; w++)
                            {
                                int idx = GetMergeIndex(bestOrder, f, xpat, ypat, layer, h, w, pw, ph, px, py, pl, pf);
                                if (idx < 0 || idx >= si.SpriteId.Count) continue;
                                uint sid = si.SpriteId[idx];
                                if (sid == 0) continue;
                                try
                                {
                                    MemoryStream stream = spriteStorage.getSpriteStream(sid);
                                    stream.Position = 0;
                                    using Bitmap tile = new(stream);
                                    int destX = (pw - 1 - w) * tileSize;
                                    int destY = (ph - 1 - h) * tileSize;
                                    // Use tile's actual size so smaller blend/addon layers are not stretched to 32x32
                                    int tw = Math.Max(1, tile.Width);
                                    int th = Math.Max(1, tile.Height);
                                    using (var gg = Graphics.FromImage(canvas))
                                        gg.DrawImage(tile, destX, destY, tw, th);
                                    wroteAny = true;
                                }
                                catch { }
                            }
                        }

                        if (wroteAny)
                        {
                            canvas = PadToStandardBottomRight(canvas);
                            composedSprites[nextId] = canvas;
                            newIds.Add((uint)nextId);
                            nextId++;
                        }
                        else
                        {
                            canvas.Dispose();
                            uint fallbackId = FindFirstNonZeroSourceSpriteId(si, f, xpat, ypat, layer, pw, ph, px, py, pl, pf);
                            newIds.Add(fallbackId);
                        }
                    }

                    if (newIds.Count == N && newIds.Any(x => x != 0))
                    {
                        si.SpriteId.Clear();
                        si.SpriteId.Add(newIds);
                        // Mark as merged: tile grid is now 1x1 large images
                        si.PatternWidth = 1; 
                        si.PatternHeight = 1;
                        int mergedSize = Math.Max(pw, ph) * tileSize;
                        si.PatternSize = (uint)RoundUpToStandard(mergedSize);
                        // These remain as multipliers for directions/addons/mounts
                        si.Layers = (uint)pl;
                        si.PatternLayers = (uint)pl;
                    }
                }
            }
        }

        return composedSprites;
    }

    /// <summary>Set pattern and animation to single-sprite state after merging multiple tiles into one.</summary>
    private static void SetSingleSpritePatternAndAnimation(SpriteInfo si)
    {
        if (si == null) return;
        si.PatternX = 1;
        si.PatternY = 1;
        si.PatternZ = 1;
        si.PatternFrames = 1;
        si.PatternLayers = 1;
        si.Animation = null;
    }

    /// <summary>Remap pattern fields so DatEditor.GetSpriteIndex indexes correctly (frames vs directions).
    /// Legacy stores directions in PatternX/Y/Z and tile grid in PatternWidth/Height; DatEditor uses PatternWidth/Height/Depth as index multipliers.</summary>
    private static void RemapPatternForAssetsIndex(Appearances appearances)
    {
        if (appearances == null) return;
        foreach (var list in new[] { appearances.Outfit, appearances.Object, appearances.Effect, appearances.Missile })
        {
            if (list == null) continue;
            foreach (var appearance in list)
            {
                if (appearance.FrameGroup == null) continue;
                foreach (var frameGroup in appearance.FrameGroup)
                {
                    var si = frameGroup.SpriteInfo;
                    if (si == null) continue;

                    // Source (Legacy format in SpriteInfo after DAT reading):
                    // si.PatternWidth/Height = Tile Grid (e.g. 2x2)
                    // si.PatternX/Y/Z = Multipliers (Directions/Addons/Mount)

                    // Target (Tibia 12+ format in Proto):
                    // pattern_width/height/depth = Multipliers (Directions/Addons/Mount)
                    // pattern_x/y/z = Tile Grid (1x1, 2x2, etc)

                    uint gridW = si.PatternWidth;
                    uint gridH = si.PatternHeight;
                    uint gridD = si.PatternZ > 1 && si.PatternWidth == 1 && si.PatternHeight == 1 ? si.PatternZ : 1; // Simplify gridZ

                    uint multW = si.PatternX != 0 ? si.PatternX : 1;
                    uint multH = si.PatternY != 0 ? si.PatternY : 1;
                    uint multD = si.PatternZ != 0 ? si.PatternZ : 1;

                    // Note: If merged, gridW/gridH were already set to 1 in MergeMultiTileSprites.
                    si.PatternWidth = multW;
                    si.PatternHeight = multH;
                    si.PatternDepth = multD;

                    si.PatternX = gridW;
                    si.PatternY = gridH;
                    si.PatternZ = gridD;

                    si.Layers = si.PatternLayers > 0 ? si.PatternLayers : 1;
                }
            }
        }
    }

    private static int GetMergeIndex(int order, int f, int xpat, int ypat, int layer, int h, int w, int pw, int ph, int px, int py, int pl, int pf)
    {
        // Legacy internal order: ((f*pz + z)*py + y)*px + x)*pl + l)*ph + h)*pw + w
        // This order differs between some Poketibia versions. Order 0 is standard Tibia.
        int pz = 1;
        switch (order)
        {
            case 0: // Standard Pattern-Major
                return ((((((f * pz + 0) * py + ypat) * px + xpat) * pl + layer) * ph + h) * pw + w);
            case 1: // Tile-Major (some items/effects)
                return (((((f * pz + 0) * py + ypat) * pl + layer) * (pw * ph) + (h * pw + w)) * px + xpat);
            case 2: // Alternate Poketibia Layer-Major
                return ((((((f * pz + 0) * pl + layer) * py + ypat) * px + xpat) * ph + h) * pw + w);
            case 3: // Column-major grid
                return ((((((f * pz + 0) * py + ypat) * px + xpat) * pl + layer) * pw + w) * ph + h);
            default:
                return (h * pw + w);
        }
    }

    private static uint FindFirstNonZeroSourceSpriteId(SpriteInfo si, int f, int xpat, int ypat, int layer, int pw, int ph, int px, int py, int pl, int pf)
    {
        if (si?.SpriteId == null || si.SpriteId.Count == 0)
            return 0;

        for (int order = 0; order < 4; order++)
        {
            for (int h = 0; h < ph; h++)
            {
                for (int w = 0; w < pw; w++)
                {
                    int idx = GetMergeIndex(order, f, xpat, ypat, layer, h, w, pw, ph, px, py, pl, pf);
                    if (idx < 0 || idx >= si.SpriteId.Count)
                        continue;

                    uint sid = si.SpriteId[idx];
                    if (sid != 0)
                        return sid;
                }
            }
        }

        return 0;
    }

    private static void ApplyBoundingForComposed(Appearances appearances, Dictionary<int, Bitmap> composedSprites)
    {
        if (appearances == null || composedSprites == null) return;
        foreach (var list in new[] { appearances.Outfit, appearances.Object, appearances.Effect, appearances.Missile })
        {
            if (list == null) continue;
            foreach (var appearance in list)
            {
                if (appearance.FrameGroup == null) continue;
                foreach (var frameGroup in appearance.FrameGroup)
                {
                    var si = frameGroup.SpriteInfo;
                    if (si?.SpriteId == null || si.SpriteId.Count == 0) continue;
                    uint firstId = si.SpriteId[0];
                    if (firstId == 0) continue;
                    if (!composedSprites.TryGetValue((int)firstId, out Bitmap bmp) || bmp == null) continue;
                    int w = bmp.Width;
                    int h = bmp.Height;
                    int square = Math.Max(w, h);
                    if (!si.HasBoundingSquare) si.BoundingSquare = (uint)square;
                    if (si.BoundingBoxPerDirection == null || si.BoundingBoxPerDirection.Count == 0)
                    {
                        si.BoundingBoxPerDirection.Clear();
                        int dirs = Math.Max(1, Math.Min(4, (int)(si.PatternX != 0 ? si.PatternX : 4)));
                        for (int i = 0; i < dirs; i++)
                            si.BoundingBoxPerDirection.Add(new Box { X = 0, Y = 0, Width = (uint)w, Height = (uint)h });
                    }
                }
            }
        }
    }

    /// <summary>
    /// Replaces any remaining SpriteId == 0 in appearances by a valid transparent sprite id.
    /// Creates one shared transparent sprite in composedSprites and reuses it everywhere.
    /// This prevents OTClient from seeing sprite id 0 (which it treats as invalid).
    /// </summary>
    private static void FixZeroSpriteIds(
        Appearances appearances,
        Dictionary<int, Bitmap> composedSprites,
        ref int maxSpriteIdUsed)
    {
        if (appearances == null)
            return;

        bool hasZero =
            (appearances.Outfit?.Any(a => a.FrameGroup.Any(fg => fg.SpriteInfo?.SpriteId?.Any(id => id == 0) == true)) == true) ||
            (appearances.Object?.Any(a => a.FrameGroup.Any(fg => fg.SpriteInfo?.SpriteId?.Any(id => id == 0) == true)) == true) ||
            (appearances.Effect?.Any(a => a.FrameGroup.Any(fg => fg.SpriteInfo?.SpriteId?.Any(id => id == 0) == true)) == true) ||
            (appearances.Missile?.Any(a => a.FrameGroup.Any(fg => fg.SpriteInfo?.SpriteId?.Any(id => id == 0) == true)) == true);

        if (!hasZero)
            return;

        int transparentId = maxSpriteIdUsed + 1;
        maxSpriteIdUsed = transparentId;

        const int size = Sprite.DefaultSize;
        var blank = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(blank))
        {
            g.Clear(Color.Transparent);
        }
        composedSprites[transparentId] = blank;

        void ReplaceInList(ICollection<Appearance> list)
        {
            if (list == null) return;
            foreach (var appearance in list)
            {
                if (appearance.FrameGroup == null) continue;
                foreach (var frameGroup in appearance.FrameGroup)
                {
                    var si = frameGroup.SpriteInfo;
                    if (si?.SpriteId == null || si.SpriteId.Count == 0) continue;
                    for (int i = 0; i < si.SpriteId.Count; i++)
                    {
                        if (si.SpriteId[i] == 0)
                            si.SpriteId[i] = (uint)transparentId;
                    }
                }
            }
        }

        ReplaceInList(appearances.Outfit);
        ReplaceInList(appearances.Object);
        ReplaceInList(appearances.Effect);
        ReplaceInList(appearances.Missile);
    }

    private static List<MainWindow.Catalog> GenerateSpriteSheets(
        string outputPath,
        SpriteStorage spriteStorage,
        int maxSpriteIdUsed,
        Dictionary<int, Bitmap> composedSprites,
        IProgress<(int percent, string message)> progress = null)
    {
        List<MainWindow.Catalog> entries = [];
        composedSprites ??= new Dictionary<int, Bitmap>();

        if (spriteStorage == null || maxSpriteIdUsed < 0)
            return entries;

        int totalSprites = maxSpriteIdUsed + 1;
        const int progressSheetStart = 30;
        const int progressSheetEnd = 92;

        // Match Beast-Assets-Editor sprite_pack.rs: fixed sheet width 384, height grows per chunk.
        const int sheetSize = 384;
        const int sheetWidth = sheetSize;
        int sheetIndex = 0;

        int firstId = 0;
        while (firstId <= maxSpriteIdUsed)
        {
            var (tileW, tileH) = GetSpriteDimensions(spriteStorage, composedSprites, firstId);

            int lastId = firstId;
            while (lastId < maxSpriteIdUsed)
            {
                var nextDim = GetSpriteDimensions(spriteStorage, composedSprites, lastId + 1);
                if (nextDim.w != tileW || nextDim.h != tileH) break;
                lastId++;
            }

            int cols = sheetWidth / tileW;
            int maxRows = sheetSize / tileH;
            int spritesPerSheet = cols * maxRows;
            if (spritesPerSheet == 0)
            {
                firstId = lastId + 1;
                continue;
            }

            int currentId = firstId;
            while (currentId <= lastId)
            {
                int chunkEnd = Math.Min(currentId + spritesPerSheet - 1, lastId);
                using Bitmap sheetBitmap = new(sheetWidth, sheetSize, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(sheetBitmap))
                {
                    // Match the editor's own asset compiler path for maximum compatibility.
                    g.Clear(Color.FromArgb(0, 255, 0, 255));

                    for (int j = currentId; j <= chunkEnd; j++)
                    {
                        int localIndex = j - currentId;
                        int row = localIndex / cols;
                        int col = localIndex % cols;

                        int destX = col * tileW;
                        int destY = row * tileH;

                        if (composedSprites.TryGetValue(j, out Bitmap composed) && composed != null)
                        {
                            g.DrawImage(composed, destX, destY, tileW, tileH);
                        }
                        else
                        {
                            try
                            {
                                // Do not dispose the stream: it is the storage's reference; disposing would wipe all sprites in the editor
                                MemoryStream stream = spriteStorage.getSpriteStream((uint)j);
                                stream.Position = 0;
                                using Bitmap spriteBitmap = new(stream);
                                g.DrawImage(spriteBitmap, destX, destY, tileW, tileH);
                            }
                            catch (Exception ex)
                            {
                                MainWindow.Log($"Failed to draw sprite {j}: {ex.Message}", "Error");
                            }
                        }
                    }
                }

                string stableName = $"sprites-{sheetIndex:D4}.bmp.lzma";
                // Use the original export implementation that the editor already consumes correctly,
                // then rename the generated hash file to a deterministic file name.
                string generatedName = outputPath;
                LZMA.ExportLzmaFile(sheetBitmap, ref generatedName);
                string generatedPath = Path.Combine(outputPath, generatedName);
                string stablePath = Path.Combine(outputPath, stableName);
                if (!generatedName.Equals(stableName, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(stablePath))
                        File.Delete(stablePath);
                    File.Move(generatedPath, stablePath);
                }

                entries.Add(new MainWindow.Catalog
                {
                    Type = "sprite",
                    File = stableName,
                    SpriteType = GetSpriteType(tileW, tileH),
                    FirstSpriteid = currentId,
                    LastSpriteid = chunkEnd,
                    Area = 0,
                    Version = 0
                });

                if (progress != null && totalSprites > 0)
                {
                    int pct = progressSheetStart + (int)((long)(chunkEnd + 1) * (progressSheetEnd - progressSheetStart) / totalSprites);
                    progress.Report((Math.Min(pct, progressSheetEnd), $"Generating sprite sheets... ({sheetIndex + 1})"));
                }

                sheetIndex++;
                currentId = chunkEnd + 1;
            }

            firstId = lastId + 1;
        }

        return entries;
    }

    private static void WriteCatalog(string outputPath, List<MainWindow.Catalog> catalogEntries)
    {
        string catalogPath = Path.Combine(outputPath, "catalog-content.json");

        using StreamWriter file = File.CreateText(catalogPath);
        JsonSerializer serializer = new()
        {
            Formatting = Formatting.Indented,
            ContractResolver = new LowercaseContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore
        };

        serializer.Serialize(file, catalogEntries);
    }

    private static string WriteAppearances(string outputPath, Appearances appearances)
    {
        string appearancesPath = Path.Combine(outputPath, "appearances.dat");
        using FileStream output = File.Create(appearancesPath);
        appearances.WriteTo(output);
        return appearancesPath;
    }

    private static int GetMaxSpriteIdUsed(Appearances appearances)
    {
        int maxId = -1;

        if (appearances == null)
            return maxId;

        foreach (var appearance in appearances.Outfit
                     .Concat(appearances.Object)
                     .Concat(appearances.Effect)
                     .Concat(appearances.Missile))
        {
            if (appearance.FrameGroup == null || appearance.FrameGroup.Count == 0)
                continue;

            foreach (var frameGroup in appearance.FrameGroup)
            {
                var spriteInfo = frameGroup.SpriteInfo;
                if (spriteInfo == null || spriteInfo.SpriteId == null)
                    continue;

                foreach (var id in spriteInfo.SpriteId)
                {
                    if (id > maxId)
                        maxId = (int)id;
                }
            }
        }

        return maxId;
    }
}
