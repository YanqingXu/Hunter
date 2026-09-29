using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class GameplayValidation
{
    public static void Run()
    {
        try
        {
            BigWorld.Editor.BorderTrialBuilder.CreateAndConfigure();
            BigWorld.YouYou2D.Editor.NativeFramework2DBuild.PrepareContent(EditorUserBuildSettings.activeBuildTarget);
            EditorSceneManager.OpenScene("Assets/_Project/Game/Scenes/BorderTrial.unity");
            new GameObject("Border Trial Gameplay Acceptance").AddComponent<GameplayPlayChecks>();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/__GameplayChecks/Validation.unity");
            EditorApplication.EnterPlaymode();
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
}
