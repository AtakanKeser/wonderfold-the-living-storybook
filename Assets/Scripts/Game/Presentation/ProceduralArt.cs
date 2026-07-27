using System.Collections.Generic;
using UnityEngine;
using Wonderfold.Core.Primitives;

namespace Wonderfold.Game.Presentation
{
    /// <summary>
    /// Generates every sprite the vertical slice needs at runtime.
    ///
    /// <para>This exists so the project is playable the moment it is opened — no art import, no missing
    /// prefab references, no pink materials. It is scaffolding for the real art pass, not a substitute
    /// for it: shapes, silhouettes and the colour hierarchy are here, the paper shaders and character
    /// animation are not.</para>
    /// </summary>
    public static class ProceduralArt
    {
        private const int TextureSize = 128;
        private const float PixelsPerUnit = 128f;

        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        /// <summary>The Midnight Carnival palette. Strong hue separation matters more than prettiness.</summary>
        public static Color ColorOf(TileColor color)
        {
            switch (color)
            {
                case TileColor.Crimson: return new Color(0.91f, 0.28f, 0.33f);
                case TileColor.Azure: return new Color(0.29f, 0.60f, 0.93f);
                case TileColor.Meadow: return new Color(0.36f, 0.78f, 0.45f);
                case TileColor.Amber: return new Color(0.98f, 0.76f, 0.25f);
                case TileColor.Violet: return new Color(0.66f, 0.44f, 0.90f);
                case TileColor.Blush: return new Color(0.95f, 0.55f, 0.72f);
                default: return new Color(0.75f, 0.75f, 0.78f);
            }
        }

        public static Color ColorOf(ObstacleType type)
        {
            switch (type)
            {
                case ObstacleType.TornPage: return new Color(0.86f, 0.82f, 0.72f);
                case ObstacleType.WaxSeal: return new Color(0.62f, 0.13f, 0.19f);
                case ObstacleType.FoldLock: return new Color(0.75f, 0.62f, 0.25f);
                case ObstacleType.ArmourPlate: return new Color(0.52f, 0.55f, 0.62f);
                case ObstacleType.StoryKnot: return new Color(0.85f, 0.70f, 0.35f);
                case ObstacleType.PaperChain: return new Color(0.70f, 0.72f, 0.80f);
                case ObstacleType.VanishingInk: return new Color(0.30f, 0.30f, 0.38f);
                case ObstacleType.BlankStain: return new Color(0.93f, 0.93f, 0.95f);
                default: return Color.gray;
            }
        }

        /// <summary>Rounded square — the base shape of every tile.</summary>
        public static Sprite RoundedSquare(float cornerRadius = 0.28f, float inset = 0.06f)
        {
            string key = $"rs:{cornerRadius}:{inset}";
            if (Cache.TryGetValue(key, out var cached)) return cached;

            var texture = NewTexture();
            var pixels = new Color[TextureSize * TextureSize];
            float radius = cornerRadius * TextureSize;
            float pad = inset * TextureSize;

            for (int y = 0; y < TextureSize; y++)
            for (int x = 0; x < TextureSize; x++)
            {
                float distance = RoundedBoxDistance(x, y, pad, radius);
                // One pixel of feathering keeps the silhouette clean at any zoom.
                float alpha = Mathf.Clamp01(0.5f - distance);
                pixels[y * TextureSize + x] = new Color(1f, 1f, 1f, alpha);
            }

            return Finish(texture, pixels, key);
        }

        /// <summary>
        /// A softened paper-cut tile: subtly uneven ink density and a folded lower-right corner make the
        /// otherwise abstract pieces read as printed card rather than plastic gems.
        /// </summary>
        public static Sprite PaperTile()
        {
            const string key = "paper-tile";
            if (Cache.TryGetValue(key, out var cached)) return cached;

            var texture = NewTexture();
            var pixels = new Color[TextureSize * TextureSize];
            float radius = TextureSize * 0.25f;
            float pad = TextureSize * 0.055f;
            for (int y = 0; y < TextureSize; y++)
            for (int x = 0; x < TextureSize; x++)
            {
                float distance = RoundedBoxDistance(x, y, pad, radius);
                float alpha = Mathf.Clamp01(0.7f - distance);
                if (x > TextureSize * 0.76f && y < TextureSize * 0.24f && x + y > TextureSize * 1.12f)
                    alpha *= 0.58f;
                float grain = ((x * 13 + y * 7) % 19) / 19f;
                pixels[y * TextureSize + x] = new Color(0.96f + grain * 0.04f,
                    0.96f + grain * 0.04f, 0.96f + grain * 0.04f, alpha);
            }

            return Finish(texture, pixels, key);
        }

