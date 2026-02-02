using Godot;
using System;
using System.Collections.Generic;

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

            // 限制：如果是非图片文件，且超过 10KB (10240 bytes)
            if (!isImage && length > 10240)
            {
                return $"Error: File is too large ({length} bytes). Text file limit is 10KB to preserve token usage. Please read specific parts or use a smaller file.";
            }

            if (isImage)
            {
                byte[] buffer = file.GetBuffer((long)length);
                string base64 = Convert.ToBase64String(buffer);

                // 处理 jpeg 的 mime type
                string mimeType = extension == "svg" ? "svg+xml" : extension;
                if (mimeType == "jpg") mimeType = "jpeg";

                return $"data:image/{mimeType};base64,{base64}";
            }
            else
            {
                // 文本处理
                return file.GetAsText();
            }
        }

        public static string SearchFiles(string keyword)
        {
            return SearchRecursive("res://", keyword);
        }

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
                if (dir.CurrentIsDir())
                {
                    results.Add(SearchRecursive(fullPath, keyword));
                }
                else
                {
                    if (fileName.Contains(keyword, System.StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(fullPath);
                    }
                }
                fileName = dir.GetNext();
            }

            // 过滤空行并连接
            var final = string.Join("\n", results).Trim();
            return string.IsNullOrEmpty(final) ? "" : final;
        }
    }
}