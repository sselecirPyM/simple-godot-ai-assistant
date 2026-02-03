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
        private PopupPanel _settingsPopup;

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

            _stopBtn = new Button { Text = "Stop", Visible = false };
            _stopBtn.Pressed += OnStopPressed;

            inputContainer.AddChild(_inputBox);
            inputContainer.AddChild(_sendBtn);
            inputContainer.AddChild(_stopBtn);
            _mainLayout.AddChild(inputContainer);

            CreateSettingsPopup();
            AppendSystemMessage("AI Assistant Ready. Configure settings to start.");
        }

        private void OnStopPressed()
        {
            _cancellationTokenSource?.Cancel();
            AppendSystemMessage("Attempting to stop generation...");
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
            if (string.IsNullOrEmpty(text) || !_sendBtn.Disabled == false) return;

            _inputBox.Text = "";
            AppendMessage("User", text);

            _chatHistory.Add(new { role = "user", content = text });

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

        // ==========================================
        // KEY MODIFICATIONS START HERE
        // ==========================================
        private async Task ProcessChatLoop(CancellationToken cancellationToken)
        {
            int safetyLoop = 0;
            bool keepGoing = true;

            while (keepGoing && safetyLoop < 10) // Increased safety loop slightly
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

                    var choice = root.GetProperty("choices")[0];
                    var message = choice.GetProperty("message");

                    // 1. Handle Content
                    string content = message.TryGetProperty("content", out var c) && c.ValueKind != JsonValueKind.Null ? c.GetString() : null;

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

                        // REMOVED: AppendSystemMessage("Processing tools...");
                        // ADDED: Detailed logging inside loop

                        // Execute Tools
                        foreach (var tc in toolCalls.EnumerateArray())
                        {
                            string id = tc.GetProperty("id").GetString();
                            var func = tc.GetProperty("function");
                            string funcName = func.GetProperty("name").GetString();
                            string argsJson = func.GetProperty("arguments").GetString();

                            // ------------------------------------------
                            // [MODIFIED] Display Tool Info
                            // ------------------------------------------
                            AppendSystemMessage($"🛠 [b]Calling Tool:[/b] [color=#88C0D0]{funcName}[/color]\nArguments: [color=#D8DEE9]{argsJson}[/color]");

                            // Yield briefly to let UI update so user sees the tool call happening immediately
                            await Task.Delay(10);

                            string result = ExecuteTool(funcName, argsJson);

                            // ------------------------------------------
                            // [MODIFIED] Display Result Summary (Optional)
                            // ------------------------------------------
                            // Truncate result if it's too long to keep chat clean
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
        // ==========================================
        // KEY MODIFICATIONS END HERE
        // ==========================================

        private string ExecuteTool(string name, string jsonArgs)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonArgs);
                var root = doc.RootElement;

                // Simple helper to safely get string property
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
                max_tokens = 8192
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
            // Modified color to be a bit more subtle for system logs
            _chatDisplay.AppendText($"[font_size=12][i][color=#d08770]{text}[/color][/i][/font_size]\n");
        }
    }
}
#endif