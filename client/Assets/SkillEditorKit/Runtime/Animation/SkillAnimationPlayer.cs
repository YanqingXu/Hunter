// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit
{
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
/// <summary>
/// 动画控制器
/// </summary>
public class SkillAnimationPlayer : MonoBehaviour, ISkillAnimationDriver
{
    [SerializeField] Animator animator;
    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;

    private AnimationNodeBase previousNode; // 上一个节点
    private AnimationNodeBase currentNode;  // 当前节点
    private int inputPort0 = 0;
    private int inputPort1 = 1;


    private Coroutine transitionCoroutine;
    private long animationRevision;
    private bool skillAnimationActive;

    private float speed;
    public float Speed
    {
        get => speed;
        set
        {
            speed = value;
            currentNode?.SetSpeed(speed);
        }
    }

    public void Init()
    {
        if (graph.IsValid()) return;
        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null) throw new InvalidOperationException("SkillAnimationPlayer requires an Animator.");
        // 创建图
        graph = PlayableGraph.Create("SkillAnimationPlayer");
        // 设置图的时间模式
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        // 创建混合器
        mixer = AnimationMixerPlayable.Create(graph, 3);
        // 创建Output
        AnimationPlayableOutput playableOutput = AnimationPlayableOutput.Create(graph, "Animation", animator);
        // 让混合器链接上Output
        playableOutput.SetSourcePlayable(mixer);
    }

    public void DestoryNode(AnimationNodeBase node)
    {
        if (node != null)
        {
            graph.Disconnect(mixer, node.InputPort);
            node.PushPool();
        }
    }

    private void StartTransitionAniamtion(float fixedTime)
    {
        if (transitionCoroutine != null) StopCoroutine(transitionCoroutine);
        transitionCoroutine = null;
        fixedTime = float.IsNaN(fixedTime) || float.IsInfinity(fixedTime) ? 0 : Mathf.Max(0, fixedTime);
        int temp = inputPort0;
        inputPort0 = inputPort1;
        inputPort1 = temp;
        if (fixedTime <= 0)
        {
            mixer.SetInputWeight(inputPort1, 0);
            mixer.SetInputWeight(inputPort0, 1);
            return;
        }
        transitionCoroutine = StartCoroutine(TransitionAniamtion(fixedTime));
    }

    // 动画过渡
    private IEnumerator TransitionAniamtion(float fixedTime)
    {
        // 当前的权重
        float currentWeight = 1;
        float speed = 1 / fixedTime;

        while (currentWeight > 0)
        {
            // 权重在减少
            currentWeight = Mathf.Clamp01(currentWeight - Time.deltaTime * speed);
            mixer.SetInputWeight(inputPort1, currentWeight);  // 减少
            mixer.SetInputWeight(inputPort0, 1 - currentWeight); // 增加
            yield return null;
        }
        transitionCoroutine = null;
    }

    /// <summary>
    /// 播放单个动画
    /// </summary>
    public void PlaySingleAniamtion(AnimationClip animationClip, float speed = 1, bool refreshAnimation = false, float transitionFixedTime = 0.25f)
    {
        SingleAnimationNode singleAnimationNode = null;
        if (currentNode == null) // 首次播放
        {
            singleAnimationNode = new SingleAnimationNode();
            singleAnimationNode.Init(graph, mixer, animationClip, speed, inputPort0);
            mixer.SetInputWeight(inputPort0, 1);
        }
        else
        {
            SingleAnimationNode preNode = currentNode as SingleAnimationNode; // 上一个节点

            // 相同的动画
            if (!refreshAnimation && !skillAnimationActive && this.speed == speed && graph.IsPlaying() &&
                preNode != null && preNode.GetAnimationClip() == animationClip) return;
            // 销毁掉当前可能被占用的Node
            DestoryNode(previousNode);
            singleAnimationNode = new SingleAnimationNode();
            singleAnimationNode.Init(graph, mixer, animationClip, speed, inputPort1);
            previousNode = currentNode;
            StartTransitionAniamtion(transitionFixedTime);
        }
        this.speed = speed;
        currentNode = singleAnimationNode;
        skillAnimationActive = false;
        animationRevision++;
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        if (graph.IsPlaying() == false) graph.Play();
    }


    /// <summary>
    /// 播放混合动画
    /// </summary>
    public void PlayBlendAnimation(List<AnimationClip> clips, float speed = 1, float transitionFixedTime = 0.25f)
    {
        BlendAnimationNode blendAnimationNode = new BlendAnimationNode();
        // 如果是第一次播放，不存在过渡
        if (currentNode == null)
        {
            blendAnimationNode.Init(graph, mixer, clips, speed, inputPort0);
            mixer.SetInputWeight(inputPort0, 1);
        }
        else
        {
            DestoryNode(previousNode);
            blendAnimationNode.Init(graph, mixer, clips, speed, inputPort1);
            previousNode = currentNode;
            StartTransitionAniamtion(transitionFixedTime);
        }
        this.speed = speed;
        currentNode = blendAnimationNode;
        skillAnimationActive = false;
        animationRevision++;
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        if (graph.IsPlaying() == false) graph.Play();
    }

    /// <summary>
    /// 播放混合动画
    /// </summary>
    public void PlayBlendAnimation(AnimationClip clip1, AnimationClip clip2, float speed = 1, float transitionFixedTime = 0.25f)
    {
        BlendAnimationNode blendAnimationNode = new BlendAnimationNode();
        // 如果是第一次播放，不存在过渡
        if (currentNode == null)
        {
            blendAnimationNode.Init(graph, mixer, clip1, clip2, speed, inputPort0);
            mixer.SetInputWeight(inputPort0, 1);
        }
        else
        {
            DestoryNode(previousNode);
            blendAnimationNode.Init(graph, mixer, clip1, clip2, speed, inputPort1);
            previousNode = currentNode;
            StartTransitionAniamtion(transitionFixedTime);
        }
        this.speed = speed;
        currentNode = blendAnimationNode;
        skillAnimationActive = false;
        animationRevision++;
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        if (graph.IsPlaying() == false) graph.Play();
    }

    /// <summary>Range clips use the skill clock, so neither a hitch nor a stopped skill can play the cropped tail.</summary>
    public Action<double> PlaySkillAnimationRange(AnimationClip clip, float clipIn, float duration, float transitionTime)
    {
        double start = SkillMediaTiming.AnimationRangeTime(clip, clipIn);
        double end = SkillMediaTiming.AnimationRangeTime(clip, clipIn + Math.Max(0, duration));
        return PlaySkillAnimationCore(clip, elapsed => Math.Max(start, Math.Min(end, start + Math.Max(0, elapsed))),
            Mathf.Min(Mathf.Max(0, transitionTime), Mathf.Max(0, duration)));
    }

    /// <summary>Clip time AND crossfade weight are advanced by the same skill clock.</summary>
    public Action<double> PlaySkillAnimation(SkillAnimationEvent data, int frameRate,
        SkillAnimationEvent outgoing = null, double outgoingElapsedAtStart = 0, double overlapSeconds = 0)
    {
        var timing = SkillAnimationTiming.Snapshot(data);
        var previousTiming = SkillAnimationTiming.Snapshot(outgoing);
        return PlaySkillAnimationCore(timing.AnimationClip, elapsed => SkillAnimationTiming.SourceTime(timing, frameRate, elapsed),
            (float)overlapSeconds, previousTiming == null ? (Func<double, double>)null :
            elapsed => SkillAnimationTiming.SourceTime(previousTiming, frameRate, outgoingElapsedAtStart + elapsed));
    }

    private Action<double> PlaySkillAnimationCore(AnimationClip clip, Func<double, double> sourceTime, float transitionTime,
        Func<double, double> outgoingTime = null)
    {
        if (float.IsNaN(transitionTime) || float.IsInfinity(transitionTime) || transitionTime < 0) transitionTime = 0;
        bool hasPrevious = currentNode != null;
        PlaySingleAniamtion(clip, 0, true, transitionTime);
        // General locomotion transitions use a coroutine. Skill transitions are sampled
        // explicitly so pause, frame catch-up and scrubbing do not use a second clock.
        if (transitionCoroutine != null) StopCoroutine(transitionCoroutine);
        transitionCoroutine = null;
        previousNode?.SetSpeed(0);
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        skillAnimationActive = true;
        var node = currentNode as SingleAnimationNode;
        var outgoingNode = previousNode as SingleAnimationNode;
        long revision = animationRevision;
        int incomingPort = node.InputPort;
        int outgoingPort = previousNode == null ? -1 : previousNode.InputPort;
        double start = sourceTime(0);
        // Reset previous time as Timeline does, preventing events/root motion from the discarded prefix.
        node.SetTime(start);
        node.SetTime(start);
        return elapsed =>
        {
            if (!skillAnimationActive || revision != animationRevision || !ReferenceEquals(node, currentNode) || !graph.IsValid()) return;
            float weight = !hasPrevious || transitionTime <= 0 ? 1 : Mathf.Clamp01((float)(Math.Max(0, elapsed) / transitionTime));
            if (outgoingPort >= 0) mixer.SetInputWeight(outgoingPort, 1 - weight);
            mixer.SetInputWeight(incomingPort, weight);
            if (outgoingTime != null && outgoingNode != null) outgoingNode.SetTime(outgoingTime(elapsed));
            node.SetTime(sourceTime(elapsed));
            graph.Evaluate(0);
        };
    }

    public void StopSkillAnimation()
    {
        if (!skillAnimationActive) return;
        skillAnimationActive = false;
        animationRevision++;
        currentNode?.SetSpeed(0);
        previousNode?.SetSpeed(0);
    }

    public void SetBlendWeight(List<float> weightList)
    {
        (currentNode as BlendAnimationNode).SetBlendWeight(weightList);
    }
    public void SetBlendWeight(float clip1Weight)
    {
        (currentNode as BlendAnimationNode).SetBlendWeight(clip1Weight);
    }


    private void OnDestroy()
    {
        StopSkillAnimation();
        if (graph.IsValid())
        {
            DestoryNode(previousNode); previousNode = null;
            DestoryNode(currentNode); currentNode = null;
            graph.Destroy();
        }
    }

    private void OnDisable()
    {
        StopSkillAnimation();
        if (transitionCoroutine != null) StopCoroutine(transitionCoroutine);
        transitionCoroutine = null;
        if (graph.IsValid()) graph.Stop();
    }

    private void OnEnable()
    {
        if (graph.IsValid() && currentNode != null) graph.Play();
    }

    #region RootMotion
    private Action<Vector3, Quaternion> rootMotionAction;
    private void OnAnimatorMove()
    {
        rootMotionAction?.Invoke(animator.deltaPosition, animator.deltaRotation);
    }
    public void SetRootMotionAction(Action<Vector3, Quaternion> rootMotionAction)
    {
        this.rootMotionAction = rootMotionAction;
    }
    public void ClearRootMotionAction()
    {
        rootMotionAction = null;
    }
    #endregion
    #region 动画事件
    private Dictionary<string, Action> eventDic = new Dictionary<string, Action>();
    // Animator会触发的实际事件函数
    private void AniamtionEvent(string eventName)
    {
        if (eventDic.TryGetValue(eventName, out Action action))
        {
            action?.Invoke();
        }
    }
    public void AddAniamtionEvent(string eventName, Action action)
    {
        if (eventDic.TryGetValue(eventName, out Action _action))
        {
            _action += action;
        }
        else
        {
            eventDic.Add(eventName, action);
        }
    }

    public void RemoveAnimationEvent(string eventName)
    {
        eventDic.Remove(eventName);
    }

    public void RemoveAnimationEvent(string eventName, Action action)
    {
        if (eventDic.TryGetValue(eventName, out Action _action))
        {
            _action -= action;
        }
    }

    public void CleanAllActionEvent()
    {
        eventDic.Clear();
    }
    #endregion

}

}
