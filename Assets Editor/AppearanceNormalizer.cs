using System;
using System.Collections.Generic;
using System.Linq;
using Tibia.Protobuf.Appearances;
using System.Drawing;
using System.IO;

namespace Assets_Editor;

public static class AppearanceNormalizer
{
    public static void NormalizeForAssets(Appearances appearances, SpriteStorage spriteStorage)
    {
        if (appearances == null)
            return;

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
                if (spriteInfo == null)
                    continue;

                // ensure minimal sane pattern sizes (legacy: PatternWidth/Height = tile grid, PatternX/Y/Z = directions/addon/mount)
                if (spriteInfo.PatternWidth == 0) spriteInfo.PatternWidth = 1;
                if (spriteInfo.PatternHeight == 0) spriteInfo.PatternHeight = 1;
                if (spriteInfo.PatternDepth == 0) spriteInfo.PatternDepth = 1;
                if (spriteInfo.Layers == 0) spriteInfo.Layers = 1;
                if (spriteInfo.PatternFrames == 0) spriteInfo.PatternFrames = 1;
                if (spriteInfo.PatternX == 0) spriteInfo.PatternX = 1;
                if (spriteInfo.PatternY == 0) spriteInfo.PatternY = 1;
                if (spriteInfo.PatternZ == 0) spriteInfo.PatternZ = 1;
                if (spriteInfo.PatternLayers == 0) spriteInfo.PatternLayers = 1;

                // normalize animation: keep frames and phases, ensure LoopType is set for assets
                if (spriteInfo.Animation != null && spriteInfo.Animation.SpritePhase != null && spriteInfo.Animation.SpritePhase.Count > 0)
                {
                    spriteInfo.PatternFrames = (uint)spriteInfo.Animation.SpritePhase.Count;
                    if (!spriteInfo.Animation.HasLoopType)
                        spriteInfo.Animation.LoopType = ANIMATION_LOOP_TYPE.Infinite;
                    for (int k = 0; k < spriteInfo.Animation.SpritePhase.Count; k++)
                    {
                        var phase = spriteInfo.Animation.SpritePhase[k];
                        if (phase.DurationMin == 0) phase.DurationMin = 100;
                        if (phase.DurationMax == 0) phase.DurationMax = 100;
                    }
                }

                if (spriteInfo.SpriteId == null || spriteInfo.SpriteId.Count == 0)
                    continue;

                uint firstId = spriteInfo.SpriteId[0];
                bool needBounding = spriteInfo.BoundingBoxPerDirection == null || 
                                    spriteInfo.BoundingBoxPerDirection.Count == 0 || 
                                    !spriteInfo.HasBoundingSquare;
                
                if (!needBounding)
                    continue;

                int tileW = Sprite.DefaultSize;
                int tileH = Sprite.DefaultSize;

                try
                {
                    MemoryStream stream = spriteStorage.getSpriteStream(firstId);
                    stream.Position = 0;
                    using Image img = Image.FromStream(stream, false, false);
                    tileW = img.Width;
                    tileH = img.Height;
                }
                catch { }

                // Logical footprint: tile grid (PatternWidth/Height) × sprite size.
                int footprintW = tileW * Math.Max(1, (int)spriteInfo.PatternWidth);
                int footprintH = tileH * Math.Max(1, (int)spriteInfo.PatternHeight);
                int square = Math.Max(footprintW, footprintH);

                // bounding square: based on the full logical footprint
                if (!spriteInfo.HasBoundingSquare)
                {
                    spriteInfo.BoundingSquare = (uint)square;
                }

                // bounding box per direction: based on the physical sprite size
                if (spriteInfo.BoundingBoxPerDirection == null ||
                    spriteInfo.BoundingBoxPerDirection.Count == 0)
                {
                    int directions = (int)(spriteInfo.PatternX != 0 ? spriteInfo.PatternX : 4);
                    directions = Math.Max(1, Math.Min(4, directions));

                    spriteInfo.BoundingBoxPerDirection.Clear();
                    for (int i = 0; i < directions; i++)
                    {
                        spriteInfo.BoundingBoxPerDirection.Add(new Box
                        {
                            X = 0,
                            Y = 0,
                            Width = (uint)footprintW,
                            Height = (uint)footprintH
                        });
                    }
                }

                if (!spriteInfo.HasIsOpaque)
                {
                    spriteInfo.IsOpaque = false;
                }
            }
        }
    }
}
