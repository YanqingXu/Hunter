using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using SpriteAsset = UnityEngine.Sprite;

namespace BigWorld.Editor
{
    /// <summary>Creates project-owned slices without changing the supplied SSW source textures.</summary>
    public static class HunterSswMaterials
    {
        public const string ArtFolder = "Assets/_Project/Game/SSWMaps/Art";
        public const string TerrainPath = ArtFolder + "/Terrain.png";
        public const string DecorationsPath = ArtFolder + "/Decorations.png";
        private const string SourceFolder = "Assets/SSW_SET1/Sliced";
        private static Dictionary<string, SpriteAsset> sprites;

        // Rect coordinates use Unity's texture convention: origin at the bottom left.
        // The forest surface is shifted down 26 source pixels so the grass meets the
        // top of a solid map cell. The original top slices contain only six grass pixels.
        private static readonly SpriteMetaData[] TerrainSlices =
        {
            Tile("ForestTop", 32, 1254),
            Tile("ForestFill", 32, 1216),
            Tile("ForestFillAlt", 64, 1216),
            Tile("ForestLeft", 0, 1248),
            Tile("ForestRight", 96, 1248),
            Tile("CaveTop", 32, 736),
            Tile("CaveFill", 64, 704),
            Tile("CaveFillAlt", 96, 704),
            Tile("CaveLeft", 0, 704),
            Tile("CaveRight", 128, 704),
            Tile("CaveRoof", 32, 608),
            Tile("KeepTop", 288, 317),
            Tile("KeepFill", 288, 256),
            Tile("KeepFillAlt", 320, 256),
            Tile("KeepLeft", 256, 288),
            Tile("KeepRight", 384, 288),
            Tile("Bridge", 864, 1152),
            Tile("BridgeAlt", 896, 1152),
            Decoration("Pillar", 430, 39, 36, 304),
            // The supplied arch is part of a brick facade; retain the complete doorway.
            Decoration("Arch", 192, 0, 96, 96)
        };

        private static readonly SpriteMetaData[] DecorationSlices =
        {
            Tile("Grass", 1120, 320),
            Tile("BushTile", 1184, 320),
            Tile("RockTile", 1120, 121),
            Decoration("TreeLarge", 0, 736, 256, 320),
            Decoration("TreeSmall", 448, 736, 128, 192),
            Decoration("Bush", 192, 320, 96, 64),
            Decoration("RockLarge", 0, 160, 256, 128),
            Decoration("RockSmall", 840, 121, 44, 14),
            Decoration("CaveRock", 0, 0, 256, 128)
        };

        /// <summary>Creates missing derived textures once; preserves existing authored importer settings.</summary>
        public static void Ensure()
        {
            Folder(ArtFolder);
            bool terrainCreated = CreateTexture(SourceFolder + "/MainLevBuild.png", TerrainPath, TerrainSlices);
            bool decorationsCreated = CreateTexture(SourceFolder + "/Decoratives.png", DecorationsPath, DecorationSlices);
            if (sprites == null || terrainCreated || decorationsCreated) LoadSprites();
        }

        /// <summary>Returns a named project-owned sprite, creating the missing slice resources as needed.</summary>
        public static SpriteAsset Sprite(string name)
        {
            if (sprites == null) Ensure();
            if (name != null && sprites.TryGetValue(name, out SpriteAsset sprite) && sprite) return sprite;
            throw new KeyNotFoundException("缺少 SSW 地图切片：" + name);
        }

        private static bool CreateTexture(string source, string destination, SpriteMetaData[] slices)
        {
            string destinationFile = Absolute(destination);
            if (File.Exists(destinationFile)) return false;
            string sourceFile = Absolute(source);
            if (!File.Exists(sourceFile)) throw new FileNotFoundException("缺少 SSW 地图源素材：" + source, sourceFile);
            File.Copy(sourceFile, destinationFile);
            AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(destination) as TextureImporter;
            if (!importer) throw new InvalidOperationException("无法导入 SSW 地图纹理：" + destination);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = 32;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.anisoLevel = 0;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.alphaIsTransparency = true;
            importer.isReadable = false;
#pragma warning disable 618
            importer.spritesheet = slices;
#pragma warning restore 618
            importer.SaveAndReimport();
            return true;
        }

        private static void LoadSprites()
        {
            sprites = new Dictionary<string, SpriteAsset>(StringComparer.Ordinal);
            foreach (string path in new[] { TerrainPath, DecorationsPath })
            {
                foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset is SpriteAsset sprite) sprites[sprite.name] = sprite;
                }
            }
        }

        private static SpriteMetaData Tile(string name, int x, int y) =>
            Slice(name, x, y, 32, 32, new Vector2(0.5f, 0.5f));

        private static SpriteMetaData Decoration(string name, int x, int y, int width, int height) =>
            Slice(name, x, y, width, height, new Vector2(0.5f, 0));

        private static SpriteMetaData Slice(string name, int x, int y, int width, int height, Vector2 pivot) =>
            new SpriteMetaData { name = name, rect = new Rect(x, y, width, height),
                alignment = (int)SpriteAlignment.Custom, pivot = pivot, border = Vector4.zero };

        private static string Absolute(string assetPath) =>
            Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length));

        private static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            Folder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
