using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TestServerRoleRun : MonoBehaviour
{
    private float m_Speed = 10f;

    private bool m_BeginRun = false;
    private float runTime = 0;

    public Transform[] PathPoint;

    public Transform CurrTrans;

    /// <summary>
    /// 当前路径点索引
    /// </summary>
    private int CurrWayPointIndex = 0;

    private float m_BeginTime = 0;
    private bool m_TurnComplete = false; //转身完毕
    private bool sample = false;

    private Vector3 endPos;
    private Vector3 beginPos;
    private Vector3 dir;
    private float dis;

    void Start()
    {
        Application.targetFrameRate = 50;
    }

    private void Update()
    {
        if (Input.GetKeyUp(KeyCode.A))
        {
            runTime = 0;
            CurrWayPointIndex = 1;
            CurrTrans.position = PathPoint[0].position;
            m_BeginTime = Time.time;
            m_BeginRun = true;
            sample = false;
            m_TurnComplete = false;
        }

        if (m_BeginRun)
        {
            runTime += Time.deltaTime;

            if (CurrWayPointIndex == PathPoint.Length)
            {
                Debug.LogError("走路完毕 耗时 " + (Time.time - m_BeginTime));
                m_BeginRun = false;
                return;
            }

            if (sample == false && Time.time - m_BeginTime > 3)
            {
                Debug.LogError("pos=" + CurrTrans.position);
                Debug.LogError("rotation=" + CurrTrans.localEulerAngles.y);
                sample = true;
            }

            //设置旋转
            //dir.y = 0; Quaternion.LookRotation(dir);

            if (!m_TurnComplete)
            {
                endPos = PathPoint[CurrWayPointIndex].position;
                beginPos = PathPoint[CurrWayPointIndex - 1].position;

                dir = (endPos - beginPos).normalized;

                float y = (float)Math.Atan2((endPos.x - beginPos.x), (endPos.z - beginPos.z)) * 180 / (float)Math.PI;
                CurrTrans.localEulerAngles = new Vector3(0, y, 0);

                m_TurnComplete = true;
            }

            //时间*速度=距离
            dis = runTime * m_Speed;
            Vector3 currPos = beginPos + dir * dis;
            CurrTrans.position = currPos;

            if (dis >= Vector3.Distance(endPos, beginPos))
            {
                CurrTrans.position = endPos; //位置修正
                runTime = 0;
                m_TurnComplete = false;
                CurrWayPointIndex++;
            }
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        for (int i = 0; i < PathPoint.Length; i++)
        {
            if (i == PathPoint.Length - 1) break;
            Transform curr = PathPoint[i];
            Transform next = PathPoint[i + 1];
            if (curr != null && next != null)
            {
                Gizmos.DrawLine(PathPoint[i].position, PathPoint[i + 1].position);
            }
        }
    }
}