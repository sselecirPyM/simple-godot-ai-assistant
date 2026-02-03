#if TOOLS
using System;
using System.IO;
using System.Text.Json;

namespace GodotAiAssistant
{
    public class AppConfig
    {
        public string Endpoint { get; set; } = "https://api.openai.com/v1/chat/completions";
        public string ApiKey { get; set; } = "";
        public string Model { get; set; } = "gpt-4o";
    }

    public static class ConfigManager
    {
        private static string GetConfigDir()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string path = Path.Combine(appData, "Godot", "plugins", "GodotAiAssistant");
            Directory.CreateDirectory(path);
            return path;
        }

        private static string GetConfigPath() => Path.Combine(GetConfigDir(), "config.json");

        public static void SaveConfig(AppConfig config)
        {
            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(GetConfigPath(), json);
        }

        public static AppConfig LoadConfig()
        {
            string path = GetConfigPath();
            if (!File.Exists(path)) return new AppConfig();
            try
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
            catch
            {
                return new AppConfig();
            }
        }
    }
}
#endif