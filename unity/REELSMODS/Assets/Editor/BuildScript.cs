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
        BuildInternal();
    }

    public static void BuildFromCli()
    {
        int code = 0;
        try
        {
            BuildInternal();
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            code = 1;
        }
        EditorApplication.Exit(code);
    }

    /// <summary>
    /// NOTE: the version is read <i>after</i> the player settings are applied,
    /// otherwise the path is computed from whatever the previous build wrote
    /// into ProjectSettings.
    /// </summary>
    static string OutputPathForCurrentVersion()
    {
        string dir = Path.Combine(ProjectRoot(), "build");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "Reel-Mods-" + PlayerSettings.bundleVersion + ".apk");
    }

    static string ProjectRoot()
    {
        return Directory.GetParent(Application.dataPath).FullName;
    }

    static void BuildInternal()
    {
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;

        // ---- player settings -------------------------------------------------
        PlayerSettings.productName = "Reel Mods";
        PlayerSettings.companyName = "ReelsMods";
        PlayerSettings.bundleVersion = "0.2.1";
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
        string outputPath = OutputPathForCurrentVersion();
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
        // GetOrCreate() is internal but it is the only path that both creates
        // Assets/XR/XRGeneralSettingsPerBuildTarget.asset and registers it as
        // an EditorBuildSettings config object. Calling AddConfigObject with a
        // freshly CreateInstance'd object throws "non-persisted config object".
        var perTargetType = typeof(XRGeneralSettingsPerBuildTarget);
        var getOrCreate = perTargetType.GetMethod("GetOrCreate",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        XRGeneralSettingsPerBuildTarget perTarget;
        if (getOrCreate != null)
        {
            perTarget = (XRGeneralSettingsPerBuildTarget)getOrCreate.Invoke(null, null);
        }
        else
        {
            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out perTarget);
        }

        if (perTarget == null)
            throw new System.Exception("could not create XR general settings");

        if (!perTarget.HasSettingsForBuildTarget(BuildTargetGroup.Android))
            perTarget.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);
        if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
            perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);

        var settings = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
        settings.InitManagerOnStart = true;
        EditorUtility.SetDirty(settings);

        // NB: the Oculus loader lives in Unity.XR.Oculus, not UnityEngine.XR.Oculus
        bool ok = XRPackageMetadataStore.AssignLoader(settings.Manager,
            "Unity.XR.Oculus.OculusLoader", BuildTargetGroup.Android);

        Debug.Log("[REELS] oculus loader assigned: " + ok);
        if (!ok)
            throw new System.Exception("could not assign the Oculus XR loader");

        EditorUtility.SetDirty(perTarget);
        AssetDatabase.SaveAssets();
    }
}
