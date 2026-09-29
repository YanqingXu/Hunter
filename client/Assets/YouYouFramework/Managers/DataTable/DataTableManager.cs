using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;
using YouYou.DataTable;

namespace YouYou
{
    public class DataTableManager : ManagerBase, IDisposable
    {
        private sealed class TableCompletion
        {
            public int Generation;
            public long Request;
            public string Name;
            public ushort Version;
            public Action Publish;
            public Exception Error;
        }

        private readonly ConcurrentQueue<TableCompletion> m_Completions = new ConcurrentQueue<TableCompletion>();
        private readonly Dictionary<string, long> m_TableRequests = new Dictionary<string, long>();
        private readonly HashSet<string> m_RequestedTables = new HashSet<string>();
        private readonly int m_MainThreadId = Thread.CurrentThread.ManagedThreadId;
        private int m_Generation;
        private long m_NextRequest;
        private bool m_Disposed;

        public DataTableManager()
        {
            AlreadyLoadTable = new Dictionary<string, ushort>();
        }

        public override void Init()
        {
            ResetData();
        }

        /// <summary>
        /// 已经在c#加载的表格
        /// </summary>
        public Dictionary<string, ushort> AlreadyLoadTable
        {
            get;
            private set;
        }

        /// <summary>
        /// 添加到已加载字典
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="version"></param>
        public void AddToAlreadyLoadTable(string tableName, ushort version)
        {
            AlreadyLoadTable[tableName] = version;
        }

