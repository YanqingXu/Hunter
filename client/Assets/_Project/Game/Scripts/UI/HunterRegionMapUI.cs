using UnityEngine;
using UnityEngine.UI;

namespace BigWorld.Gameplay
{
    /// <summary>A horizontal map of authored regions. Clue coordinates never enter the HUD.</summary>
    public sealed class HunterRegionMapUI : MonoBehaviour
    {
        private static readonly Color Candidate = new Color(.13f, .34f, .39f);
        private static readonly Color Excluded = new Color(.18f, .19f, .21f);
        private static readonly Color Located = new Color(.55f, .25f, .15f);
        private LevelSession2D level;
        private Image[] regions;
        private Text[] labels;
        private Text playerMarker, bossMarker, exitMarker;
        public int RegionCount => regions == null ? 0 : regions.Length;
        public bool BossMarkerVisible => bossMarker && bossMarker.gameObject.activeSelf;
        public bool ExitMarkerVisible => exitMarker && exitMarker.gameObject.activeSelf;
        public RectTransform PlayerMarker => playerMarker.rectTransform;
        public bool IsRegionDimmed(int index) => regions[index].color == Excluded;

        public void Initialize(LevelSession2D session, Font font)
        {
            level = session;
            if (!level || !level.Hunt) return;
            var hunt = level.Hunt;
            regions = new Image[hunt.RegionCount]; labels = new Text[regions.Length];
            for (int i = 0; i < regions.Length; i++)
            {
                var rect = Rect("Search Region " + (i + 1), transform);
                float left = MapX(hunt.RegionEdges[i]), right = MapX(hunt.RegionEdges[i + 1]);
                Place(rect, new Vector2((left + right) * .5f, -45), new Vector2(right - left - 3, 26));
                regions[i] = rect.gameObject.AddComponent<Image>(); regions[i].raycastTarget = false;
                labels[i] = Label("Region Name", rect, font, new Vector2(0, -13), rect.sizeDelta, 14);
                labels[i].rectTransform.anchorMin = labels[i].rectTransform.anchorMax = new Vector2(.5f, 1);
            }
            playerMarker = Label("Player Map Marker", transform, font, Vector2.zero, new Vector2(46, 22), 14);
            playerMarker.text = "▲ 你"; playerMarker.color = new Color(.3f, .95f, .85f);
            bossMarker = Label("Boss Map Marker", transform, font, Vector2.zero, new Vector2(54, 22), 14);
            bossMarker.text = "BOSS"; bossMarker.color = new Color(1, .68f, .4f);
            exitMarker = Label("Exit Map Marker", transform, font, Vector2.zero, new Vector2(54, 22), 14);
            exitMarker.text = "撤离"; exitMarker.color = new Color(1, .86f, .3f);
            Refresh();
        }
        public void Refresh()
        {
            if (regions == null || !level || !level.Hunt || level.Hunt.State == null) return;
            var hunt = level.Hunt;
            for (int i = 0; i < regions.Length; i++)
            {
                bool excluded = hunt.IsBossLocated ? i != hunt.BossRegion : !hunt.IsRegionCandidate(i);
                regions[i].color = excluded ? Excluded : hunt.IsBossLocated ? Located : Candidate;
                labels[i].color = excluded ? new Color(.58f, .6f, .62f) : Color.white;
                labels[i].text = (excluded ? "× " : "") + "区域 " + (i + 1);
            }
            if (level.Player) playerMarker.rectTransform.anchoredPosition = new Vector2(MarkerX(level.Player.transform.position.x), -73);
            bossMarker.gameObject.SetActive(hunt.IsBossLocated && !hunt.ExitMarkerVisible);
            bossMarker.rectTransform.anchoredPosition = new Vector2(MarkerX(hunt.BossRegions[hunt.BossRegion].x), -73);
            exitMarker.gameObject.SetActive(hunt.ExitMarkerVisible && level.ExitPoint);
            if (level.ExitPoint) exitMarker.rectTransform.anchoredPosition = new Vector2(MarkerX(level.ExitPoint.position.x), -73);
            // Keep both labels readable when standing at the exit.
            if (ExitMarkerVisible && Mathf.Abs(exitMarker.rectTransform.anchoredPosition.x - playerMarker.rectTransform.anchoredPosition.x) < 48)
                playerMarker.rectTransform.anchoredPosition += Vector2.left * 48;
            else if (BossMarkerVisible && Mathf.Abs(bossMarker.rectTransform.anchoredPosition.x - playerMarker.rectTransform.anchoredPosition.x) < 48)
                playerMarker.rectTransform.anchoredPosition += Vector2.right * (playerMarker.rectTransform.anchoredPosition.x < 470 ? 48 : -48);
        }
        private float MapX(float x)
        {
            var edges = level.Hunt.RegionEdges;
            return 14 + Mathf.InverseLerp(edges[0], edges[edges.Length - 1], x) * 532;
        }
        private float MarkerX(float x) => Mathf.Clamp(MapX(x), 40, 520);
        private static RectTransform Rect(string name, Transform parent)
        { var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false); return rect; }
        private static void Place(RectTransform rect, Vector2 position, Vector2 size)
        { rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(.5f, .5f); rect.anchoredPosition = position; rect.sizeDelta = size; }
        private static Text Label(string name, Transform parent, Font font, Vector2 position, Vector2 size, int fontSize)
        {
            var rect = Rect(name, parent); Place(rect, position, size); var text = rect.gameObject.AddComponent<Text>();
            text.font = font; text.fontSize = fontSize; text.alignment = TextAnchor.MiddleCenter; text.color = Color.white;
            text.raycastTarget = false; return text;
        }
    }
}
