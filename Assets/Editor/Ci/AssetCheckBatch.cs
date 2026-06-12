#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Ci
{
    /// <summary>
    /// GitHub Actions BatchMode 入口：-executeMethod Ci.AssetCheckBatch.Run
    /// </summary>
    public static class AssetCheckBatch
    {
        [Serializable]
        public class Issue
        {
            public string file;
            public string severity; // blocker|major|minor|info
            public string category;
            public string issue;
            public string suggestion;
            public string standard_ref;
            public float confidence = 0.9f;
        }

        [Serializable]
        public class Report
        {
            public string summary;
            public string risk_level = "info";
            public List<Issue> issues = new();
        }

        public static void Run()
        {
            var report = new Report();
            try
            {
                CheckNaming(report);
                CheckTextures(report);
                // 在此扩展：Addressables、Audio、Model 等

                report.risk_level = CalcRisk(report.issues);
                report.summary = report.issues.Count == 0
                    ? "BatchMode 本地检查通过，未发现明显规范问题。"
                    : $"BatchMode 发现 {report.issues.Count} 项问题。";

                var json = JsonUtility.ToJson(report, true);
                var outPath = Path.Combine(Directory.GetCurrentDirectory(), "ci-asset-report.json");
                File.WriteAllText(outPath, json);
                Debug.Log($"[AssetCheckBatch] Report written: {outPath}\n{json}");

                // CI 只负责产出报告；资源规范问题不应导致 pipeline 红（Agent 侧会汇总）
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                report.summary = "BatchMode 检查异常: " + ex.Message;
                report.risk_level = "major";
                report.issues.Add(new Issue
                {
                    severity = "major",
                    category = "pipeline",
                    issue = ex.Message,
                    suggestion = "查看 unity-check.log",
                    standard_ref = "06_Build_Failure_Fix_SOP",
                });
                File.WriteAllText("ci-asset-report.json", JsonUtility.ToJson(report, true));
                Debug.LogError(ex);
                EditorApplication.Exit(1);
            }
        }

        static void CheckNaming(Report report)
        {
            var guids = AssetDatabase.FindAssets("", new[] { "Assets" });
            foreach (var guid in guids.Take(500)) // CI 可先抽样或全量，按项目调整
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || Directory.Exists(path)) continue;
                if (path.Contains("/Tutorials/", StringComparison.OrdinalIgnoreCase)) continue;
                var name = Path.GetFileName(path);
                if (name.StartsWith("Tex_") || name.StartsWith("UI_Icon_") || name.StartsWith("Prefab_"))
                    continue;
                if (path.EndsWith(".png") || path.EndsWith(".prefab") || path.EndsWith(".mat"))
                {
                    report.issues.Add(new Issue
                    {
                        file = path,
                        severity = "major",
                        category = "naming",
                        issue = $"资源命名不符合 Tex_/UI_Icon_/Prefab_ 前缀规范: {name}",
                        suggestion = "参考 07_Naming_And_Directory_Rules.md 重命名",
                        standard_ref = "07_Naming_And_Directory_Rules",
                    });
                }
            }
        }

        static void CheckTextures(Report report)
        {
            var guids = AssetDatabase.FindAssets("t:Texture", new[] { "Assets" });
            foreach (var guid in guids.Take(200))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                if (path.Contains("UI") && importer.mipmapEnabled)
                {
                    report.issues.Add(new Issue
                    {
                        file = path,
                        severity = "major",
                        category = "importer",
                        issue = "UI 贴图不应开启 Mipmap",
                        suggestion = "TextureImporter.generateMipMaps = false",
                        standard_ref = "02_Unity_Importer_Settings",
                    });
                }
            }
        }

        static string CalcRisk(List<Issue> issues)
        {
            if (issues.Any(i => i.severity == "blocker")) return "blocker";
            if (issues.Any(i => i.severity == "major")) return "major";
            if (issues.Any(i => i.severity == "minor")) return "minor";
            return "info";
        }
    }
}
#endif
