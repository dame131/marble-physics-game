#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using System.IO;

public static class BuildAndroid
{
    [MenuItem("Marble Avalanche/Build Android Development APK")]
    public static void Build()
    {
        const string scene="Assets/Scenes/MarbleAvalanche.unity";
        if(!File.Exists(scene))throw new FileNotFoundException("Open the project so the scene can be created",scene);
        Directory.CreateDirectory("Builds");
        PlayerSettings.productName="Marble Avalanche";
        PlayerSettings.companyName="Mr131 Games";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,"com.mr131.marbleavalanche");
        PlayerSettings.Android.minSdkVersion=AndroidSdkVersions.AndroidApiLevel26;
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android,BuildTarget.Android);
        var options=new BuildPlayerOptions {
            scenes=new []{scene},
            locationPathName="Builds/MarbleAvalanche-debug.apk",
            target=BuildTarget.Android,
            options=BuildOptions.Development|BuildOptions.AllowDebugging
        };
        var report=BuildPipeline.BuildPlayer(options);
        if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new System.Exception("Android build failed: "+report.summary.result);
        Debug.Log("Android APK: "+Path.GetFullPath(options.locationPathName));
    }
}
#endif
