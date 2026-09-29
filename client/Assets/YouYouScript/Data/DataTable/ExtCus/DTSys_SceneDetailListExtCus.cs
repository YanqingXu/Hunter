using System.Collections.Generic;
using YouYou.DataTable;

public static partial class DTSys_SceneDetailListExt
{
    static List<DTSys_SceneDetail> ret = new List<DTSys_SceneDetail>();
    public static List<DTSys_SceneDetail> GetListBySceneId(this DTSys_SceneDetailList sys_scenedetailList, int sceneId, int sceneGrade)
    {
        ret.Clear();
        int len = m_List.Count;
        for (int i = 0; i < len; i++)
        {
            DTSys_SceneDetail sys_SceneDetail = m_List[i];
            if (sys_SceneDetail.SceneId == sceneId && sys_SceneDetail.SceneGrade <= sceneGrade)
            {
                ret.Add(sys_SceneDetail);
            }
        }
        return ret;
    }
}