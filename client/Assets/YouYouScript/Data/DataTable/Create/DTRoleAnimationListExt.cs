using FlatBuffers;
using System.Collections.Generic;
using YouYou;
using YouYou.DataTable;

/// <summary>
/// Create By 悠游课堂 http://www.u3dol.com 
/// </summary>
public static partial class DTRoleAnimationListExt
{
    private static Dictionary<int, DTRoleAnimation?> m_Dic = new Dictionary<int, DTRoleAnimation?>();
    private static List<DTRoleAnimation> m_List = new List<DTRoleAnimation>();

    #region LoadData 加载数据表数据
    /// <summary>
    /// 加载数据表数据
    /// </summary>
    public static void LoadData(this DTRoleAnimationList dtroleanimationList)
    {
        GameEntry.DataTable.RegisterTableLoad(DataTableDefine.DTRoleAnimationName);

        //1.拿到这个表格的buffer
        GameEntry.DataTable.GetDataTableBuffer(DataTableDefine.DTRoleAnimationName, (byte[] buffer) =>
        {
            //2.加载数据 并 把数据初始化到字典
            Init(DTRoleAnimationList.GetRootAsDTRoleAnimationList(new ByteBuffer(buffer)));
        });
    }
    #endregion

    /// <summary>
    /// 初始化到字典
    /// </summary>
    public static void Init(DTRoleAnimationList dtroleanimationList)
    {
        GameEntry.DataTable.RunTableLoad(DataTableDefine.DTRoleAnimationName, DataTableDefine.DTRoleAnimationVersion, () =>
        {
            var nextDic = new Dictionary<int, DTRoleAnimation?>();
            var nextList = new List<DTRoleAnimation>();
            int len = dtroleanimationList.DTRoleAnimationsLength;
            for (int j = 0; j < len; j++)
            {
                DTRoleAnimation ? dtroleanimation = dtroleanimationList.DTRoleAnimations(j);
                if (dtroleanimation != null)
                {
                    nextList.Add(dtroleanimation.Value);
                    nextDic[dtroleanimation.Value.Id] = dtroleanimation;
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
    public static DTRoleAnimation? GetEntity(this DTRoleAnimationList dtroleanimationList, int id)
    {
        DTRoleAnimation ? dtroleanimation;
        m_Dic.TryGetValue(id, out dtroleanimation);
        return dtroleanimation;
    }

    /// <summary>
    /// 获取数据实体值
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public static DTRoleAnimation GetEntityValue(this DTRoleAnimationList dtroleanimationList, int id)
    {
        DTRoleAnimation ? dtroleanimation = dtroleanimationList.GetEntity(id);
        if (dtroleanimation != null)
        {
            return dtroleanimation.Value;
        }
        return default;
    }

    /// <summary>
    /// 获取列表
    /// </summary>
    /// <returns></returns>
    public static List<DTRoleAnimation> GetList(this DTRoleAnimationList dtroleanimationList)
    {
        return m_List;
    }

    /// <summary>释放旧表的缓存与字节引用；后续 Init 使用同一原 FlatBuffers 格式重新加载。</summary>
    public static void ResetData()
    {
        m_Dic.Clear();
        m_List.Clear();
        ret.Clear();
    }
}
