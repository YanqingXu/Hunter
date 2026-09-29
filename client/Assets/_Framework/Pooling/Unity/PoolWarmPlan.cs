using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace BigWorld.Pooling.Unity
{
    [CreateAssetMenu(menuName = "BigWorld/Pooling/Warm Plan", fileName = "WarmPlan")]
    public sealed class PoolWarmPlan : ScriptableObject
    {
        public string PlanId;
        public WarmPlanItem[] Items = Array.Empty<WarmPlanItem>();
        /// <summary>
        /// 将预热资产中的项目转换为完整池键需求，拒绝重复项，再按计划身份提交到作用域所属服务。
        /// </summary>
        /// <param name="scope">承接本次借用或请求的作用域，必须属于当前服务且仍开放。</param>
        /// <param name="options">本次计划中各池预热的期限设置。</param>
        /// <param name="cancellationToken">取消信号可以由后台线程发出，实际状态修改由服务线程处理。</param>
        /// <returns>计划各池的异步预热任务。</returns>
        /// <remarks>在创建 PoolService 的线程调用；Unity 项目中该线程必须是主线程。</remarks>
        public Task<WarmResult>[] Submit(PoolScope scope, WarmOptions options = default, CancellationToken cancellationToken = default)
        {
            var demand = new Dictionary<PoolKey, int>();
            foreach (var item in Items)
            {
                var key = new PoolKey(PoolKind.Prefab, item.PoolId, item.Variant);
                if (demand.ContainsKey(key)) throw new ArgumentException("Duplicate pool in warm plan: " + key);
                demand.Add(key, item.ExpectedConcurrentDemand);
            }
            return scope.OwnerService.SubmitWarmPlan(scope, PlanId, demand, options, cancellationToken);
        }
    }
}
