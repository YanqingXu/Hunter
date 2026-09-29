using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class HUDText : MonoBehaviour
{
    protected class Entry
    {
        public float time; //加入时候的时间
        public float stay = 0f; //停留时间
        public float offset = 0f; //移动偏移
        public GameObject Item; //对应的预设克隆体
        public Vector2 InitOffset; //出生时候的偏移

        private Text m_txt;
        private bool m_HasFindTxt = false;
        //文本
        public Text txt
        {
            get
            {
                if (m_txt == null && !m_HasFindTxt)
                {
                    Transform trans = Item.transform.Find("txt");
                    m_HasFindTxt = true;
                    if (trans != null)
                    {
                        m_txt = trans.GetComponent<Text>();
                    }
                }
                return m_txt;
            }
        }

        private Image m_img; //图标
        private bool m_HasFindImg = false;

        public Image img
        {
            get
            {
                if (m_img == null && !m_HasFindImg)
                {
                    Transform trans = Item.transform.Find("img");
                    m_HasFindImg = true;
                    if (trans != null)
                    {
                        m_img = trans.GetComponent<Image>();
                    }
                }
                return m_img;
            }
        }

        public Vector3 cachedPos; //缓存坐标

        public float movementStart { get { return time + stay; } }
    }

    /// <summary>
    /// Sorting comparison function.
    /// </summary>

    static int Comparison(Entry a, Entry b)
    {
        if (a.movementStart < b.movementStart) return -1;
        if (a.movementStart > b.movementStart) return 1;
        return 0;
    }

    [Header("要克隆的对象")]
    public GameObject Item;

    [Header("要克隆的对象高度")]
    public float ItemHeight = 20;

    [Header("对象池的保留数量")]
    public int PoolCount = 10;

    /// <summary>
    /// 位置偏移曲线
    /// </summary>
    public AnimationCurve offsetCurve = new AnimationCurve(new Keyframe[] { new Keyframe(0f, 0f), new Keyframe(3f, 40f) });

    /// <summary>
    /// 透明度曲线
    /// </summary>
    public AnimationCurve alphaCurve = new AnimationCurve(new Keyframe[] { new Keyframe(1f, 1f), new Keyframe(3f, 0f) });

    /// <summary>
    /// 缩放曲线
    /// </summary>
    public AnimationCurve scaleCurve = new AnimationCurve(new Keyframe[] { new Keyframe(0f, 0f), new Keyframe(0.25f, 1f) });

    /// <summary>
    /// 对象列表
    /// </summary>
    private List<Entry> mList = new List<Entry>();

    /// <summary>
    /// 未使用的对象列表
    /// </summary>
    private List<Entry> mUnused = new List<Entry>();

    /// <summary>
    /// 索引 计数器
    /// </summary>
    private int counter = 0;

    private Keyframe[] offsets = null;
    private Keyframe[] alphas = null;
    private Keyframe[] scales = null;

    private void Awake()
    {
        offsets = offsetCurve.keys;
        alphas = alphaCurve.keys;
        scales = scaleCurve.keys;
    }

    private void Start()
    {

    }

    /// <summary>
    /// 添加子物体
    /// </summary>
    /// <param name="parent"></param>
    /// <returns></returns>
    private GameObject AddChild(GameObject parent, GameObject prefab)
    {
        GameObject go = Instantiate(prefab);
        if (parent != null)
        {
            Transform t = go.transform;
            t.SetParent(parent.transform);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            go.layer = parent.layer;
        }
        return go;
    }

    /// <summary>
    /// 创建对象
    /// </summary>
    private Entry Create()
    {
        // See if an unused entry can be reused
        if (mUnused.Count > 0)
        {
            Entry ent = mUnused[mUnused.Count - 1];
            mUnused.RemoveAt(mUnused.Count - 1);
            ent.time = Time.realtimeSinceStartup;

            ent.offset = 0f;
            ent.cachedPos = gameObject.transform.position;
            mList.Add(ent);
            return ent;
        }

        // New entry
        Entry ne = new Entry();
        ne.time = Time.realtimeSinceStartup;
        ne.Item = AddChild(gameObject, Item);
        ne.cachedPos = gameObject.transform.position;
        ne.Item.name = counter.ToString();
        ne.Item.transform.localScale = new Vector3(0.001f, 0.001f, 0.001f);

        mList.Add(ne);
        ++counter;
        return ne;
    }

    /// <summary>
    /// 删除对象
    /// </summary>
    void Delete(Entry ent)
    {
        mList.Remove(ent);
        ent.Item.SetActive(false);
        if (mUnused.Count >= PoolCount)
        {
            Destroy(ent.Item);
        }
        else
        {
            mUnused.Add(ent);
        }
    }

    /// <summary>
    /// 添加一个对象数据
    /// </summary>

    private void Add(string text, Vector2 offset, Sprite spr, Color c, float stayDuration)
    {
        if (!enabled) return;

        float time = Time.realtimeSinceStartup;
        float val = 0f;

        //创建一个新对象
        Entry ne = Create();
        ne.stay = stayDuration;
        ne.txt.color = new Color(c.r, c.g, c.b, 0);
        ne.InitOffset = offset;

        ne.txt.text = text;

        if (ne.img != null)
        {
            ne.img.sprite = spr;
        }

        //排序
        mList.Sort(Comparison);
    }

    /// <summary>
    /// 禁用的时候 从列表移除
    /// </summary>
    void OnDisable()
    {
        for (int i = mList.Count; i > 0;)
        {
            Entry ent = mList[--i];
            if (ent.txt != null) ent.txt.enabled = false;
            else mList.RemoveAt(i);
        }
    }

    /// <summary>
    /// Update the position of all labels, as well as update their size and alpha.
    /// </summary>

    void Update()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying) return;
