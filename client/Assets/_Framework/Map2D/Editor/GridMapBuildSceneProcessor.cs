using System.Collections.Generic;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace BigWorld.Map2D.Editor
{
    /// <summary>Omits full editor previews from built streaming scenes.</summary>
    public sealed class GridMapBuildSceneProcessor : IProcessSceneWithReport
    {
        public int callbackOrder => 1000;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            // Unity also calls scene processors when entering Play mode, with no build report.
            // Only strip the build pipeline's scene copy; leave the editor's scene untouched.
            if (report == null) return;
            StripScenePreview(scene);
        }

        /// <summary>
        /// Explicit build/test operation. Clears only tilemaps owned by streaming renderers,
        /// preserving their hierarchy and asset references. Cancels queued editor rebuilds;
        /// runtime OnEnable will restore streaming. Returns the number of tilemaps processed.
        /// Does not save the scene or run automatically for editing/Play mode.
        /// </summary>
        public static int StripScenePreview(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return 0;
            int stripped = 0;
            var clearedOwners = new HashSet<GridMapRenderer>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (MapGeneratedPart part in root.GetComponentsInChildren<MapGeneratedPart>(true))
                {
                    GridMapRenderer owner = part.Owner;
                    if (part.IsRoot || owner == null || !owner.StreamingEnabled || owner.gameObject.scene != scene) continue;
                    Transform parent = part.transform.parent;
                    MapGeneratedPart grid = parent == null ? null : parent.GetComponent<MapGeneratedPart>();
                    if (grid == null || !grid.IsRoot || grid.Owner != owner || grid.transform.parent != owner.transform) continue;
                    Tilemap tilemap = part.GetComponent<Tilemap>();
                    if (tilemap == null) continue;
                    // Cancel the ExecuteAlways OnEnable/OnValidate rebuild queued while the
                    // build scene was loaded, so it cannot repopulate the stripped scene copy.
                    if (clearedOwners.Add(owner)) owner.ClearPreview();
                    tilemap.ClearAllTiles();
                    tilemap.CompressBounds();
                    TilemapCollider2D collider = tilemap.GetComponent<TilemapCollider2D>();
                    if (collider != null) collider.ProcessTilemapChanges();
                    stripped++;
                }
            }
            return stripped;
        }
    }
}
