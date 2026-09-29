using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TestClientRun : MonoBehaviour
{
    /// <summary>
    /// 路经点
    /// </summary>
    public Transform[] m_VectorPath;

    /// <summary>
    /// 当前路经点索引
    /// </summary>
    private int m_CurrPointIndex;

    public CharacterController m_CharacterController;

    public bool m_IsRun = false;

    
    private Vector3 endPos;
    private Vector3 beginPos;
    private Vector3 dir;
    private Vector3 rotation;
    private float dis;
    private bool m_TurnComplete = false; //转向完毕
    
    void Start()
    {
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyUp(KeyCode.A))
        {
            m_CharacterController.enabled = false;
            m_CharacterController.transform.position = m_VectorPath[0].position;
            m_CurrPointIndex = 1;
            m_TurnComplete = false;
            m_CharacterController.enabled = true;
            m_IsRun = true;
        }

        if (m_IsRun)
        {
            //如果整个路径走完了 切换待机
            if (m_CurrPointIndex >= m_VectorPath.Length)
            {
                m_IsRun = false;
                Debug.LogError("TestClient整个路径走完了");
                return;
            }

            if (!m_TurnComplete)
            {
                endPos = m_VectorPath[m_CurrPointIndex].position;
                beginPos = m_VectorPath[m_CurrPointIndex - 1].position;

                dir = (endPos - beginPos).normalized;
                
                rotation = dir;
                //立刻转身
                rotation.y = 0;
                m_CharacterController.transform.rotation = Quaternion.LookRotation(rotation);
                
                m_TurnComplete = true;
            }
            
            m_CharacterController.Move(dir * Time.deltaTime * 10);

            //判断是否应该向下一个点移动
            float dis = Vector3.Distance(m_CharacterController.transform.position, beginPos);

            //当到达临时目标点了
            if (dis >= Vector3.Distance(endPos, beginPos))
            {
                m_CharacterController.enabled = false;
                m_CharacterController.transform.position = endPos; //位置修正
                m_CharacterController.enabled = true;
                Debug.LogError("TestClient位置修正");
                m_TurnComplete = false;
                m_CurrPointIndex++;
            }
        }
    }
}