        /// <summary>A tiny dog-eared corner used on tiles and restored paper scenery.</summary>
        public static Sprite FoldedCorner()
        {
            const string key = "folded-corner";
            if (Cache.TryGetValue(key, out var cached)) return cached;
            var texture = NewTexture();
            var pixels = new Color[TextureSize * TextureSize];
            for (int y = 0; y < TextureSize; y++)
            for (int x = 0; x < TextureSize; x++)
            {
                float u = x / (float)TextureSize;
                float v = y / (float)TextureSize;
                bool inside = u > 0.56f && v < 0.44f && u + v > 1.12f;
                pixels[y * TextureSize + x] = new Color(1f, 1f, 1f, inside ? 0.82f : 0f);
            }
            return Finish(texture, pixels, key);
        }

        /// <summary>Ring used for booster overlays and selection highlights.</summary>
        public static Sprite Ring(float thickness = 0.10f)
        {
            string key = $"ring:{thickness}";
            if (Cache.TryGetValue(key, out var cached)) return cached;

            var texture = NewTexture();
            var pixels = new Color[TextureSize * TextureSize];
            float outer = TextureSize * 0.46f;
            float inner = outer - TextureSize * thickness;
            var centre = new Vector2(TextureSize * 0.5f, TextureSize * 0.5f);

            for (int y = 0; y < TextureSize; y++)
            for (int x = 0; x < TextureSize; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), centre);
                float alpha = Mathf.Clamp01(Mathf.Min(outer - d, d - inner));
                pixels[y * TextureSize + x] = new Color(1f, 1f, 1f, alpha);
            }

            return Finish(texture, pixels, key);
        }

        /// <summary>Solid disc — ink blooms, meter fills, character stand-ins.</summary>
        public static Sprite Disc()
        {
            const string key = "disc";
            if (Cache.TryGetValue(key, out var cached)) return cached;

            var texture = NewTexture();
            var pixels = new Color[TextureSize * TextureSize];
            var centre = new Vector2(TextureSize * 0.5f, TextureSize * 0.5f);
            float radius = TextureSize * 0.46f;

            for (int y = 0; y < TextureSize; y++)
            for (int x = 0; x < TextureSize; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), centre);
                pixels[y * TextureSize + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(radius - d));
            }

            return Finish(texture, pixels, key);
        }

        /// <summary>Arrow, used for rockets so their firing axis is readable at a glance.</summary>
        public static Sprite Chevron()
        {
            const string key = "chevron";
            if (Cache.TryGetValue(key, out var cached)) return cached;

            var texture = NewTexture();
            var pixels = new Color[TextureSize * TextureSize];

            for (int y = 0; y < TextureSize; y++)
            for (int x = 0; x < TextureSize; x++)
            {
                float u = x / (float)TextureSize;
                float v = Mathf.Abs(y / (float)TextureSize - 0.5f) * 2f;
                bool inside = u > 0.15f && u < 0.85f && v < 1f - Mathf.Abs(u - 0.5f) * 1.6f;
                pixels[y * TextureSize + x] = new Color(1f, 1f, 1f, inside ? 1f : 0f);
            }

            return Finish(texture, pixels, key);
        }

        /// <summary>Flat 1x1 sprite for panels and bars.</summary>
        public static Sprite Solid()
        {
            const string key = "solid";
            if (Cache.TryGetValue(key, out var cached)) return cached;

            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
            texture.SetPixels(pixels);
            texture.Apply();

            var sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            sprite.name = key;
            Cache[key] = sprite;
            return sprite;
        }

        private static float RoundedBoxDistance(int x, int y, float pad, float radius)
        {
            float half = TextureSize * 0.5f - pad;
            float px = Mathf.Abs(x + 0.5f - TextureSize * 0.5f);
            float py = Mathf.Abs(y + 0.5f - TextureSize * 0.5f);
            float qx = px - (half - radius);
            float qy = py - (half - radius);
            float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }

        private static Texture2D NewTexture() =>
            new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

        private static Sprite Finish(Texture2D texture, Color[] pixels, string key)
        {
            texture.SetPixels(pixels);
            texture.Apply();

            var sprite = Sprite.Create(texture, new Rect(0, 0, TextureSize, TextureSize),
                new Vector2(0.5f, 0.5f), PixelsPerUnit);
            sprite.name = key;
            Cache[key] = sprite;
            return sprite;
        }

        public static Sprite LoadAuthoredSprite(string name)
        {
            if (Cache.TryGetValue(name, out var cached)) return cached;

            // Resources is the supported runtime path. Unity imports the image and selects the
            // platform-appropriate decoder; bypassing that with ImageConversion would require the
            // optional Image Conversion module, which this lightweight project does not depend on.
            var tex = Resources.Load<Texture2D>($"Art/{name}");

            if (tex != null)
            {
                float ppu = Mathf.Max(tex.width, tex.height) * 0.95f; // Leaves a bit of padding so it fits nicely in a cell
                var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), ppu);
                sprite.name = name;
                Cache[name] = sprite;
                return sprite;
            }
            
            return null; // Return null if not found, allowing fallbacks to procedural
        }
    }
}
