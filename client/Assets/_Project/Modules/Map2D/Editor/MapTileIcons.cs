using System.IO;
using UnityEditor;
using UnityEngine;

namespace BigWorld.Map2D.Editor
{
    /// <summary>Small original placeholder sprites so the first map is readable without external art.</summary>
    internal static class MapTileIcons
    {
        public const string Folder = "Assets/_Project/Game/Art/MapTiles";

        public static Sprite EnsureSprite(string id)
        {
            GridMapAssets.EnsureFolder(Folder);
            string path = Folder + "/" + id + ".png";
            if (!File.Exists(path))
            {
                var image = new Texture2D(16, 16, TextureFormat.RGBA32, false);
                var pixels = new Color[256];
                Color edge = C(36, 38, 45), earth = C(111, 73, 52), gold = C(238, 187, 69);
                System.Action<int, int, int, int, Color> box = (x, y, w, h, color) =>
                {
                    for (int yy = y; yy < y + h; yy++)
                    for (int xx = x; xx < x + w; xx++)
                        if (xx >= 0 && xx < 16 && yy >= 0 && yy < 16) pixels[yy * 16 + xx] = color;
                };
                switch (id)
                {
                    case "Ground":
                        box(0, 0, 16, 16, earth); box(0, 13, 16, 3, C(100, 161, 80));
                        box(0, 12, 16, 1, C(60, 104, 62));
                        box(2, 5, 3, 2, C(144, 99, 63)); box(9, 8, 4, 2, C(144, 99, 63));
                        box(7, 1, 3, 2, C(76, 55, 48)); box(14, 4, 2, 2, C(76, 55, 48));
                        box(1, 15, 3, 1, C(164, 196, 105)); box(10, 15, 4, 1, C(164, 196, 105));
                        break;
                    case "Spikes":
                        box(0, 0, 16, 2, edge); box(0, 2, 16, 1, C(181, 54, 61));
                        for (int center = 2; center < 16; center += 5)
                        for (int y = 3; y < 13; y++)
                        {
                            int radius = (12 - y) / 4;
                            box(center - radius, y, radius * 2 + 1, 1, C(176, 195, 201));
                            box(center, y, 1, 1, C(234, 242, 233));
                        }
                        break;
                    case "SpawnPoint":
                        box(3, 0, 9, 2, edge); box(5, 2, 2, 13, C(213, 226, 231));
                        box(7, 9, 7, 5, C(52, 189, 227)); box(7, 13, 7, 1, C(167, 243, 251));
                        box(10, 8, 4, 2, C(38, 128, 182));
                        break;
                    case "ExplosiveBarrel":
                        box(3, 1, 10, 14, edge); box(4, 2, 8, 12, C(181, 60, 49));
                        box(5, 2, 2, 12, C(224, 92, 60)); box(3, 3, 10, 2, C(110, 123, 132));
                        box(3, 11, 10, 2, C(110, 123, 132)); box(4, 14, 8, 1, C(178, 185, 173));
                        box(7, 6, 3, 4, gold); box(8, 7, 1, 2, edge);
                        break;
                    case "TreasureChest":
                        box(1, 1, 14, 11, edge); box(2, 2, 12, 9, earth);
                        box(3, 12, 10, 2, edge); box(3, 10, 10, 3, C(160, 99, 52));
                        box(2, 7, 12, 2, gold); box(3, 2, 2, 10, C(214, 146, 53));
                        box(11, 2, 2, 10, C(214, 146, 53)); box(7, 6, 3, 4, C(251, 218, 116));
                        box(8, 7, 1, 2, edge);
                        break;
                    case "Wall":
                        box(0, 0, 16, 16, C(80, 62, 56));
                        for (int row = 0; row < 4; row++)
                        {
                            int offset = (row & 1) == 0 ? 0 : -4;
                            for (int x = offset; x < 16; x += 8)
                            {
                                box(x, row * 4 + 1, 7, 3, C(153, 105, 77));
                                box(x + 1, row * 4 + 3, 5, 1, C(184, 135, 95));
                            }
                        }
                        break;
                    case "Window":
                        box(1, 1, 14, 14, edge); box(2, 2, 12, 12, C(142, 89, 49));
                        box(3, 3, 10, 10, C(72, 137, 164)); box(4, 4, 8, 8, C(123, 192, 214));
                        box(4, 10, 3, 2, C(211, 238, 230)); box(9, 5, 2, 2, C(211, 238, 230));
                        box(7, 3, 2, 10, C(114, 70, 45)); box(3, 7, 10, 2, C(114, 70, 45));
                        box(0, 0, 16, 2, C(176, 126, 74));
                        break;
                    case "WoodenDoor":
                        box(2, 0, 12, 16, edge); box(3, 1, 10, 14, C(148, 91, 45));
                        box(4, 1, 2, 14, C(185, 119, 57)); box(7, 1, 2, 14, C(169, 103, 47));
                        box(10, 1, 2, 14, C(185, 119, 57));
                        box(3, 3, 10, 2, C(91, 78, 66)); box(3, 11, 10, 2, C(91, 78, 66));
                        box(4, 4, 1, 1, C(173, 177, 162)); box(4, 12, 1, 1, C(173, 177, 162));
                        box(10, 7, 2, 2, gold); box(10, 8, 1, 1, C(255, 225, 128));
                        break;
                    case "Stairs":
                        for (int step = 0; step < 4; step++)
                        {
                            int height = (step + 1) * 4;
                            box(step * 4, 0, 4, height, C(109, 91, 73));
                            box(step * 4, height - 2, 4, 2, C(202, 181, 137));
                            box(step * 4, 0, 1, height - 2, C(74, 67, 59));
                        }
                        break;
                    default:
                        Object.DestroyImmediate(image);
                        return null;
                }
                image.SetPixels(pixels); image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG()); Object.DestroyImmediate(image);
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 16;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static Color C(byte r, byte g, byte b) { return new Color32(r, g, b, 255); }
    }
}
