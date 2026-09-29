using FlatBuffers;
using System.Collections.Generic;
using YouYou;
using YouYou.DataTable;

/// <summary>
/// Create By 悠游课堂 http://www.u3dol.com 
/// </summary>
public static partial class DTSys_AudioListExt
{
    private static Dictionary<int, DTSys_Audio?> m_Dic = new Dictionary<int, DTSys_Audio?>();
    private static List<DTSys_Audio> m_List = new List<DTSys_Audio>();

    #region LoadData 加载数据表数据
    /// <summary>
    /// 加载数据表数据
    /// </summary>
    public static void LoadData(this DTSys_AudioList dtsys_audioList)
    {
        GameEntry.DataTable.RegisterTableLoad(DataTableDefine.DTSys_AudioName);

        //1.拿到这个表格的buffer
        GameEntry.DataTable.GetDataTableBuffer(DataTableDefine.DTSys_AudioName, (byte[] buffer) =>
        {
            //2.加载数据 并 把数据初始化到字典
            Init(DTSys_AudioList.GetRootAsDTSys_AudioList(new ByteBuffer(buffer)));
        });
    }
    #endregion

    /// <summary>
    /// 初始化到字典
    /// </summary>
    public static void Init(DTSys_AudioList dtsys_audioList)
    {
        GameEntry.DataTable.RunTableLoad(DataTableDefine.DTSys_AudioName, DataTableDefine.DTSys_AudioVersion, () =>
        {
            var nextDic = new Dictionary<int, DTSys_Audio?>();
            var nextList = new List<DTSys_Audio>();
            int len = dtsys_audioList.DTSysAudiosLength;
            for (int j = 0; j < len; j++)
            {
                DTSys_Audio ? dtsys_audio = dtsys_audioList.DTSysAudios(j);
                if (dtsys_audio != null)
                {
                    nextList.Add(dtsys_audio.Value);
                    nextDic[dtsys_audio.Value.Id] = dtsys_audio;
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
    public static DTSys_Audio? GetEntity(this DTSys_AudioList dtsys_audioList, int id)
    {
        DTSys_Audio ? dtsys_audio;
        m_Dic.TryGetValue(id, out dtsys_audio);
        return dtsys_audio;
    }

    /// <summary>
    /// 获取数据实体值
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public static DTSys_Audio GetEntityValue(this DTSys_AudioList dtsys_audioList, int id)
    {
        DTSys_Audio ? dtsys_audio = dtsys_audioList.GetEntity(id);
        if (dtsys_audio != null)
        {
            return dtsys_audio.Value;
        }
        return default;
    }

    /// <summary>
    /// 获取列表
    /// </summary>
    /// <returns></returns>
    public static List<DTSys_Audio> GetList(this DTSys_AudioList dtsys_audioList)
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
