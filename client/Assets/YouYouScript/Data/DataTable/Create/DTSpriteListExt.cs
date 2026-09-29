using FlatBuffers;
using System.Collections.Generic;
using YouYou;
using YouYou.DataTable;

/// <summary>
/// Create By 悠游课堂 http://www.u3dol.com 
/// </summary>
public static partial class DTSpriteListExt
{
    private static Dictionary<int, DTSprite?> m_Dic = new Dictionary<int, DTSprite?>();
    private static List<DTSprite> m_List = new List<DTSprite>();

    #region LoadData 加载数据表数据
    /// <summary>
    /// 加载数据表数据
    /// </summary>
    public static void LoadData(this DTSpriteList dtspriteList)
    {
        GameEntry.DataTable.RegisterTableLoad(DataTableDefine.DTSpriteName);

        //1.拿到这个表格的buffer
        GameEntry.DataTable.GetDataTableBuffer(DataTableDefine.DTSpriteName, (byte[] buffer) =>
        {
            //2.加载数据 并 把数据初始化到字典
            Init(DTSpriteList.GetRootAsDTSpriteList(new ByteBuffer(buffer)));
        });
    }
    #endregion

    /// <summary>
    /// 初始化到字典
    /// </summary>
    public static void Init(DTSpriteList dtspriteList)
    {
        GameEntry.DataTable.RunTableLoad(DataTableDefine.DTSpriteName, DataTableDefine.DTSpriteVersion, () =>
        {
            var nextDic = new Dictionary<int, DTSprite?>();
            var nextList = new List<DTSprite>();
            int len = dtspriteList.DTSpritesLength;
            for (int j = 0; j < len; j++)
            {
                DTSprite ? dtsprite = dtspriteList.DTSprites(j);
                if (dtsprite != null)
                {
                    nextList.Add(dtsprite.Value);
                    nextDic[dtsprite.Value.Id] = dtsprite;
                }
            }

            //3.派发单个表加载完毕事件
            return () =>
            {
                ResetData();
                m_List.AddRange(nextList);
                foreach (var entry in nextDic) m_Dic.Add(entry.Key, entry.Value);
            };
        });
    }

    /// <summary>
    /// 获取数据实体
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public static DTSprite? GetEntity(this DTSpriteList dtspriteList, int id)
    {
        DTSprite ? dtsprite;
        m_Dic.TryGetValue(id, out dtsprite);
        return dtsprite;
    }

    /// <summary>
    /// 获取数据实体值
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public static DTSprite GetEntityValue(this DTSpriteList dtspriteList, int id)
    {
        DTSprite ? dtsprite = dtspriteList.GetEntity(id);
        if (dtsprite != null)
        {
            return dtsprite.Value;
        }
        return default;
    }

    /// <summary>
    /// 获取列表
    /// </summary>
    /// <returns></returns>
    public static List<DTSprite> GetList(this DTSpriteList dtspriteList)
    {
        return m_List;
    }

    /// <summary>释放旧表的缓存与字节引用；后续 Init 使用同一原 FlatBuffers 格式重新加载。</summary>
    public static void ResetData()
    {
        m_Dic.Clear();
        m_List.Clear();
    }
}
