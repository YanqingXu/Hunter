using FlatBuffers;
using System.Collections.Generic;
using YouYou;
using YouYou.DataTable;

/// <summary>
/// Create By 悠游课堂 http://www.u3dol.com 
/// </summary>
public static partial class DTSys_SceneListExt
{
    private static Dictionary<int, DTSys_Scene?> m_Dic = new Dictionary<int, DTSys_Scene?>();
    private static List<DTSys_Scene> m_List = new List<DTSys_Scene>();

    #region LoadData 加载数据表数据
    /// <summary>
    /// 加载数据表数据
    /// </summary>
    public static void LoadData(this DTSys_SceneList dtsys_sceneList)
    {
        GameEntry.DataTable.RegisterTableLoad(DataTableDefine.DTSys_SceneName);

        //1.拿到这个表格的buffer
        GameEntry.DataTable.GetDataTableBuffer(DataTableDefine.DTSys_SceneName, (byte[] buffer) =>
        {
            //2.加载数据 并 把数据初始化到字典
            Init(DTSys_SceneList.GetRootAsDTSys_SceneList(new ByteBuffer(buffer)));
        });
    }
    #endregion

    /// <summary>
    /// 初始化到字典
    /// </summary>
    public static void Init(DTSys_SceneList dtsys_sceneList)
    {
        GameEntry.DataTable.RunTableLoad(DataTableDefine.DTSys_SceneName, DataTableDefine.DTSys_SceneVersion, () =>
        {
            var nextDic = new Dictionary<int, DTSys_Scene?>();
            var nextList = new List<DTSys_Scene>();
            int len = dtsys_sceneList.DTSysScenesLength;
            for (int j = 0; j < len; j++)
            {
                DTSys_Scene ? dtsys_scene = dtsys_sceneList.DTSysScenes(j);
                if (dtsys_scene != null)
                {
                    nextList.Add(dtsys_scene.Value);
                    nextDic[dtsys_scene.Value.Id] = dtsys_scene;
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
    public static DTSys_Scene? GetEntity(this DTSys_SceneList dtsys_sceneList, int id)
    {
        DTSys_Scene ? dtsys_scene;
        m_Dic.TryGetValue(id, out dtsys_scene);
        return dtsys_scene;
    }

    /// <summary>
    /// 获取数据实体值
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public static DTSys_Scene GetEntityValue(this DTSys_SceneList dtsys_sceneList, int id)
    {
        DTSys_Scene ? dtsys_scene = dtsys_sceneList.GetEntity(id);
        if (dtsys_scene != null)
        {
            return dtsys_scene.Value;
        }
        return default;
    }

    /// <summary>
    /// 获取列表
    /// </summary>
    /// <returns></returns>
    public static List<DTSys_Scene> GetList(this DTSys_SceneList dtsys_sceneList)
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
