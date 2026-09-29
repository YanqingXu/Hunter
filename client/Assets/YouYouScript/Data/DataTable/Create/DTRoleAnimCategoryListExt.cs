using FlatBuffers;
using System.Collections.Generic;
using YouYou;
using YouYou.DataTable;

/// <summary>
/// Create By 悠游课堂 http://www.u3dol.com 
/// </summary>
public static partial class DTRoleAnimCategoryListExt
{
    private static Dictionary<int, DTRoleAnimCategory?> m_Dic = new Dictionary<int, DTRoleAnimCategory?>();
    private static List<DTRoleAnimCategory> m_List = new List<DTRoleAnimCategory>();

    #region LoadData 加载数据表数据
    /// <summary>
    /// 加载数据表数据
    /// </summary>
    public static void LoadData(this DTRoleAnimCategoryList dtroleanimcategoryList)
    {
        GameEntry.DataTable.RegisterTableLoad(DataTableDefine.DTRoleAnimCategoryName);

        //1.拿到这个表格的buffer
        GameEntry.DataTable.GetDataTableBuffer(DataTableDefine.DTRoleAnimCategoryName, (byte[] buffer) =>
        {
            //2.加载数据 并 把数据初始化到字典
            Init(DTRoleAnimCategoryList.GetRootAsDTRoleAnimCategoryList(new ByteBuffer(buffer)));
        });
    }
    #endregion

    /// <summary>
    /// 初始化到字典
    /// </summary>
    public static void Init(DTRoleAnimCategoryList dtroleanimcategoryList)
    {
        GameEntry.DataTable.RunTableLoad(DataTableDefine.DTRoleAnimCategoryName, DataTableDefine.DTRoleAnimCategoryVersion, () =>
        {
            var nextDic = new Dictionary<int, DTRoleAnimCategory?>();
            var nextList = new List<DTRoleAnimCategory>();
            int len = dtroleanimcategoryList.DTRoleAnimCategorysLength;
            for (int j = 0; j < len; j++)
            {
                DTRoleAnimCategory ? dtroleanimcategory = dtroleanimcategoryList.DTRoleAnimCategorys(j);
                if (dtroleanimcategory != null)
                {
                    nextList.Add(dtroleanimcategory.Value);
                    nextDic[dtroleanimcategory.Value.Id] = dtroleanimcategory;
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
    public static DTRoleAnimCategory? GetEntity(this DTRoleAnimCategoryList dtroleanimcategoryList, int id)
    {
        DTRoleAnimCategory ? dtroleanimcategory;
        m_Dic.TryGetValue(id, out dtroleanimcategory);
        return dtroleanimcategory;
    }

    /// <summary>
    /// 获取数据实体值
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public static DTRoleAnimCategory GetEntityValue(this DTRoleAnimCategoryList dtroleanimcategoryList, int id)
    {
        DTRoleAnimCategory ? dtroleanimcategory = dtroleanimcategoryList.GetEntity(id);
        if (dtroleanimcategory != null)
        {
            return dtroleanimcategory.Value;
        }
        return default;
    }

    /// <summary>
    /// 获取列表
    /// </summary>
    /// <returns></returns>
    public static List<DTRoleAnimCategory> GetList(this DTRoleAnimCategoryList dtroleanimcategoryList)
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
