using System;
using System.Collections;
using System.IO;
using FlatBuffers;
using UnityEngine;
using UnityEngine.SceneManagement;
using YouYou;
using YouYou.DataTable;

/// <summary>Only the test rows and scenes are fixtures; parsing and switching use original production code.</summary>
public static class YouYouSceneValidation
{
    public const string FirstName = "YouYouValidationSceneA";
    public const string SecondName = "YouYouValidationSceneB";
    private const int FirstId = 61001, SecondId = 61002;

    public static IEnumerator Run(Action<bool, string> check)
    {
        byte[] originalScenes = ReadTable("DTSys_Scene");
        byte[] originalDetails = ReadTable("DTSys_SceneDetail");
        DTSys_SceneListExt.Init(DTSys_SceneList.GetRootAsDTSys_SceneList(new ByteBuffer(CreateScenes())));
        DTSys_SceneDetailListExt.Init(DTSys_SceneDetailList.GetRootAsDTSys_SceneDetailList(new ByteBuffer(CreateDetails())));
        yield return Until(() => GameEntry.DataTable.Sys_SceneList.GetEntity(FirstId).HasValue &&
            GameEntry.DataTable.Sys_SceneDetailList.GetEntity(FirstId).HasValue, "Original generated scene table initialization");
        check(GameEntry.DataTable.Sys_SceneList.GetEntity(SecondId).HasValue, "Original FlatBuffers schema resolves both fixture scene IDs");
        int callbacks = 0, progressEvents = 0;
        CommonEvent.OnActionHandler progress = value =>
        { if (value is BaseParams args && args.IntParam1 == (int)LoadingType.ChangeScene && args.FloatParam1 > 0) progressEvents++; };
        GameEntry.Event.CommonEvent.AddEventListener(SysEventId.LoadingProgressChange, progress);
        try
        {
            GameEntry.Scene.LoadScene(FirstId, false, () => callbacks++);
            yield return Until(() => callbacks == 1 && IsLoaded(FirstName), "Original first additive scene load");
            check(GameEntry.Scene.CurrSceneEntity.Value.Id == FirstId && GameObject.Find(FirstName + "-Actual-Scene-Content"),
                "Original Scene.LoadScene loads real scene objects additively");
            GameEntry.Scene.LoadScene(SecondId, false, () => callbacks++);
            yield return Until(() => callbacks == 2 && IsLoaded(SecondName) && !IsLoaded(FirstName), "Original unload/load scene transition");
            check(GameEntry.Scene.CurrSceneEntity.Value.Id == SecondId && GameObject.Find(SecondName + "-Actual-Scene-Content"),
                "Original scene transition unloads the previous scene and loads the next");
            check(progressEvents > 0, "Original scene loader publishes actual loading progress");
            GameEntry.Scene.LoadScene(FirstId, false, () => callbacks++);
            yield return Until(() => callbacks == 3 && IsLoaded(FirstName) && !IsLoaded(SecondName), "Original return scene transition");
            check(callbacks == 3, "Original scene callback completes once per transition and supports returning");
            yield return SceneManager.UnloadSceneAsync(FirstName);
        }
        finally
        {
            GameEntry.Event.CommonEvent.RemoveEventListener(SysEventId.LoadingProgressChange, progress);
            // Restore real table contents without touching their original files.
            DTSys_SceneListExt.Init(DTSys_SceneList.GetRootAsDTSys_SceneList(new ByteBuffer(originalScenes)));
            DTSys_SceneDetailListExt.Init(DTSys_SceneDetailList.GetRootAsDTSys_SceneDetailList(new ByteBuffer(originalDetails)));
        }
        yield return Until(() => !GameEntry.DataTable.Sys_SceneList.GetEntity(FirstId).HasValue &&
            GameEntry.DataTable.Sys_SceneList.GetList().Count > 0, "Restore original scene tables after validation");
        check(true, "Original scene table records restored after isolated validation");
    }

    private static byte[] ReadTable(string name) => ZlibHelper.DeCompressBytes(File.ReadAllBytes(
        Path.Combine(Application.dataPath, "Download/DataTable/" + name + ".bytes")));
    private static bool IsLoaded(string name) { var scene = SceneManager.GetSceneByName(name); return scene.IsValid() && scene.isLoaded; }
    private static IEnumerator Until(Func<bool> predicate, string label)
    {
        float end = Time.realtimeSinceStartup + 20;
        while (!predicate()) { if (Time.realtimeSinceStartup > end) throw new TimeoutException(label); yield return null; }
    }
    private static byte[] CreateScenes()
    {
        var builder = new FlatBufferBuilder(256);
        var rows = new Offset<DTSys_Scene>[2];
        string[] names = { FirstName, SecondName };
        for (int i = 0; i < 2; i++) rows[i] = DTSys_Scene.CreateDTSys_Scene(builder, FirstId + i,
            NameOffset: builder.CreateString(names[i]), SceneNameOffset: builder.CreateString(names[i]), BGMId: -1);
        var vector = DTSys_SceneList.CreateDTSysScenesVector(builder, rows);
        var table = DTSys_SceneList.CreateDTSys_SceneList(builder, vector); builder.Finish(table.Value);
        return builder.SizedByteArray();
    }
    private static byte[] CreateDetails()
    {
        var builder = new FlatBufferBuilder(256);
        var rows = new Offset<DTSys_SceneDetail>[2];
        string[] names = { FirstName, SecondName };
        for (int i = 0; i < 2; i++) rows[i] = DTSys_SceneDetail.CreateDTSys_SceneDetail(builder,
            Id: FirstId + i, SceneId: FirstId + i, SceneNameOffset: builder.CreateString(names[i]),
            ScenePathOffset: builder.CreateString("Assets/YouYouFullValidation/Fixtures/" + names[i] + ".unity"), SceneGrade: 0);
        var vector = DTSys_SceneDetailList.CreateDTSysSceneDetailsVector(builder, rows);
        var table = DTSys_SceneDetailList.CreateDTSys_SceneDetailList(builder, vector); builder.Finish(table.Value);
        return builder.SizedByteArray();
    }
}
