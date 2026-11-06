using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;

/// <summary>
/// Headless build for the REELS MODS Quest APK.
///   Unity.exe -batchmode -quit -projectPath . -buildTarget Android \
///             -executeMethod BuildScript.BuildFromCli -logFile build.log
/// </summary>
public static class BuildScript
{
    const string ScenePath = "Assets/Scenes/Main.unity";

    [MenuItem("REELS/Build APK")]
    public static void Build()
    {
        BuildInternal(FindOutputPath());
    }

    public static void BuildFromCli()
    {
        int code = 0;
        try
        {
            BuildInternal(FindOutputPath());
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            code = 1;
        }
        EditorApplication.Exit(code);
    }

    static string FindOutputPath()
    {
        string dir = Path.Combine(ProjectRoot(), "build");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "REELS-MODS-" + PlayerSettings.bundleVersion + ".apk");
    }

    static string ProjectRoot()
    {
        return Directory.GetParent(Application.dataPath).FullName;
    }

    static void BuildInternal(string outputPath)
    {
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;

        // ---- player settings -------------------------------------------------
        PlayerSettings.productName = "REELS MODS";
        PlayerSettings.companyName = "ReelsMods";
        PlayerSettings.bundleVersion = "0.2.0";
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.reelsmods.menu");
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        // 2022.3.2f1's AndroidSdkVersions enum stops at API 30, so leave the
        // target SDK on Unity's default and only pin the floor.
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
        PlayerSettings.Android.androidIsGame = true;
        PlayerSettings.stripEngineCode = false;
        PlayerSettings.MTRendering = true;
        PlayerSettings.resizableWindow = false;
        // input stays on the legacy manager: StandaloneInputModule + Input.*

        EnsureShaders();
        EnableOculusXr();

        // ---- scene -----------------------------------------------------------
        Directory.CreateDirectory(Path.Combine(ProjectRoot(), "Assets", "Scenes"));
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var bootstrap = new GameObject("Bootstrap");
        bootstrap.AddComponent<ReelsMods.MenuManager>();
        if (!EditorSceneManager.SaveScene(scene, ScenePath))
            throw new System.Exception("could not save scene " + ScenePath);
        EditorSceneManager.OpenScene(ScenePath);

        // ---- build -----------------------------------------------------------
        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = outputPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.None
        };

        var report = BuildPipeline.BuildPlayer(options);
        var summary = report.summary;

        Debug.Log(string.Format("[REELS] build {0}  errors={1}  warnings={2}  size={3}  time={4}",
            summary.result, summary.totalErrors, summary.totalWarnings,
            summary.totalSize, summary.totalTime));

        if (summary.result != BuildResult.Succeeded)
            throw new System.Exception("build failed: " + summary.result);

        Debug.Log("[REELS] APK -> " + outputPath);
    }

    /// <summary>
    /// The whole UI is built in code, so no material or shader is referenced
    /// from an asset. Without this the build strips UI/Default and the panel
    /// renders magenta (or not at all) on device.
    /// </summary>
    static void EnsureShaders()
    {
        string[] required = { "UI/Default", "Sprites/Default", "Unlit/Color" };

        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
        if (assets == null || assets.Length == 0)
        {
            Debug.LogWarning("[REELS] GraphicsSettings.asset not found, skipping shader whitelist");
            return;
        }

        var so = new SerializedObject(assets[0]);
        var arr = so.FindProperty("m_AlwaysIncludedShaders");
        if (arr == null || !arr.isArray)
        {
            Debug.LogWarning("[REELS] m_AlwaysIncludedShaders not found");
            return;
        }

        var have = new System.Collections.Generic.HashSet<string>();
        for (int i = 0; i < arr.arraySize; i++)
        {
            var s = arr.GetArrayElementAtIndex(i).objectReferenceValue as Shader;
            if (s != null) have.Add(s.name);
        }

        foreach (var name in required)
        {
            if (have.Contains(name)) continue;
            var sh = Shader.Find(name);
            if (sh == null) { Debug.LogWarning("[REELS] shader not found: " + name); continue; }
            arr.arraySize = arr.arraySize + 1;
            arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
            Debug.Log("[REELS] whitelisted shader: " + name);
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
    }

    /// <summary>Turn on the Oculus loader for the Android target.</summary>
    static void EnableOculusXr()
    {
        XRGeneralSettingsPerBuildTarget perTarget = null;
        EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out perTarget);

        if (perTarget == null)
        {
            perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            perTarget.name = "XR General Settings";
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
        }

        var settings = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<XRGeneralSettings>();
            settings.name = "Android Settings";
            perTarget.SetSettingsForBuildTarget(BuildTargetGroup.Android, settings);
        }

        if (settings.Manager == null)
        {
            var mgr = ScriptableObject.CreateInstance<XRManagerSettings>();
            mgr.name = "Android XR Managers";
            settings.Manager = mgr;
        }

        settings.InitManagerOnStart = true;

        bool ok = XRPackageMetadataStore.AssignLoader(settings.Manager,
            "UnityEngine.XR.Oculus.OculusLoader", BuildTargetGroup.Android);

        Debug.Log("[REELS] oculus loader assigned: " + ok);
        EditorUtility.SetDirty(perTarget);
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
    }
}
