using Il2CppSystem.Collections.Generic;
using Il2CppSystem.IO;
using MelonLoader.TinyJSON;
using Styletor.Jsons;
using System;
using UnhollowerBaseLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Styletor.Utils
{
    public class SpriteSnipperUtil
    {
        public static readonly Dictionary<Texture2D, Texture2D> ourTextureToReadableMap = new();
        public static readonly Dictionary<Texture2D, Texture2D> ourGrayscaledTexturesMap = new();
        public static readonly Dictionary<Sprite, Sprite> ourGrayscaledSpritesMap = new();

        public static void SaveSpriteAsPngWithMetadata(Sprite original, string path)
        {
            var newTexture = Copy(EnsureReadable(original.texture), original.textureRect);

            var bytes = newTexture.EncodeAsPng();
            if (bytes != null)
            {
                File.WriteAllBytes(path, bytes);
                var metadata = new SpriteJson(original);
                System.IO.File.WriteAllText(path + ".json", JSON.Dump(metadata));
            }

            Object.Destroy(newTexture);
        }

        private static void ProcessPixelsToGrayscaleNormalized(Il2CppStructArray<Color> pixels)
        {
            var scaleFactor = 0f;
            for (var i = 0; i < pixels.Count; i++)
            {
                var pixel = pixels[i];
                if (pixel.a == 0) continue;

                var g = pixel.Grayscale();
                if (g > scaleFactor)
                    scaleFactor = g;
            }

            for (var i = 0; i < pixels.Count; i++)
            {
                var pixel = pixels[i];
                var g = pixel.Grayscale() / scaleFactor;
                pixels[i] = new Color { r = g, g = g, b = g, a = pixel.a };
            }
        }

        private static void ProcessPixelsToGrayscale(Il2CppStructArray<Color> pixels)
        {
            for (var i = 0; i < pixels.Count; i++)
            {
                var pixel = pixels[i];
                var g = pixel.Grayscale();
                pixels[i] = new Color { r = g, g = g, b = g, a = pixel.a };
            }
        }

        public static Texture2D GetGrayscaledTexture(Texture2D original, bool normalizeWhite)
        {
            if (ourGrayscaledTexturesMap.ContainsKey(original))
                return ourGrayscaledTexturesMap[original];

            var newTexture = Copy(EnsureReadable(original), normalizeWhite ? ProcessPixelsToGrayscaleNormalized : ProcessPixelsToGrayscale);

            newTexture.hideFlags |= HideFlags.DontUnloadUnusedAsset;

            return ourGrayscaledTexturesMap[original] = newTexture;
        }

        private static Rect GetTextureRect(Sprite sprite)
        {
            if (!sprite.packed || sprite.packingMode != SpritePackingMode.Tight) 
                return sprite.textureRect;
            
            var xMin = 1f;
            var yMin = 1f;
            var xMax = 0f;
            var yMax = 0f;
                
            foreach (var v in sprite.uv)
            {
                if (xMax < v.x) xMax = v.x;
                if (xMin > v.x) xMin = v.x;
                if (yMax < v.y) yMax = v.y;
                if (yMin > v.y) yMin = v.y;
            }

            var texSize = (sprite.texture.width, sprite.texture.height);
            return new Rect
            {
                m_XMin = xMin * texSize.width,
                m_YMin = yMin * texSize.height,
                m_Width = (xMax - xMin) * texSize.width,
                m_Height = (yMax - yMin) * texSize.height
            };

        }

        public static Sprite GetGrayscaledSprite(Sprite original, bool normalizeWhite)
        {
            if (ourGrayscaledSpritesMap.ContainsKey(original))
                return ourGrayscaledSpritesMap[original];

            var newTexture = Copy(EnsureReadable(original.texture), GetTextureRect(original), normalizeWhite ? ProcessPixelsToGrayscaleNormalized : ProcessPixelsToGrayscale);

            newTexture.hideFlags |= HideFlags.DontUnloadUnusedAsset;

            var rect = new Rect(0, 0, newTexture.width, newTexture.height);
            var pivot = original.pivot / original.rect.size;
            var border = original.border;

            var newSprite = Sprite.CreateSprite_Injected(newTexture, ref rect, ref pivot, original.pixelsPerUnit, 0,
                SpriteMeshType.FullRect, ref border, false);

            newSprite.hideFlags |= HideFlags.DontUnloadUnusedAsset;

            return ourGrayscaledSpritesMap[original] = newSprite;
        }

        private static Texture2D EnsureReadable(Texture2D source)
        {
            if (source.isReadable) return source;
            if (ourTextureToReadableMap.ContainsKey(source)) return ourTextureToReadableMap[source];

            var newTexture = ourTextureToReadableMap[source] = ForceReadTexture(source);
            newTexture.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            return newTexture;
        }

        public static Texture2D Copy(Texture2D orig, Action<Il2CppStructArray<Color>>? processPixels = null)
        {
            return Copy(orig, new Rect(0, 0, orig.width, orig.height), processPixels);
        }

        // These two methods are adapted from UnityExplorer, licensed under GPLv3
        // https://github.com/sinai-dev/UnityExplorer/blob/6aa9b3aa15dfccefb4661b1ce7e521b6492e04fd/src/Core/Runtime/TextureUtilProvider.cs#L10
        public static Texture2D Copy(Texture2D orig, Rect rect, Action<Il2CppStructArray<Color>>? processPixels = null)
        {
            if (!orig.isReadable)
                orig = ForceReadTexture(orig);

            var pixels = orig.GetPixels((int)rect.x, (int)rect.y, (int)rect.width, (int)rect.height);
            var newTex = new Texture2D((int)rect.width, (int)rect.height);
            processPixels?.Invoke(pixels);
            newTex.SetPixels(pixels);
            newTex.Apply();
            return newTex;
        }

        public static Texture2D ForceReadTexture(Texture2D tex)
        {
            var origFilter = tex.filterMode;
            tex.filterMode = FilterMode.Point;

            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
            rt.filterMode = FilterMode.Point;
            RenderTexture.active = rt;

            Graphics.Blit2(tex, rt);

            var newTex = new Texture2D(tex.width, tex.height, TextureFormat.ARGB32, false);

            newTex.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            newTex.Apply(false, false);

            RenderTexture.active = null;
            tex.filterMode = origFilter;

            RenderTexture.ReleaseTemporary(rt);

            return newTex;
        }
    }
}