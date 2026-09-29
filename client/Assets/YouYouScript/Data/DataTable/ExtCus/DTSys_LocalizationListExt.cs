using FlatBuffers;
using System.Collections.Generic;
using YouYou;
using YouYou.DataTable;

public static class DTSys_LocalizationListExt
{
    private static Dictionary<string, string> m_Dic = new Dictionary<string, string>();


    #region LoadData

    public static void LoadData(this DTSys_LocalizationList sys_LocalizationList)
    {
        GameEntry.DataTable.RegisterTableLoad("Sys_Localization");

        GameEntry.DataTable.GetDataTableBuffer("Localization/" + GameEntry.CurrLanguage.ToString(), (byte[] buffer) =>
        {
            Init(DTSys_LocalizationList.GetRootAsDTSys_LocalizationList(new ByteBuffer(buffer)));
        });
    }
    #endregion

    public static void Init(DTSys_LocalizationList sys_LocalizationList)
    {
        GameEntry.DataTable.RunTableLoad("Sys_Localization", 2, () =>
        {
            var nextDic = new Dictionary<string, string>();
            int len = sys_LocalizationList.DTSysLocalizationsLength;
            for (int j = 0; j < len; j++)
            {
                DTSys_Localization? sys_Localization = sys_LocalizationList.DTSysLocalizations(j);
                if (sys_Localization != null)
                {
                    nextDic[sys_Localization.Value.Key] = sys_Localization.Value.Value;
                }
            }
            return () =>
            {
                ResetData();
                foreach (var entry in nextDic) m_Dic.Add(entry.Key, entry.Value);
            };
        });
    }

    public static string GetValue(this DTSys_LocalizationList sys_LocalizationList, string key)
    {
        string value = null;
        m_Dic.TryGetValue(key, out value);
        return value;
    }

    /// <summary>释放旧表的缓存与字节引用；后续 Init 使用同一原 FlatBuffers 格式重新加载。</summary>
    public static void ResetData()
    {
        m_Dic.Clear();
    }
}
