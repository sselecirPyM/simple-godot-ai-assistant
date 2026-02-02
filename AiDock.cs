#if TOOLS
using Godot;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace GodotAiAssistant
{
    [Tool]
    public partial class AiDock : EditorDock
    {
        private VBoxContainer _mainLayout;
        private RichTextLabel _chatDisplay;
        private TextEdit _inputBox;
        private Button _sendBtn;
        private Button _settingsBtn;
        private PopupPanel _settingsPopup;

        // Settings UI
        private LineEdit _urlEdit, _keyEdit, _modelEdit;

        private AppConfig _config;
        private readonly System.Net.Http.HttpClient _httpClient = new System.Net.Http.HttpClient();
        private List<object> _chatHistory = new List<object>();

        public override void _Ready()
        {
            _config = ConfigManager.LoadConfig();
            SetupUi();
        }

        private void SetupUi()
        {
            // Layout
            _mainLayout = new VBoxContainer { LayoutMode = 1, AnchorsPreset = (int)LayoutPreset.FullRect };
            AddChild(_mainLayout);

            // Toolbar
            var toolBar = new HBoxContainer();
            _settingsBtn = new Button { Text = "Settings" };
            _settingsBtn.Pressed += ShowSettings;
            toolBar.AddChild(_settingsBtn);

            var clearBtn = new Button { Text = "Clear Chat" };
            clearBtn.Pressed += () => { _chatHistory.Clear(); _chatDisplay.Text = ""; };
            toolBar.AddChild(clearBtn);
            _mainLayout.AddChild(toolBar);

            // Chat Display
            _chatDisplay = new RichTextLabel
            {
                SizeFlagsVertical = SizeFlags.ExpandFill,
                FocusMode = FocusModeEnum.Click,
                SelectionEnabled = true,
                BbcodeEnabled = true,
                ScrollFollowing = true
            };
            _mainLayout.AddChild(_chatDisplay);

            // Input Area
            var inputContainer = new HBoxContainer { CustomMinimumSize = new Vector2(0, 100) };
            _inputBox = new TextEdit { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _sendBtn = new Button { Text = "Send" };
            _sendBtn.Pressed += OnSendPressed;

            inputContainer.AddChild(_inputBox);
            inputContainer.AddChild(_sendBtn);
            _mainLayout.AddChild(inputContainer);

            CreateSettingsPopup();
            AppendSystemMessage("AI Assistant Ready. Configure settings to start.");
        }

        private void CreateSettingsPopup()
        {
            _settingsPopup = new PopupPanel { Size = new Vector2I(400, 250) };
            var vbox = new VBoxContainer();

            vbox.AddChild(new Label { Text = "Endpoint URL:" });
            _urlEdit = new LineEdit { Text = _config.Endpoint };
            vbox.AddChild(_urlEdit);

            vbox.AddChild(new Label { Text = "API Key:" });
            _keyEdit = new LineEdit { Text = _config.ApiKey, Secret = true };
            vbox.AddChild(_keyEdit);

            vbox.AddChild(new Label { Text = "Model Name:" });
            _modelEdit = new LineEdit { Text = _config.Model };
            vbox.AddChild(_modelEdit);

            var saveBtn = new Button { Text = "Save" };
            saveBtn.Pressed += SaveSettings;
            vbox.AddChild(saveBtn);

            _settingsPopup.AddChild(vbox);
            AddChild(_settingsPopup);
        }

        private void ShowSettings()
        {
            _urlEdit.Text = _config.Endpoint;
            _keyEdit.Text = _config.ApiKey;
            _modelEdit.Text = _config.Model;
            _settingsPopup.PopupCentered();
        }

        private void SaveSettings()
        {
            _config.Endpoint = _urlEdit.Text;
            _config.ApiKey = _keyEdit.Text;
            _config.Model = _modelEdit.Text;
            ConfigManager.SaveConfig(_config);
            _settingsPopup.Hide();
            AppendSystemMessage("Settings saved.");
        }

        private async void OnSendPressed()
        {
            string text = _inputBox.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;

            _inputBox.Text = "";
            AppendMessage("User", text);

            _chatHistory.Add(new { role = "user", content = text });
            _sendBtn.Disabled = true;

            await ProcessChatLoop();

            _sendBtn.Disabled = false;
        }

        // 处理多轮对话和函数调用的核心循环
        private async Task ProcessChatLoop()
        {
            int safetyLoop = 0;
            bool keepGoing = true;

            while (keepGoing && safetyLoop < 5) // 防止死循环
            {
                safetyLoop++;
                try
                {
                    var responseJson = await SendToApi();

                    using var doc = JsonDocument.Parse(responseJson);
                    var root = doc.RootElement;

                    // 错误处理
                    if (root.TryGetProperty("error", out var error))
                    {
                        AppendSystemMessage($"API Error: {error.GetProperty("message")}");
                        return;
                    }

                    var choice = root.GetProperty("choices")[0];
                    var message = choice.GetProperty("message");
                    var finishReason = choice.GetProperty("finish_reason").GetString();

                    // 1. 处理普通回复
                    string content = message.TryGetProperty("content", out var c) && c.ValueKind != JsonValueKind.Null ? c.GetString() : null;

                    // 将助手的回复加入历史
                    var assistantMsg = new Dictionary<string, object> { ["role"] = "assistant" };
                    if (content != null) assistantMsg["content"] = content;

                    // 2. 处理工具调用
                    if (message.TryGetProperty("tool_calls", out var toolCalls))
                    {
                        var toolCallsList = new List<object>();
                        foreach (var tc in toolCalls.EnumerateArray())
                        {
                            toolCallsList.Add(JsonSerializer.Deserialize<object>(tc.GetRawText()));
                        }
                        assistantMsg["tool_calls"] = toolCallsList;
                        _chatHistory.Add(assistantMsg); // 添加含有 tool_calls 的消息到历史

                        if (content != null) AppendMessage("AI", content);
                        AppendSystemMessage("Processing tools...");

                        // 执行工具
                        foreach (var tc in toolCalls.EnumerateArray())
                        {
                            string id = tc.GetProperty("id").GetString();
                            var func = tc.GetProperty("function");
                            string funcName = func.GetProperty("name").GetString();
                            string argsJson = func.GetProperty("arguments").GetString();

                            string result = ExecuteTool(funcName, argsJson);

                            // 将工具结果加入历史
                            _chatHistory.Add(new
                            {
                                role = "tool",
                                tool_call_id = id,
                                content = result
                            });
                        }
                        // 循环继续，将工具结果发回给 LLM
                    }
                    else
                    {
                        // 没有工具调用，对话结束
                        if (content != null) AppendMessage("AI", content);
                        _chatHistory.Add(assistantMsg);
                        keepGoing = false;
                    }
                }
                catch (Exception ex)
                {
                    AppendSystemMessage($"Exception: {ex.Message}");
                    keepGoing = false;
                }
            }
        }

        private string ExecuteTool(string name, string jsonArgs)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonArgs);
                var root = doc.RootElement;

                switch (name)
                {
                    case "list_directory":
                        string p1 = root.GetProperty("path").GetString();
                        return AiTools.ListDirectory(p1);
                    case "read_file":
                        string p2 = root.GetProperty("path").GetString();
                        return AiTools.ReadFile(p2);
                    case "search_files":
                        string p3 = root.GetProperty("keyword").GetString();
                        return AiTools.SearchFiles(p3);
                    default:
                        return "Error: Unknown tool.";
                }
            }
            catch (Exception ex)
            {
                return $"Error executing tool {name}: {ex.Message}";
            }
        }

        private async Task<string> SendToApi()
        {
            var requestBody = new
            {
                model = _config.Model,
                messages = _chatHistory,
                tools = AiTools.GetToolDefinitions() // 注入工具
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.ApiKey);

            var response = await _httpClient.PostAsync(_config.Endpoint, content);
            return await response.Content.ReadAsStringAsync();
        }

        private void AppendMessage(string sender, string text)
        {
            string color = sender == "User" ? "#88c0d0" : "#a3be8c";
            _chatDisplay.AppendText($"[b][color={color}]{sender}:[/color][/b]\n{text}\n\n");
        }

        private void AppendSystemMessage(string text)
        {
            _chatDisplay.AppendText($"[i][color=#d08770]{text}[/color][/i]\n\n");
        }
    }
}
#endif