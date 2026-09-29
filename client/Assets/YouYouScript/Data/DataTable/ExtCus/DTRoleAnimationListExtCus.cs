using System.Collections.Generic;
using YouYou.DataTable;

public static partial class DTRoleAnimationListExt
{
    static List<DTRoleAnimation> ret = new List<DTRoleAnimation>();

    /// <summary>
    /// 根据分组编号获取角色动画列表
    /// </summary>
    /// <param name="roleAnimationList"></param>
    /// <param name="groupId"></param>
    /// <returns></returns>
    public static List<DTRoleAnimation> GetListByGroupId(this DTRoleAnimationList roleAnimationList, int groupId)
    {
        ret.Clear();
        int len = m_List.Count;
        for (int i = 0; i < len; i++)
        {
            DTRoleAnimation roleAnimation = m_List[i];
            if (roleAnimation.GroupId == groupId)
            {
                ret.Add(roleAnimation);
            }
        }
        return ret;
    }
}