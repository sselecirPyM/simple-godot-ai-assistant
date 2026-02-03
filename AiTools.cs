using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace GodotAiAssistant
{
    public static class AiTools
    {
        private static readonly HashSet<string> ImageExtensions = new HashSet<string>
        {
            "png", "jpg", "jpeg", "webp", "svg", "bmp", "tga"
        };

        public static object[] GetToolDefinitions()
        {
            return new object[]
            {
                new {
                    type = "function",
                    function = new {
                        name = "list_directory",
                        description = "List files and folders in a specific directory within the project (res://).",
                        parameters = new {
                            type = "object",
                            properties = new {
                                path = new { type = "string", description = "The path to list (e.g., 'res://scripts/')" }
                            },
                            required = new[] { "path" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "read_file",
                        description = "Read the content of a specific file.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                path = new { type = "string", description = "The full path of the file to read (e.g., 'res://main.cs')" }
                            },
                            required = new[] { "path" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "search_files",
                        description = "Search for files containing a specific name keyword.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                keyword = new { type = "string", description = "The partial filename to search for." }
                            },
                            required = new[] { "keyword" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "get_scene_tree",
                        description = "Get the tree structure of the currently open scene. Returns Name, Type, and Instance ID.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                node_id = new { type = "string", description = "The Instance ID of the node to inspect. Pass '0' to get the entire current scene root." }
                            },
                            required = new[] { "node_id" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "get_node_properties",
                        description = "Get the properties of a specific node by its Instance ID.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                node_id = new { type = "string", description = "The Instance ID of the node." }
                            },
                            required = new[] { "node_id" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "get_selected_nodes",
                        description = "Get the list of nodes currently selected in the Godot Editor. Returns Name, NodePath, Class, and ID.",
                        parameters = new { type = "object", properties = new { } } // No parameters needed
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "get_node_properties_by_path",
                        description = "Get the properties of a node by its scene path (relative to the edited scene root).",
                        parameters = new {
                            type = "object",
                            properties = new {
                                path = new { type = "string", description = "The node path (e.g. 'Player/Camera3D' or '.' for root)." }
                            },
                            required = new[] { "path" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "get_node_property_value",
                        description = "Get a specific property value or sub-resource from a node using a path. " +
                                      "Useful for accessing nested resources like 'mesh/material/albedo_color' or 'surface_material_override/0'.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                node_path = new { type = "string", description = "Path to the scene node (e.g. 'Player/MeshInstance')." },
                                property_path = new { type = "string", description = "Path to the property or sub-resource (e.g. 'mesh:material:albedo_color' or 'surface_material_override/0'). Slashes are automatically converted to colons where appropriate." }
                            },
                            required = new[] { "node_path", "property_path" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "create_file",
                        description = "Create or overwrite a text file (e.g., .gd, .cs, .tscn, .txt) at a specific path. Please check the directory before using this tool to ensure that files are not accidentally overwritten.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                path = new { type = "string", description = "The full path (e.g., 'res://scripts/my_script.gd')." },
                                content = new { type = "string", description = "The text content to write into the file." }
                            },
                            required = new[] { "path", "content" }
                        }
                    }
                },
                new {
                    type = "function",
                    function = new {
                        name = "run_gdscript",
                        description = "Execute a temporary GDScript snippet immediately and return the result. " +
                                      "The script MUST contain a 'func run():' method which returns a value (String, Dictionary, or basic type). " +
                                      "Use this to perform complex calculations, inspect deep scene state, or perform batch editor operations. The \"tool\" keyword was removed in Godot 4. Use the \"@tool\" annotation instead.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                code = new { type = "string", description = "The full GDScript code. It must extend a class (default RefCounted) and implement 'func run()'." }
                            },
                            required = new[] { "code" }
                        }
                    }
                }
            };
        }

        public static string ListDirectory(string path)
        {
            using var dir = DirAccess.Open(path);
            if (dir == null) return $"Error: Could not open directory {path}. {DirAccess.GetOpenError()}";

            dir.ListDirBegin();
            string fileName = dir.GetNext();
            var files = new List<string>();

            while (fileName != "")
            {
                if (dir.CurrentIsDir()) files.Add($"[DIR] {fileName}");
                else files.Add($"[FILE] {fileName}");
                fileName = dir.GetNext();
            }
            return string.Join("\n", files);
        }

        public static string ReadFile(string path)
        {
            if (!FileAccess.FileExists(path)) return "Error: File not found.";

            using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            if (file == null) return "Error: Could not open file.";

            ulong length = file.GetLength();
            string extension = path.GetExtension().ToLower();
            bool isImage = ImageExtensions.Contains(extension);

            if (!isImage && length > 10240)
                return $"Error: File is too large ({length} bytes). Text file limit is 10KB.";

            if (isImage)
            {
                byte[] buffer = file.GetBuffer((long)length);
                string base64 = Convert.ToBase64String(buffer);
                string mimeType = extension == "svg" ? "svg+xml" : (extension == "jpg" ? "jpeg" : extension);
                return $"data:image/{mimeType};base64,{base64}";
            }
            return file.GetAsText();
        }

        public static string SearchFiles(string keyword) => SearchRecursive("res://", keyword);

        private static string SearchRecursive(string dirPath, string keyword)
        {
            using var dir = DirAccess.Open(dirPath);
            if (dir == null) return "";
            dir.ListDirBegin();
            string fileName = dir.GetNext();
            var results = new List<string>();
            while (fileName != "")
            {
                if (fileName == "." || fileName == "..") { fileName = dir.GetNext(); continue; }
                string fullPath = dirPath.PathJoin(fileName);
                if (dir.CurrentIsDir()) results.Add(SearchRecursive(fullPath, keyword));
                else if (fileName.Contains(keyword, StringComparison.OrdinalIgnoreCase)) results.Add(fullPath);
                fileName = dir.GetNext();
            }
            return string.Join("\n", results).Trim();
        }

        private static Node GetNodeFromId(string idStr)
        {
            var root = GetEditedRoot();
            if (root == null) return null;

            if (idStr == "0") return root;

            if (ulong.TryParse(idStr, out ulong instanceId))
            {
                var obj = GodotObject.InstanceFromId(instanceId);
                return obj as Node;
            }
            return null;
        }


        public static string GetSceneTree(string nodeIdStr)
        {
            var node = GetNodeFromId(nodeIdStr);
            if (node == null) return "Error: Could not find node or no scene is open.";

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            BuildTree(node, sb, 0);
            return sb.ToString();
        }

        private static void BuildTree(Node node, System.Text.StringBuilder sb, int depth)
        {
            string indent = new string(' ', depth * 2);
            string line = $"{indent}- {node.Name} ({node.GetType().Name}) [ID: {node.GetInstanceId()}]";

            if (!string.IsNullOrEmpty(node.SceneFilePath))
                line += $" [Scene: {node.SceneFilePath}]";

            sb.AppendLine(line);

            //foreach (var child in node.GetChildren())
            //{
            //    BuildTreeRecursive(child, sb, depth + 1);
            //}
        }

        private static Node GetEditedRoot()
        {
            var editorInterface = EditorInterface.Singleton;
            return editorInterface?.GetEditedSceneRoot();
        }
        public static string GetSelectedNodes()
        {
            var editorInterface = EditorInterface.Singleton;
            if (editorInterface == null) return "Error: EditorInterface not found.";

            var selection = editorInterface.GetSelection();
            var nodes = selection.GetSelectedNodes();

            if (nodes.Count == 0) return "No nodes currently selected.";

            var resultList = new List<object>();

            foreach (Node node in nodes)
            {
                var root = editorInterface.GetEditedSceneRoot();
                string path = root != null ? root.GetPathTo(node) : node.GetPath();

                resultList.Add(new
                {
                    Name = node.Name,
                    Class = node.GetType().Name,
                    Path = path,
                    InstanceId = node.GetInstanceId().ToString(),
                    SceneFile = node.SceneFilePath
                });
            }

            return JsonSerializer.Serialize(resultList, new JsonSerializerOptions { WriteIndented = true });
        }

        public static string GetNodePropertiesByPath(string path)
        {
            var root = GetEditedRoot();
            if (root == null) return "Error: No scene currently open.";

            Node targetNode;

            if (string.IsNullOrEmpty(path) || path == ".")
            {
                targetNode = root;
            }
            else
            {
                targetNode = root.GetNodeOrNull(path);
            }

            if (targetNode == null) return $"Error: Could not find node at path '{path}'.";

            return SerializeGodotObject(targetNode);
        }
        public static string GetNodeProperties(string nodeIdStr)
        {
            var node = GetNodeFromId(nodeIdStr);
            if (node == null) return "Error: Could not find node with that ID.";

            return SerializeGodotObject(node);
        }

        public static string GetNodePropertyValue(string nodePath, string propertyPath)
        {
            var root = GetEditedRoot();
            if (root == null) return "Error: No scene currently open.";

            Node targetNode = (string.IsNullOrEmpty(nodePath) || nodePath == ".") ? root : root.GetNodeOrNull(nodePath);
            if (targetNode == null) return $"Error: Could not find node at path '{nodePath}'.";

            try
            {
                Variant val = targetNode.GetIndexed(propertyPath);

                if (val.VariantType == Variant.Type.Nil && propertyPath.Contains("/"))
                {
                    string altPath = propertyPath.Replace("/", ":");
                    val = targetNode.GetIndexed(altPath);
                }

                if (val.VariantType == Variant.Type.Nil)
                {
                    string topProp = propertyPath.Split(new[] { '/', ':' })[0];
                    if (targetNode.Get(topProp).VariantType == Variant.Type.Nil)
                        return $"Error: Property path '{propertyPath}' returned Nil (or path is invalid).";
                }

                if (val.Obj is GodotObject obj)
                {
                    return SerializeGodotObject(obj);
                }
                else
                {
                    return val.ToString();
                }
            }
            catch (Exception ex)
            {
                return $"Error accessing property '{propertyPath}': {ex.Message}";
            }
        }

        private static string SerializeGodotObject(GodotObject obj)
        {
            if (obj == null) return "null";

            var propDict = new Dictionary<string, object>();

            propDict["_Info_"] = new
            {
                Class = obj.GetType().Name,
                InstanceId = obj.GetInstanceId().ToString(),
                ToString = obj.ToString()
            };

            // 如果是 Node，添加额外信息
            if (obj is Node node)
            {
                propDict["_NodeInfo_"] = new
                {
                    Name = node.Name,
                    Path = node.GetPath().ToString()
                };

                // 变换简写
                if (node is Node2D n2d)
                {
                    propDict["GlobalPosition"] = n2d.GlobalPosition.ToString();
                    propDict["Position"] = n2d.Position.ToString();
                    propDict["RotationDegrees"] = n2d.RotationDegrees;
                }
                else if (node is Node3D n3d)
                {
                    propDict["GlobalPosition"] = n3d.GlobalPosition.ToString();
                    propDict["Position"] = n3d.Position.ToString();
                    propDict["RotationDegrees"] = n3d.RotationDegrees.ToString();
                }
            }

            var properties = obj.GetPropertyList();

            foreach (var prop in properties)
            {
                string name = prop["name"].AsString();
                int usage = prop["usage"].AsInt32();

                // 过滤规则：脚本变量、存储变量、编辑器变量
                bool isScriptVar = (usage & (int)PropertyUsageFlags.ScriptVariable) != 0;
                bool isStorage = (usage & (int)PropertyUsageFlags.Storage) != 0;
                bool isEditor = (usage & (int)PropertyUsageFlags.Editor) != 0;

                if (!isScriptVar && !isStorage && !isEditor) continue;
                if (name.StartsWith("metadata/") || name.Contains("script/source")) continue;
                if (name.EndsWith(".cs")) continue; // 忽略 C# 脚本自身引用

                try
                {
                    Variant val = obj.Get(name);

                    // 处理对象引用，避免循环递归，只返回 ID 和类型
                    if (val.VariantType == Variant.Type.Object && val.Obj != null)
                    {
                        var childObj = val.Obj;
                        string resPath = (childObj is Resource r) ? r.ResourcePath : "";
                        if (val.Obj is not GodotObject godotObject)
                        {
                            godotObject = null;
                        }

                        propDict[name] = $"<Object: {childObj.GetType().Name} (ID: {godotObject?.GetInstanceId()}) {resPath}>";
                    }
                    else
                    {
                        string valStr = val.ToString();
                        if (valStr.Length > 200) valStr = valStr.Substring(0, 200) + "...(truncated)";
                        propDict[name] = valStr;
                    }
                }
                catch (Exception)
                {
                    propDict[name] = "<Error reading property>";
                }
            }

            return JsonSerializer.Serialize(propDict, new JsonSerializerOptions { WriteIndented = true });
        }

        public static string CreateFile(string path, string content)
        {
            try
            {
                // 1. Ensure the directory exists
                string dir = path.GetBaseDir();
                using var dirAccess = DirAccess.Open("res://");

                if (dirAccess == null)
                    return "Error: Cannot access res:// directory.";

                if (!dirAccess.DirExists(dir))
                {
                    Error err = dirAccess.MakeDirRecursive(dir);
                    if (err != Error.Ok) return $"Error creating directory '{dir}': {err}";
                }

                // 2. Write the file
                using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
                if (file == null)
                {
                    return $"Error: Could not open file '{path}' for writing. Error code: {FileAccess.GetOpenError()}";
                }

                file.StoreString(content);
                file.Flush(); // Ensure write

                // 3. Trigger Editor Refresh
                CallDeferredRefresh();

                return $"Success: File created/overwritten at '{path}'.";
            }
            catch (Exception ex)
            {
                return $"Exception creating file: {ex.Message}";
            }
        }

        private static void CallDeferredRefresh()
        {
            // We run this on the main thread to ensure the EditorFileSystem updates safely
            var editorInterface = EditorInterface.Singleton;
            if (editorInterface != null)
            {
                editorInterface.GetResourceFilesystem().Scan();
            }
        }

        public static string RunGdScript(string code)
        {
            try
            {
                using var script = new GDScript();
                script.SourceCode = code;

                Error err = script.Reload();
                if (err != Error.Ok)
                {
                    return $"GDScript Syntax Error: {err}. Please check your code.";
                }

                GodotObject instance;
                try
                {
                    if (script.CanInstantiate())
                    {
                        instance = (GodotObject)script.New();
                    }
                    else
                    {
                        return "Error: Script cannot be instantiated. Ensure it extends a valid class (e.g., RefCounted, Node) or implicitly defaults to RefCounted.";
                    }
                }
                catch (Exception ex)
                {
                    return $"Instantiation Error: {ex.Message}";
                }

                if (!instance.HasMethod("run"))
                {
                    return "Error: The provided GDScript does not contain a 'func run():' method.";
                }

                Variant result = instance.Call("run");

                if (instance is Node node && !node.IsInsideTree())
                {
                    node.QueueFree();
                }

                if (result.Obj is GodotObject resObj)
                {
                    return $"[Object: {resObj.GetType().Name} ID:{resObj.GetInstanceId()}]";
                }

                return result.ToString();
            }
            catch (Exception ex)
            {
                return $"Runtime Error executing GDScript: {ex.Message}";
            }
        }
    }
}