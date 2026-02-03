#if TOOLS
using Godot;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
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
        private Button _stopBtn;
        private Button _settingsBtn;

        private PanelContainer _settingsPanel;

        // 用于显示 Token 计数的 Label
        private Label _contextLabel;

        // Settings UI
        private LineEdit _urlEdit, _keyEdit, _modelEdit;

        private AppConfig _config;
        private readonly System.Net.Http.HttpClient _httpClient = new System.Net.Http.HttpClient();
        private List<object> _chatHistory = new List<object>();

        private CancellationTokenSource _cancellationTokenSource;

        public override void _Ready()
        {
            _config = ConfigManager.LoadConfig();
            SetupUi();
        }

        private void SetupUi()
        {
            // Clean up children if reloading
            foreach (Node child in GetChildren()) child.QueueFree();

            // Layout
            _mainLayout = new VBoxContainer { LayoutMode = 1, AnchorsPreset = (int)LayoutPreset.FullRect };
            AddChild(_mainLayout);

            // --- Toolbar ---
            var toolBar = new HBoxContainer();
            _settingsBtn = new Button { Text = "Settings", ToggleMode = true };
            _settingsBtn.Toggled += OnSettingsToggled;
            toolBar.AddChild(_settingsBtn);

            var clearBtn = new Button { Text = "Clear Chat" };
            clearBtn.Pressed += () =>
            {
                _chatHistory.Clear();
                _chatDisplay.Text = "";
                if (_contextLabel != null) _contextLabel.Text = "Tokens: 0";
            };
            toolBar.AddChild(clearBtn);

            // 占位符
            var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            toolBar.AddChild(spacer);

            // 上下文长度显示
            _contextLabel = new Label
            {
                Text = "Tokens: 0",
                VerticalAlignment = VerticalAlignment.Center,
                Modulate = new Color(0.7f, 0.7f, 0.7f)
            };
            toolBar.AddChild(_contextLabel);

            _mainLayout.AddChild(toolBar);

            // --- Settings Area ---
            CreateSettingsPanel();
            _mainLayout.AddChild(_settingsPanel);

            // --- Chat Display ---
            _chatDisplay = new RichTextLabel
            {
                SizeFlagsVertical = SizeFlags.ExpandFill,
                FocusMode = FocusModeEnum.Click,
                SelectionEnabled = true,
                BbcodeEnabled = true,
                ScrollFollowing = true
            };
            _mainLayout.AddChild(_chatDisplay);

            // --- Input Area ---
            var inputContainer = new HBoxContainer { CustomMinimumSize = new Vector2(0, 100) };
            _inputBox = new TextEdit { SizeFlagsHorizontal = SizeFlags.ExpandFill };

            _sendBtn = new Button { Text = "Send" };
            _sendBtn.Pressed += OnSendPressed;

            _stopBtn = new Button { Text = "Stop", Visible = false };
            _stopBtn.Pressed += OnStopPressed;

            inputContainer.AddChild(_inputBox);
            inputContainer.AddChild(_sendBtn);
            inputContainer.AddChild(_stopBtn);
            _mainLayout.AddChild(inputContainer);

            AppendSystemMessage("AI Assistant Ready. Configure settings to start.");
        }
        private void CreateSettingsPanel()
        {
            // 1. 最外层面板
            _settingsPanel = new PanelContainer
            {
                Visible = false,
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };

            // 2. 边距容器
            var marginContainer = new MarginContainer();
            marginContainer.AddThemeConstantOverride("margin_top", 10);
            marginContainer.AddThemeConstantOverride("margin_bottom", 10);
            marginContainer.AddThemeConstantOverride("margin_left", 10);
            marginContainer.AddThemeConstantOverride("margin_right", 10);
            _settingsPanel.AddChild(marginContainer);

            // 3. 垂直布局容器
            var contentLayout = new VBoxContainer();
            marginContainer.AddChild(contentLayout);

            // 4. 输入框区域
            var grid = new GridContainer { Columns = 2 };
            grid.SizeFlagsHorizontal = SizeFlags.ExpandFill;

            // Endpoint
            grid.AddChild(new Label { Text = "Endpoint URL:" });
            _urlEdit = new LineEdit { Text = _config.Endpoint, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            grid.AddChild(_urlEdit);

            // API Key
            grid.AddChild(new Label { Text = "API Key:" });
            _keyEdit = new LineEdit { Text = _config.ApiKey, Secret = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            grid.AddChild(_keyEdit);

            // Model
            grid.AddChild(new Label { Text = "Model Name:" });
            _modelEdit = new LineEdit { Text = _config.Model, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            grid.AddChild(_modelEdit);

            contentLayout.AddChild(grid);

            contentLayout.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });

            var saveBtn = new Button { Text = "Save Settings & Close", CustomMinimumSize = new Vector2(0, 30) };
            saveBtn.Pressed += SaveSettings;

            contentLayout.AddChild(saveBtn);
        }

        private void OnSettingsToggled(bool toggledOn)
        {
            _settingsPanel.Visible = toggledOn;

            if (toggledOn)
            {
                // 打开时刷新显示的数据
                _urlEdit.Text = _config.Endpoint;
                _keyEdit.Text = _config.ApiKey;
                _modelEdit.Text = _config.Model;
            }
        }

        private void UpdateTokenDisplay(int promptTokens, int completionTokens, int totalTokens)
        {
            if (_contextLabel == null) return;

            // 显示格式：Total (Input + Output)
            _contextLabel.Text = $"Tokens: {totalTokens} (In: {promptTokens} / Out: {completionTokens})";

            // 简单的颜色警示 (根据 Total Tokens)
            if (totalTokens > 12000) _contextLabel.Modulate = Colors.Red;
            else if (totalTokens > 8000) _contextLabel.Modulate = Colors.Yellow;
            else _contextLabel.Modulate = new Color(0.7f, 0.7f, 0.7f);
        }

        private void SaveSettings()
        {
            _config.Endpoint = _urlEdit.Text;
            _config.ApiKey = _keyEdit.Text;
            _config.Model = _modelEdit.Text;
            ConfigManager.SaveConfig(_config);

            AppendSystemMessage("Settings saved.");

            // 保存后关闭设置面板，并弹起 Settings 按钮
            _settingsPanel.Visible = false;
            _settingsBtn.ButtonPressed = false;
        }

        private void OnStopPressed()
        {
            _cancellationTokenSource?.Cancel();
            AppendSystemMessage("Attempting to stop generation...");
        }

        private async void OnSendPressed()
        {
            string text = _inputBox.Text.Trim();
            if (string.IsNullOrEmpty(text) || !_sendBtn.Disabled == false) return;

            _inputBox.Text = "";
            AppendMessage("User", text);

            _chatHistory.Add(new { role = "user", content = text });
            // 注意：这里不再调用 UpdateContextCount，因为我们等待 API 返回准确值

            _sendBtn.Disabled = true;
            _stopBtn.Visible = true;
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                await ProcessChatLoop(_cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                AppendSystemMessage("Generation stopped by user.");
            }
            catch (Exception ex)
            {
                AppendSystemMessage($"An unexpected error occurred: {ex.Message}");
            }
            finally
            {
                _sendBtn.Disabled = false;
                _stopBtn.Visible = false;
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private async Task ProcessChatLoop(CancellationToken cancellationToken)
        {
            int safetyLoop = 0;
            bool keepGoing = true;

            while (keepGoing && safetyLoop < 15)
            {
                safetyLoop++;
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var responseJson = await SendToApi(cancellationToken);

                    using var doc = JsonDocument.Parse(responseJson);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("error", out var error))
                    {
                        AppendSystemMessage($"API Error: {error.GetProperty("message")}");
                        return;
                    }

                    if (root.TryGetProperty("usage", out var usage))
                    {
                        int pTokens = usage.TryGetProperty("prompt_tokens", out var p) ? p.GetInt32() : 0;
                        int cTokens = usage.TryGetProperty("completion_tokens", out var c) ? c.GetInt32() : 0;
                        int tTokens = usage.TryGetProperty("total_tokens", out var t) ? t.GetInt32() : 0;

                        // 更新 UI 显示
                        UpdateTokenDisplay(pTokens, cTokens, tTokens);
                    }

                    var choice = root.GetProperty("choices")[0];
                    var message = choice.GetProperty("message");

                    // 1. Handle Content
                    string content = message.TryGetProperty("content", out var cVal) && cVal.ValueKind != JsonValueKind.Null ? cVal.GetString() : null;

                    var assistantMsg = new Dictionary<string, object> { ["role"] = "assistant" };
                    if (content != null) assistantMsg["content"] = content;

                    // 2. Handle Tool Calls
                    if (message.TryGetProperty("tool_calls", out var toolCalls))
                    {
                        var toolCallsList = new List<object>();
                        foreach (var tc in toolCalls.EnumerateArray())
                        {
                            toolCallsList.Add(JsonSerializer.Deserialize<object>(tc.GetRawText()));
                        }
                        assistantMsg["tool_calls"] = toolCallsList;
                        _chatHistory.Add(assistantMsg);

                        if (content != null) AppendMessage("AI", content);

                        // Execute Tools
                        foreach (var tc in toolCalls.EnumerateArray())
                        {
                            string id = tc.GetProperty("id").GetString();
                            var func = tc.GetProperty("function");
                            string funcName = func.GetProperty("name").GetString();
                            string argsJson = func.GetProperty("arguments").GetString();

                            AppendSystemMessage($"🛠 [b]Calling Tool:[/b] [color=#88C0D0]{funcName}[/color]\nArguments: [color=#D8DEE9]{argsJson}[/color]");

                            await Task.Delay(10); // UI Refresh

                            string result = ExecuteTool(funcName, argsJson);

                            string resultPreview = result.Length > 150 ? result.Substring(0, 150) + "..." : result;
                            AppendSystemMessage($"✅ [b]Result:[/b] [color=#A3BE8C]{resultPreview}[/color]");

                            _chatHistory.Add(new
                            {
                                role = "tool",
                                tool_call_id = id,
                                content = result
                            });
                        }
                        // 工具执行完毕，循环继续，将发送新的请求，届时会获得最新的 Token 计数
                    }
                    else
                    {
                        // No tool calls, just text
                        if (content != null) AppendMessage("AI", content);
                        _chatHistory.Add(assistantMsg);
                        keepGoing = false;
                    }
                }
                catch (Exception ex)
                {
                    if (ex is OperationCanceledException) throw;

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

                string GetArg(string key) => root.TryGetProperty(key, out var p) ? p.ToString() : "";

                switch (name)
                {
                    case "list_directory":
                        return AiTools.ListDirectory(GetArg("path"));
                    case "read_file":
                        return AiTools.ReadFile(GetArg("path"));
                    case "search_files":
                        return AiTools.SearchFiles(GetArg("keyword"));
                    case "get_scene_tree":
                        return AiTools.GetSceneTree(GetArg("node_id"));
                    case "get_node_properties":
                        return AiTools.GetNodeProperties(GetArg("node_id"));
                    case "get_selected_nodes":
                        return AiTools.GetSelectedNodes();
                    case "get_node_properties_by_path":
                        return AiTools.GetNodePropertiesByPath(GetArg("path"));
                    case "get_node_property_value":
                        return AiTools.GetNodePropertyValue(GetArg("node_path"), GetArg("property_path"));
                    case "create_file":
                        return AiTools.CreateFile(GetArg("path"), GetArg("content"));
                    case "run_gdscript":
                        return AiTools.RunGdScript(GetArg("code"));
                    default:
                        return "Error: Unknown tool.";
                }
            }
            catch (Exception ex)
            {
                return $"Error executing tool {name}: {ex.Message}";
            }
        }

        private async Task<string> SendToApi(CancellationToken cancellationToken)
        {
            var requestBody = new
            {
                model = _config.Model,
                messages = _chatHistory,
                tools = AiTools.GetToolDefinitions(),
                max_tokens = 16384
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.ApiKey);

            var response = await _httpClient.PostAsync(_config.Endpoint, content, cancellationToken);
            return await response.Content.ReadAsStringAsync();
        }

        private void AppendMessage(string sender, string text)
        {
            string color = sender == "User" ? "#88c0d0" : "#a3be8c";
            _chatDisplay.AppendText($"[b][color={color}]{sender}:[/color][/b]\n{text}\n\n");
        }

        private void AppendSystemMessage(string text)
        {
            _chatDisplay.AppendText($"[font_size=12][i][color=#d08770]{text}[/color][/i][/font_size]\n");
        }
    }
}
#endif