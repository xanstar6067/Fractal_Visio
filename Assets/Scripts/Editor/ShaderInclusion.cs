using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace FractalVisio.EditorTools
{
    /// <summary>
    /// Keeps every shader under <c>Assets/Shaders</c> in Always Included Shaders (GraphicsSettings).
    ///
    /// The app finds all its shaders by name - each fractal's, the frame compositor, the glass blur -
    /// and <c>Shader.Find</c> in a player build sees only shaders the build contains, which is only
    /// what something references. In the editor every shader is found, so a shader missing from the
    /// list shows only on the device: no gallery preview, and the fractal drawn on the CPU alone. The
    /// list lives in ProjectSettings and was kept by hand, and twice it was not complete: Burning Ship
    /// and the blur until 2026-09-23, and on 2026-09-24 both Julia sets in a build made on another PC.
    /// The file there had them after the pull; the editor, open through the pull, kept the list it had
    /// loaded before - it does not read GraphicsSettings back from disk, not even on a script reload.
    ///
    /// So the list is completed from the folder at two moments:
    /// <list type="bullet">
    /// <item>when a shader in the folder is imported, and after every domain reload - a pull that
    /// brings a shader, or this file, lands here - and the settings are saved;</item>
    /// <item>before every build, on whatever the editor holds in memory, which is what the build
    /// reads. This one alone would do; the other keeps the file and the editor in step.</item>
    /// </list>
    /// It only adds: an entry is never removed here.
    /// </summary>
    internal sealed class ShaderInclusion : AssetPostprocessor, IPreprocessBuildWithReport
    {
        private const string ShaderFolder = "Assets/Shaders";
        private const string SettingsPath = "ProjectSettings/GraphicsSettings.asset";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            // In memory only: the build reads the settings the editor holds, and saving assets while
            // a build starts is asking for trouble. The next import or reload saves them.
            Log(Complete(false), "before the build");
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths,
            bool didDomainReload)
        {
            if (didDomainReload || TouchesShaders(importedAssets) || TouchesShaders(movedAssets))
            {
                Log(Complete(true), didDomainReload ? "after a script reload" : "after a shader import");
            }
        }

        /// <summary>Add each shader in the folder the list lacks. Returns the names added.</summary>
        internal static List<string> Complete(bool save)
        {
            var added = new List<string>();
            var settings = AssetDatabase.LoadAssetAtPath<GraphicsSettings>(SettingsPath);
            if (settings == null)
            {
                return added;
            }

            var serialized = new SerializedObject(settings);
            var list = serialized.FindProperty("m_AlwaysIncludedShaders");
            if (list == null || !list.isArray)
            {
                return added;
            }

            var present = new HashSet<Shader>();
            for (var i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue is Shader shader)
                {
                    present.Add(shader);
                }
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Shader", new[] { ShaderFolder }))
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(guid));
                if (shader == null || !present.Add(shader))
                {
                    continue;
                }

                var index = list.arraySize;
                list.InsertArrayElementAtIndex(index);
                list.GetArrayElementAtIndex(index).objectReferenceValue = shader;
                added.Add(shader.name);
            }

            if (added.Count == 0)
            {
                return added;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            if (save)
            {
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }

            return added;
        }

        private static bool TouchesShaders(string[] paths)
        {
            if (paths == null)
            {
                return false;
            }

            for (var i = 0; i < paths.Length; i++)
            {
                if (paths[i].StartsWith(ShaderFolder + "/", StringComparison.Ordinal) &&
                    paths[i].EndsWith(".shader", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void Log(List<string> added, string when)
        {
            if (added.Count > 0)
            {
                Debug.Log("Always Included Shaders completed " + when + ": " + string.Join(", ", added));
            }
        }
    }
}
