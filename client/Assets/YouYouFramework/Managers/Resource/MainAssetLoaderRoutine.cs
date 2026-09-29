//===================================================
//作    者：边涯  http://www.u3dol.com
//创建时间：
//备    注：
//===================================================
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace YouYou
{
    /// <summary>
    /// 主资源加载器
    /// </summary>
    public class MainAssetLoaderRoutine
    {
        /// <summary>
        /// 当前的资源信息实体
        /// </summary>
        private AssetEntity m_CurrAssetEntity;

        /// <summary>
        /// 当前的资源实体
        /// </summary>
        private ResourceEntity m_CurrResourceEntity;

        /// <summary>
        /// 主资源加载完毕
        /// </summary>
        private BaseAction<ResourceEntity> m_OnComplete;

        /// <summary>
        /// 主资源包
        /// </summary>
        private AssetBundle m_MainAssetBundle;

        /// <summary>
        /// 依赖资源包名字哈希
        /// </summary>
        private HashSet<string> m_DependsAssetBundleNames = new HashSet<string>();

        /// <summary>
        /// 加载主资源
        /// </summary>
        /// <param name="assetCategory">资源分类</param>
        /// <param name="assetFullName">资源路径</param>
        public void Load(AssetCategory assetCategory, string assetFullName, BaseAction<ResourceEntity> onComplete = null)
        {
#if DISABLE_ASSETBUNDLE && UNITY_EDITOR
            m_CurrResourceEntity = GameEntry.Pool.DequeueClassObject<ResourceEntity>();
            m_CurrResourceEntity.Category = assetCategory;
            m_CurrResourceEntity.IsAssetBundle = false;
            m_CurrResourceEntity.ResourceName = assetFullName;
            m_CurrResourceEntity.Target = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetFullName);
            onComplete?.Invoke(m_CurrResourceEntity);
#else
            m_OnComplete = onComplete;
            m_CurrAssetEntity = GameEntry.Resource.ResourceLoaderManager.GetAssetEntity(assetCategory, assetFullName);
            if (m_CurrAssetEntity == null)
            {
                GameEntry.LogError("assetFullName no exists " + assetFullName);
                return;
            }
            LoadMainAsset();
#endif
        }

        /// <summary>
        /// 真正的加载主资源
        /// </summary>
        private void LoadMainAsset()
        {
            //1.从分类资源池(AssetPool)中查找
            m_CurrResourceEntity = GameEntry.Pool.AssetPool[m_CurrAssetEntity.Category].Spawn(m_CurrAssetEntity.AssetFullName);
            if (m_CurrResourceEntity != null)
            {
                //Debug.LogError("从分类资源池加载" + assetEntity.ResourceName);
                //说明资源在分类资源池中存在
                m_OnComplete?.Invoke(m_CurrResourceEntity);
                return;
            }

            //2.加载这个资源所依赖的资源包
            List<AssetDependsEntity> dependsAssetList = m_CurrAssetEntity.DependsAssetList;
            if (dependsAssetList != null)
            {
                foreach (AssetDependsEntity assetDependsEntity in dependsAssetList)
                {
                    AssetEntity assetEntity = GameEntry.Resource.ResourceLoaderManager.GetAssetEntity(assetDependsEntity.Category, assetDependsEntity.AssetFullName);
                    m_DependsAssetBundleNames.Add(assetEntity.AssetBundleName);
                }
            }

            //3.循环依赖哈希 加入任务组
            TaskGroup taskGroup = GameEntry.Task.CreateTaskGroup();

            foreach (string bundleName in m_DependsAssetBundleNames)
            {
                TaskRoutine taskRoutine = GameEntry.Task.CreateTaskRoutine();
                taskRoutine.CurrTask = () =>
                {
                    //依赖资源 只是加载资源包
                    GameEntry.Resource.ResourceLoaderManager.LoadAssetBundle(bundleName, onComplete: (AssetBundle bundle) =>
                     {
                         taskRoutine.Leave();
                     });
                };
                taskGroup.AddTask(taskRoutine);
            }

            //4.加载主资源包
            TaskRoutine taskRoutineLoadMain = GameEntry.Task.CreateTaskRoutine();
            taskRoutineLoadMain.CurrTask = () =>
            {
                GameEntry.Resource.ResourceLoaderManager.LoadAssetBundle(m_CurrAssetEntity.AssetBundleName, onComplete: (AssetBundle bundle) =>
                {
                    m_MainAssetBundle = bundle;
                    taskRoutineLoadMain.Leave();
                });
            };
            taskGroup.AddTask(taskRoutineLoadMain);

            taskGroup.OnComplete = () =>
            {
                if (m_MainAssetBundle == null)
                {
                    GameEntry.LogError("MainAssetBundle not exists " + m_CurrAssetEntity.AssetFullName);
                }
                GameEntry.Resource.ResourceLoaderManager.LoadAsset(m_CurrAssetEntity.AssetFullName, m_MainAssetBundle, onComplete: (UnityEngine.Object obj) =>
                {
                    //再次检查 很重要 不检查引用计数会出错
                    m_CurrResourceEntity = GameEntry.Pool.AssetPool[m_CurrAssetEntity.Category].Spawn(m_CurrAssetEntity.AssetFullName);
                    if (m_CurrResourceEntity != null)
                    {
                        m_OnComplete?.Invoke(m_CurrResourceEntity);
                        Reset();
                        return;
                    }

                    m_CurrResourceEntity = GameEntry.Pool.DequeueClassObject<ResourceEntity>();
                    m_CurrResourceEntity.Category = m_CurrAssetEntity.Category;
                    m_CurrResourceEntity.IsAssetBundle = false;
                    m_CurrResourceEntity.ResourceName = m_CurrAssetEntity.AssetFullName;
                    m_CurrResourceEntity.Target = obj;

                    GameEntry.Pool.AssetPool[m_CurrAssetEntity.Category].Register(m_CurrResourceEntity);
                    m_OnComplete?.Invoke(m_CurrResourceEntity);
                    Reset();
                });
            };

            taskGroup.Run(true);
        }

        /// <summary>
        /// 重置
        /// </summary>
        private void Reset()
        {
            m_OnComplete = null;
            m_CurrAssetEntity = null;
            m_CurrResourceEntity = null;
            m_MainAssetBundle = null;
            m_DependsAssetBundleNames.Clear();
            GameEntry.Pool.EnqueueClassObject(this);
        }
    }
}