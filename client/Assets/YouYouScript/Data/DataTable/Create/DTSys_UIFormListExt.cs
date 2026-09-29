using FlatBuffers;
using System.Collections.Generic;
using YouYou;
using YouYou.DataTable;

/// <summary>
/// Create By 悠游课堂 http://www.u3dol.com 
/// </summary>
public static partial class DTSys_UIFormListExt
{
    private static Dictionary<int, DTSys_UIForm?> m_Dic = new Dictionary<int, DTSys_UIForm?>();
    private static List<DTSys_UIForm> m_List = new List<DTSys_UIForm>();

    #region LoadData 加载数据表数据
    /// <summary>
    /// 加载数据表数据
    /// </summary>
    public static void LoadData(this DTSys_UIFormList dtsys_uiformList)
    {
        GameEntry.DataTable.RegisterTableLoad(DataTableDefine.DTSys_UIFormName);

        //1.拿到这个表格的buffer
        GameEntry.DataTable.GetDataTableBuffer(DataTableDefine.DTSys_UIFormName, (byte[] buffer) =>
        {
            //2.加载数据 并 把数据初始化到字典
            Init(DTSys_UIFormList.GetRootAsDTSys_UIFormList(new ByteBuffer(buffer)));
        });
    }
    #endregion

    /// <summary>
    /// 初始化到字典
    /// </summary>
    public static void Init(DTSys_UIFormList dtsys_uiformList)
    {
        GameEntry.DataTable.RunTableLoad(DataTableDefine.DTSys_UIFormName, DataTableDefine.DTSys_UIFormVersion, () =>
        {
            var nextDic = new Dictionary<int, DTSys_UIForm?>();
            var nextList = new List<DTSys_UIForm>();
            int len = dtsys_uiformList.DTSysUIFormsLength;
            for (int j = 0; j < len; j++)
            {
                DTSys_UIForm ? dtsys_uiform = dtsys_uiformList.DTSysUIForms(j);
                if (dtsys_uiform != null)
                {
                    nextList.Add(dtsys_uiform.Value);
                    nextDic[dtsys_uiform.Value.Id] = dtsys_uiform;
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
    public static DTSys_UIForm? GetEntity(this DTSys_UIFormList dtsys_uiformList, int id)
    {
        DTSys_UIForm ? dtsys_uiform;
        m_Dic.TryGetValue(id, out dtsys_uiform);
        return dtsys_uiform;
    }

    /// <summary>
    /// 获取数据实体值
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public static DTSys_UIForm GetEntityValue(this DTSys_UIFormList dtsys_uiformList, int id)
    {
        DTSys_UIForm ? dtsys_uiform = dtsys_uiformList.GetEntity(id);
        if (dtsys_uiform != null)
        {
            return dtsys_uiform.Value;
        }
        return default;
    }

    /// <summary>
    /// 获取列表
    /// </summary>
    /// <returns></returns>
    public static List<DTSys_UIForm> GetList(this DTSys_UIFormList dtsys_uiformList)
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
