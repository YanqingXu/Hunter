using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using YouYou;

public class SelectRoleSceneCtrl : MonoBehaviour
{
    /// <summary>
    /// 当前场景相机
    /// </summary>
    [SerializeField]
    private UniversalAdditionalCameraData m_CameraData;

    [SerializeField]
    private Material m_SkyboxMaterial;

    /// <summary>
    /// 角色容器
    /// </summary>
    public Transform RoleContainer;

    public static SelectRoleSceneCtrl Instance;

    private void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        //设置天空盒
        RenderSettings.skybox = m_SkyboxMaterial;

        GameEntry.CameraCtrl.Close();
        m_CameraData.cameraStack.Add(GameEntry.CameraCtrl.UICamera);
    }
}