#endif
        float time = Time.realtimeSinceStartup;

        /*
        Keyframe[] offsets = offsetCurve.keys;
        Keyframe[] alphas = alphaCurve.keys;
        Keyframe[] scales = scaleCurve.keys;
        */

        if (null == offsets) return;
        if (null == alphas) return;
        if (null == scales) return;

        float offsetEnd = offsets[offsets.Length - 1].time;
        float alphaEnd = alphas[alphas.Length - 1].time;
        float scalesEnd = scales[scales.Length - 1].time;
        float totalEnd = Mathf.Max(scalesEnd, Mathf.Max(offsetEnd, alphaEnd));

        for (int i = mList.Count; i > 0;)
        {
            Entry ent = mList[--i];
            float currentTime = time - ent.movementStart;
            ent.offset = offsetCurve.Evaluate(currentTime);

            //设置颜色曲线
            ent.txt.color = new Color(ent.txt.color.r, ent.txt.color.g, ent.txt.color.b, alphaCurve.Evaluate(currentTime));
            //ent.img.color = new Color(ent.img.color.r, ent.img.color.g, ent.img.color.b, alphaCurve.Evaluate(currentTime));


            //设置缩放曲线
            float s = scaleCurve.Evaluate(time - ent.time);
            if (s < 0.001f) s = 0.001f;
            ent.Item.transform.localScale = new Vector3(s, s, s);

            //到期 删除对象
            if (currentTime > totalEnd) Delete(ent);
        }

        float offset = 0f;

        //移动对象
        for (int i = mList.Count; i > 0;)
        {
            Entry ent = mList[--i];
            ent.Item.SetActive(true);
            offset = Mathf.Max(offset, ent.offset);
            ent.Item.transform.localPosition = ent.cachedPos + new Vector3(0f, offset, 0f) + new Vector3(ent.InitOffset.x, ent.InitOffset.y, 0);
            offset += Mathf.Round(ent.Item.transform.localScale.y * ItemHeight);
        }
    }

    /// <summary>
    /// 显示上弹文字
    /// </summary>
    /// <param name="text"></param>
    public void ShowHUDTip(string text)
    {
        ShowHUDTip(text, Vector2.zero, Color.white);
    }

    public void ShowHUDTip(string text, Vector2 offset)
    {
        ShowHUDTip(text, offset, Color.white);
    }


    /// <summary>
    /// 显示上弹文字
    /// </summary>
    /// <param name="text"></param>
    /// <param name="color"></param>
    public void ShowHUDTip(string text, Vector2 offset, Color color, float stayDuration = 0.1f)
    {
        Add(text, offset, null, color, stayDuration);
    }

    /// <summary>
    /// 全部隐藏删除;
    /// </summary>
    public void DeleteAll()
    {
        for (int i = 0; i < mList.Count; i++)
            mList[i].Item.SetActive(false);

        for (int i = 0; i < mUnused.Count; i++)
            mUnused[i].Item.SetActive(false);
    }
}
