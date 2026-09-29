using BigWorld.Map2D;
using Cinemachine;
using SkillEditorKit;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BigWorld.YouYou2D
{
    /// <summary>Owns a scene-local Cinemachine rig for an XY platformer camera.</summary>
    [DefaultExecutionOrder(1000), DisallowMultipleComponent]
    [RequireComponent(typeof(Camera), typeof(CinemachineBrain))]
    [AddComponentMenu("BigWorld/2D Framework/Camera Follow")]
    public sealed class CameraFollow2D : MonoBehaviour
    {
        [Header("跟随目标与地图")]
        public Transform Target;
        [Tooltip("按地图完整矩形限制可视范围；不参与地形碰撞。")]
        public GridMapRenderer Map;
        [Header("构图（世界单位）")]
        [Min(.01f)] public float OrthographicSize = 5;
        [Min(.01f)] public float CameraDistance = 10;
        [Min(0)] public float LookAheadDistance = 2;
        public float VerticalOffset = 2;
        [Header("平滑与跳跃缓冲")]
        [Min(0)] public float HorizontalDamping = .35f;
        [Min(0)] public float VerticalDamping = .8f;
        [Range(0, .8f), Tooltip("屏幕高度比例；目标在此范围内上下移动时不推动相机。")]
        public float VerticalDeadZone = .25f;
        [Min(0), Tooltip("单帧移动超过此世界距离时自动重置跟随；0 表示仅显式调用 Snap。")]
        public float TeleportDistance = 8;

        public CinemachineVirtualCamera VirtualCamera { get; private set; }
        public CinemachineBrain Brain { get; private set; }
        private Camera output;
        private CinemachineFramingTransposer framing;
        private CinemachineConfiner2D confiner;
        private PolygonCollider2D boundary;
        private GameObject rig;
        private Transform previousTarget;
        private Vector3 previousPosition;
        private readonly Vector2[] corners = new Vector2[4];
        private bool hasBounds;
        private CinemachineBrain.UpdateMethod previousUpdate;
        private CinemachineBrain.BrainUpdateMethod previousBlendUpdate;
        private bool previousIgnoreTimeScale;

        private void Awake()
        {
            output = GetComponent<Camera>();
            Brain = GetComponent<CinemachineBrain>();
            output.orthographic = true;
        }

        private void OnEnable()
        {
            previousUpdate = Brain.m_UpdateMethod;
            previousBlendUpdate = Brain.m_BlendUpdateMethod;
            previousIgnoreTimeScale = Brain.m_IgnoreTimeScale;
            // Read the interpolated Rigidbody2D transform after movement, then evaluate once.
            Brain.m_UpdateMethod = CinemachineBrain.UpdateMethod.ManualUpdate;
            Brain.m_BlendUpdateMethod = CinemachineBrain.BrainUpdateMethod.LateUpdate;
            Brain.m_IgnoreTimeScale = false;
            rig = new GameObject("CM 2D Follow (runtime)");
            rig.SetActive(false);
            SceneManager.MoveGameObjectToScene(rig, gameObject.scene);
            var virtualObject = new GameObject("Virtual Camera");
            virtualObject.transform.SetParent(rig.transform, false);
            VirtualCamera = virtualObject.AddComponent<CinemachineVirtualCamera>();
            VirtualCamera.m_Lens = LensSettings.FromCamera(output);
            VirtualCamera.m_Lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
            framing = VirtualCamera.AddCinemachineComponent<CinemachineFramingTransposer>();
            framing.m_LookaheadTime = 0; // Direction comes from the same facing source as skills.
            framing.m_DeadZoneWidth = 0;
            framing.m_UnlimitedSoftZone = true;
            framing.m_CenterOnActivate = true;
            framing.m_ZDamping = 0;
            var boundsObject = new GameObject("Map Camera Bounds");
            boundsObject.transform.SetParent(rig.transform, false);
            // This collider is geometry data only, excluded from physics and raycasts.
            boundary = boundsObject.AddComponent<PolygonCollider2D>();
            boundary.enabled = false;
            boundary.isTrigger = true;
            confiner = virtualObject.AddComponent<CinemachineConfiner2D>();
            confiner.m_Damping = 0; // Hard screen-edge limit; body damping already smooths movement.
            confiner.m_Padding = 0;
            rig.SetActive(true);
        }

        private void Start() { Snap(); }

        /// <summary>Call after a respawn or teleport, including a short teleport within the dead zone.</summary>
        public void Snap()
        {
            if (!isActiveAndEnabled || !Target || !VirtualCamera) return;
            Configure();
            VirtualCamera.PreviousStateIsValid = false;
            VirtualCamera.InternalUpdateCameraState(Vector3.up, -1);
            Brain.ManualUpdate();
            RememberTarget();
        }

        private void LateUpdate()
        {
            if (!Target || Time.timeScale <= 0) return;
            if (Target != previousTarget || (TeleportDistance > 0 &&
                (Target.position - previousPosition).sqrMagnitude > TeleportDistance * TeleportDistance))
            {
                Snap();
                return;
            }
            Configure();
            Brain.ManualUpdate();
            RememberTarget();
        }

        private void RememberTarget()
        {
            previousTarget = Target;
            previousPosition = Target.position;
        }

        private void Configure()
        {
            VirtualCamera.Follow = Target;
            VirtualCamera.transform.rotation = Quaternion.identity;
            float direction = (SkillFacing2D.Rotation(Target) * Vector3.right).x < 0 ? -1 : 1;
            var offset = new Vector3(direction * Mathf.Max(0, LookAheadDistance), VerticalOffset, 0);
            // Keep offsets in world XY even when the character rotates or mirrors its visual.
            framing.m_TrackedObjectOffset = Quaternion.Inverse(Target.rotation) * offset;
            framing.m_XDamping = Mathf.Max(0, HorizontalDamping);
            framing.m_YDamping = Mathf.Max(0, VerticalDamping);
            framing.m_DeadZoneHeight = Mathf.Clamp(VerticalDeadZone, 0, .8f);
            framing.m_CameraDistance = Mathf.Max(.01f, CameraDistance);
            RefreshBounds();
        }

        private void RefreshBounds()
        {
            var map = Map ? Map.Map : null;
            float size = Mathf.Max(.01f, OrthographicSize);
            bool valid = map && Map.transform.lossyScale.x != 0 && Map.transform.lossyScale.y != 0;
            if (valid)
            {
                Vector2 min = map.Origin;
                Vector2 max = min + new Vector2(map.Width, map.Height) * map.CellSize;
                bool changed = !hasBounds;
                changed |= SetCorner(0, Map.transform.TransformPoint(new Vector3(min.x, min.y)));
                changed |= SetCorner(1, Map.transform.TransformPoint(new Vector3(max.x, min.y)));
                changed |= SetCorner(2, Map.transform.TransformPoint(new Vector3(max.x, max.y)));
                changed |= SetCorner(3, Map.transform.TransformPoint(new Vector3(min.x, max.y)));
                if (changed)
                {
                    // World coordinates on an independent, stationary transform avoid inheriting camera movement.
                    boundary.points = corners;
                    confiner.InvalidateCache();
                }
                confiner.m_BoundingShape2D = boundary;

                // Fit narrow/small maps and changed screen aspect ratios without exposing outside space.
                Vector3 right = Map.transform.InverseTransformVector(Vector3.right);
                Vector3 up = Map.transform.InverseTransformVector(Vector3.up);
                float aspect = Mathf.Max(.01f, output.aspect);
                float fitX = (max.x - min.x) * .5f / (Mathf.Abs(right.x) * aspect + Mathf.Abs(up.x));
                float fitY = (max.y - min.y) * .5f / (Mathf.Abs(right.y) * aspect + Mathf.Abs(up.y));
                size = Mathf.Max(.001f, Mathf.Min(size, Mathf.Min(fitX, fitY) * .999f));
            }
            else confiner.m_BoundingShape2D = null;
            hasBounds = valid;
            confiner.enabled = valid;
            if (!Mathf.Approximately(VirtualCamera.m_Lens.OrthographicSize, size))
            {
                VirtualCamera.m_Lens.OrthographicSize = size;
                // Cinemachine 2.x caches a solution per lens; invalidate when the lens changes.
                confiner.InvalidateCache();
            }
            confiner.m_MaxWindowSize = size;
        }

        private bool SetCorner(int index, Vector2 value)
        {
            if (corners[index] == value) return false;
            corners[index] = value;
            return true;
        }

        private void OnDisable()
        {
            if (rig) { rig.SetActive(false); Destroy(rig); }
            VirtualCamera = null;
            previousTarget = null;
            hasBounds = false;
            if (Brain)
            {
                Brain.m_UpdateMethod = previousUpdate;
                Brain.m_BlendUpdateMethod = previousBlendUpdate;
                Brain.m_IgnoreTimeScale = previousIgnoreTimeScale;
            }
        }
    }
}
