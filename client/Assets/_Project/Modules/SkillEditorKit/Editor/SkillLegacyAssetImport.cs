using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace SkillEditorKit.Editor
{
    /// <summary>Optional one-way copy of the original project's data. There is no compiled dependency on its types.</summary>
    public static class SkillLegacyAssetImport
    {
        [MenuItem("技能编辑器（独立版）/将选中的旧版技能另存为独立版")]
        private static void ConvertSelected()
        {
            var source = Selection.activeObject;
            if (source == null || source is SkillClip || source.GetType().FullName != "SkillClip")
            { EditorUtility.DisplayDialog("转换旧技能", "请先在原项目中选中一个旧版 SkillClip。独立版可以与旧版并存，不需要删除旧编辑器。", "确定"); return; }
            string path = EditorUtility.SaveFilePanelInProject("另存为独立技能", source.name + "_Independent", "asset",
                "创建新资产，不修改旧技能。动画、音效、特效等资源保持引用，需要另外迁移。普通表现资源建议使用不带游戏脚本的预制体。");
            if (string.IsNullOrEmpty(path)) return;
            SkillClip converted = null;
            try
            {
                if (File.Exists(path) || AssetDatabase.LoadMainAssetAtPath(path) != null) throw new IOException("目标已存在，请选择新名称；不会覆盖。");
                converted = ScriptableObject.CreateInstance<SkillClip>();
                CopyFields(source, converted);
                if (converted.DataVersion > SkillClip.CurrentDataVersion) throw new InvalidOperationException("旧资源的数据版本高于当前独立版，不能安全转换。");
                AssetDatabase.CreateAsset(converted, path); SkillEditorChangeUtility.MarkChanged(converted); SkillEditorChangeUtility.SaveNow(converted);
                Selection.activeObject = converted; EditorGUIUtility.PingObject(converted);
                EditorUtility.DisplayDialog("转换完成", "已创建独立版技能，原资产未修改。请检查配置和预览；自定义事件（如 AddBuff）需要由目标项目接入，带游戏脚本的预制体不会自动解耦。", "确定");
            }
            catch (Exception ex)
            {
                if (converted != null && !EditorUtility.IsPersistent(converted)) UnityEngine.Object.DestroyImmediate(converted);
                EditorUtility.DisplayDialog("转换未完成", ex.Message, "确定");
            }
        }
        internal static void CopyFields(object source, SkillClip target)
        {
            if (source == null || target == null) throw new ArgumentNullException();
            var seen = new Dictionary<object, object>(new ReferenceComparer()) { [source] = target };
            CopyObject(source, target, seen);
        }
        private static void CopyObject(object source, object target, Dictionary<object, object> seen)
        {
            foreach (var field in source.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var destination = target.GetType().GetField(field.Name, BindingFlags.Public | BindingFlags.Instance);
                if (destination == null || destination.IsInitOnly)
                    throw new InvalidOperationException("无法转换字段，已停止以避免丢失数据：" + source.GetType().Name + "." + field.Name);
                destination.SetValue(target, Copy(field.GetValue(source), destination.FieldType, seen));
            }
        }
        private static object Copy(object source, Type targetType, Dictionary<object, object> seen)
        {
            if (source == null) return null;
            if (source is UnityEngine.Object unityObject)
            {
                if (!targetType.IsInstanceOfType(unityObject)) throw new InvalidOperationException("无法转换资源引用：" + targetType.Name);
                return unityObject;
            }
            if (targetType.IsEnum) return Enum.ToObject(targetType, Convert.ToInt64(source));
            if (targetType.IsValueType || targetType == typeof(string))
            {
                if (!targetType.IsInstanceOfType(source)) throw new InvalidOperationException("字段类型不兼容：" + targetType.Name);
                return source;
            }
            if (seen.TryGetValue(source, out var existing)) return existing;
            if (seen.Count > 10000) throw new InvalidOperationException("数据对象过多，已停止转换。");
            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                var result = (IDictionary)Activator.CreateInstance(targetType); seen.Add(source, result);
                var types = targetType.GetGenericArguments();
                if (!(source is IDictionary dictionary)) throw new InvalidOperationException("旧字典类型不兼容。");
                foreach (DictionaryEntry entry in dictionary) result.Add(Copy(entry.Key, types[0], seen), Copy(entry.Value, types[1], seen));
                return result;
            }
            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(List<>))
            {
                var result = (IList)Activator.CreateInstance(targetType); seen.Add(source, result);
                if (!(source is IEnumerable sequence)) throw new InvalidOperationException("旧列表类型不兼容。");
                foreach (var entry in sequence) result.Add(Copy(entry, targetType.GetGenericArguments()[0], seen));
                return result;
            }
            var actual = typeof(SkillClip).Assembly.GetType("SkillEditorKit." + source.GetType().Name);
            if (actual == null || !targetType.IsAssignableFrom(actual) || actual.IsAbstract)
                throw new InvalidOperationException("未识别的旧事件类型，已停止以避免丢失数据：" + source.GetType().FullName);
            var copy = Activator.CreateInstance(actual); seen.Add(source, copy); CopyObject(source, copy, seen); return copy;
        }
        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
        }
    }
}
