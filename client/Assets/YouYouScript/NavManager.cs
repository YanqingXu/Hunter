using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class NavManager : MonoBehaviour
{
    /// <summary>
    /// 寻路代理
    /// </summary>
    public NavMeshAgent Agent;

    private NavMeshPath path;

    // Start is called before the first frame update
    void Start()
    {
        path = new NavMeshPath();
    }

    int add = 200;

    void Update()
    {
        if (Input.GetKeyUp(KeyCode.A))
        {

            float beginTime = Time.realtimeSinceStartup;
            //for (int m = 0; m < 3000; m++)
            //{
                Agent.enabled = true;
                Agent.Warp(new Vector3(171.9f, add + 25.5f, 345.6f));
                Agent.CalculatePath(new Vector3(172.1f, add + 25.5f, 331.6f), path);
                if (path.status == NavMeshPathStatus.PathComplete)
                {
                    //Debug.LogError("寻路完毕");
                    //for (int i = 1; i < path.corners.Length; ++i)
                    //{
                    //    Debug.LogError(path.corners[i]);
                    //}
                }
            //}
            Debug.LogError("totalTime=" + (Time.realtimeSinceStartup - beginTime));
        }

        if (path == null) return;

#if UNITY_EDITOR
        for (int i = 1; i < path.corners.Length; ++i)
        {
            Debug.DrawLine(path.corners[i - 1], path.corners[i], Color.yellow);
        }
#endif
    }
}
