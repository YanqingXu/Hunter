using FlatBuffers;
using System.Collections.Generic;
using YouYou;
using YouYou.DataTable;

/// <summary>
/// Create By 悠游课堂 http://www.u3dol.com 
/// </summary>
public static partial class DTBattleAttrListExt
{
    private static Dictionary<int, DTBattleAttr?> m_Dic = new Dictionary<int, DTBattleAttr?>();
    private static List<DTBattleAttr> m_List = new List<DTBattleAttr>();

    #region LoadData 加载数据表数据
    /// <summary>
    /// 加载数据表数据
    /// </summary>
    public static void LoadData(this DTBattleAttrList dtbattleattrList)
    {
        GameEntry.DataTable.RegisterTableLoad(DataTableDefine.DTBattleAttrName);

        //1.拿到这个表格的buffer
        GameEntry.DataTable.GetDataTableBuffer(DataTableDefine.DTBattleAttrName, (byte[] buffer) =>
        {
            //2.加载数据 并 把数据初始化到字典
            Init(DTBattleAttrList.GetRootAsDTBattleAttrList(new ByteBuffer(buffer)));
        });
    }
    #endregion

    /// <summary>
    /// 初始化到字典
    /// </summary>
    public static void Init(DTBattleAttrList dtbattleattrList)
    {
        GameEntry.DataTable.RunTableLoad(DataTableDefine.DTBattleAttrName, DataTableDefine.DTBattleAttrVersion, () =>
        {
            var nextDic = new Dictionary<int, DTBattleAttr?>();
            var nextList = new List<DTBattleAttr>();
            int len = dtbattleattrList.DTBattleAttrsLength;
            for (int j = 0; j < len; j++)
            {
                DTBattleAttr ? dtbattleattr = dtbattleattrList.DTBattleAttrs(j);
                if (dtbattleattr != null)
                {
                    nextList.Add(dtbattleattr.Value);
                    nextDic[dtbattleattr.Value.Id] = dtbattleattr;
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
    public static DTBattleAttr? GetEntity(this DTBattleAttrList dtbattleattrList, int id)
    {
        DTBattleAttr ? dtbattleattr;
        m_Dic.TryGetValue(id, out dtbattleattr);
        return dtbattleattr;
    }

    /// <summary>
    /// 获取数据实体值
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public static DTBattleAttr GetEntityValue(this DTBattleAttrList dtbattleattrList, int id)
    {
        DTBattleAttr ? dtbattleattr = dtbattleattrList.GetEntity(id);
        if (dtbattleattr != null)
        {
            return dtbattleattr.Value;
        }
        return default;
    }

    /// <summary>
    /// 获取列表
    /// </summary>
    /// <returns></returns>
    public static List<DTBattleAttr> GetList(this DTBattleAttrList dtbattleattrList)
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
