using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Unity6SkillAI.Editor
{
    /// <summary>
    /// Write tools. Every one of them is gated behind the approval switch and
    /// records a single Unity undo group so one AI action equals one Ctrl+Z.
    /// </summary>
    internal static class UnityWriteTools
    {
        private const string UndoPrefix = "Unity6 Skill AI: ";
        private static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

        public static void Register(List<ToolDefinition> list)
        {
            ToolRegistry.Add(list, "create_gameobject",
                "在当前场景创建 GameObject，可指定名称、父对象、基础几何体类型(Cube/Sphere/Capsule/Cylinder/Plane/Quad)、本地坐标/缩放和要挂载的组件。",
                ToolRegistry.Schema(MiniJson.Dict(
                    "name", ToolRegistry.Prop("string", "对象名称"),
                    "parent", ToolRegistry.Prop("string", "父对象层级路径或名称，可留空表示根节点"),
                    "primitive", ToolRegistry.Prop("string", "可选基础几何体：Cube/Sphere/Capsule/Cylinder/Plane/Quad；留空创建空对象"),
                    "position", ToolRegistry.ArrayProp("number", "本地坐标 [x,y,z]"),
                    "scale", ToolRegistry.ArrayProp("number", "本地缩放 [x,y,z]"),
                    "components", ToolRegistry.ArrayProp("string", "要添加的组件类型名，如 Rigidbody、BoxCollider、Light"))),
                true, CreateGameObject);

            ToolRegistry.Add(list, "add_component",
                "给场景中已有的 GameObject 添加一个组件（按类型名）。",
                ToolRegistry.Schema(MiniJson.Dict(
                    "target", ToolRegistry.Prop("string", "目标对象层级路径或名称"),
                    "component", ToolRegistry.Prop("string", "组件类型名，如 Rigidbody、AudioSource")),
                    "target", "component"),
                true, AddComponent);

            ToolRegistry.Add(list, "set_transform",
                "修改已有 GameObject 的本地坐标、欧拉角或缩放，未提供的项保持不变。",
                ToolRegistry.Schema(MiniJson.Dict(
                    "target", ToolRegistry.Prop("string", "目标对象层级路径或名称"),
                    "position", ToolRegistry.ArrayProp("number", "本地坐标 [x,y,z]"),
                    "rotation", ToolRegistry.ArrayProp("number", "本地欧拉角 [x,y,z]"),
                    "scale", ToolRegistry.ArrayProp("number", "本地缩放 [x,y,z]")),
                    "target"),
                true, SetTransform);

            ToolRegistry.Add(list, "delete_gameobject",
                "删除场景中的 GameObject（可通过 Ctrl+Z 撤销）。",
                ToolRegistry.Schema(MiniJson.Dict(
                    "target", ToolRegistry.Prop("string", "目标对象层级路径或名称")), "target"),
                true, DeleteGameObject);

            ToolRegistry.Add(list, "create_script",
                "把一个完整的 C# 脚本写入 Assets/ 下的指定路径并触发导入。写之前应先用 get_project_info / read_script 确认版本与既有代码。写入后 Unity 可能编译并发生域重载，务必随后调用 get_compile_errors 自查。",
                ToolRegistry.Schema(MiniJson.Dict(
                    "path", ToolRegistry.Prop("string", "Assets/ 开头、.cs 结尾的完整路径"),
                    "content", ToolRegistry.Prop("string", "完整 C# 源码文本")),
                    "path", "content"),
                true, CreateScript);

            ToolRegistry.Add(list, "create_material",
                "创建 .mat 材质资源，可指定 Shader 与颜色。默认优先 Universal Render Pipeline/Lit，回退 Standard。",
                ToolRegistry.Schema(MiniJson.Dict(
                    "path", ToolRegistry.Prop("string", "Assets/ 开头、.mat 结尾的完整路径"),
                    "shader", ToolRegistry.Prop("string", "Shader 名称，可留空自动选择"),
                    "color", ToolRegistry.ArrayProp("number", "颜色 [r,g,b,a]，取值 0-1")),
                    "path"),
                true, CreateMaterial);

            ToolRegistry.Add(list, "save_scene",
                "保存当前打开的所有场景。",
                ToolRegistry.Schema(new Dictionary<string, object>()), true, SaveScene);

            ToolRegistry.Add(list, "refresh_assets",
                "强制刷新资源数据库并触发脚本编译，用于写入脚本后让 Unity 重新导入。",
                ToolRegistry.Schema(new Dictionary<string, object>()), true, RefreshAssets);
        }

        private static void BeginUndoGroup(string label)
        {
            Undo.SetCurrentGroupName(UndoPrefix + label);
        }

        private static void EndUndoGroup()
        {
            Undo.IncrementCurrentGroup();
        }

        private static ToolResult CreateGameObject(Dictionary<string, object> args)
        {
            string name = ToolArgs.S(args, "name", "New GameObject");
            string primitive = ToolArgs.S(args, "primitive");
            string parent = ToolArgs.S(args, "parent");
            BeginUndoGroup("创建 " + name);

            GameObject go;
            if (!string.IsNullOrEmpty(primitive))
            {
                PrimitiveType primitiveType;
                if (!Enum.TryParse(primitive, true, out primitiveType))
                    return ToolResult.Failure("无法识别的基础几何体类型: " + primitive);
                go = GameObject.CreatePrimitive(primitiveType);
            }
            else
            {
                go = new GameObject();
            }
            go.name = name;
            Undo.RegisterCreatedObjectUndo(go, UndoPrefix + "创建 " + name);

            if (!string.IsNullOrEmpty(parent))
            {
                var parentObject = GameObjectLookup.Find(parent);
                if (parentObject == null)
                {
                    Undo.DestroyObjectImmediate(go);
                    return ToolResult.Failure("找不到父对象: " + parent + "（先用 get_scene_hierarchy 确认路径）");
                }
                Undo.SetTransformParent(go.transform, parentObject.transform, UndoPrefix + "设置父级");
            }

            var position = ToolArgs.Vec3(args, "position", null);
            var scale = ToolArgs.Vec3(args, "scale", null);
            if (position != null)
                go.transform.localPosition = new Vector3(position[0], position[1], position[2]);
            if (scale != null)
                go.transform.localScale = new Vector3(scale[0], scale[1], scale[2]);

            var added = new List<string>();
            var failed = new List<string>();
            foreach (var componentName in ToolArgs.Strings(args, "components"))
            {
                var type = ResolveType(componentName);
                if (type == null || !typeof(Component).IsAssignableFrom(type))
                {
                    failed.Add(componentName + "(未找到类型)");
                    continue;
                }
                var component = Undo.AddComponent(go, type);
                added.Add(component == null ? componentName : component.GetType().Name);
            }

            EditorUtility.SetDirty(go);
            EndUndoGroup();
            var sb = new StringBuilder();
            sb.Append("已创建 ").AppendLine(GameObjectLookup.PathOf(go.transform));
            sb.Append("组件: ").AppendLine(string.Join(", ", added.ToArray()));
            if (failed.Count > 0)
                sb.Append("未能添加的组件: ").AppendLine(string.Join(", ", failed.ToArray()));
            return ToolResult.Success(sb.ToString());
        }

        private static ToolResult AddComponent(Dictionary<string, object> args)
        {
            string target = ToolArgs.S(args, "target");
            string componentName = ToolArgs.S(args, "component");
            var go = GameObjectLookup.Find(target);
            if (go == null)
                return ToolResult.Failure("找不到对象: " + target);
            var type = ResolveType(componentName);
            if (type == null || !typeof(Component).IsAssignableFrom(type))
                return ToolResult.Failure("找不到组件类型: " + componentName + "（确认相关包已安装，或使用完整类型名）");
            BeginUndoGroup("添加组件 " + componentName);
            var component = Undo.AddComponent(go, type);
            EndUndoGroup();
            return ToolResult.Success("已给 " + GameObjectLookup.PathOf(go.transform) + " 添加 " +
                (component == null ? componentName : component.GetType().FullName));
        }

        private static ToolResult SetTransform(Dictionary<string, object> args)
        {
            string target = ToolArgs.S(args, "target");
            var go = GameObjectLookup.Find(target);
            if (go == null)
                return ToolResult.Failure("找不到对象: " + target);
            BeginUndoGroup("修改 Transform");
            Undo.RecordObject(go.transform, UndoPrefix + "修改 Transform");
            var position = ToolArgs.Vec3(args, "position", null);
            var rotation = ToolArgs.Vec3(args, "rotation", null);
            var scale = ToolArgs.Vec3(args, "scale", null);
            if (position != null)
                go.transform.localPosition = new Vector3(position[0], position[1], position[2]);
            if (rotation != null)
                go.transform.localEulerAngles = new Vector3(rotation[0], rotation[1], rotation[2]);
            if (scale != null)
                go.transform.localScale = new Vector3(scale[0], scale[1], scale[2]);
            EditorUtility.SetDirty(go.transform);
            EndUndoGroup();
            return ToolResult.Success("已更新 " + GameObjectLookup.PathOf(go.transform) +
                " pos=" + go.transform.localPosition + " rot=" + go.transform.localEulerAngles +
                " scale=" + go.transform.localScale);
        }

        private static ToolResult DeleteGameObject(Dictionary<string, object> args)
        {
            string target = ToolArgs.S(args, "target");
            var go = GameObjectLookup.Find(target);
            if (go == null)
                return ToolResult.Failure("找不到对象: " + target);
            string path = GameObjectLookup.PathOf(go.transform);
            BeginUndoGroup("删除 " + path);
            Undo.DestroyObjectImmediate(go);
            EndUndoGroup();
            return ToolResult.Success("已删除 " + path + "（Ctrl+Z 可撤销）");
        }

        private static ToolResult CreateScript(Dictionary<string, object> args)
        {
            string assetPath = ToolArgs.S(args, "path");
            string content = ToolArgs.S(args, "content");
            string absolute;
            string error;
            if (!UnityReadTools.ValidateAssetPath(assetPath, ".cs", out absolute, out error))
                return ToolResult.Failure(error);
            if (string.IsNullOrEmpty(content))
                return ToolResult.Failure("content 为空，未写入任何文件。");
            try
            {
                string directory = Path.GetDirectoryName(absolute);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);
                bool existed = File.Exists(absolute);
                string previous = existed ? File.ReadAllText(absolute) : "";
                UndoLog.RecordScript(assetPath, previous, existed);
                File.WriteAllText(absolute, content, new UTF8Encoding(false));
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                AssetDatabase.Refresh();
                return ToolResult.Success("脚本已写入 " + assetPath + "（" + content.Length + " 字符）。" +
                    "Unity 将编译该脚本并可能发生域重载；完成后请调用 get_compile_errors 检查并修正错误。", true);
            }
            catch (Exception e)
            {
                return ToolResult.Failure("写入脚本失败: " + e.Message);
            }
        }

        private static ToolResult CreateMaterial(Dictionary<string, object> args)
        {
            string assetPath = ToolArgs.S(args, "path");
            string absolute;
            string error;
            if (!UnityReadTools.ValidateAssetPath(assetPath, ".mat", out absolute, out error))
                return ToolResult.Failure(error);
            string shaderName = ToolArgs.S(args, "shader");
            Shader shader = null;
            if (!string.IsNullOrEmpty(shaderName))
                shader = Shader.Find(shaderName);
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            if (shader == null)
                return ToolResult.Failure("找不到可用的 Shader，请显式指定 shader 参数。");
            try
            {
                string directory = Path.GetDirectoryName(absolute);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);
                if (File.Exists(absolute))
                    return ToolResult.Failure("材质已存在，避免覆盖: " + assetPath);
                var material = new Material(shader);
                var color = ToolArgs.Vec4(args, "color", null);
                if (color != null)
                    material.color = new Color(color[0], color[1], color[2], color[3]);
                if (material.HasProperty("_BaseColor"))
                    material.SetColor("_BaseColor", material.color);
                AssetDatabase.CreateAsset(material, assetPath);
                AssetDatabase.SaveAssets();
                return ToolResult.Success("已创建材质 " + assetPath + "，Shader=" + shader.name +
                    "，颜色=" + material.color);
            }
            catch (Exception e)
            {
                return ToolResult.Failure("创建材质失败: " + e.Message);
            }
        }

        private static ToolResult SaveScene(Dictionary<string, object> args)
        {
            bool ok = EditorSceneManager.SaveOpenScenes();
            return ok ? ToolResult.Success("已保存所有打开的场景。") : ToolResult.Failure("保存场景失败或被取消。");
        }

        private static ToolResult RefreshAssets(Dictionary<string, object> args)
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            return ToolResult.Success("已触发资源刷新与脚本编译。请稍后用 get_compile_errors 检查结果。", true);
        }

        internal static Type ResolveType(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            Type cached;
            if (TypeCache.TryGetValue(name, out cached))
                return cached;

            string[] candidates =
            {
                name,
                "UnityEngine." + name + ", UnityEngine.CoreModule",
                "UnityEngine." + name + ", UnityEngine.PhysicsModule",
                "UnityEngine." + name + ", UnityEngine.AudioModule",
                "UnityEngine." + name + ", UnityEngine.UI",
                "UnityEngine.UI." + name + ", UnityEngine.UI",
                "UnityEditor." + name + ", UnityEditor.CoreModule",
                "UnityEngine.InputSystem." + name + ", Unity.InputSystem",
                "Unity.Cinemachine." + name + ", Unity.Cinemachine",
                "TMPro." + name + ", Unity.TextMeshPro"
            };
            foreach (var candidate in candidates)
            {
                var found = Type.GetType(candidate, false, true);
                if (found != null && typeof(Component).IsAssignableFrom(found))
                {
                    TypeCache[name] = found;
                    return found;
                }
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }
                catch (Exception) { continue; }
                if (types == null)
                    continue;
                foreach (var type in types)
                {
                    if (type == null || !typeof(Component).IsAssignableFrom(type))
                        continue;
                    if (string.Equals(type.Name, name, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(type.FullName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        TypeCache[name] = type;
                        return type;
                    }
                }
            }
            return null;
        }
    }
}
