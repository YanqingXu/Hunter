using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using YouYou;

public class AOIArea : MonoBehaviour
{
    /// <summary>
    /// 区域编号
    /// </summary>
    public int AreaId;

    /// <summary>
    /// 当前行
    /// </summary>
    public int CurrRow;

    /// <summary>
    /// 当前列
    /// </summary>
    public int CurrColumn;

    /// <summary>
    /// 关联区域列表
    /// </summary>
    public List<AOIArea> ConnectAreaList = new List<AOIArea>();

    /// <summary>
    /// 左上角坐标
    /// </summary>
    public Vector3 TopLeftPos;

    /// <summary>
    /// 右下角坐标
    /// </summary>
    public Vector3 BottomRightPos;

    /// <summary>
    /// 可行走区域单元格宽度
    /// </summary>
    public float CellWith = 0.5f;

    /// <summary>
    /// 可行走区域单元格数据
    /// </summary>
    public List<int> CellData = new List<int>();

    /// <summary>
    /// 设置关联区域
    /// </summary>
    public void SetConnectArea()
    {
        ConnectAreaList.Clear();

        //根据区域编号 获取这个区域关联的区域
        //获取这个点，关联的九宫格 左上角行列 和 右下角行列
        int minRow = CurrRow - 1;
        int minColumn = CurrColumn - 1;
        int maxRow = CurrRow + 1;
        int maxColumn = CurrColumn + 1;

        //循环字典
        foreach (var item in AOIManager.AOIAreaDic)
        {
            AOIArea aoiArea = item.Value;

            //排除自己
            if (aoiArea.AreaId == AreaId)
            {
                continue;
            }

            if (aoiArea.CurrRow >= minRow
                && aoiArea.CurrRow <= maxRow
                && aoiArea.CurrColumn >= minColumn &&
                aoiArea.CurrColumn <= maxColumn
            )
            {
                ConnectAreaList.Add(aoiArea);
            }
        }

        SetWorldPos();
    }

    /// <summary>
    /// 设置左上角和右下角世界坐标
    /// </summary>
    public void SetWorldPos()
    {
        TopLeftPos = transform.TransformPoint(-0.5f, 0, 0.5f);
        BottomRightPos = transform.TransformPoint(0.5f, 0, -0.5f);

        SetCellData();
    }

    public void SetCellData()
    {
        CellData.Clear();

        //单元格宽度
        float cellWidth = 0.5f;
        float cellCount = 30 / cellWidth;

        for (int i = 0; i < cellCount; i++)
        {
            for (int j = 0; j < cellCount; j++)
            {
                NavMeshHit hit;

                //是否可行走
                bool canRun = false;

                //碰到了墙
                bool touchWall = false;

                Vector3 starPos = TopLeftPos + new Vector3(j * cellWidth, -10, i * -1 * cellWidth);
                //发射射线检测 是否碰到了墙
                if (Physics.Raycast(starPos - new Vector3(0, 100, 0), Vector3.up, 100, 1 << LayerMask.NameToLayer("Wall")))
                {
                    touchWall = true;
                }

                if (!touchWall)
                {
                    for (int k = -40; k < 30; ++k)
                    {
                        if (NavMesh.SamplePosition(starPos + new Vector3(0, k, 0), out hit, 0.5f, 1))
                        {
                            canRun = true;
                            break;
                        }
                    }
                }
                CellData.Add(canRun ? 1 : 0);
                Debug.DrawRay(starPos, Vector3.up * 5, canRun ? Color.yellow : Color.red, 10);
            }
        }
    }

    /// <summary>
    /// 创建AOI数据
    /// </summary>
    /// <returns></returns>
    public AOIAreaData CreateAOIAreaData()
    {
        AOIAreaData aOIAreaData = new AOIAreaData();

        aOIAreaData.AreaId = this.AreaId;
        aOIAreaData.CurrRow = this.CurrRow;
        aOIAreaData.CurrColumn = this.CurrColumn;

        aOIAreaData.TopLeftPos_X = this.TopLeftPos.x;
        aOIAreaData.TopLeftPos_Y = this.TopLeftPos.y;
        aOIAreaData.TopLeftPos_Z = this.TopLeftPos.z;

        aOIAreaData.BottomRightPos_X = this.BottomRightPos.x;
        aOIAreaData.BottomRightPos_Y = this.BottomRightPos.y;
        aOIAreaData.BottomRightPos_Z = this.BottomRightPos.z;

        aOIAreaData.ConnectAreaList = new List<int>();
        foreach (var item in ConnectAreaList)
        {
            aOIAreaData.ConnectAreaList.Add(item.AreaId);
        }

        aOIAreaData.CellData = CellData;

        return aOIAreaData;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(MyConstDefine.PlayerTag))
        {
            GameEntry.Data.RoleDataManager.PlayerEnterAOIArea(AreaId);
        }
    }
}