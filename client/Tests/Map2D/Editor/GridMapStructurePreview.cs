using System;
using System.Collections.Generic;
using System.IO;
using BigWorld.Map2D;
using BigWorld.Map2D.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Read-only asset preview export for an isolated Unity validation project.
/// Execute GridMapStructurePreview.RunBatch with -mapStructurePreview <directory>.
/// Decodes source image bytes into temporary CPU-readable textures; no importer,
/// scene, map, type, or template is modified, and no editor window is opened.
/// </summary>
public static class GridMapStructurePreview
{
    private const int CellPixels = 32;
    private const int Padding = 16;
    private static readonly Color32 Background = new Color32(22, 28, 39, 255);

    private sealed class Bitmap
    {
        public int Width;
        public int Height;
        public Color32[] Pixels;
    }

    [Serializable] private sealed class ExportItem
    {
        public string name;
        public string asset;
        public string image;
        public int cellsWide;
        public int cellsHigh;
    }

    [Serializable] private sealed class ExportReport
    {
        public string generatedUtc;
        public int pixelsPerCell;
        public string overview;
        public List<ExportItem> templates = new List<ExportItem>();
        public List<string> fallbackTypes = new List<string>();
    }

    private static readonly Dictionary<string, Bitmap> DecodedImages = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> FallbackTypes = new HashSet<string>();

    public static void RunBatch()
    {
        string output = GetOutputDirectory();
        Directory.CreateDirectory(output);
        string[] assets = { GridMapStructureAssets.HutPath, GridMapStructureAssets.HousePath, GridMapStructureAssets.WarehousePath };
        string[] fileNames = { "WoodenHut.png", "TwoStoreyHouse.png", "Warehouse.png" };
        var rendered = new List<Bitmap>();
        var report = new ExportReport { generatedUtc = DateTime.UtcNow.ToString("O"), pixelsPerCell = CellPixels };
        try
        {
            for (int i = 0; i < assets.Length; i++)
            {
                MapStructureTemplate template = AssetDatabase.LoadAssetAtPath<MapStructureTemplate>(assets[i]);
                if (template == null || template.Layout == null)
                    throw new InvalidOperationException("Preview requires an existing template with layout: " + assets[i]);
                Bitmap bitmap = Render(template.Layout);
                string imagePath = Path.Combine(output, fileNames[i]);
                WritePng(bitmap, imagePath);
                rendered.Add(bitmap);
                report.templates.Add(new ExportItem
                {
                    name = template.DisplayName,
                    asset = assets[i],
                    image = imagePath,
                    cellsWide = template.Width,
                    cellsHigh = template.Height
                });
            }
            report.overview = Path.Combine(output, "HousesOverview.png");
            WritePng(Combine(rendered), report.overview);
            report.fallbackTypes.AddRange(FallbackTypes);
            File.WriteAllText(Path.Combine(output, "preview-manifest.json"), JsonUtility.ToJson(report, true));
            Debug.Log("Map2D structure previews exported to " + output);
        }
        finally
        {
            DecodedImages.Clear();
            FallbackTypes.Clear();
        }
    }

