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
                        description = "Get the properties (variables, transforms, exports) of a specific node in JSON format.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                node_id = new { type = "string", description = "The Instance ID of the node." }
                            },
                            required = new[] { "node_id" }
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
            var editorInterface = EditorInterface.Singleton;
            if (editorInterface == null) return null;

            var editedSceneRoot = editorInterface.GetEditedSceneRoot();
            if (editedSceneRoot == null) return null;

            if (idStr == "0") return editedSceneRoot;

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

        public static string GetNodeProperties(string nodeIdStr)
        {
            var node = GetNodeFromId(nodeIdStr);
            if (node == null) return "Error: Could not find node.";

            var propDict = new Dictionary<string, object>();
            
            propDict["Name"] = node.Name;
            propDict["Class"] = node.GetType().Name;
            propDict["GlobalPosition"] = (node is Node2D n2d) ? n2d.GlobalPosition.ToString() : 
                                         (node is Node3D n3d) ? n3d.GlobalPosition.ToString() : "N/A";

            var properties = node.GetPropertyList();

            foreach (var prop in properties)
            {
                string name = prop["name"].AsString();
                int usage = prop["usage"].AsInt32();

                bool isScriptVar = (usage & (int)PropertyUsageFlags.ScriptVariable) != 0;
                bool isStorage = (usage & (int)PropertyUsageFlags.Storage) != 0;

                if (!isScriptVar && !isStorage) continue;
                if (name.StartsWith("metadata/")) continue; 

                Variant val = node.Get(name);
                
                // Convert Variant to a readable string for JSON
                propDict[name] = val.Obj?.ToString() ?? val.ToString();
            }

            return JsonSerializer.Serialize(propDict, new JsonSerializerOptions { WriteIndented = true });
        }
    }
}