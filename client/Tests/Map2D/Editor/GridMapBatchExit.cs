using System;
using System.Globalization;
using UnityEditor;
using UnityEngine;

/// <summary>Finishes isolated batch probes after Play Mode cleanup and imports have settled.</summary>
[InitializeOnLoad]
public static class GridMapBatchExit
{
    private const string SessionKey = "BigWorld.Map2D.BatchExit.Pending";
    private const double StableSeconds = 2;
    private const double TimeoutSeconds = 30;
    [Serializable]
    private sealed class Pending
    {
        public int code;
        public string reportPath;
        public string requestedUtc;
        public bool prepared;
        public bool forceExit;
    }

    private static Pending pending;
    private static double idleSince = -1;
    private static int idleUpdates;
    private static bool exitQueued;

    static GridMapBatchExit()
    {
        if (Application.isBatchMode && !string.IsNullOrEmpty(SessionState.GetString(SessionKey, "")))
            EditorApplication.delayCall += Resume;
    }

    public static void Request(int exitCode, string reportPath)
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Stable batch exit is restricted to a dedicated validation process.");
        pending = new Pending { code = exitCode, reportPath = reportPath, requestedUtc = DateTime.UtcNow.ToString("O") };
        Save();
        Resume();
    }

    private static void Resume()
    {
        string saved = SessionState.GetString(SessionKey, "");
        if (string.IsNullOrEmpty(saved)) return;
        pending = JsonUtility.FromJson<Pending>(saved);
        idleSince = -1;
        idleUpdates = 0;
        exitQueued = false;
        EditorApplication.update -= Update;
        EditorApplication.update += Update;
    }

    private static bool Busy => EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating;

    private static void Update()
    {
        if (pending == null || exitQueued) return;
        try
        {
            var started = DateTime.Parse(pending.requestedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
            if ((DateTime.UtcNow - started).TotalSeconds > TimeoutSeconds)
            {
                Fail("Editor did not settle after Play Mode cleanup within 30 seconds. Report: " + pending.reportPath);
                return;
            }
            if (Busy) { idleSince = -1; idleUpdates = 0; return; }
            if (!pending.prepared)
            {
                // Persist before import callbacks can reload the domain. Flush the saves
                // that Editor.Exit would otherwise start while native modules are shutting down.
                pending.prepared = true;
                Save();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                idleSince = -1;
                idleUpdates = 0;
                return;
            }
            if (idleSince < 0) idleSince = EditorApplication.timeSinceStartup;
            idleUpdates++;
            if (idleUpdates >= 3 && EditorApplication.timeSinceStartup - idleSince >= StableSeconds) QueueExit();
        }
        catch (Exception exception) { Fail(exception.ToString()); }
    }

    private static void Fail(string message)
    {
        Debug.LogError(message);
        pending.code = 1;
        pending.forceExit = true;
        Save();
        QueueExit();
    }

    private static void QueueExit()
    {
        if (exitQueued) return;
        exitQueued = true;
        EditorApplication.delayCall += CommitExit;
    }

    private static void CommitExit()
    {
        exitQueued = false;
        if (pending == null) return;
        if (!pending.forceExit && Busy) { idleSince = -1; idleUpdates = 0; return; }
        int code = pending.code;
        string report = pending.reportPath;
        pending = null;
        EditorApplication.update -= Update;
        EditorApplication.delayCall -= Resume;
        SessionState.EraseString(SessionKey);
        Debug.Log("BIGWORLD_MAP_EDITOR_EXIT_REQUESTED: code=" + code + ", editorStable=" + !Busy + ", report=" + report);
        EditorApplication.Exit(code);
    }

    private static void Save() { SessionState.SetString(SessionKey, JsonUtility.ToJson(pending)); }
}
