using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace StealthAssistWPF.Services
{
    /// <summary>
    /// The user's own settings: which AI to use, their own API keys, and their own speech key.
    /// Saved only on this computer, encrypted with Windows DPAPI (readable only by the same Windows user).
    /// Nothing here is ever sent anywhere except to the provider the user picked.
    /// </summary>
    public class AiSettings
    {
        public string Provider { get; set; } = AiProviders.Auto;
        public string ApiKey { get; set; } = "";
        public string Model { get; set; } = "";
        /// <summary>Only for "custom" (any OpenAI-compatible server) and "ollama".</summary>
        public string BaseUrl { get; set; } = "";

        // Speech to text. Empty Azure key = free, offline Windows speech recognition.
        // With a key: the user's own Azure Speech resource (more accurate; Azure has a free tier).
        public string AzureSpeechKey { get; set; } = "";
        public string AzureSpeechRegion { get; set; } = "eastus";

        public bool ShowDebugMessages { get; set; } = false;

        private static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YokAI");
        private static readonly string FilePath = Path.Combine(Folder, "settings.dat");
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("YokAI-settings-v1");

        public static AiSettings Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new AiSettings();
                byte[] data = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser);
                return JsonConvert.DeserializeObject<AiSettings>(Encoding.UTF8.GetString(data)) ?? new AiSettings();
            }
            catch
            {
                return new AiSettings();
            }
        }

        public void Save()
        {
            Directory.CreateDirectory(Folder);
            byte[] data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(this));
            File.WriteAllBytes(FilePath, ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser));
        }

        public static void Delete()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
        }

        public bool UseAzureSpeech => !string.IsNullOrWhiteSpace(AzureSpeechKey) && !string.IsNullOrWhiteSpace(AzureSpeechRegion);
    }

    public static class AiProviders
    {
        public const string Auto = "auto";             // Claude Code if installed, otherwise needs a key
        public const string ClaudeCode = "claude-code"; // the user's own signed-in Claude Code, no key
        public const string Anthropic = "anthropic";
        public const string OpenAI = "openai";
        public const string Groq = "groq";
        public const string OpenRouter = "openrouter";
        public const string Ollama = "ollama";           // free, runs locally
        public const string Custom = "custom";           // any OpenAI-compatible server

        public static readonly string[] All = { Auto, ClaudeCode, Anthropic, OpenAI, Groq, OpenRouter, Ollama, Custom };

        public static string DisplayName(string p)
        {
            switch (p)
            {
                case Auto: return "Automatic (Claude Code if installed)";
                case ClaudeCode: return "Claude Code (your own, no key)";
                case Anthropic: return "Claude API (Anthropic key)";
                case OpenAI: return "OpenAI";
                case Groq: return "Groq";
                case OpenRouter: return "OpenRouter";
                case Ollama: return "Ollama (local, free)";
                case Custom: return "Custom (OpenAI-compatible)";
                default: return p;
            }
        }

        public static string DefaultModel(string p)
        {
            switch (p)
            {
                case Anthropic: return "claude-opus-5-5";
                case OpenAI: return "gpt-4o-mini";
                case Groq: return "llama-3.3-70b-versatile";
                case OpenRouter: return "anthropic/claude-sonnet-4";
                case Ollama: return "llama3.2";
                default: return "";
            }
        }

        public static bool NeedsKey(string p) =>
            p == Anthropic || p == OpenAI || p == Groq || p == OpenRouter || p == Custom;
    }
}
