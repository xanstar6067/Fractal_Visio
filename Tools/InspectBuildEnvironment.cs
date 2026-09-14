var report = UnityEditor.Build.Reporting.BuildReport.GetLatestReport();
return new {
    editor = UnityEditor.EditorApplication.applicationPath,
    target = UnityEditor.EditorUserBuildSettings.activeBuildTarget.ToString(),
    androidSupported = UnityEditor.BuildPipeline.IsBuildTargetSupported(UnityEditor.BuildTargetGroup.Android, UnityEditor.BuildTarget.Android),
    lastOutput = report == null ? null : report.summary.outputPath,
    lastResult = report == null ? null : report.summary.result.ToString(),
    packedShaders = report == null ? null : System.Linq.Enumerable.ToArray(
        System.Linq.Enumerable.Distinct(System.Linq.Enumerable.Select(
            System.Linq.Enumerable.Where(System.Linq.Enumerable.SelectMany(report.packedAssets, p => p.contents),
                c => c.sourceAssetPath.EndsWith(".shader")), c => c.sourceAssetPath))),
    scenes = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(UnityEditor.EditorBuildSettings.scenes, s => new { s.path, s.enabled }))
};
