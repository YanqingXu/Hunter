using BigWorld.Pooling.Unity;
using UnityEditor;
using UnityEngine;

namespace BigWorld.Pooling.Editor
{
    public sealed class PoolMonitorWindow : EditorWindow
    {
        private Vector2 scroll;
        /// <summary>
        /// 响应编辑器菜单，打开或聚焦对象池监控窗口。
        /// </summary>
        [MenuItem("Window/BigWorld/Pool Monitor")]
        private static void Open() => GetWindow<PoolMonitorWindow>("Pool Monitor");
        /// <summary>
        /// 在运行模式下定期请求窗口重绘，以刷新池和作用域的诊断数据。
        /// </summary>
        private void OnInspectorUpdate() { if (EditorApplication.isPlaying) Repaint(); }
        /// <summary>
        /// 绘制所有活动驱动的预算、池状态、请求、释放故障及作用域借用信息；非运行模式显示使用提示。
        /// </summary>
        private void OnGUI()
        {
            if (!EditorApplication.isPlaying) { EditorGUILayout.HelpBox("Enter Play Mode with a PoolDriver to inspect live pools.", MessageType.Info); return; }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var driver in FindObjectsOfType<PoolDriver>())
            {
                var service = driver.Service; if (service == null) continue;
                EditorGUILayout.LabelField(driver.name + " · " + service.State, EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Resident {service.ResidentSlots} | estimated instance bytes {service.EstimatedResidentBytes:N0}");
                var scheduler = service.Scheduler;
                EditorGUILayout.LabelField($"Frame work {scheduler.WorkMilliseconds:F2} ms | creates {scheduler.CreatesThisFrame} | destroys {scheduler.DestroyRequestsThisFrame}");
                foreach (var pool in service.Pools)
                {
                    var s = pool.GetSnapshot();
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.LabelField(s.Key.ToString(), EditorStyles.boldLabel);
                    EditorGUILayout.LabelField($"{s.State} | resident {s.ResidentSlots} | borrowed {s.Borrowed} | idle {s.Idle} | peak {s.PeakBorrowed}");
                    EditorGUILayout.LabelField($"Preparing/reserved {s.Preparing + s.CreateReserved} | retiring {s.PendingDestroy + s.DestroyIssued} | faulted {s.DestroyFaulted}");
                    EditorGUILayout.LabelField($"Requests {s.PendingRequests} | resource {s.WaitingResource} | capacity {s.WaitingCapacity} | budget {s.WaitingBudget}");
                    EditorGUILayout.LabelField($"Hits {s.CacheHitRate:P1} | creates sync/async/warm {s.SyncCreates}/{s.AsyncCreates}/{s.WarmCreates}");
                    if (!string.IsNullOrEmpty(s.CloseFault)) EditorGUILayout.HelpBox(s.CloseFault, MessageType.Error);
                    EditorGUILayout.EndVertical();
                }
                EditorGUILayout.LabelField("Scopes", EditorStyles.boldLabel);
                foreach (var scope in service.Scopes)
                {
                    EditorGUILayout.LabelField($"#{scope.Id} {scope.Name} · {scope.State} | borrowed {scope.BorrowedCount} | requests {scope.PendingCount} | retiring {scope.RetiringCount}");
                    foreach (var borrower in scope.DescribeBorrowers()) EditorGUILayout.LabelField("  " + borrower, EditorStyles.miniLabel);
                }
            }
            EditorGUILayout.EndScrollView();
        }
    }

    [CustomEditor(typeof(PoolCatalog))]
    public sealed class PoolCatalogInspector : UnityEditor.Editor
    {
        /// <summary>
        /// 绘制配置资产默认 Inspector，并提供显式校验按钮，将校验结果或异常输出到 Console。
        /// </summary>
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (GUILayout.Button("Validate configuration"))
            {
                try { ((PoolCatalog)target).Validate(); Debug.Log("Pool catalog is valid.", target); }
                catch (System.Exception ex) { Debug.LogException(ex, target); }
            }
        }
    }
}
