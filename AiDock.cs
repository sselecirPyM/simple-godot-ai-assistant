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
        private Label _contextLabel;

        private Label _imageStatusLabel;
        private Button _clearImageBtn;
        private HBoxContainer _imageStatusContainer;

        // Settings UI
        private LineEdit _urlEdit, _keyEdit, _modelEdit;

        private AppConfig _config;
        private readonly System.Net.Http.HttpClient _httpClient = new System.Net.Http.HttpClient();
        private List<object> _chatHistory = new List<object>();

        private CancellationTokenSource _cancellationTokenSource;

        private string _pendingImageBase64 = null;

        public override void _Ready()
        {
            _config = ConfigManager.LoadConfig();
            SetupUi();
        }

        private void SetupUi()
        {
            foreach (Node child in GetChildren()) child.QueueFree();

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
                ClearPendingImage();
                if (_contextLabel != null) _contextLabel.Text = "Tokens: 0";
            };
            toolBar.AddChild(clearBtn);

            var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            toolBar.AddChild(spacer);

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

            _imageStatusContainer = new HBoxContainer { Visible = false };
            _imageStatusLabel = new Label
            {
                Text = "🖼 Image attached from clipboard",
                Modulate = Colors.LightGreen,
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            _clearImageBtn = new Button { Text = "x", Flat = true };
            _clearImageBtn.Pressed += ClearPendingImage;

            _imageStatusContainer.AddChild(_imageStatusLabel);
            _imageStatusContainer.AddChild(_clearImageBtn);
            _mainLayout.AddChild(_imageStatusContainer);

            // --- Input Area ---
            var inputContainer = new HBoxContainer { CustomMinimumSize = new Vector2(0, 100) };
            _inputBox = new TextEdit
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                AutowrapMode = TextServer.AutowrapMode.Word,
                WrapMode = TextEdit.LineWrappingMode.Boundary
            };
            _inputBox.GuiInput += OnInputBoxGuiInput;

            _sendBtn = new Button { Text = "Send" };
            _sendBtn.Pressed += OnSendPressed;

            _stopBtn = new Button { Text = "Stop", Visible = false };
            _stopBtn.Pressed += OnStopPressed;

            inputContainer.AddChild(_inputBox);
            inputContainer.AddChild(_sendBtn);
            inputContainer.AddChild(_stopBtn);
            _mainLayout.AddChild(inputContainer);

            AppendSystemMessage("AI Assistant Ready. Configure settings to start. Paste images directly.");
        }

        private void OnInputBoxGuiInput(InputEvent @event)
        {
            if (@event is InputEventKey keyEvent && keyEvent.Pressed)
            {
                // 检测 Ctrl+V (macOS 检测 Meta+V)
                if (keyEvent.Keycode == Key.V && (keyEvent.CtrlPressed || keyEvent.MetaPressed))
                {
                    if (DisplayServer.ClipboardHasImage())
                    {
                        var img = DisplayServer.ClipboardGetImage();
                        if (img != null)
                        {
                            // 将图片转换为 PNG 字节，再转 Base64
                            byte[] pngBuffer = img.SavePngToBuffer();
                            _pendingImageBase64 = Convert.ToBase64String(pngBuffer);

                            // 更新 UI 状态
                            _imageStatusContainer.Visible = true;
                            AppendSystemMessage("Captured image from clipboard.");

                            // 标记事件已处理，防止 TextEdit 尝试粘贴非文本数据（虽然 Godot 通常会忽略）
                            GetViewport().SetInputAsHandled();
                        }
                    }
                }
            }
        }

        private void ClearPendingImage()
        {
            _pendingImageBase64 = null;
            _imageStatusContainer.Visible = false;
        }

        private void CreateSettingsPanel()
        {
            _settingsPanel = new PanelContainer
            {
                Visible = false,
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };

            var marginContainer = new MarginContainer();
            marginContainer.AddThemeConstantOverride("margin_top", 10);
            marginContainer.AddThemeConstantOverride("margin_bottom", 10);
            marginContainer.AddThemeConstantOverride("margin_left", 10);
            marginContainer.AddThemeConstantOverride("margin_right", 10);
            _settingsPanel.AddChild(marginContainer);

            var contentLayout = new VBoxContainer();
            marginContainer.AddChild(contentLayout);

            var grid = new GridContainer { Columns = 2 };
            grid.SizeFlagsHorizontal = SizeFlags.ExpandFill;

            grid.AddChild(new Label { Text = "Endpoint URL:" });
            _urlEdit = new LineEdit { Text = _config.Endpoint, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            grid.AddChild(_urlEdit);

            grid.AddChild(new Label { Text = "API Key:" });
            _keyEdit = new LineEdit { Text = _config.ApiKey, Secret = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            grid.AddChild(_keyEdit);

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
                _urlEdit.Text = _config.Endpoint;
                _keyEdit.Text = _config.ApiKey;
                _modelEdit.Text = _config.Model;
            }
        }

        private void UpdateTokenDisplay(int promptTokens, int completionTokens, int totalTokens)
        {
            if (_contextLabel == null) return;
            _contextLabel.Text = $"Tokens: {totalTokens} (In: {promptTokens} / Out: {completionTokens})";
            if (totalTokens > 16000) _contextLabel.Modulate = Colors.Red;
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

            // [修改] 只有当文本为空 且 图片也为空时才返回
            if ((string.IsNullOrEmpty(text) && string.IsNullOrEmpty(_pendingImageBase64)) || !_sendBtn.Disabled == false) return;

            _inputBox.Text = "";

            // [修改] UI 显示反馈：如果有图片，提示 [Image]
            string displayMsg = text;
            if (!string.IsNullOrEmpty(_pendingImageBase64))
            {
                displayMsg += "\n[i][color=#8FBCBB](Attached Image)[/color][/i]";
            }
            AppendMessage("User", displayMsg);

            // --- [修改] 构建消息体 ---
            object messageContent;

            if (!string.IsNullOrEmpty(_pendingImageBase64))
            {
                // 如果有图片，按照 OpenAI Vision API 格式构建 content 数组
                var contentList = new List<object>();

                // 1. 添加文字 (如果存在)
                if (!string.IsNullOrEmpty(text))
                {
                    contentList.Add(new { type = "text", text = text });
                }

                // 2. 添加图片
                contentList.Add(new
                {
                    type = "image_url",
                    image_url = new { url = $"data:image/png;base64,{_pendingImageBase64}" }
                });

                messageContent = contentList;

                // 发送后清除图片缓存
                ClearPendingImage();
            }
            else
            {
                // 纯文本
                messageContent = text;
            }

            _chatHistory.Add(new { role = "user", content = messageContent });
            // ------------------------

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
                if (_cancellationTokenSource != null)
                {
                    _cancellationTokenSource.Dispose();
                    _cancellationTokenSource = null;
                }
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
                        UpdateTokenDisplay(pTokens, cTokens, tTokens);
                    }

                    var choice = root.GetProperty("choices")[0];
                    var message = choice.GetProperty("message");

                    string content = message.TryGetProperty("content", out var cVal) && cVal.ValueKind != JsonValueKind.Null ? cVal.GetString() : null;

                    string reasoning = null;
                    if (message.TryGetProperty("reasoning_content", out var rVal) && rVal.ValueKind != JsonValueKind.Null)
                    {
                        reasoning = rVal.GetString();
                    }
                    else if (message.TryGetProperty("reasoning", out var rVal2) && rVal2.ValueKind != JsonValueKind.Null)
                    {
                        reasoning = rVal2.GetString();
                    }

                    var assistantMsg = new Dictionary<string, object> { ["role"] = "assistant" };

                    if (content != null) assistantMsg["content"] = content;

                    if (!string.IsNullOrEmpty(reasoning))
                    {
                        assistantMsg["reasoning_content"] = reasoning;
                        AppendSystemMessage($"🧠 Received reasoning ({reasoning.Length} chars)");
                    }

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

                        foreach (var tc in toolCalls.EnumerateArray())
                        {
                            string id = tc.GetProperty("id").GetString();
                            var func = tc.GetProperty("function");
                            string funcName = func.GetProperty("name").GetString();
                            string argsJson = func.GetProperty("arguments").GetString();

                            AppendSystemMessage($"🛠 [b]Calling Tool:[/b] [color=#88C0D0]{funcName}[/color]\nArguments: [color=#D8DEE9]{argsJson}[/color]");

                            await Task.Delay(10);

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
                    }
                    else
                    {
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
                    case "get_object_properties":
                        return AiTools.GetObjectProperties(GetArg("object_id"));
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