        /// <summary>
        /// 根据表格名称和版本号检查是否已经在c#加载
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="version"></param>
        /// <returns></returns>
        public bool CheckAlreadyLoadTable(string tableName, ushort version)
        {
            ushort ver = 0;
            if (AlreadyLoadTable.TryGetValue(tableName, out ver))
            {
                if (ver == version)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 总共需要加载的表格数量
        /// </summary>
        public int TotalTableCount = 0;

        /// <summary>
        /// 当前加载的表格数量
        /// </summary>
        public int CurrLoadTableCount = 0;

        public DTSys_LocalizationList Sys_LocalizationList;
        public DTSys_AudioList Sys_AudioList;
        public DTSys_CodeList Sys_CodeList;
        public DTSys_EffectList Sys_EffectList;
        public DTSys_PrefabList Sys_PrefabList;
        public DTSys_SceneList Sys_SceneList;
        public DTSys_SceneDetailList Sys_SceneDetailList;
        public DTSys_StorySoundList Sys_StorySoundList;
        public DTSys_UIFormList Sys_UIFormList;

        public DTChapterList ChapterList;
        public DTJobList JobList;
        public DTJobLevelList JobLevelList;
        public DTBaseRoleList BaseRoleList;
        public DTRoleAnimationList RoleAnimationList;
        public DTRoleAnimCategoryList RoleAnimCategoryList;
        public DTSpriteList SpriteList;

        public DTSkillLevelList SkillLevelList;
        public DTBuffList BuffList;

        /// <summary>
        /// 加载表格
        /// </summary>
        public void LoadDataTable()
        {
            ResetData();
            Sys_LocalizationList.LoadData();
            Sys_AudioList.LoadData();
            Sys_CodeList.LoadData();
            Sys_EffectList.LoadData();
            Sys_PrefabList.LoadData();
            Sys_SceneList.LoadData();
            Sys_SceneDetailList.LoadData();
            Sys_StorySoundList.LoadData();
            Sys_UIFormList.LoadData();

            JobList.LoadData();
            JobLevelList.LoadData();
            BaseRoleList.LoadData();
            RoleAnimationList.LoadData();
            RoleAnimCategoryList.LoadData();
            SpriteList.LoadData();
            SkillLevelList.LoadData();
            BuffList.LoadData();
        }

        /// <summary>
        /// 表格资源包
        /// </summary>
        private AssetBundle m_DataTableBundle;

        /// <summary>
        /// 加载表格
        /// </summary>
        public void LoadDataAllTable()
        {
#if DISABLE_ASSETBUNDLE
            LoadDataTable();
#else
            ResetData();
            int generation = m_Generation;
            GameEntry.Resource.ResourceLoaderManager.LoadAssetBundle(ConstDefine.DataTableAssetBundlePath, onComplete: (AssetBundle bundle) =>
            {
                if (!IsCurrent(generation)) return;
                if (!bundle) throw new InvalidOperationException("原数据表 AssetBundle 加载失败。");
                m_DataTableBundle = bundle;
                LoadDataTable();
            });
#endif
        }

        /// <summary>
        /// 获取表格的字节数组
        /// </summary>
        /// <param name="tableName"></param>
        /// <returns></returns>
        public void GetDataTableBuffer(string tableName, BaseAction<byte[]> onComplete)
        {
            long request = BeginBufferRequest(tableName);
            int generation = m_Generation;
            var resource = GameEntry.Resource;
#if DISABLE_ASSETBUNDLE
            GameEntry.Time.Yield(() =>
            {
                if (!IsCurrentBuffer(generation, tableName, request)) return;
                byte[] buffer = IOUtil.GetFileBuffer(string.Format("{0}/download/DataTable/{1}.bytes", resource.LocalFilePath, tableName));
                if (buffer == null) throw new InvalidOperationException("找不到原数据表：" + tableName);
                onComplete?.Invoke(ZlibHelper.DeCompressBytes(buffer));
            });
#else
            resource.ResourceLoaderManager.LoadAsset(resource.GetLastPathName(tableName), m_DataTableBundle, onComplete: (UnityEngine.Object obj) =>
            {
                if (!IsCurrentBuffer(generation, tableName, request)) return;
                TextAsset asset = obj as TextAsset;
                if (!asset) throw new InvalidOperationException("原 AssetBundle 中找不到数据表：" + tableName);
                if (onComplete != null)
                {
                    onComplete(ZlibHelper.DeCompressBytes(asset.bytes));
                }
            });
#endif
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            ResetData();
            m_DataTableBundle = null; // 资源池持有并负责卸载共享的 AssetBundle。
            m_Disposed = true;
        }

        /// <summary>由宿主提供实际表字节；仍使用原 zlib / FlatBuffers 数据与原表扩展。</summary>
        public void GetDataTableBuffer(string tableName, byte[] buffer, BaseAction<byte[]> onComplete, bool compressed = true)
        {
            BeginBufferRequest(tableName);
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            byte[] owned = (byte[])buffer.Clone();
            onComplete?.Invoke(compressed ? ZlibHelper.DeCompressBytes(owned) : owned);
        }

        /// <summary>同一轮重复请求同一张表只占一个加载进度名额。</summary>
        public void RegisterTableLoad(string tableName)
        {
            EnsureActive();
            if (m_RequestedTables.Add(tableName)) TotalTableCount++;
        }

        /// <summary>保留异步解析；解析结果和框架事件只在入口主线程提交。</summary>
        public void RunTableLoad(string tableName, ushort version, Func<Action> parse)
        {
            EnsureActive();
            if (parse == null) throw new ArgumentNullException(nameof(parse));
            long request = ++m_NextRequest;
            int generation = m_Generation;
            m_TableRequests[tableName] = request;
            System.Threading.Tasks.Task.Run(() =>
            {
                var completion = new TableCompletion { Name = tableName, Version = version, Request = request, Generation = generation };
                try { completion.Publish = parse(); }
                catch (Exception error) { completion.Error = error; }
                m_Completions.Enqueue(completion);
            });
        }

        public void OnUpdate()
        {
            if (m_Disposed) return;
            EnsureActive();
            while (m_Completions.TryDequeue(out var completion))
            {
                if (!IsCurrent(completion.Generation) ||
                    !m_TableRequests.TryGetValue(completion.Name, out var request) || request != completion.Request) continue;
                if (completion.Error != null) { Debug.LogException(completion.Error); continue; }
                completion.Publish?.Invoke();
                AddToAlreadyLoadTable(completion.Name, completion.Version);
                GameEntry.Event.CommonEvent.Dispatch(SysEventId.LoadOneDataTableComplete, completion.Name);
            }
        }

        /// <summary>清除所有原表扩展的静态缓存并使旧异步请求失效，不改变表结构或版本。</summary>
        public void ResetData()
        {
            EnsureActive();
            m_Generation++;
            m_TableRequests.Clear();
            m_RequestedTables.Clear();
            while (m_Completions.TryDequeue(out _)) { }
            AlreadyLoadTable.Clear();
            TotalTableCount = 0;
            CurrLoadTableCount = 0;
            DTBaseRoleListExt.ResetData();
            DTBattleAttrListExt.ResetData();
            DTBuffListExt.ResetData();
            DTJobLevelListExt.ResetData();
            DTJobListExt.ResetData();
            DTPVPSceneMonsterPointListExt.ResetData();
            DTRoleAnimationListExt.ResetData();
            DTRoleAnimCategoryListExt.ResetData();
            DTSkillLevelListExt.ResetData();
            DTSkillListExt.ResetData();
            DTSpriteListExt.ResetData();
            DTSys_AudioListExt.ResetData();
            DTSys_CodeListExt.ResetData();
            DTSys_EffectListExt.ResetData();
            DTSys_PrefabListExt.ResetData();
            DTSys_SceneDetailListExt.ResetData();
            DTSys_SceneListExt.ResetData();
            DTSys_StorySoundListExt.ResetData();
            DTSys_UIFormListExt.ResetData();
            DTSys_LocalizationListExt.ResetData();
        }

        private long BeginBufferRequest(string tableName)
        {
            EnsureActive();
            if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentException("数据表名称不能为空。", nameof(tableName));
            long request = ++m_NextRequest;
            m_TableRequests[GetCacheName(tableName)] = request;
            return request;
        }

        private bool IsCurrent(int generation)
        {
            return !m_Disposed && generation == m_Generation && ReferenceEquals(GameEntry.DataTable, this);
        }

        private bool IsCurrentBuffer(int generation, string tableName, long request)
        {
            return IsCurrent(generation) && m_TableRequests.TryGetValue(GetCacheName(tableName), out var current) && current == request;
        }

        private static string GetCacheName(string tableName)
        {
            return tableName.StartsWith("Localization/", StringComparison.OrdinalIgnoreCase) ? "Sys_Localization" : tableName;
        }

        private void EnsureActive()
        {
            if (m_Disposed) throw new ObjectDisposedException(nameof(DataTableManager));
            if (Thread.CurrentThread.ManagedThreadId != m_MainThreadId)
                throw new InvalidOperationException("数据表请求与缓存发布必须在 GameEntry 主线程执行。");
        }
    }
}
