using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StealthAssistWPF.Services
{
    /// <summary>
    /// Sends a prompt (and optional PNG screenshots, base64) to the AI the user chose.
    /// Supports the user's own Claude Code install (no key), the Claude API, and any
    /// OpenAI-compatible API (OpenAI, Groq, OpenRouter, Ollama, custom).
    /// </summary>
    public class AiClient
    {
        public const string SystemPrompt =
            "You are Yok-AI, a fast assistant that helps the user during calls, meetings and while working on their screen. " +
            "Answer directly and briefly. Lead with the answer, then at most a few short points. " +
            "When you are given screenshots, read what is on them and answer the question they show. " +
            "For code, give working code in a fenced code block. Never mention that you are an AI model unless asked.";

        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

        private readonly AiSettings settings;

        public AiClient(AiSettings settings)
        {
            this.settings = settings;
        }

        /// <summary>The provider that will really be used (resolves "auto").</summary>
        public string EffectiveProvider
        {
            get
            {
                if (settings.Provider != AiProviders.Auto) return settings.Provider;
                if (FindClaudeCode() != null) return AiProviders.ClaudeCode;
                if (!string.IsNullOrWhiteSpace(settings.ApiKey))
                    return settings.ApiKey.StartsWith("sk-ant-") ? AiProviders.Anthropic : AiProviders.OpenAI;
                return null;
            }
        }

        public string ReadyProblem()
        {
            string p = EffectiveProvider;
            if (p == null) return "No AI is set up. Install and sign in to Claude Code, or open Settings and add an API key.";
            if (p == AiProviders.ClaudeCode && FindClaudeCode() == null)
                return "Claude Code was not found. Install it (https://claude.com/claude-code) and run 'claude' once to sign in.";
            if (AiProviders.NeedsKey(p) && string.IsNullOrWhiteSpace(settings.ApiKey))
                return "Add your API key in Settings.";
            if (p == AiProviders.Custom && string.IsNullOrWhiteSpace(settings.BaseUrl))
                return "Add the server address (Base URL) in Settings.";
            return null;
        }

        public async Task<string> AskAsync(string message, List<string> imagesBase64 = null)
        {
            string problem = ReadyProblem();
            if (problem != null) throw new InvalidOperationException(problem);

            string provider = EffectiveProvider;
            switch (provider)
            {
                case AiProviders.ClaudeCode: return await AskClaudeCodeAsync(message, imagesBase64);
                case AiProviders.Anthropic: return await AskAnthropicAsync(message, imagesBase64);
                default: return await AskOpenAiCompatibleAsync(provider, message, imagesBase64);
            }
        }

        private string ModelOr(string provider)
        {
            return string.IsNullOrWhiteSpace(settings.Model) ? AiProviders.DefaultModel(provider) : settings.Model.Trim();
        }

        #region Claude Code (the user's own install and sign-in)

        public static string FindClaudeCode()
        {
            var candidates = new List<string>();
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            candidates.Add(Path.Combine(home, ".local", "bin", "claude.exe"));      // native installer
            candidates.Add(Path.Combine(appData, "npm", "claude.cmd"));              // npm -g
            candidates.Add(Path.Combine(localAppData, "Programs", "claude", "claude.exe"));

            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                foreach (string name in new[] { "claude.exe", "claude.cmd" })
                {
                    try { candidates.Add(Path.Combine(dir.Trim('"'), name)); } catch { }
                }
            }
            return candidates.FirstOrDefault(File.Exists);
        }

        private async Task<string> AskClaudeCodeAsync(string message, List<string> imagesBase64)
        {
            string exe = FindClaudeCode();
            string workDir = Path.Combine(Path.GetTempPath(), "YokAI", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workDir);

            try
            {
                var prompt = new StringBuilder();
                prompt.AppendLine(SystemPrompt);
                prompt.AppendLine();

                if (imagesBase64 != null && imagesBase64.Count > 0)
                {
                    prompt.AppendLine("Look at these screenshots first (read each file):");
                    for (int i = 0; i < imagesBase64.Count; i++)
                    {
                        string file = Path.Combine(workDir, $"screenshot_{i + 1}.png");
                        File.WriteAllBytes(file, Convert.FromBase64String(StripDataPrefix(imagesBase64[i])));
                        prompt.AppendLine(file);
                    }
                    prompt.AppendLine();
                }
                prompt.AppendLine(message);

                string args = "-p --output-format text --allowedTools Read";
                if (!string.IsNullOrWhiteSpace(settings.Model))
                    args += " --model " + settings.Model.Trim();

                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    WorkingDirectory = workDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (var proc = new Process { StartInfo = psi })
                {
                    proc.Start();
                    byte[] input = new UTF8Encoding(false).GetBytes(prompt.ToString());
                    await proc.StandardInput.BaseStream.WriteAsync(input, 0, input.Length);
                    proc.StandardInput.Close();

                    Task<string> outTask = proc.StandardOutput.ReadToEndAsync();
                    Task<string> errTask = proc.StandardError.ReadToEndAsync();

                    Task finished = await Task.WhenAny(Task.Run(() => proc.WaitForExit()), Task.Delay(TimeSpan.FromMinutes(3)));
                    if (!proc.HasExited)
                    {
                        try { proc.Kill(); } catch { }
                        throw new TimeoutException("Claude Code took too long to answer.");
                    }

                    string output = (await outTask).Trim();
                    string error = (await errTask).Trim();
                    if (proc.ExitCode != 0 || string.IsNullOrEmpty(output))
                    {
                        string detail = string.IsNullOrEmpty(error) ? output : error;
                        throw new Exception("Claude Code failed" + (string.IsNullOrEmpty(detail) ? "." : ": " + detail) +
                                            " If you have not signed in yet, open a terminal and run 'claude' once.");
                    }
                    return output;
                }
            }
            finally
            {
                try { Directory.Delete(workDir, true); } catch { }
            }
        }

        #endregion

        #region Claude API (Messages API, the user's own key)

        private async Task<string> AskAnthropicAsync(string message, List<string> imagesBase64)
        {
            string model = ModelOr(AiProviders.Anthropic);
            var content = new JArray();
            if (imagesBase64 != null)
            {
                foreach (string img in imagesBase64)
                {
                    content.Add(new JObject
                    {
                        ["type"] = "image",
                        ["source"] = new JObject
                        {
                            ["type"] = "base64",
                            ["media_type"] = "image/png",
                            ["data"] = StripDataPrefix(img)
                        }
                    });
                }
            }
            content.Add(new JObject { ["type"] = "text", ["text"] = message });

            var body = new JObject
            {
                ["model"] = model,
                ["max_tokens"] = 16000,
                ["system"] = SystemPrompt,
                ["messages"] = new JArray { new JObject { ["role"] = "user", ["content"] = content } }
            };

            // Current Claude models: keep answers quick with low effort, and let the server
            // retry on another model if a safety check declines the request.
            bool currentGen = model.StartsWith("claude-opus-5") || model.StartsWith("claude-sonnet-5-5") ||
                              model.StartsWith("claude-fable-5") || model.StartsWith("claude-haiku-5-5");
            bool fallbackModel = model == "claude-opus-5-5" || model == "claude-opus-5" || model == "claude-sonnet-5-5" ||
                                 model == "claude-fable-5-2" || model == "claude-fable-5-1";
            if (currentGen) body["output_config"] = new JObject { ["effort"] = "low" };
            if (fallbackModel) body["fallbacks"] = "default";

            using (var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages"))
            {
                req.Headers.Add("x-api-key", settings.ApiKey.Trim());
                req.Headers.Add("anthropic-version", "2023-06-01");
                if (fallbackModel) req.Headers.Add("anthropic-beta", "server-side-fallback-2026-07-01");
                req.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");

                using (var resp = await Http.SendAsync(req))
                {
                    string json = await resp.Content.ReadAsStringAsync();
                    if (!resp.IsSuccessStatusCode)
                        throw new Exception($"Claude API error {(int)resp.StatusCode}: {ErrorText(json)}");

                    JObject obj = JObject.Parse(json);
                    if ((string)obj["stop_reason"] == "refusal")
                        return "Claude declined to answer this request.";

                    var text = new StringBuilder();
                    foreach (var block in obj["content"] ?? new JArray())
                        if ((string)block["type"] == "text") text.Append((string)block["text"]);
                    return text.ToString().Trim();
                }
            }
        }

        #endregion

        #region OpenAI-compatible (OpenAI, Groq, OpenRouter, Ollama, custom)

        private string BaseUrlFor(string provider)
        {
            switch (provider)
            {
                case AiProviders.OpenAI: return "https://api.openai.com/v1";
                case AiProviders.Groq: return "https://api.groq.com/openai/v1";
                case AiProviders.OpenRouter: return "https://openrouter.ai/api/v1";
                case AiProviders.Ollama:
                    return string.IsNullOrWhiteSpace(settings.BaseUrl) ? "http://localhost:11434/v1" : settings.BaseUrl.Trim().TrimEnd('/');
                default: return settings.BaseUrl.Trim().TrimEnd('/');
            }
        }

        private async Task<string> AskOpenAiCompatibleAsync(string provider, string message, List<string> imagesBase64)
        {
            JToken userContent;
            if (imagesBase64 != null && imagesBase64.Count > 0)
            {
                var parts = new JArray { new JObject { ["type"] = "text", ["text"] = message } };
                foreach (string img in imagesBase64)
                {
                    parts.Add(new JObject
                    {
                        ["type"] = "image_url",
                        ["image_url"] = new JObject { ["url"] = "data:image/png;base64," + StripDataPrefix(img) }
                    });
                }
                userContent = parts;
            }
            else
            {
                userContent = message;
            }

            var body = new JObject
            {
                ["model"] = ModelOr(provider),
                ["messages"] = new JArray
                {
                    new JObject { ["role"] = "system", ["content"] = SystemPrompt },
                    new JObject { ["role"] = "user", ["content"] = userContent }
                }
            };

            using (var req = new HttpRequestMessage(HttpMethod.Post, BaseUrlFor(provider) + "/chat/completions"))
            {
                if (!string.IsNullOrWhiteSpace(settings.ApiKey))
                    req.Headers.Add("Authorization", "Bearer " + settings.ApiKey.Trim());
                req.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");

                using (var resp = await Http.SendAsync(req))
                {
                    string json = await resp.Content.ReadAsStringAsync();
                    if (!resp.IsSuccessStatusCode)
                        throw new Exception($"{AiProviders.DisplayName(provider)} error {(int)resp.StatusCode}: {ErrorText(json)}");

                    JObject obj = JObject.Parse(json);
                    return ((string)obj["choices"]?[0]?["message"]?["content"] ?? "").Trim();
                }
            }
        }

        #endregion

        private static string StripDataPrefix(string b64)
        {
            int comma = b64.IndexOf("base64,", StringComparison.Ordinal);
            return comma >= 0 ? b64.Substring(comma + 7) : b64;
        }

        private static string ErrorText(string json)
        {
            try
            {
                JObject o = JObject.Parse(json);
                return (string)o["error"]?["message"] ?? (string)o["error"] ?? json;
            }
            catch { return json; }
        }
    }
}