    private static Bitmap Render(GridMapAsset map)
    {
        Bitmap result = Blank(map.Width * CellPixels + Padding * 2, map.Height * CellPixels + Padding * 2);
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                // Unity pixel arrays and map coordinates both start at the lower-left.
                int left = Padding + x * CellPixels;
                int bottom = Padding + y * CellPixels;
                DrawTile(result, map.GetCell(x, y, MapLayer.Terrain), left, bottom);
                DrawTile(result, map.GetCell(x, y, MapLayer.Objects), left, bottom);
            }
        return result;
    }

    private static void DrawTile(Bitmap destination, MapTileType type, int left, int bottom)
    {
        if (type == null) return;
        if (type.Sprite == null)
        {
            FallbackTypes.Add(type.DisplayName);
            for (int y = 0; y < CellPixels; y++)
                for (int x = 0; x < CellPixels; x++) Blend(destination, left + x, bottom + y, type.Tint);
            return;
        }

        string assetPath = AssetDatabase.GetAssetPath(type.Sprite);
        Bitmap source = DecodeImage(assetPath);
        Rect rect = type.Sprite.rect;
        // Sprite rectangles use imported texture dimensions. Account for importer
        // scaling while reading the original file, without changing readability.
        float scaleX = (float)source.Width / type.Sprite.texture.width;
        float scaleY = (float)source.Height / type.Sprite.texture.height;
        int sourceLeft = Mathf.Clamp(Mathf.RoundToInt(rect.x * scaleX), 0, source.Width - 1);
        int sourceBottom = Mathf.Clamp(Mathf.RoundToInt(rect.y * scaleY), 0, source.Height - 1);
        int sourceWidth = Mathf.Clamp(Mathf.RoundToInt(rect.width * scaleX), 1, source.Width - sourceLeft);
        int sourceHeight = Mathf.Clamp(Mathf.RoundToInt(rect.height * scaleY), 1, source.Height - sourceBottom);
        float fit = Mathf.Min((float)CellPixels / sourceWidth, (float)CellPixels / sourceHeight);
        int drawWidth = Mathf.Max(1, Mathf.RoundToInt(sourceWidth * fit));
        int drawHeight = Mathf.Max(1, Mathf.RoundToInt(sourceHeight * fit));
        int offsetX = (CellPixels - drawWidth) / 2;
        int offsetY = (CellPixels - drawHeight) / 2;
        Color tint = type.Tint;
        for (int y = 0; y < drawHeight; y++)
            for (int x = 0; x < drawWidth; x++)
            {
                // Integer nearest-neighbor scaling keeps the original 16px art sharp.
                int sourceX = sourceLeft + x * sourceWidth / drawWidth;
                int sourceY = sourceBottom + y * sourceHeight / drawHeight;
                Color color = source.Pixels[sourceX + sourceY * source.Width];
                color.r *= tint.r;
                color.g *= tint.g;
                color.b *= tint.b;
                color.a *= tint.a;
                Blend(destination, left + offsetX + x, bottom + offsetY + y, color);
            }
    }

    private static Bitmap DecodeImage(string assetPath)
    {
        Bitmap cached;
        if (DecodedImages.TryGetValue(assetPath, out cached)) return cached;
        if (string.IsNullOrEmpty(assetPath)) throw new InvalidOperationException("A template sprite has no source asset path.");
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string absolute = Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        if (!File.Exists(absolute)) throw new FileNotFoundException("Template sprite source image was not found.", absolute);
        Texture2D image = null;
        try
        {
            image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            image.hideFlags = HideFlags.HideAndDontSave;
            if (!image.LoadImage(File.ReadAllBytes(absolute), false))
                throw new InvalidOperationException("Cannot decode the template sprite source image: " + assetPath);
            cached = new Bitmap { Width = image.width, Height = image.height, Pixels = image.GetPixels32() };
            DecodedImages.Add(assetPath, cached);
            return cached;
        }
        finally { if (image != null) Object.DestroyImmediate(image); }
    }

    private static void Blend(Bitmap image, int x, int y, Color foreground)
    {
        float alpha = Mathf.Clamp01(foreground.a);
        if (alpha <= 0f) return;
        int index = x + y * image.Width;
        Color background = image.Pixels[index];
        image.Pixels[index] = new Color(
            Mathf.Clamp01(foreground.r) * alpha + background.r * (1f - alpha),
            Mathf.Clamp01(foreground.g) * alpha + background.g * (1f - alpha),
            Mathf.Clamp01(foreground.b) * alpha + background.b * (1f - alpha), 1f);
    }

    private static Bitmap Blank(int width, int height)
    {
        Bitmap image = new Bitmap { Width = width, Height = height, Pixels = new Color32[width * height] };
        for (int i = 0; i < image.Pixels.Length; i++) image.Pixels[i] = Background;
        return image;
    }

    private static Bitmap Combine(List<Bitmap> images)
    {
        const int gap = 24;
        int width = gap;
        int height = 0;
        foreach (Bitmap image in images)
        {
            width += image.Width + gap;
            height = Math.Max(height, image.Height);
        }
        Bitmap result = Blank(width, height + gap * 2);
        int offset = gap;
        foreach (Bitmap image in images)
        {
            for (int y = 0; y < image.Height; y++)
                Array.Copy(image.Pixels, y * image.Width, result.Pixels, (y + gap) * result.Width + offset, image.Width);
            offset += image.Width + gap;
        }
        return result;
    }

    private static void WritePng(Bitmap bitmap, string path)
    {
        Texture2D output = null;
        try
        {
            output = new Texture2D(bitmap.Width, bitmap.Height, TextureFormat.RGBA32, false);
            output.hideFlags = HideFlags.HideAndDontSave;
            output.SetPixels32(bitmap.Pixels);
            output.Apply(false, false);
            File.WriteAllBytes(path, output.EncodeToPNG());
        }
        finally { if (output != null) Object.DestroyImmediate(output); }
    }

    private static string GetOutputDirectory()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        for (int i = 0; i < arguments.Length - 1; i++)
            if (arguments[i] == "-mapStructurePreview") return Path.GetFullPath(arguments[i + 1]);
        return Path.GetFullPath(Path.Combine(Application.dataPath, "../TestResults/Map2D/StructurePreviews"));
    }
}
