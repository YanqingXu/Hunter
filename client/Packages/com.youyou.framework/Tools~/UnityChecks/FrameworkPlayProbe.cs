using System;
using System.Collections;
using UnityEngine;
using YouYou.Framework;
using YouYou.Framework.Validation;

public sealed class FrameworkPlayProbe : MonoBehaviour
{
    private IEnumerator Start()
    {
        var checks = Verify();
        while (true)
        {
            object current;
            try { if (!checks.MoveNext()) yield break; current = checks.Current; }
            catch (Exception e)
            {
                Debug.LogException(e);
#if UNITY_EDITOR
                UnityEditor.EditorApplication.Exit(1);
#endif
                yield break;
            }
            yield return current;
        }
    }

    private IEnumerator Verify()
    {
        var entry = new GameObject("Framework").AddComponent<FrameworkEntry>();
        CoreChecks.Assert(entry.Context != null && FrameworkEntry.Instance == entry);
        int count = 0;
        entry.Context.Event.CommonEvent.AddEventListener(10, _ => count++);
        entry.Context.Time.CreateTimeAction().Init(delayTime: .05f,
            onUpdate: _ => entry.Context.Event.CommonEvent.Dispatch(10)).Run();
        yield return new WaitForSecondsRealtime(.3f);
        CoreChecks.Assert(count == 1, "Unity Update must tick the timer exactly once.");
        var duplicate = new GameObject("Duplicate").AddComponent<FrameworkEntry>();
        yield return null;
        CoreChecks.Assert(!duplicate && FrameworkEntry.Instance == entry);
        Destroy(entry.gameObject);
        yield return null;
        CoreChecks.Assert(!FrameworkEntry.Instance, "OnDestroy must release the global entry.");
        var restarted = new GameObject("Restarted").AddComponent<FrameworkEntry>();
        CoreChecks.Assert(restarted.Context != null && FrameworkEntry.Instance == restarted);
        Destroy(restarted.gameObject);
        yield return null;
        Debug.Log("YOUYOU_FRAMEWORK_VALIDATION_PASSED: Core, Unity Editor and Play Mode lifecycle.");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }
}
