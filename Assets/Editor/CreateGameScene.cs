#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.IO;

[InitializeOnLoad]
public static class CreateGameScene
{
    static CreateGameScene() { EditorApplication.delayCall += EnsureScene; }
    [MenuItem("Marble Avalanche/Create Playable Scene")]
    static void EnsureScene()
    {
        const string path="Assets/Scenes/MarbleAvalanche.unity";
        if(File.Exists(path)) return;
        Directory.CreateDirectory("Assets/Scenes");
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var root=new GameObject("Marble Avalanche Game"); root.AddComponent<MarbleAvalancheGame>();
        EditorSceneManager.SaveScene(scene,path);
        EditorBuildSettings.scenes=new [] {new EditorBuildSettingsScene(path,true)};
        AssetDatabase.SaveAssets();
        Debug.Log("Marble Avalanche scene created. Open Assets/Scenes/MarbleAvalanche.unity and press Play.");
    }
    [MenuItem("Marble Avalanche/Import Private Portrait")]
    static void ImportPortrait()
    {
        var file=EditorUtility.OpenFilePanel("Select the couple photograph", "", "jpg,jpeg,png");
        if(string.IsNullOrEmpty(file))return;
        Directory.CreateDirectory("Assets/Resources");
        File.Copy(file,"Assets/Resources/Portrait.jpg",true);
        AssetDatabase.Refresh();
        Debug.Log("Private portrait imported locally. The file is ignored by Git.");
    }
}
#endif
