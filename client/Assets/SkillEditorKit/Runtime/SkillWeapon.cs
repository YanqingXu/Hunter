// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit
{
using System;
using UnityEngine;

public class SkillWeapon : MonoBehaviour
{
    [SerializeField] private Collider detectionCollider;
    [SerializeField] private Collider2D detectionCollider2D;
    private bool is2D;
    private LayerMask attackDetectionLayer;
    private Action<ISkillHitTarget, SkillHitData> onDetection;
    private SkillHitData attackData;
    private bool detecting;
    public bool IsDetecting => detecting;
    public void Init(LayerMask attackDetectionLayer, Action<ISkillHitTarget, SkillHitData> onDetection)
    {
        if (detectionCollider != null) detectionCollider.enabled = false;
        if (detectionCollider2D != null) detectionCollider2D.enabled = false;
        this.attackDetectionLayer = attackDetectionLayer;
        this.onDetection = onDetection;
    }
    public void StartDetection(SkillHitData attackData)
    {
        is2D = attackData.Instance != null && attackData.Instance.Space == SkillSpace.TwoD;
        if (is2D && detectionCollider2D == null) detectionCollider2D = GetComponent<Collider2D>();
        if (is2D ? detectionCollider2D == null : detectionCollider == null)
        {
            Debug.LogError("武器攻击检测缺少对应空间的 Collider / Collider2D。", this);
            return;
        }
        this.attackData = attackData;
        detecting = true;
        if (is2D) detectionCollider2D.enabled = true; else detectionCollider.enabled = true;
    }

    public void StopDetection()
    {
        detecting = false;
        attackData = default;
        if (detectionCollider != null) detectionCollider.enabled = false;
        if (detectionCollider2D != null) detectionCollider2D.enabled = false;
    }

    private void OnTriggerStay(Collider other)
    {
        // 判断是否在LayerMask的范围内
        if (detecting && detectionCollider != null && detectionCollider.enabled && other != null && (attackDetectionLayer.value & (1 << other.gameObject.layer)) != 0)
        {
            ISkillHitTarget hitTarget = other.GetComponentInParent<ISkillHitTarget>();
            if (hitTarget != null)
            {
                attackData.hitPoint = other.ClosestPoint(transform.position);
                onDetection?.Invoke(hitTarget, attackData);
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D other) => OnTriggerStay2D(other);
    private void OnTriggerStay2D(Collider2D other)
    {
        if (!detecting || !is2D || detectionCollider2D == null || !detectionCollider2D.enabled || other == null ||
            (attackDetectionLayer.value & (1 << other.gameObject.layer)) == 0) return;
        var target = other.GetComponentInParent<ISkillHitTarget>();
        if (target == null) return;
        attackData.hitPoint = SkillCollider.ClosestPoint(other, transform.position);
        onDetection?.Invoke(target, attackData);
    }

    private void OnDisable()
    {
        StopDetection();
    }
}

}
