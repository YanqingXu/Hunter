using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class AOIAreaData
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
    public List<int> ConnectAreaList;

    /// <summary>
    /// 左上角坐标
    /// </summary>
    public double TopLeftPos_X;
    public double TopLeftPos_Y;
    public double TopLeftPos_Z;

    /// <summary>
    /// 右下角坐标
    /// </summary>
    public double BottomRightPos_X;
    public double BottomRightPos_Y;
    public double BottomRightPos_Z;

    /// <summary>
    /// 可行走区域单元格宽度
    /// </summary>
    public double CellWith = 0.5f;

    /// <summary>
    /// 可行走区域单元格数据
    /// </summary>
    public List<int> CellData;
}
