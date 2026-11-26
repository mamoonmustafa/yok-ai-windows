using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using StealthAssistWPF.Services;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using NAudio.Wave;
using Newtonsoft.Json;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Diagnostics;
using Microsoft.Win32;
using System.Windows.Media.Effects;
using System.Net;
using System.Security.Authentication;
using NAudio.MediaFoundation;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using System.Threading;
using System.Text.RegularExpressions;
using System.Windows.Media;

// Add this directive at the top:
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Orientation = System.Windows.Controls.Orientation;
using MediaColor = System.Windows.Media.Color;
using MediaFontFamily = System.Windows.Media.FontFamily;
using DrawingSize = System.Drawing.Size;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingGraphics = System.Drawing.Graphics;
using System.Linq;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;

namespace StealthAssistWPF
{
    // Add this new class outside the MainWindow class (at namespace level)
    public class ChatMessage
    {
        public DateTime Timestamp { get; set; }
        public string MessageType { get; set; } // "User", "AI"
        public string Content { get; set; }
        public List<string> Images { get; set; } = new List<string>(); // NEW: Store images
        public string RequestType { get; set; } = "text"; // NEW: Track request type

        public ChatMessage(string messageType, string content)
        {
            Timestamp = DateTime.Now;
            MessageType = messageType;
            Content = content;
        }

        // NEW: Constructor for messages with images
        public ChatMessage(string messageType, string content, List<string> images, string requestType = "text")
        {
            Timestamp = DateTime.Now;
            MessageType = messageType;
            Content = content;
            Images = images ?? new List<string>();
            RequestType = requestType;
        }
    }

    public class MarkdownContent
    {
        public List<MarkdownPart> Parts { get; set; } = new List<MarkdownPart>();
    }

    public class MarkdownPart
    {
        public string Type { get; set; } // "h1", "h2", "h3", "paragraph", "code", "list", "numbered-list"
        public string Content { get; set; }
    }

    public class ReplyContext
    {
        public string MessageId { get; set; }
        public string OriginalContent { get; set; }
        public string SenderType { get; set; }
        public DateTime Timestamp { get; set; }

        public ReplyContext(string messageId, string content, string senderType)
        {
            MessageId = messageId;
            OriginalContent = content;
            SenderType = senderType;
            Timestamp = DateTime.Now;
        }
    }

    public partial class MainWindow : Window
    {

        #region Fields

        // Real-time transcription fields
        private SpeechRecognizer speechRecognizer;
        private AudioInputStream audioInputStream;
        private PushAudioInputStream pushStream;
        private bool isRealtimeTranscriptionActive = false;
        private DispatcherTimer transcriptionTimer;
        private DateTime transcriptionStartTime;
        private int transcriptionWordCount = 0;
        private StringBuilder fullTranscriptionText = new StringBuilder();

        private Queue<byte[]> audioBuffer = new Queue<byte[]>();
        private bool isSpeechDetected = false;
        private DateTime lastSpeechTime = DateTime.MinValue;
        private const int SILENCE_TIMEOUT_MS = 1500; // Send buffer after 1.5s of silence
        private const double SPEECH_THRESHOLD = 0.01; // Adjust based on testing
        private const int MAX_BUFFER_CHUNKS = 30; // Keep ~3 seconds of audio buffered

        // Add these fields in your MainWindow class with other fields
        private List<ChatMessage> chatHistory = new List<ChatMessage>();
        private bool isChatMemoryEnabled = false;

        // The user's own AI + speech settings (stored encrypted on this PC only)
        private AiSettings aiSettings = AiSettings.Load();

        private bool isFocusModeActive = false;
        private Border focusedMessageBubble = null;
        private List<UIElement> hiddenElements = new List<UIElement>();
        private Thickness originalMainBorderMargin;
        private double originalWindowWidth;
        private double originalWindowHeight;

        // Reply functionality fields
        private bool isInReplyMode = false;
        private ReplyContext currentReplyContext = null;
        private Border replyContextBar = null;
        private string replyMessageId = "";
        private Dictionary<string, string> messageContentMap = new Dictionary<string, string>();

        // Window state
        private bool isVisible = true;
        private IntPtr _windowHandle;
        private HwndSource _hwndSource;

        private string currentSelectedText = "";

        private bool isTranscriptionUIEnabled = false;

        private bool isWindows11OrHigher = false;
        // Add this field
        private int remainingCredits = 0;

        // Default Prompts
        private string defaultScreenshotPrompt = "";
        private string defaultAudioPrompt = "Analyze this meeting participant's statement for key points, concerns, questions, or decisions. Provide brief actionable insights";
        private string defaultSelectionPrompt = "Analyze this selected transcription text and provide insights";

        // Cursor handling
        private bool isHidingCursor = false;


        // Audio capture components
        private WasapiLoopbackCapture audioCapture;

        // Multiple screenshot support
        private List<string> screenshotBuffer = new List<string>();
        private bool isCapturingMultiple = false;
        private int maxScreenshots = 3;
        private string screenshotSummaryText = "";

        private bool isInitializing = true; // Add this with your other fields

        // UI Mode Tracking
        private bool isScreenshotModeActive = false;
        private bool isMinimized = false;
        private bool isShortcutsPanelVisible = false;

        // Session timer
        private DispatcherTimer sessionTimer;

        private bool isLoggedIn = false;

        // Billed time tracking fields
        private DateTime billedTimeSessionStart = DateTime.MinValue;
        private TimeSpan totalBilledTime = TimeSpan.Zero;
        private bool isBillingActive = false;
        private readonly object billingLock = new object();

        // Stealth mode fields
        private Image customCursorImage;
        private System.Collections.Generic.HashSet<Key> allowedKeys = new System.Collections.Generic.HashSet<Key>
        {
            Key.LeftCtrl, Key.RightCtrl,
            Key.LeftAlt, Key.RightAlt,
            Key.LeftShift, Key.RightShift,
            Key.Space, Key.C, Key.X, Key.V, Key.H, Key.K, Key.S
        };

        // Hybrid recording system fields
        private WaveFileWriter localRecordingWriter;
        private string currentRecordingPath;
        private CircularBuffer<byte> audioCircularBuffer;
        private const int CIRCULAR_BUFFER_SECONDS = 600; // 10 minutes
        private const int SAMPLE_RATE = 16000;
        private const int BYTES_PER_SECOND = SAMPLE_RATE * 2; // 16-bit audio
        private readonly object bufferLock = new object();

        // Enhanced VAD with context
        private Queue<AudioChunk> recentAudioChunks = new Queue<AudioChunk>();
        private const int PRE_SPEECH_CONTEXT_MS = 1000; // 1 second before speech
        private const int POST_SPEECH_CONTEXT_MS = 500;  // 0.5 seconds after speech
        private DateTime lastVoiceActivityTime = DateTime.MinValue;
        private bool isCurrentlyInSpeechSession = false;

        // Smart streaming control
        private Queue<AudioChunk> pendingStreamBuffer = new Queue<AudioChunk>();
        private bool isStreamingToAzure = false;
        private DateTime streamSessionStart = DateTime.MinValue;
        private const int MAX_SILENCE_GAP_MS = 2000; // 2 seconds max gap before ending session
        private const int MIN_SPEECH_DURATION_MS = 300; // Minimum 300ms speech to start streaming

        // Adaptive VAD with learning
        private MovingAverage noiseFloorTracker = new MovingAverage(50); // 50 sample moving average
        private MovingAverage speechLevelTracker = new MovingAverage(20); // 20 sample moving average
        private double adaptiveThreshold = 0.01;
        private const double ADAPTATION_RATE = 0.1;

        private Queue<AudioSegment> recentAudioSegments = new Queue<AudioSegment>();
        private bool isInSilencePeriod = false;
        private DateTime silenceStartTime = DateTime.MinValue;
        private StringBuilder currentOtherSpeakerBuffer = new StringBuilder();
        private const int SILENCE_THRESHOLD_MS = 2500; // 2.5 seconds indicates user is speaking
        private const double SILENCE_AUDIO_THRESHOLD = 0.005; // Very low audio level = silence
        private DateTime lastOtherSpeakerActivity = DateTime.MinValue;
        private bool isCapturingOtherSpeaker = false;
        private int consecutiveSilenceChunks = 0;
        private const int MIN_SILENCE_CHUNKS = 15; // ~1.5 seconds of consistent silence

        // Audio processing performance
        private int processedChunksCount = 0;
        private int streamedChunksCount = 0;
        private DateTime sessionStartTime = DateTime.MinValue;

        private DispatcherTimer selectionDelayTimer;
        private string lastAutoFilledText = "";
        private bool isAutoFilled = false;

        // Sentence completion with better logic
        private StringBuilder currentTranscriptBuffer = new StringBuilder();
        private List<string> recentSentences = new List<string>();
        private const int MAX_SENTENCES_FOR_ANALYSIS = 5;
        private Timer sentenceAnalysisTimer;
        private readonly object sentenceLock = new object();

        // Keyboard protection state
        private bool isEnhancedKeyProtectionActive = false;
        private IntPtr keyboardHookId = IntPtr.Zero;
        private Utilities.NativeMethods.LowLevelKeyboardProc keyboardProc;
        private System.Collections.Generic.HashSet<Key> appHotkeys = new System.Collections.Generic.HashSet<Key>
        {
            Key.Space, Key.C, Key.X, Key.V, Key.K, // Hotkey keys
        };

        // Install paths
        private static readonly string AppDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YokAI");
        // Add these to your existing native methods
        [DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        private const uint LWA_ALPHA = 0x2;
        private const uint LWA_COLORKEY = 0x1;
        #endregion

        public MainWindow()
        {
            InitializeComponent();
            //NAudio.MediaFoundation.MediaFoundationApi.Startup();
            MediaFoundationApi.Startup();

            // Set up event handlers for window initialization
            this.SourceInitialized += MainWindow_SourceInitialized;

            InitializeAntiDetection();
            Version osVersion = Environment.OSVersion.Version;
            isWindows11OrHigher = (osVersion.Major > 10 || (osVersion.Major == 10 && osVersion.Build >= 22000));

            // Set initial UI states
            ShortcutsPanel.Visibility = Visibility.Collapsed;
            NormalInputGrid.Visibility = Visibility.Visible;
            ScreenshotModeGrid.Visibility = Visibility.Collapsed;

            // Welcome message is already added in XAML as a system bubble
            // No need to add it again here

            UpdateTranscriptionToggleUI();

            // Initialize audio capture
            InitializeAudioCapture();

            InitializeTextSelectionFeature();

            isInitializing = false;
        }
        // Add these methods to your MainWindow class

        // Startup: open the app directly if an AI is ready, otherwise show Settings.
        private void CheckPreviousLogin()
        {
            if (new AiClient(aiSettings).ReadyProblem() == null)
            {
                HideLoginScreen();
            }
            else
            {
                ShowLoginScreen();
            }
        }

        // Kept for the existing call sites. The app is free: there are no credits.
        private bool CheckCredits(bool showMessage = true)
        {
            string problem = new AiClient(aiSettings).ReadyProblem();
            if (problem != null)
            {
                if (showMessage) AddMessage("Yok-AI", problem);
                return false;
            }
            return true;
        }

        private void ShowLoginScreen()
        {
            PopulateSettingsForm();
            LoginGrid.Visibility = Visibility.Visible;
            isLoggedIn = false;
        }

        private void HideLoginScreen()
        {
            LoginGrid.Visibility = Visibility.Collapsed;
            isLoggedIn = true;
            UpdateCreditsDisplay();
        }

        private bool isPopulatingSettings = false;

        private void PopulateSettingsForm()
        {
            isPopulatingSettings = true;
            ProviderComboBox.Items.Clear();
            foreach (string p in AiProviders.All)
            {
                ProviderComboBox.Items.Add(new ComboBoxItem { Content = AiProviders.DisplayName(p), Tag = p });
            }
            int index = Array.IndexOf(AiProviders.All, aiSettings.Provider);
            ProviderComboBox.SelectedIndex = index < 0 ? 0 : index;

            ApiKeyBox.Password = aiSettings.ApiKey;
            ModelTextBox.Text = aiSettings.Model;
            BaseUrlTextBox.Text = aiSettings.BaseUrl;
            AzureKeyBox.Password = aiSettings.AzureSpeechKey;
            AzureRegionTextBox.Text = aiSettings.AzureSpeechRegion;
            LoginStatusText.Visibility = Visibility.Collapsed;
            isPopulatingSettings = false;
            UpdateProviderHint();
        }

        private string SelectedProvider()
        {
            return (ProviderComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? AiProviders.Auto;
        }

        private void ProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!isPopulatingSettings) UpdateProviderHint();
        }

        private void UpdateProviderHint()
        {
            if (ProviderHintText == null) return;
            string p = SelectedProvider();
            bool needsKey = AiProviders.NeedsKey(p) || p == AiProviders.Auto;
            bool needsUrl = p == AiProviders.Custom || p == AiProviders.Ollama;

            ApiKeyLabel.Visibility = ApiKeyBox.Visibility = needsKey ? Visibility.Visible : Visibility.Collapsed;
            BaseUrlLabel.Visibility = BaseUrlTextBox.Visibility = needsUrl ? Visibility.Visible : Visibility.Collapsed;
            ApiKeyLabel.Text = p == AiProviders.Auto ? "API key (only used if Claude Code is not installed)" : "API key";

            string claude = AiClient.FindClaudeCode();
            string hint;
            switch (p)
            {
                case AiProviders.Auto:
                    hint = claude != null
                        ? "Claude Code found on this PC. Yok-AI will use your own Claude Code sign-in; no key needed."
                        : "Claude Code not found. Install it and run 'claude' once to sign in, or paste a Claude (sk-ant-...) or OpenAI key.";
                    break;
                case AiProviders.ClaudeCode:
                    hint = claude != null ? "Found: " + claude : "Claude Code not found. Install it from claude.com/claude-code and run 'claude' once to sign in.";
                    break;
                case AiProviders.Ollama:
                    hint = "Free and local. Install Ollama, run 'ollama pull llama3.2'. Leave the address empty for http://localhost:11434/v1.";
                    break;
                case AiProviders.Groq:
                    hint = "Fast and has a free tier. The default model reads text only, so screenshots need a vision model.";
                    break;
                case AiProviders.Custom:
                    hint = "Any server that speaks the OpenAI chat API, for example http://localhost:1234/v1 (LM Studio).";
                    break;
                default:
                    hint = "Default model: " + AiProviders.DefaultModel(p);
                    break;
            }
            ProviderHintText.Text = hint;
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            var updated = new AiSettings
            {
                Provider = SelectedProvider(),
                ApiKey = ApiKeyBox.Password.Trim(),
                Model = ModelTextBox.Text.Trim(),
                BaseUrl = BaseUrlTextBox.Text.Trim(),
                AzureSpeechKey = AzureKeyBox.Password.Trim(),
                AzureSpeechRegion = string.IsNullOrWhiteSpace(AzureRegionTextBox.Text) ? "eastus" : AzureRegionTextBox.Text.Trim(),
                ShowDebugMessages = aiSettings.ShowDebugMessages
            };

            string problem = new AiClient(updated).ReadyProblem();
            if (problem != null)
            {
                ShowLoginError(problem);
                return;
            }

            try
            {
                updated.Save();
            }
            catch (Exception ex)
            {
                ShowLoginError("Could not save settings: " + ex.Message);
                return;
            }

            aiSettings = updated;
            HideLoginScreen();
            AddMessage("Yok-AI", "Settings saved. Using " + AiProviders.DisplayName(new AiClient(aiSettings).EffectiveProvider) + ".");
        }

        // Settings "x": back to the chat when an AI is already set up; on a first run with nothing set up, quit.
        private void CloseSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (new AiClient(aiSettings).ReadyProblem() == null) HideLoginScreen();
            else Close();
        }

        private void CreditsLabel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            ShowLoginScreen();
        }

        private void ShowLoginError(string message)
        {
            Dispatcher.Invoke(() => {
                LoginStatusText.Text = message;
                LoginStatusText.Foreground = new SolidColorBrush(Colors.IndianRed);
                LoginStatusText.Visibility = Visibility.Visible;
            });
        }

        private void MainWindow_SourceInitialized(object sender, EventArgs e)
        {
            try
            {
                // Get the window handle as early as possible
                _windowHandle = new WindowInteropHelper(this).Handle;
                _hwndSource = HwndSource.FromHwnd(_windowHandle);
                _hwndSource?.AddHook(new HwndSourceHook(WndProc));

                // Apply window stealth styles first
                int exStyle = Utilities.NativeMethods.GetWindowLong(_windowHandle, Utilities.NativeMethods.GWL_EXSTYLE);
                exStyle |= Utilities.NativeMethods.WS_EX_LAYERED | Utilities.NativeMethods.WS_EX_TOOLWINDOW;
                Utilities.NativeMethods.SetWindowLong(_windowHandle, Utilities.NativeMethods.GWL_EXSTYLE, exStyle);

                // Apply transparency using SetLayeredWindowAttributes instead of WPF's AllowsTransparency
                // Alpha = 245 gives slight transparency (adjust as needed)
                SetLayeredWindowAttributes(_windowHandle, 0, 245, LWA_ALPHA);

                // Now apply the display affinity to hide from screen capture
                Utilities.NativeMethods.SetWindowDisplayAffinity(_windowHandle, Utilities.NativeMethods.WDA_EXCLUDEFROMCAPTURE);

                // Apply other stealth techniques
                InitializeAntiDetection();
            }
            catch
            {
                // Silent fail - we'll try again in the Window_Loaded event
            }
        }

        #region Window Events

        #region Anti-Detection

        // Call this method from your constructor after InitializeComponent()
        private void InitializeAntiDetection()
        {
            // Apply all anti-detection techniques
            ImplementProcessProtection();
            ApplyWindowCloaking();
        }

        private void ImplementProcessProtection()
        {
            try
            {
                // Get current process
                Process currentProcess = Process.GetCurrentProcess();

                // Change priority to make it less visible in task manager sorts
                currentProcess.PriorityClass = ProcessPriorityClass.BelowNormal;

                // You could potentially rename the process in memory
                // This is complex and usually requires native code injection
            }
            catch
            {
                // Silent fail
            }
        }

        private void ApplyWindowCloaking()
        {
            try
            {
                // Hide window from standard window enumeration
                int exStyle = Utilities.NativeMethods.GetWindowLong(_windowHandle, Utilities.NativeMethods.GWL_EXSTYLE);
                exStyle |= Utilities.NativeMethods.WS_EX_TOOLWINDOW; // Hides from ALT+TAB
                exStyle |= Utilities.NativeMethods.WS_EX_LAYERED;    // Enables transparency
                Utilities.NativeMethods.SetWindowLong(_windowHandle, Utilities.NativeMethods.GWL_EXSTYLE, exStyle);

                // Exclude from screen capture
                Utilities.NativeMethods.SetWindowDisplayAffinity(_windowHandle, Utilities.NativeMethods.WDA_EXCLUDEFROMCAPTURE);
            }
            catch
            {
                // Silent fail
            }
        }

        private void ApplyAdditionalStealth()
        {
            try
            {
                // Apply more aggressive anti-detection techniques

                // Change window title again
                this.Title = "Yok-AI";

                // Further reduce visibility
                this.ShowInTaskbar = false;
                this.WindowStyle = WindowStyle.None;

                // Automatically activate enhanced keyboard protection
                if (!isEnhancedKeyProtectionActive)
                {
                    ToggleKeyProtection();
                }
            }
            catch
            {
                // Silent fail
            }
        }

        #endregion
       
        // Toggle the context panel visibility
        private void ContextButton_Click(object sender, RoutedEventArgs e)
        {
            // If shortcuts panel is visible, hide it
            if (ShortcutsPanel.Visibility == Visibility.Visible)
                ShortcutsPanel.Visibility = Visibility.Collapsed;

            // Toggle context panel
            MeetingContextPanel.Visibility =
                MeetingContextPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;

            // If showing the panel, populate text boxes with current default prompts
            if (MeetingContextPanel.Visibility == Visibility.Visible)
            {
                // Populate text boxes with default prompts
                ScreenshotPromptTextBox.Text = defaultScreenshotPrompt;
                SelectionPromptTextBox.Text = defaultSelectionPrompt;  // ADD THIS LINE

                // Enable save button
                SaveContextButton.IsEnabled = true;
            }
        }
        
        // Cancel and close the panel
        private void CancelContextButton_Click(object sender, RoutedEventArgs e)
        {
            MeetingContextPanel.Visibility = Visibility.Collapsed;
        }

        private void SaveContextButton_Click(object sender, RoutedEventArgs e)
        {
            // Get prompt values from text boxes
            string audioPrompt = AudioPromptTextBox.Text.Trim();
            string screenshotPrompt = ScreenshotPromptTextBox.Text.Trim();
            defaultAudioPrompt = AudioPromptTextBox.Text.Trim();
            string selectionPrompt = SelectionPromptTextBox.Text.Trim();  // ADD THIS LINE

            // Set the default prompts
            defaultScreenshotPrompt = screenshotPrompt;
            defaultSelectionPrompt = selectionPrompt;  // ADD THIS LINE

            // Show indicator that prompts are set
            ContextActiveIndicator.Visibility = Visibility.Visible;

            // Add an indicator to the UI that prompts are saved
            AddMessage("Yok-AI", "Default prompts saved successfully.");

            // Close the panel
            MeetingContextPanel.Visibility = Visibility.Collapsed;
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            isShortcutsPanelVisible = !isShortcutsPanelVisible;
            ShortcutsPanel.Visibility = isShortcutsPanelVisible ? Visibility.Visible : Visibility.Collapsed;
        }


        private async void ScreenshotButton_Click(object sender, RoutedEventArgs e)
        {
            await CaptureScreenWithThumbnail();
        }

        private async Task CaptureScreenWithThumbnail()
        {
            try
            {
                // Check if we've reached the maximum number for this capture session
                if (screenshotBuffer.Count >= maxScreenshots)
                {
                    AddMessage("Yok-AI", $"Maximum screenshots reached ({maxScreenshots}). Please analyze or clear screenshots.");
                    return;
                }

                string base64Image;

                if (isWindows11OrHigher)
                {
                    // Windows 11 method - Hide window completely
                    this.Hide();
                    await Task.Delay(300);
                    base64Image = CaptureScreenToBase64();
                    this.Show();
                    // Reapply the display affinity just to be safe
                    Utilities.NativeMethods.SetWindowDisplayAffinity(_windowHandle, Utilities.NativeMethods.WDA_EXCLUDEFROMCAPTURE);
                }
                else
                {
                    // Windows 10 method - Use minimize technique
                    double currentWidth = this.Width;
                    double currentHeight = this.Height;
                    double currentOpacity = MainBorder.Opacity;
                    Effect currentEffect = MainBorder.Effect;

                    this.Width = 1;
                    this.Height = 1;
                    MainBorder.Opacity = 0.01;
                    MainBorder.Effect = new BlurEffect { Radius = 1 };

                    await Task.Delay(300);
                    base64Image = CaptureScreenToBase64();

                    this.Width = currentWidth;
                    this.Height = currentHeight;
                    MainBorder.Opacity = currentOpacity;
                    MainBorder.Effect = currentEffect;
                }

                if (!string.IsNullOrEmpty(base64Image))
                {
                    // Add to buffer
                    screenshotBuffer.Add(base64Image);

                    // Create and display thumbnail
                    CreateThumbnail(base64Image, screenshotBuffer.Count);

                    // Enter screenshot mode
                    EnterScreenshotMode();
                }
                else
                {
                    AddMessage("Yok-AI", "Failed to capture screen.");
                }
            }
            catch (Exception ex)
            {
                AddMessage("Yok-AI", $"Error capturing screen: {ex.Message}");
                this.Show(); // Ensure window is visible in case of error
            }
        }


        // Shows which AI is in use (click it to open Settings)
        private void UpdateCreditsDisplay()
        {
            Dispatcher.InvokeAsync(() => {
                string p = new AiClient(aiSettings).EffectiveProvider;
                CreditsLabel.Text = p == null ? "not set up" : AiProviders.DisplayName(p);
            });
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {

            CheckPreviousLogin();
            // Get the window handle
            if (_windowHandle == IntPtr.Zero)
            {
                _windowHandle = new WindowInteropHelper(this).Handle;
                _hwndSource = HwndSource.FromHwnd(_windowHandle);
                _hwndSource?.AddHook(new HwndSourceHook(WndProc));
            }
            // Apply window stealth techniques
            MakeWindowStealth();

            // Register hotkeys
            RegisterAllHotkeys();

            // Install keyboard hook for basic protection at startup
            if (keyboardHookId == IntPtr.Zero)
            {
                keyboardProc = UnifiedKeyboardHook;
                keyboardHookId = SetHook(keyboardProc);
            }

            // Initialize memory toggle state
            MemoryToggleButton.Foreground = (SolidColorBrush)FindResource("TextSecondaryBrush");
            MemoryToggleButton.ToolTip = "Chat Memory: OFF";

            // Add to Window_Loaded method at the end
            this.SizeChanged += MainWindow_SizeChanged;

        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Maintain stealth properties when window is resized
            if (!isInitializing && _windowHandle != IntPtr.Zero)
            {
                Dispatcher.BeginInvoke(new Action(() => {
                    MaintainStealthInFocusMode();
                }), DispatcherPriority.Background);
            }
        }

        private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (RealtimeTranscriptionDisplay != null)
            {
                RealtimeTranscriptionDisplay.PreviewKeyDown -= TranscriptionDisplay_KeyDown;
            }

            if (selectionDelayTimer != null)
            {
                selectionDelayTimer.Stop();
                selectionDelayTimer = null;
            }
            // Clean up real-time transcription
            if (isRealtimeTranscriptionActive)
            {
                await StopRealtimeTranscription();
            }
            // Clean up resources
            if (keyboardHookId != IntPtr.Zero)
            {
                Utilities.NativeMethods.UnhookWindowsHookEx(keyboardHookId);
                keyboardHookId = IntPtr.Zero;
            }
            // Unregister hotkeys
            UnregisterAllHotkeys();

            // If it's an emergency exit, consider clearing the login data
            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && Keyboard.IsKeyDown(Key.X))
            {
                try
                {
                    AiSettings.Delete();
                }
                catch
                {
                    // Silent fail
                }
            }
            //NAudio.MediaFoundation.MediaFoundationApi.Shutdown();
            
            MediaFoundationApi.Shutdown();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                Utilities.NativeMethods.ReleaseCapture();
                Utilities.NativeMethods.SendMessage(_windowHandle, Utilities.NativeMethods.WM_NCLBUTTONDOWN, Utilities.NativeMethods.HT_CAPTION, 0);
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleVisibility();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void UserInput_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Handle ESC key to exit reply mode
            if (e.Key == Key.Escape && isInReplyMode)
            {
                ExitReplyMode();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter)
            {
                if (Keyboard.Modifiers == ModifierKeys.Control)
                {
                    // Ctrl+Enter: Insert newline
                    int caretIndex = UserInput.CaretIndex;
                    UserInput.Text = UserInput.Text.Insert(caretIndex, "\n");
                    UserInput.CaretIndex = caretIndex + 1;
                }
                else if (Keyboard.Modifiers == ModifierKeys.None)
                {
                    // Plain Enter: Send message
                    SendMessageAndClearFocus();
                }

                e.Handled = true;
            }
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            SendMessage();
        }

        private void KeystrokeHideButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleKeyProtection();
        }

        private void CursorHideButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleCursorHiding();
        }

        #endregion

        #region Real-time Transcription

        private async void TranscriptionToggle_Click(object sender, MouseButtonEventArgs e)
        {
            if (!isLoggedIn)
            {
                AddMessage("Yok-AI", "Please log in to use real-time transcription.");
                return;
            }

            if (!isTranscriptionUIEnabled)
            {
                await EnableTranscription();
            }
            else
            {
                await DisableTranscription();
            }
        }
        private async Task EnableTranscription()
        {
            try
            {
                // Start the actual transcription
                await StartRealtimeTranscription();

                // Update UI state
                isTranscriptionUIEnabled = true;
                UpdateTranscriptionToggleUI();

                AddMessage("Yok-AI", "Live transcription enabled.");
            }
            catch (Exception ex)
            {
                AddMessage("Yok-AI", $"Failed to enable transcription: {ex.Message}");
            }
        }

        private async Task DisableTranscription()
        {
            try
            {
                // Stop the actual transcription
                await StopRealtimeTranscription();

                // Update UI state
                isTranscriptionUIEnabled = false;
                UpdateTranscriptionToggleUI();

                AddMessage("Yok-AI", "Live transcription disabled.");
            }
            catch (Exception ex)
            {
                AddMessage("Yok-AI", $"Failed to disable transcription: {ex.Message}");
            }
        }

        private void UpdateTranscriptionToggleUI()
        {
            if (isTranscriptionUIEnabled)
            {
                // Enabled state - bright and active
                ToggleBackground.Background = new SolidColorBrush(Color.FromRgb(0, 120, 215)); // Blue
                ToggleSlider.Fill = new SolidColorBrush(Colors.White);
                ToggleSlider.HorizontalAlignment = HorizontalAlignment.Right;
                ToggleSlider.Margin = new Thickness(0, 0, 4, 0);
                ToggleIcon.Text = "🎤";
                ToggleIcon.Foreground = new SolidColorBrush(Colors.White);
                ToggleIcon.HorizontalAlignment = HorizontalAlignment.Right;
                ToggleIcon.Margin = new Thickness(0, 0, 8, 0);

                // Remove disabled overlay
                DisabledOverlay.Visibility = Visibility.Collapsed;

                // Brighten the transcription area
                TranscriptionScrollViewer.Opacity = 1.0;
                RealtimeTranscriptionDisplay.Opacity = 1.0;
            }
            else
            {
                // Disabled state - dark and inactive
                ToggleBackground.Background = new SolidColorBrush(Color.FromRgb(64, 64, 64)); // Dark gray
                ToggleSlider.Fill = new SolidColorBrush(Color.FromRgb(102, 102, 102));
                ToggleSlider.HorizontalAlignment = HorizontalAlignment.Left;
                ToggleSlider.Margin = new Thickness(4, 0, 0, 0);
                ToggleIcon.Text = "🔇";
                ToggleIcon.Foreground = new SolidColorBrush(Color.FromRgb(153, 153, 153));
                ToggleIcon.HorizontalAlignment = HorizontalAlignment.Left;
                ToggleIcon.Margin = new Thickness(8, 0, 0, 0);

                // Show disabled overlay
                DisabledOverlay.Visibility = Visibility.Visible;

                // Dim the transcription area
                TranscriptionScrollViewer.Opacity = 0.5;
                RealtimeTranscriptionDisplay.Opacity = 0.5;

                // Update status
                TranscriptionStatusDot.Fill = new SolidColorBrush(Colors.Gray);
                TranscriptionStatusText.Text = "Disabled";
            }
        }
        // Live transcription: free offline Windows speech by default, or the user's own Azure Speech key if they added one.
        private WindowsSpeech windowsSpeech;

        private async Task StartRealtimeTranscription()
        {
            try
            {
                // Initialize local recording
                InitializeLocalRecording();

                // Initialize circular buffer for 10 minutes of audio
                int bufferSize = CIRCULAR_BUFFER_SECONDS * BYTES_PER_SECOND;
                audioCircularBuffer = new CircularBuffer<byte>(bufferSize);

                if (aiSettings.UseAzureSpeech)
                {
                    var config = SpeechConfig.FromSubscription(aiSettings.AzureSpeechKey, aiSettings.AzureSpeechRegion);
                    config.SpeechRecognitionLanguage = "en-US";
                    config.EnableDictation();

                    pushStream = AudioInputStream.CreatePushStream(AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1));
                    var audioConfig = AudioConfig.FromStreamInput(pushStream);

                    speechRecognizer = new SpeechRecognizer(config, audioConfig);
                    speechRecognizer.Recognizing += OnRecognizing;
                    speechRecognizer.Recognized += OnRecognizedHybrid;
                    speechRecognizer.Canceled += OnCanceled;
                    speechRecognizer.SessionStarted += OnSessionStarted;
                    speechRecognizer.SessionStopped += OnSessionStopped;

                    await speechRecognizer.StartContinuousRecognitionAsync();
                }
                else
                {
                    windowsSpeech = new WindowsSpeech();
                    windowsSpeech.Partial += text => Dispatcher.InvokeAsync(() => UpdatePartialTranscription(text));
                    windowsSpeech.Final += text => Dispatcher.InvokeAsync(() => HandleFinalText(text));
                    windowsSpeech.Error += msg => Dispatcher.InvokeAsync(() => AddMessage("Yok-AI", "Windows speech error: " + msg));

                    string problem = windowsSpeech.Start("en-US");
                    if (problem != null)
                    {
                        windowsSpeech.Dispose();
                        windowsSpeech = null;
                        AddMessage("Yok-AI", problem);
                        CleanupHybridSystem();
                        return;
                    }
                    AddMessage("Yok-AI", "Using free offline Windows speech (" + windowsSpeech.RecognizerName + "). For better accuracy, add your own Azure Speech key in Settings.");
                    TranscriptionStatusDot.Fill = new SolidColorBrush(Colors.LightGreen);
                    TranscriptionStatusText.Text = "Listening";
                }

                // Start hybrid audio processing
                StartHybridAudioProcessing();

                // Initialize sentence analysis timer
                InitializeSentenceAnalysis();

                // Update UI
                isRealtimeTranscriptionActive = true;
                sessionStartTime = DateTime.Now;
                UpdateTranscriptionUI();
                RealtimeTranscriptionPanel.Visibility = Visibility.Visible;

                lock (billingLock)
                {
                    totalBilledTime = TimeSpan.Zero;
                    isBillingActive = false;
                    billedTimeSessionStart = DateTime.MinValue;
                }

                AddMessage("Yok-AI", "Hybrid transcription started - Local recording + Smart streaming active.");
                UpdateStreamingStatus("Local recording active");
            }
            catch (Exception ex)
            {
                AddMessage("Yok-AI", $"Failed to start hybrid transcription: {ex.Message}");
                await StopRealtimeTranscription();
            }
        }

        private void InitializeLocalRecording()
        {
            try
            {
                // Create recording file with timestamp
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string recordingsDir = Path.Combine(AppDataPath, "Recordings");

                if (!Directory.Exists(recordingsDir))
                {
                    Directory.CreateDirectory(recordingsDir);
                }

                currentRecordingPath = Path.Combine(recordingsDir, $"meeting_{timestamp}.wav");

                // Initialize wave file writer for local backup
                var waveFormat = new WaveFormat(16000, 16, 1); // 16kHz, 16-bit, mono
                localRecordingWriter = new WaveFileWriter(currentRecordingPath, waveFormat);

                AddMessage("Debug", $"Local recording started: {Path.GetFileName(currentRecordingPath)}");
            }
            catch (Exception ex)
            {
                AddMessage("Debug", $"Failed to initialize local recording: {ex.Message}");
            }
        }

        private void InitializeSentenceAnalysis()
        {
            // Initialize sentence analysis timer (runs every 3 seconds)
            sentenceAnalysisTimer = new Timer(AnalyzePendingSentences, null,
                TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
        }

        private async Task StopRealtimeTranscription()
        {
            try
            {
                isRealtimeTranscriptionActive = false;

                // Stop recognition (your existing code)
                if (speechRecognizer != null)
                {
                    await speechRecognizer.StopContinuousRecognitionAsync();
                    speechRecognizer.Dispose();
                    speechRecognizer = null;
                }

                if (windowsSpeech != null)
                {
                    windowsSpeech.Dispose();
                    windowsSpeech = null;
                }

                // Stop audio forwarding (your existing code)
                StopAudioForwarding();

                // Clean up streams (your existing code)
                if (pushStream != null)
                {
                    pushStream.Dispose();
                    pushStream = null;
                }

                // Stop timers (your existing code)
                if (transcriptionTimer != null)
                {
                    transcriptionTimer.Stop();
                    transcriptionTimer = null;
                }

                // NEW: Clean up hybrid system
                CleanupHybridSystem();

                // Update UI
                UpdateTranscriptionUI();
                UpdateStreamingStatus("Stopped");

                AddMessage("Yok-AI", "Hybrid transcription stopped. Local recording saved.");
            }
            catch (Exception ex)
            {
                AddMessage("Yok-AI", $"Error stopping transcription: {ex.Message}");
            }
        }

        private void CleanupHybridSystem()
        {
            try
            {
                // Stop sentence analysis
                if (sentenceAnalysisTimer != null)
                {
                    sentenceAnalysisTimer.Dispose();
                    sentenceAnalysisTimer = null;
                }

                // Close local recording
                if (localRecordingWriter != null)
                {
                    localRecordingWriter.Dispose();
                    localRecordingWriter = null;

                    if (!string.IsNullOrEmpty(currentRecordingPath))
                    {
                        AddMessage("Yok-AI", $"Full recording saved: {Path.GetFileName(currentRecordingPath)}");
                    }
                }

                // Clear buffers
                lock (sentenceLock)
                {
                    currentTranscriptBuffer.Clear();
                    recentSentences.Clear();
                }

                lock (bufferLock)
                {
                    audioCircularBuffer?.Clear();
                }

                recentAudioChunks.Clear();
                pendingStreamBuffer.Clear();

                // Reset counters
                processedChunksCount = 0;
                streamedChunksCount = 0;
                isCurrentlyInSpeechSession = false;
                isStreamingToAzure = false;

                // ADD THIS: Reset billed time tracking
                lock (billingLock)
                {
                    // Final billed time calculation
                    if (isBillingActive && billedTimeSessionStart != DateTime.MinValue)
                    {
                        TimeSpan finalSessionTime = DateTime.Now - billedTimeSessionStart;
                        totalBilledTime = totalBilledTime.Add(finalSessionTime);
                    }

                    // Log final billed time for this session
                    AddMessage("Yok-AI", aiSettings.UseAzureSpeech ? $"Session completed. Azure billed time: {FormatBilledTime(totalBilledTime)}" : $"Session completed. Speech time: {FormatBilledTime(totalBilledTime)}");

                    // Reset for next session
                    totalBilledTime = TimeSpan.Zero;
                    isBillingActive = false;
                    billedTimeSessionStart = DateTime.MinValue;
                }

                recentAudioSegments.Clear();
                isInSilencePeriod = false;
                isCapturingOtherSpeaker = false;
                currentOtherSpeakerBuffer.Clear();
                consecutiveSilenceChunks = 0;
                silenceStartTime = DateTime.MinValue;
                lastOtherSpeakerActivity = DateTime.MinValue;

                AddMessage("Debug", "Speaker detection cleanup completed");

                AddMessage("Debug", "Hybrid system cleanup completed");
            }
            catch (Exception ex)
            {
                AddMessage("Debug", $"Cleanup error: {ex.Message}");
            }
        }

        private void StartAudioForwarding()
        {
            try
            {
                if (audioCapture == null)
                {
                    InitializeAudioCapture();
                }

                // Modify the existing audioCapture DataAvailable handler
                audioCapture.DataAvailable -= OnAudioDataAvailable; // Remove old handler if exists
                audioCapture.DataAvailable += OnAudioDataAvailable;

                if (audioCapture.CaptureState != NAudio.CoreAudioApi.CaptureState.Capturing)
                {
                    audioCapture.StartRecording();
                }
            }
            catch (Exception ex)
            {
                AddMessage("Yok-AI", $"Failed to start audio forwarding: {ex.Message}");
            }
        }

        private void StopAudioForwarding()
        {
            try
            {
                if (audioCapture != null && audioCapture.CaptureState == NAudio.CoreAudioApi.CaptureState.Capturing)
                {
                    audioCapture.StopRecording();
                }
                ClearVADState();
            }
            catch (Exception ex)
            {
                AddMessage("Yok-AI", $"Error stopping audio forwarding: {ex.Message}");
            }
        }
        private void ClearVADState()
        {
            try
            {
                audioBuffer.Clear();
                isSpeechDetected = false;
                lastSpeechTime = DateTime.MinValue;
                AddMessage("Debug", "VAD state cleared");
            }
            catch
            {
                // Silent fail
            }
        }

        private void OnAudioDataAvailable(object sender, WaveInEventArgs e)
        {
            try
            {
                if (!isRealtimeTranscriptionActive) return;

                // Step 1: Always save to local recording (full fidelity backup)
                SaveToLocalRecording(e.Buffer, e.BytesRecorded);

                // Step 2: Add to circular buffer (10-minute rolling backup)
                AddToCircularBuffer(e.Buffer, e.BytesRecorded);

                // Step 3: Process for smart streaming
                ProcessAudioForSmartStreaming(e.Buffer, e.BytesRecorded);

                // Free Windows speech costs nothing, so it gets all audio (it needs the pauses to end phrases).
                var ws = windowsSpeech;
                if (ws != null)
                {
                    ws.Write(ConvertAudioFormat(e.Buffer, e.BytesRecorded, audioCapture.WaveFormat));
                }

                // Update performance counters
                processedChunksCount++;

                // Update streaming status periodically
                if (processedChunksCount % 50 == 0) // Every ~1 second
                {
                    UpdatePerformanceStatus();
                }
            }
            catch (Exception ex)
            {
                AddMessage("Debug", $"Audio processing error: {ex.Message}");
            }
        }

        private void SaveToLocalRecording(byte[] buffer, int bytesRecorded)
        {
            try
            {
                if (localRecordingWriter != null)
                {
                    // Convert to 16kHz mono if needed, then write
                    byte[] convertedAudio = ConvertAudioFormat(buffer, bytesRecorded, audioCapture.WaveFormat);
                    if (convertedAudio != null && convertedAudio.Length > 0)
                    {
                        localRecordingWriter.Write(convertedAudio, 0, convertedAudio.Length);
                        localRecordingWriter.Flush(); // Ensure data is written immediately
                    }
                }
            }
            catch (Exception ex)
            {
                AddMessage("Debug", $"Local recording error: {ex.Message}");
            }
        }

        private void AddToCircularBuffer(byte[] buffer, int bytesRecorded)
        {
            try
            {
                lock (bufferLock)
                {
                    // Add each byte to circular buffer
                    for (int i = 0; i < bytesRecorded; i++)
                    {
                        audioCircularBuffer.Add(buffer[i]);
                    }
                }
            }
            catch
            {
                // Silent fail for buffer operations
            }
        }

        private void ProcessAudioForSmartStreaming(byte[] buffer, int bytesRecorded)
        {
            try
            {
                // Calculate audio levels
                float rmsLevel = CalculateRMSLevel(buffer, bytesRecorded);

                // Update adaptive VAD
                UpdateAdaptiveVAD(rmsLevel);

                // Determine if this chunk contains speech
                bool isSpeech = IsAdaptiveSpeechDetected(rmsLevel);

                // Create audio chunk
                AudioChunk chunk = new AudioChunk(buffer, rmsLevel, isSpeech);

                // Add to recent chunks for context
                recentAudioChunks.Enqueue(chunk);
                while (recentAudioChunks.Count > 100) // Keep ~2 seconds of context
                {
                    recentAudioChunks.Dequeue();
                }

                // Process speech session logic
                if (isSpeech)
                {
                    ProcessSpeechDetected(chunk);
                }
                else
                {
                    ProcessSilenceDetected(chunk);
                }

                ProcessAudioLevelForSpeakerDetection(rmsLevel);
            }
            catch (Exception ex)
            {
                AddMessage("Debug", $"Smart streaming error: {ex.Message}");
            }
        }

        private void UpdateAdaptiveVAD(float currentLevel)
        {
            // Add to noise floor tracker (lower levels)
            if (currentLevel < adaptiveThreshold * 0.5)
            {
                noiseFloorTracker.Add(currentLevel);
            }

            // Add to speech level tracker (higher levels)
            if (currentLevel > adaptiveThreshold * 2.0)
            {
                speechLevelTracker.Add(currentLevel);
            }

            // Update adaptive threshold
            if (noiseFloorTracker.Count > 10 && speechLevelTracker.Count > 5)
            {
                double noiseFloor = noiseFloorTracker.Average;
                double speechLevel = speechLevelTracker.Average;

                // Threshold should be between noise floor and speech level
                double newThreshold = noiseFloor + (speechLevel - noiseFloor) * 0.3;

                // Smooth adaptation
                adaptiveThreshold = adaptiveThreshold * (1 - ADAPTATION_RATE) + newThreshold * ADAPTATION_RATE;

                // Keep within reasonable bounds
                adaptiveThreshold = Math.Max(0.005, Math.Min(0.1, adaptiveThreshold));
            }
        }

        private bool IsAdaptiveSpeechDetected(float currentLevel)
        {
            return currentLevel > adaptiveThreshold;
        }

        private void ProcessSpeechDetected(AudioChunk chunk)
        {
            lastVoiceActivityTime = DateTime.Now;

            if (!isCurrentlyInSpeechSession)
            {
                // Start new speech session
                StartSpeechSession();
            }

            // Add to pending stream buffer
            pendingStreamBuffer.Enqueue(chunk);

            // Stream if we have enough content
            ProcessPendingStreamBuffer();
        }

        private void ProcessSilenceDetected(AudioChunk chunk)
        {
            if (isCurrentlyInSpeechSession)
            {
                double silenceDuration = (DateTime.Now - lastVoiceActivityTime).TotalMilliseconds;

                if (silenceDuration > MAX_SILENCE_GAP_MS)
                {
                    // End speech session
                    EndSpeechSession();
                }
                else
                {
                    // Still in session, add small silence for context
                    pendingStreamBuffer.Enqueue(chunk);
                }
            }
        }

        private void StartSpeechSession()
        {
            isCurrentlyInSpeechSession = true;
            streamSessionStart = DateTime.Now;

            // Start billing timer
            lock (billingLock)
            {
                if (!isBillingActive)
                {
                    isBillingActive = true;
                    billedTimeSessionStart = DateTime.Now;
                }
            }

            // Add pre-speech context
            AddPreSpeechContext();

            UpdateStreamingStatus("Speech detected");
            AddMessage("Debug", "Started speech session - Billing started");
        }

        private void EndSpeechSession()
        {
            isCurrentlyInSpeechSession = false;

            // Stop billing timer and accumulate time
            lock (billingLock)
            {
                if (isBillingActive && billedTimeSessionStart != DateTime.MinValue)
                {
                    TimeSpan sessionBilledTime = DateTime.Now - billedTimeSessionStart;
                    totalBilledTime = totalBilledTime.Add(sessionBilledTime);
                    isBillingActive = false;
                    billedTimeSessionStart = DateTime.MinValue;

                    AddMessage("Debug", $"Speech session ended - Added {sessionBilledTime.TotalSeconds:F1}s to billed time");
                }
            }

            // Process any remaining buffer
            ProcessPendingStreamBuffer();

            UpdateStreamingStatus("Speech ended - Analyzing...");
        }

        private void AddPreSpeechContext()
        {
            // Add recent audio chunks as context (last 1 second)
            var contextChunks = recentAudioChunks.Where(c =>
                (DateTime.Now - c.Timestamp).TotalMilliseconds <= PRE_SPEECH_CONTEXT_MS).ToList();

            foreach (var chunk in contextChunks)
            {
                pendingStreamBuffer.Enqueue(chunk);
            }
        }

        private void ProcessPendingStreamBuffer()
        {
            try
            {
                // Process chunks in buffer
                while (pendingStreamBuffer.Count > 0)
                {
                    var chunk = pendingStreamBuffer.Dequeue();
                    StreamChunkToAzure(chunk);
                }
            }
            catch (Exception ex)
            {
                AddMessage("Debug", $"Stream buffer processing error: {ex.Message}");
            }
        }

        private void StreamChunkToAzure(AudioChunk chunk)
        {
            try
            {
                if (pushStream != null)
                {
                    byte[] convertedAudio = ConvertAudioFormat(chunk.Data, chunk.Data.Length, audioCapture.WaveFormat);
                    if (convertedAudio != null && convertedAudio.Length > 0)
                    {
                        pushStream.Write(convertedAudio);
                        streamedChunksCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                AddMessage("Debug", $"Azure streaming error: {ex.Message}");
            }
        }

        private void UpdatePerformanceStatus()
        {
            Dispatcher.InvokeAsync(() =>
            {
                double efficiency = processedChunksCount > 0 ?
                    (double)streamedChunksCount / processedChunksCount * 100 : 0;

                // Calculate current billed time
                TimeSpan currentBilledTime;
                lock (billingLock)
                {
                    currentBilledTime = totalBilledTime;
                    if (isBillingActive && billedTimeSessionStart != DateTime.MinValue)
                    {
                        currentBilledTime = currentBilledTime.Add(DateTime.Now - billedTimeSessionStart);
                    }
                }

                // Format billed time
                string billedTimeText;
                if (currentBilledTime.TotalHours >= 1)
                {
                    billedTimeText = $"{(int)currentBilledTime.TotalHours}h {currentBilledTime.Minutes}m {currentBilledTime.Seconds}s";
                }
                else if (currentBilledTime.TotalMinutes >= 1)
                {
                    billedTimeText = $"{currentBilledTime.Minutes}m {currentBilledTime.Seconds}s";
                }
                else
                {
                    billedTimeText = $"{currentBilledTime.Seconds}s";
                }

                string status = aiSettings.UseAzureSpeech
                    ? $"Efficiency: {efficiency:F1}% | Processed: {processedChunksCount} | Streamed: {streamedChunksCount} | Azure billed: {billedTimeText}"
                    : $"Listening (free Windows speech) | Speech time: {billedTimeText}";
                UpdateStreamingStatus(status);
            });
        }
        
        private void UpdateStreamingStatus(string status)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (StreamingStatusText != null)
                {
                    StreamingStatusText.Text = status;
                }
            });
        }

        private void OnRecognizedHybrid(object sender, SpeechRecognitionEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Result.Text))
            {
                string text = e.Result.Text;
                Dispatcher.InvokeAsync(() => HandleFinalText(text));
            }
        }

        // Same handling for Azure and Windows speech results.
        private void HandleFinalText(string text)
        {
            AddFinalTranscription(text);
            ProcessRecognizedTextForSentences(text);
            ProcessTranscriptionForSpeakerChange(text);
        }

        private void ProcessRecognizedTextForSentences(string recognizedText)
        {
            try
            {
                lock (sentenceLock)
                {
                    // Add to current transcript buffer
                    currentTranscriptBuffer.Append(" " + recognizedText.Trim());

                    // Check for sentence completion markers
                    string currentText = currentTranscriptBuffer.ToString().Trim();

                    // Look for sentence endings
                    string[] sentenceMarkers = { ".", "!", "?", "...", ":", ";" };
                    bool foundSentenceEnd = sentenceMarkers.Any(marker => currentText.EndsWith(marker));

                    if (foundSentenceEnd && currentText.Length > 15) // Minimum meaningful sentence length
                    {
                        // Complete sentence found
                        recentSentences.Add(currentText);
                        currentTranscriptBuffer.Clear();

                        // Keep only recent sentences for analysis
                        while (recentSentences.Count > MAX_SENTENCES_FOR_ANALYSIS)
                        {
                            recentSentences.RemoveAt(0);
                        }

                        AddMessage("Debug", $"Sentence captured: {currentText.Substring(0, Math.Min(40, currentText.Length))}...");
                    }
                }
            }
            catch (Exception ex)
            {
                AddMessage("Debug", $"Sentence processing error: {ex.Message}");
            }
        }

        private void ProcessTranscriptionForSpeakerChange(string recognizedText)
        {
            try
            {
                if (!isInSilencePeriod && isCapturingOtherSpeaker)
                {
                    // We're capturing other speaker's text
                    currentOtherSpeakerBuffer.AppendLine(recognizedText);
                    lastOtherSpeakerActivity = DateTime.Now;

                    AddMessage("Debug", $"Capturing other speaker: {recognizedText.Substring(0, Math.Min(30, recognizedText.Length))}...");
                }
            }
            catch (Exception ex)
            {
                AddMessage("Debug", $"Speaker change processing error: {ex.Message}");
            }
        }

        private void ProcessAudioLevelForSpeakerDetection(float audioLevel)
        {
            try
            {
                bool isCurrentlySilent = audioLevel < SILENCE_AUDIO_THRESHOLD;

                // Add to recent segments for analysis
                recentAudioSegments.Enqueue(new AudioSegment(audioLevel, isCurrentlySilent));
                while (recentAudioSegments.Count > 50) // Keep last 50 segments (~5 seconds)
                {
                    recentAudioSegments.Dequeue();
                }

                if (isCurrentlySilent)
                {
                    consecutiveSilenceChunks++;

                    if (!isInSilencePeriod && consecutiveSilenceChunks >= MIN_SILENCE_CHUNKS)
                    {
                        // Transition to silence period (user is speaking)
                        StartSilencePeriod();
                    }
                }
                else
                {
                    consecutiveSilenceChunks = 0;

                    if (isInSilencePeriod)
                    {
                        // Transition back to audio (other speakers resumed)
                        EndSilencePeriod();
                    }
                    else if (!isCapturingOtherSpeaker)
                    {
                        // Start capturing other speaker
                        StartCapturingOtherSpeaker();
                    }
                }
            }
            catch (Exception ex)
            {
                AddMessage("Debug", $"Audio level processing error: {ex.Message}");
            }
        }

        private void StartSilencePeriod()
        {
            try
            {
                isInSilencePeriod = true;
                silenceStartTime = DateTime.Now;

                // Process the other speaker segment that just ended
                if (isCapturingOtherSpeaker && currentOtherSpeakerBuffer.Length > 10)
                {
                    string otherSpeakerText = currentOtherSpeakerBuffer.ToString().Trim();
                    ProcessOtherSpeakerSegment(otherSpeakerText);
                }

                // Reset other speaker capture
                isCapturingOtherSpeaker = false;
                currentOtherSpeakerBuffer.Clear();

                UpdateStreamingStatus("User speaking - Others silent");
                AddMessage("Debug", "Silence period started - User is speaking");
            }
            catch (Exception ex)
            {
                AddMessage("Debug", $"Start silence error: {ex.Message}");
            }
        }

        private void EndSilencePeriod()
        {
            try
            {
                if (isInSilencePeriod)
                {
                    TimeSpan silenceDuration = DateTime.Now - silenceStartTime;
                    AddMessage("Debug", $"Silence period ended - Duration: {silenceDuration.TotalSeconds:F1}s");
                }

                isInSilencePeriod = false;
                silenceStartTime = DateTime.MinValue;

                // Don't immediately start capturing - wait for actual transcription
                UpdateStreamingStatus("Others speaking - Monitoring");
            }
            catch (Exception ex)
            {
                AddMessage("Debug", $"End silence error: {ex.Message}");
            }
        }

        private void StartCapturingOtherSpeaker()
        {
            try
            {
                if (!isInSilencePeriod && !isCapturingOtherSpeaker)
                {
                    isCapturingOtherSpeaker = true;
                    currentOtherSpeakerBuffer.Clear();
                    lastOtherSpeakerActivity = DateTime.Now;

                    AddMessage("Debug", "Started capturing other speaker");
                }
            }
            catch (Exception ex)
            {
                AddMessage("Debug", $"Start capturing error: {ex.Message}");
            }
        }

        private async void ProcessOtherSpeakerSegment(string otherSpeakerText)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(otherSpeakerText) || otherSpeakerText.Length < 20)
                {
                    AddMessage("Debug", "Other speaker segment too short, skipping AI analysis");
                    return;
                }

                // Check credits silently
                if (!CheckCredits(false))
                {
                    AddMessage("Debug", "No credits available for speaker analysis");
                    return;
                }

                // Add the other speaker's text to chat history as user input
                string displayText = otherSpeakerText;
                AddPromptMessage($"\"{displayText}\": {defaultAudioPrompt}");

                AddMessage("Speaker Analysis", $"Analyzing: {otherSpeakerText.Substring(0, Math.Min(50, otherSpeakerText.Length))}...");

                // In ProcessOtherSpeakerSegment method, replace the try block with this:
                try
                {
                    string prompt = $"\"{otherSpeakerText}\": {defaultAudioPrompt}";


                    string response = await SendMessageToAPI(prompt, "text");

                    Dispatcher.Invoke(() => {
                        RemoveLastMessage("Speaker Analysis");
                        AddResponseMessage(response);
                    });
                }
                catch (Exception apiEx)
                {
                    Dispatcher.Invoke(() => {
                        RemoveLastMessage("Speaker Analysis");
                        AddMessage("Speaker Analysis", "Analysis temporarily unavailable");
                    });
                    AddMessage("Debug", $"API error in speaker analysis: {apiEx.Message}");
                }
            }
            catch (Exception ex)
            {
                AddMessage("Debug", $"Process speaker segment error: {ex.Message}");
            }
        }
        
        private async void AnalyzePendingSentences(object state)
        {
            try
            {
                List<string> sentencesToAnalyze;

                lock (sentenceLock)
                {
                    if (recentSentences.Count < 2) return; // Need at least 2 sentences

                    // Copy sentences for analysis
                    sentencesToAnalyze = new List<string>(recentSentences);
                    recentSentences.Clear(); // Clear after copying
                }

                if (sentencesToAnalyze.Count > 0)
                {
                    await AnalyzeSentencesWithAI(sentencesToAnalyze);
                }
            }
            catch (Exception ex)
            {
                AddMessage("Debug", $"Sentence analysis error: {ex.Message}");
            }
        }

        private async Task AnalyzeSentencesWithAI(List<string> sentences)
        {
            // In AnalyzeSentencesWithAI method, replace the try block with this:
            try
            {
                // Check credits silently
                if (!CheckCredits(false)) return;

                string combinedText = string.Join(" ", sentences);
                string prompt = $"Analyze this meeting segment for key points and action items:\n\n{combinedText}";

                Dispatcher.Invoke(() => AddMessage("AI Insights", "Analyzing recent discussion..."));

                try
                {
                    string response = await SendMessageToAPI(prompt, "text");

                    Dispatcher.Invoke(() => {
                        RemoveLastMessage("AI Insights");
                        AddResponseMessage(response);
                    });
                }
                catch
                {
                    Dispatcher.Invoke(() => {
                        RemoveLastMessage("AI Insights");
                        AddMessage("AI Insights", "Analysis temporarily unavailable.");
                    });
                }
            }
            catch (Exception ex)
            {
                AddMessage("Debug", $"AI analysis error: {ex.Message}");
            }
        }

        private bool DetectSpeechInChunk(byte[] buffer, int bytesRecorded)
        {
            try
            {
                // Simple volume-based VAD using RMS
                double sum = 0;
                int sampleCount = 0;

                for (int i = 0; i < bytesRecorded; i += 2)
                {
                    if (i + 1 < bytesRecorded)
                    {
                        // Convert two bytes to 16-bit sample
                        short sample = (short)(buffer[i] | (buffer[i + 1] << 8));
                        sum += (sample * sample);
                        sampleCount++;
                    }
                }

                if (sampleCount == 0) return false;

                // Calculate RMS
                double rms = Math.Sqrt(sum / sampleCount);
                double normalizedRms = rms / short.MaxValue;

                return normalizedRms > SPEECH_THRESHOLD;
            }
            catch
            {
                return false; // Default to no speech on error
            }
        }

        private void FlushBufferToAzure()
        {
            try
            {
                AddMessage("Debug", $"Flushing {audioBuffer.Count} buffered audio chunks to Azure");

                // Send all buffered audio chunks to Azure
                while (pushStream != null && audioBuffer.Count > 0)
                {
                    byte[] bufferedChunk = audioBuffer.Dequeue();
                    byte[] convertedAudio = ConvertAudioFormat(bufferedChunk, bufferedChunk.Length, audioCapture.WaveFormat);
                    if (convertedAudio != null && convertedAudio.Length > 0)
                    {
                        pushStream.Write(convertedAudio);
                    }
                }
            }
            catch (Exception ex)
            {
                AddMessage("Debug", $"Error flushing buffer: {ex.Message}");
            }
        }

        private byte[] ConvertAudioFormat(byte[] inputBuffer, int bytesRecorded, WaveFormat sourceFormat)
        {
            try
            {
                // Create a wave stream from the input buffer
                using (var inputStream = new MemoryStream(inputBuffer, 0, bytesRecorded))
                using (var rawSourceStream = new RawSourceWaveStream(inputStream, sourceFormat))
                {
                    // Target format: 16kHz, 16-bit, mono
                    var targetFormat = new WaveFormat(16000, 16, 1);

                    // Resample if needed
                    using (var resampler = new NAudio.Wave.MediaFoundationResampler(rawSourceStream, targetFormat))
                    {
                        // Read the resampled data using a buffer approach
                        var outputStream = new MemoryStream();
                        byte[] buffer = new byte[4096];
                        int bytesRead;

                        while ((bytesRead = resampler.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            outputStream.Write(buffer, 0, bytesRead);
                        }

                        return outputStream.ToArray();
                    }
                }
            }
            catch
            {
                // Return empty array on conversion error
                return new byte[0];
            }
        }
        
        // Speech recognition event handlers
        private void OnRecognizing(object sender, SpeechRecognitionEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Result.Text))
            {
                Dispatcher.InvokeAsync(() => UpdatePartialTranscription(e.Result.Text));
            }
        }

        private void OnRecognized(object sender, SpeechRecognitionEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Result.Text))
            {
                Dispatcher.InvokeAsync(() => AddFinalTranscription(e.Result.Text));
            }
        }

        private void OnCanceled(object sender, SpeechRecognitionCanceledEventArgs e)
        {
            Dispatcher.InvokeAsync(() => {
                if (e.Reason == CancellationReason.Error)
                {
                    AddMessage("Yok-AI", $"Speech recognition error: {e.ErrorDetails}");
                    _ = StopRealtimeTranscription();
                }
            });
        }

        private void OnSessionStarted(object sender, SessionEventArgs e)
        {
            Dispatcher.InvokeAsync(() => {
                TranscriptionStatusDot.Fill = new SolidColorBrush(Colors.LightGreen);
                TranscriptionStatusText.Text = "Listening";
            });
        }

        private void OnSessionStopped(object sender, SessionEventArgs e)
        {
            Dispatcher.InvokeAsync(() => {
                TranscriptionStatusDot.Fill = new SolidColorBrush(Colors.Gray);
                TranscriptionStatusText.Text = "Stopped";
            });
        }

        private void UpdatePartialTranscription(string text)
        {
            try
            {
                FlowDocument doc = RealtimeTranscriptionDisplay.Document;

                // Find or create the partial results paragraph
                Paragraph partialPara = null;

                // Look for existing partial paragraph (last paragraph with italic text)
                if (doc.Blocks.Count > 0 && doc.Blocks.LastBlock is Paragraph lastPara)
                {
                    foreach (var inline in lastPara.Inlines)
                    {
                        if (inline is Run run && run.FontStyle == FontStyles.Italic)
                        {
                            partialPara = lastPara;
                            break;
                        }
                    }
                }

                if (partialPara == null)
                {
                    // Create new partial paragraph
                    partialPara = new Paragraph() { Margin = new Thickness(0, 2, 0, 2) };
                    doc.Blocks.Add(partialPara);
                }

                // Clear and update partial paragraph
                partialPara.Inlines.Clear();

                // Add timestamp
                Run timeRun = new Run($"[{DateTime.Now:HH:mm:ss}] ");
                timeRun.Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80)); // Dimmed Gray
                timeRun.FontSize = 9;  // Slightly smaller
                timeRun.FontWeight = FontWeights.Light;  // Lighter weight

                partialPara.Inlines.Add(timeRun);  // ← ADD THIS LINE!

                // Add partial text
                Run textRun = new Run(text);
                textRun.Foreground = new SolidColorBrush(Colors.LightGray);
                textRun.FontStyle = FontStyles.Italic;
                partialPara.Inlines.Add(textRun);

                // Scroll to end
                RealtimeTranscriptionDisplay.ScrollToEnd();
            }
            catch
            {
                // Silent fail
            }
        }

        private void AddFinalTranscription(string text)
        {
            try
            {
                FlowDocument doc = RealtimeTranscriptionDisplay.Document;

                // Remove any partial results paragraph
                if (doc.Blocks.Count > 0 && doc.Blocks.LastBlock is Paragraph lastPara)
                {
                    foreach (var inline in lastPara.Inlines)
                    {
                        if (inline is Run run && run.FontStyle == FontStyles.Italic)
                        {
                            doc.Blocks.Remove(lastPara);
                            break;
                        }
                    }
                }

                // Create final paragraph
                Paragraph finalPara = new Paragraph() { Margin = new Thickness(0, 2, 0, 2) };

                // Add timestamp
                Run timeRun = new Run($"[{DateTime.Now:HH:mm:ss}] ");
                timeRun.Foreground = new SolidColorBrush(Color.FromRgb(100, 149, 237)); // Dimmed SkyBlue
                timeRun.FontSize = 9;  // Slightly smaller
                timeRun.FontWeight = FontWeights.Light;  // Lighter weight

                finalPara.Inlines.Add(timeRun); 

                // Add final text
                Run textRun = new Run(text);
                textRun.Foreground = new SolidColorBrush(Colors.White);
                finalPara.Inlines.Add(textRun);

                doc.Blocks.Add(finalPara);

                // Update full transcription text
                fullTranscriptionText.AppendLine($"[{DateTime.Now:HH:mm:ss}] {text}");

                // Update word count
                transcriptionWordCount += text.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
                TranscriptionWordCountLabel.Text = $"{transcriptionWordCount} words";

                // Scroll to end
                RealtimeTranscriptionDisplay.ScrollToEnd();
            }
            catch
            {
                // Silent fail
            }
        }

        private void UpdateTranscriptionUI()
        {
            if (isRealtimeTranscriptionActive)
            {
                TranscriptionStatusDot.Fill = new SolidColorBrush(Colors.LightGreen);
                TranscriptionStatusText.Text = "Listening";
            }
            else
            {
                if (isTranscriptionUIEnabled)
                {
                    TranscriptionStatusDot.Fill = new SolidColorBrush(Colors.Orange);
                    TranscriptionStatusText.Text = "Ready";
                }
                else
                {
                    TranscriptionStatusDot.Fill = new SolidColorBrush(Colors.Gray);
                    TranscriptionStatusText.Text = "Disabled";
                }
            }
        }

        private async void ClearTranscriptionButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Clear the display
                FlowDocument doc = new FlowDocument();
                Paragraph para = new Paragraph();
                Run run = new Run("Transcription cleared...");
                run.Foreground = new SolidColorBrush(Colors.Gray);
                run.FontStyle = FontStyles.Italic;
                para.Inlines.Add(run);
                doc.Blocks.Add(para);
                RealtimeTranscriptionDisplay.Document = doc;

                // Reset counters
                fullTranscriptionText.Clear();
                transcriptionWordCount = 0;
                TranscriptionWordCountLabel.Text = "0 words";

                AddMessage("Yok-AI", "Transcription cleared.");
            }
            catch (Exception ex)
            {
                AddMessage("Yok-AI", $"Error clearing transcription: {ex.Message}");
            }
        }

        private async void ExportTranscriptionButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Check if there's any content to export
                bool hasTranscription = fullTranscriptionText.Length > 0;
                bool hasChatHistory = chatHistory.Count > 0;

                if (!hasTranscription && !hasChatHistory)
                {
                    AddMessage("Yok-AI", "No transcription or chat history to export.");
                    return;
                }

                // Create save file dialog
                Microsoft.Win32.SaveFileDialog saveDialog = new Microsoft.Win32.SaveFileDialog();
                saveDialog.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";
                saveDialog.DefaultExt = "txt";
                saveDialog.FileName = $"Session_Export_{DateTime.Now:yyyyMMdd_HHmmss}";

                if (saveDialog.ShowDialog() == true)
                {
                    // Create export content with header
                    StringBuilder exportContent = new StringBuilder();
                    exportContent.AppendLine("=== Yok-AI Session Export ===");
                    exportContent.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd}");
                    exportContent.AppendLine($"Session Duration: {FormatSessionDuration()}");
                    exportContent.AppendLine($"Speech Time: {FormatBilledTime(totalBilledTime)}");
                    exportContent.AppendLine($"Transcription Words: {transcriptionWordCount}");
                    exportContent.AppendLine($"Chat Messages: {chatHistory.Count}");
                    exportContent.AppendLine();

                    // Add transcription if available
                    if (hasTranscription)
                    {
                        exportContent.AppendLine("=== Live Transcription ===");
                        exportContent.AppendLine(fullTranscriptionText.ToString());
                        exportContent.AppendLine();
                    }

                    // Add chat history if available
                    if (hasChatHistory)
                    {
                        exportContent.AppendLine("=== Chat History ===");

                        foreach (var chatMsg in chatHistory)
                        {
                            // Format timestamp
                            string timestamp = chatMsg.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");

                            // Add message header
                            exportContent.AppendLine($"[{timestamp}] {chatMsg.MessageType}:");

                            // Add message content
                            exportContent.AppendLine(chatMsg.Content);

                            // Add image info if present
                            if (chatMsg.Images != null && chatMsg.Images.Count > 0)
                            {
                                exportContent.AppendLine($"   [Attached {chatMsg.Images.Count} image(s)]");
                            }

                            // Add request type info if not standard text
                            if (!string.IsNullOrEmpty(chatMsg.RequestType) && chatMsg.RequestType != "text")
                            {
                                exportContent.AppendLine($"   [Request Type: {chatMsg.RequestType}]");
                            }

                            exportContent.AppendLine(); // Blank line between messages
                        }
                    }

                    // Write to file
                    File.WriteAllText(saveDialog.FileName, exportContent.ToString());

                    // Success message
                    string itemsText = string.Join(" and ", new[]
                    {
                        hasTranscription ? "transcription" : null,
                        hasChatHistory ? "chat history" : null
                    }.Where(x => x != null));
                    
                    AddMessage("Yok-AI", $"Session {itemsText} exported to: {Path.GetFileName(saveDialog.FileName)}");
                }
            }
            catch (Exception ex)
            {
                AddMessage("Yok-AI", $"Error exporting session: {ex.Message}");
            }
        }
        private async void SendToAIButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (fullTranscriptionText.Length == 0)
                {
                    AddMessage("Yok-AI", "No transcription to send to AI.");
                    return;
                }

                if (!CheckCredits())
                    return;

                string transcriptText = fullTranscriptionText.ToString();
                string prompt = "Please analyze this meeting transcription and provide a summary with key points:\n\n" + transcriptText;

                AddMessage("You", "Analyze meeting transcription");
                AddMessage("Yok-AI", "Analyzing transcription...");

                try
                {
                    string response = await SendMessageToAPI(prompt, "text");
                    RemoveThinkingMessage();
                    AddResponseMessage(response);
                }
                catch (Exception ex)
                {
                    RemoveThinkingMessage();
                    AddMessage("Yok-AI", $"Error analyzing transcription: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                AddMessage("Yok-AI", $"Error sending transcription to AI: {ex.Message}");
            }
        }

        #endregion


        #region Stealth Features

        private void MakeWindowStealth()
        {
            try
            {
                if (_windowHandle == IntPtr.Zero)
                {
                    _windowHandle = new WindowInteropHelper(this).Handle;
                }

                if (_windowHandle != IntPtr.Zero)
                {
                    // Make the window layered and hide from taskbar
                    int exStyle = Utilities.NativeMethods.GetWindowLong(_windowHandle, Utilities.NativeMethods.GWL_EXSTYLE);
                    exStyle |= Utilities.NativeMethods.WS_EX_LAYERED | Utilities.NativeMethods.WS_EX_TOOLWINDOW;
                    Utilities.NativeMethods.SetWindowLong(_windowHandle, Utilities.NativeMethods.GWL_EXSTYLE, exStyle);

                    // Set transparency using SetLayeredWindowAttributes instead of WPF's AllowsTransparency
                    SetLayeredWindowAttributes(_windowHandle, 0, 245, LWA_ALPHA);

                    // Set the window to be excluded from capture
                    bool result = Utilities.NativeMethods.SetWindowDisplayAffinity(_windowHandle, Utilities.NativeMethods.WDA_EXCLUDEFROMCAPTURE);
                    
                    // If that fails, try WDA_MONITOR as fallback (black box in capture)
                    if (!result)
                    {
                        // Set the window to be excluded from capture
                        Utilities.NativeMethods.SetWindowDisplayAffinity(_windowHandle, Utilities.NativeMethods.WDA_EXCLUDEFROMCAPTURE);
                    }

                    // Additional window tweaks that help with release mode
                    this.ShowInTaskbar = false;
                    this.Topmost = true;
                }
            }
            catch
            {
                // Silent fail - no logs
            }
        }

        // Add a method to call from App.xaml.cs to ensure the stealth mode is applied
        // even if something goes wrong in the window initialization
        public void ReapplyStealthMeasures()
        {
            Dispatcher.InvokeAsync(() => {
                if (_windowHandle == IntPtr.Zero)
                {
                    _windowHandle = new WindowInteropHelper(this).Handle;
                }

                if (_windowHandle != IntPtr.Zero)
                {
                    // Reapply all stealth techniques
                    int exStyle = Utilities.NativeMethods.GetWindowLong(_windowHandle, Utilities.NativeMethods.GWL_EXSTYLE);
                    exStyle |= Utilities.NativeMethods.WS_EX_LAYERED | Utilities.NativeMethods.WS_EX_TOOLWINDOW;
                    Utilities.NativeMethods.SetWindowLong(_windowHandle, Utilities.NativeMethods.GWL_EXSTYLE, exStyle);

                    // Always reapply transparency
                    SetLayeredWindowAttributes(_windowHandle, 0, 245, LWA_ALPHA);

                    // Reapply display affinity
                    Utilities.NativeMethods.SetWindowDisplayAffinity(_windowHandle, Utilities.NativeMethods.WDA_EXCLUDEFROMCAPTURE);
                }
            });
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;

            if (msg == WM_HOTKEY)
            {
                int hotkeyId = wParam.ToInt32();

                try
                {
                    switch (hotkeyId)
                    {
                        case Utilities.NativeMethods.HOTKEY_CURSOR_HIDE:
                            ToggleCursorHiding();
                            break;
                        case Utilities.NativeMethods.HOTKEY_KEYSTROKE_HIDE:
                            ToggleKeyProtection();
                            break;
                        case Utilities.NativeMethods.HOTKEY_TOGGLE_UI:
                            ToggleVisibility();
                            break;
                        case Utilities.NativeMethods.HOTKEY_SCREEN_CAPTURE:
                            CaptureScreen();
                            break;
                        
                        case Utilities.NativeMethods.HOTKEY_EMERGENCY_EXIT:
                            if (screenshotBuffer.Count > 0)
                            {
                                ExitScreenshotMode();
                            }
                            else
                            {
                                this.Close();
                                System.Windows.Application.Current.Shutdown();
                            }
                            break;
                    }

                    handled = true;
                }
                catch (Exception ex)
                {
                    AddMessage("Yok-AI", $"Error processing hotkey: {ex.Message}");
                }
            }

            return IntPtr.Zero;
        }
        private void RegisterAllHotkeys()
        {
            // Unregister any existing hotkeys first
            UnregisterAllHotkeys();

            // Register hotkeys with system; name any combo another app already owns.
            uint mods = Utilities.NativeMethods.MOD_CONTROL | Utilities.NativeMethods.MOD_ALT;
            var hotkeys = new (int id, uint vk, string label)[]
            {
                (Utilities.NativeMethods.HOTKEY_TOGGLE_UI, Utilities.NativeMethods.VK_SPACE, "Ctrl+Alt+Space"),
                (Utilities.NativeMethods.HOTKEY_SCREEN_CAPTURE, Utilities.NativeMethods.VK_C, "Ctrl+Alt+C"),
                (Utilities.NativeMethods.HOTKEY_EMERGENCY_EXIT, Utilities.NativeMethods.VK_X, "Ctrl+Alt+X"),
                (Utilities.NativeMethods.HOTKEY_MULTI_CAPTURE_START, (uint)'M', "Ctrl+Alt+M"),
                (Utilities.NativeMethods.HOTKEY_MULTI_CAPTURE_FINISH, (uint)'F', "Ctrl+Alt+F"),
                (Utilities.NativeMethods.HOTKEY_KEYSTROKE_HIDE, (uint)'K', "Ctrl+Alt+K"),
                (Utilities.NativeMethods.HOTKEY_CURSOR_HIDE, (uint)'H', "Ctrl+Alt+H"),
            };
            var failed = new List<string>();
            foreach (var h in hotkeys)
            {
                if (!Utilities.NativeMethods.RegisterHotKey(_windowHandle, h.id, mods, h.vk)) failed.Add(h.label);
            }
            if (failed.Count > 0)
            {
                AddMessage("Yok-AI", "Warning: " + string.Join(", ", failed) + " could not be registered; another app is already using " + (failed.Count == 1 ? "it." : "them."));
            }
        }

        private void UnregisterAllHotkeys()
        {
            // Unregister all hotkeys
            Utilities.NativeMethods.UnregisterHotKey(_windowHandle, Utilities.NativeMethods.HOTKEY_TOGGLE_UI);
            Utilities.NativeMethods.UnregisterHotKey(_windowHandle, Utilities.NativeMethods.HOTKEY_SCREEN_CAPTURE);
            Utilities.NativeMethods.UnregisterHotKey(_windowHandle, Utilities.NativeMethods.HOTKEY_EMERGENCY_EXIT);
            Utilities.NativeMethods.UnregisterHotKey(_windowHandle, Utilities.NativeMethods.HOTKEY_MULTI_CAPTURE_START);
            Utilities.NativeMethods.UnregisterHotKey(_windowHandle, Utilities.NativeMethods.HOTKEY_MULTI_CAPTURE_FINISH);
            Utilities.NativeMethods.UnregisterHotKey(_windowHandle, Utilities.NativeMethods.HOTKEY_KEYSTROKE_HIDE);
            Utilities.NativeMethods.UnregisterHotKey(_windowHandle, Utilities.NativeMethods.HOTKEY_CURSOR_HIDE);
        }

        #endregion

        #region UI Visibility

        private void ToggleVisibility()
        {
            isMinimized = !isMinimized;

            if (isMinimized)
            {
                // Shrink to minimal presence but still accessible
                this.Width = 5;
                this.Height = 5;

                // Make almost transparent with blur
                MainBorder.Effect = new BlurEffect { Radius = 1 };
                MainBorder.Opacity = 0.05;
            }
            else
            {
                // Restore normal size and show content
                this.Width = 500;
                this.Height = 650;
                MainBorder.Effect = null;
                MainBorder.Opacity = 1;
            }
        }
        #endregion

        #region Keystroke & Cursor Hiding


        // Toggle cursor hiding
        private void ToggleCursorHiding()
        {
            // Just call keyboard hiding instead
            ToggleKeyProtection();
            AddMessage("Yok-AI", "Ctrl+Alt+H does the same as Ctrl+Alt+K (hide keystrokes).");
        }

        // Toggle enhanced key protection mode
        private void ToggleKeyProtection()
        {
            // Toggle the protection state
            isEnhancedKeyProtectionActive = !isEnhancedKeyProtectionActive;

            // Ensure we have a keyboard hook installed regardless of mode
            if (keyboardHookId == IntPtr.Zero)
            {
                keyboardProc = UnifiedKeyboardHook;
                keyboardHookId = SetHook(keyboardProc);
            }

            // Apply visual indicators based on state
            if (isEnhancedKeyProtectionActive)
            {
                // Visual indicator that protection is active
                UserInput.Background = new SolidColorBrush(MediaColor.FromRgb(30, 30, 30));

                // Focus the appropriate input field based on current mode, but with a slight delay
                Dispatcher.InvokeAsync(() => {
                    UserInput.Focus();
                }, DispatcherPriority.Background);

                AddMessage("Yok-AI", "Enhanced keyboard protection activated. All keystrokes are now hidden from keyloggers.");
            }
            else
            {
                // Reset visual indicators
                UserInput.Background = (SolidColorBrush)FindResource("BackgroundLightBrush");

                AddMessage("Yok-AI", "Enhanced keyboard protection deactivated. Basic protection still active.");
            }
        }
        
        // Set keyboard hook for all input protection
        private IntPtr SetHook(Utilities.NativeMethods.LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return Utilities.NativeMethods.SetWindowsHookEx(Utilities.NativeMethods.WH_KEYBOARD_LL, proc,
                    Utilities.NativeMethods.GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        // Enhanced unified keyboard hook callback
        private IntPtr UnifiedKeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                Key key = KeyInterop.KeyFromVirtualKey(vkCode);

                // Check if this is the Ctrl+Alt+K hotkey combination
                bool isCtrlAltK = (Keyboard.Modifiers & ModifierKeys.Control) != 0 &&
                                  (Keyboard.Modifiers & ModifierKeys.Alt) != 0 &&
                                  key == Key.K;

                // ALWAYS handle Ctrl+Alt+K toggle hotkey first, regardless of any other conditions
                if (isCtrlAltK && wParam.ToInt32() == Utilities.NativeMethods.WM_KEYDOWN)
                {
                    // Process the toggle directly
                    Dispatcher.BeginInvoke(new Action(() => {
                        ToggleKeyProtection();
                    }));

                    // Always intercept this keystroke
                    return (IntPtr)1;
                }

                // Handle other Ctrl+Alt hotkeys
                bool isCtrlAlt = (Keyboard.Modifiers & ModifierKeys.Control) != 0 &&
                                 (Keyboard.Modifiers & ModifierKeys.Alt) != 0;

                if (isCtrlAlt && appHotkeys.Contains(key) && key != Key.K) // Skip K as we handled it above
                {
                    // Let other hotkeys through to the WndProc handler
                    return Utilities.NativeMethods.CallNextHookEx(keyboardHookId, nCode, wParam, lParam);
                }

                // Enhanced protection mode for other keystrokes
                if (isEnhancedKeyProtectionActive)
                {
                    if (wParam.ToInt32() == Utilities.NativeMethods.WM_KEYDOWN)
                    {
                        // Check for Enter key in main input when protection is active
                        if (this.IsActive && UserInput.IsFocused && key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
                        {
                            // Handle Enter key to send message and clear focus
                            Dispatcher.BeginInvoke(new Action(() => {
                                SendMessageAndClearFocus();
                            }));

                            return (IntPtr)1;
                        }

                        // Only process normal keys if we have focus AND a text box is focused
                        if (this.IsActive && UserInput.IsFocused)
                        {
                            // Skip processing if it's our toggle hotkey
                            if (!isCtrlAltK)
                            {
                                Dispatcher.Invoke(() => {
                                    ProcessKeyForTextBox(key);
                                });
                                // Block the keystroke
                                return (IntPtr)1;
                            }
                        }
                    }
                }
            }

            // For all other cases, pass the keystroke normally
            return Utilities.NativeMethods.CallNextHookEx(keyboardHookId, nCode, wParam, lParam);
        }
        
        // Process keys directly for our active text box
        private void ProcessKeyForTextBox(Key key)
        {

            // Only proceed if main input has focus
            if (!UserInput.IsFocused) return;

            // Get the active textbox
            TextBox activeTextBox = UserInput;

            try
            {
                // Handle different key types
                if (key >= Key.A && key <= Key.Z)
                {
                    // Handle letters
                    bool isShift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                    char c = isShift ? (char)('A' + (key - Key.A)) : (char)('a' + (key - Key.A));
                    InsertTextAtCaret(activeTextBox, c.ToString());
                }
                else if (key >= Key.D0 && key <= Key.D9)
                {
                    // Handle number keys
                    if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                    {
                        // Shift+number keys for symbols
                        char[] shiftSymbols = { ')', '!', '@', '#', '$', '%', '^', '&', '*', '(' };
                        InsertTextAtCaret(activeTextBox, shiftSymbols[key - Key.D0].ToString());
                    }
                    else
                    {
                        // Regular number keys
                        InsertTextAtCaret(activeTextBox, (key - Key.D0).ToString());
                    }
                }
                else if (key >= Key.NumPad0 && key <= Key.NumPad9)
                {
                    // Handle numpad keys
                    InsertTextAtCaret(activeTextBox, (key - Key.NumPad0).ToString());
                }
                else
                {
                    // Handle special keys
                    switch (key)
                    {
                        case Key.Space:
                            InsertTextAtCaret(activeTextBox, " ");
                            break;
                        case Key.Back:
                            HandleBackspace(activeTextBox);
                            break;
                        case Key.Delete:
                            HandleDelete(activeTextBox);
                            break;
                        case Key.Left:
                            if (activeTextBox.CaretIndex > 0)
                                activeTextBox.CaretIndex--;
                            break;
                        case Key.Right:
                            if (activeTextBox.CaretIndex < activeTextBox.Text.Length)
                                activeTextBox.CaretIndex++;
                            break;


                        case Key.Enter:
                            // Special handling for Enter key (always UserInput now)
                            if (Keyboard.Modifiers == ModifierKeys.Control)
                            {
                                // Ctrl+Enter - insert newline
                                InsertTextAtCaret(activeTextBox, "\n");
                            }
                            else if (Keyboard.Modifiers == ModifierKeys.None)
                            {
                                // Plain Enter - send message and move focus out
                                SendMessageAndClearFocus();
                            }
                            else
                            {
                                // Other modifiers - just add a newline
                                InsertTextAtCaret(activeTextBox, "\n");
                            }
                            break;

                        case Key.OemPeriod:
                            InsertTextAtCaret(activeTextBox, (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? ">" : ".");
                            break;
                        case Key.OemComma:
                            InsertTextAtCaret(activeTextBox, (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? "<" : ",");
                            break;
                        case Key.OemSemicolon:
                            InsertTextAtCaret(activeTextBox, (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? ":" : ";");
                            break;
                        case Key.OemQuestion:
                            InsertTextAtCaret(activeTextBox, (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? "?" : "/");
                            break;
                            // Add handling for more special keys as needed
                    }
                }
            }
            catch
            {
                // Silent fail
            }
        }

        // Helper method to send message and clear focus
        private void SendMessageAndClearFocus()
        {
            // Send the message
            SendMessage();

            // Clear focus by focusing on a non-input element
            Dispatcher.InvokeAsync(() => {
                ChatScrollViewer.Focus(); // Move focus to chat scroll viewer to clear any textbox focus
            });
        }

        // Helper method to insert text at the current caret position of the specified textbox
        private void InsertTextAtCaret(TextBox textBox, string text)
        {
            int caretIndex = textBox.CaretIndex;
            textBox.Text = textBox.Text.Insert(caretIndex, text);
            textBox.CaretIndex = caretIndex + text.Length;
        }

        // Helper method to handle backspace key for the specified textbox
        private void HandleBackspace(TextBox textBox)
        {
            int caretIndex = textBox.CaretIndex;
            if (caretIndex > 0)
            {
                textBox.Text = textBox.Text.Remove(caretIndex - 1, 1);
                textBox.CaretIndex = caretIndex - 1;
            }
        }

        // Helper method to handle delete key for the specified textbox
        private void HandleDelete(TextBox textBox)
        {
            int caretIndex = textBox.CaretIndex;
            if (caretIndex < textBox.Text.Length)
            {
                textBox.Text = textBox.Text.Remove(caretIndex, 1);
                textBox.CaretIndex = caretIndex;
            }
        }

        #endregion

        #region Screen Capture
        private async void CaptureScreen()
        {
            // Unified screenshot capture - call the same method as button
            await CaptureScreenWithThumbnail();
        }

        private void AddScreenshotThumbnail(int screenshotNumber)
        {
            // Create a special bubble for screenshot thumbnails
            Border thumbnailBubble = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(50, 4, 8, 4),
                HorizontalAlignment = HorizontalAlignment.Right,
                MaxWidth = 300
            };

            StackPanel contentPanel = new StackPanel();

            // Add screenshot icon and text
            StackPanel thumbnailContent = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };

            TextBlock iconText = new TextBlock
            {
                Text = "📷",
                FontSize = 14,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            TextBlock screenshotText = new TextBlock
            {
                Text = $"Screenshot {screenshotNumber} captured",
                Foreground = new SolidColorBrush(Color.FromRgb(173, 216, 230)),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };

            thumbnailContent.Children.Add(iconText);
            thumbnailContent.Children.Add(screenshotText);
            contentPanel.Children.Add(thumbnailContent);

            thumbnailBubble.Child = contentPanel;

            // Add to chat container
            ChatBubbleContainer.Children.Add(thumbnailBubble);
            ScrollToBottom();
        }
        // Capture screen and convert to base64
        private string CaptureScreenToBase64()
        {
            try
            {
                var screenWidth = System.Windows.SystemParameters.PrimaryScreenWidth;
                var screenHeight = System.Windows.SystemParameters.PrimaryScreenHeight;

                using (var bitmap = new DrawingBitmap(
                    (int)screenWidth,
                    (int)screenHeight,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (var graphics = DrawingGraphics.FromImage(bitmap))
                    {
                        graphics.CopyFromScreen(
                            0, 0,
                            0, 0,
                            new DrawingSize((int)screenWidth, (int)screenHeight),
                            System.Drawing.CopyPixelOperation.SourceCopy);
                    }

                    // Convert to base64
                    using (MemoryStream ms = new MemoryStream())
                    {
                        bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        byte[] imageBytes = ms.ToArray();
                        return Convert.ToBase64String(imageBytes);
                    }
                }
            }
            catch (Exception ex)
            {
                AddMessage("Yok-AI", $"Error capturing screenshot: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Voice Capture

        // Initialize audio capture for system audio
        private void InitializeAudioCapture()
        {
            try
            {
                audioCapture = new WasapiLoopbackCapture();
                audioCapture.DataAvailable += OnAudioDataAvailable;
            }
            catch (Exception ex)
            {
                AddMessage("Yok-AI", $"Failed to initialize audio capture: {ex.Message}");
            }
        }
        
        #endregion


        #region Context Management

        // File operations for contexts
        private void SaveContextToFile(string name, string context)
        {
            try
            {
                string filePath = Path.Combine(AppDataPath, $"context_{name}.dat");

                // Ensure directory exists
                if (!Directory.Exists(AppDataPath))
                {
                    Directory.CreateDirectory(AppDataPath);
                }

                string encryptedContext = EncryptString(context);
                File.WriteAllText(filePath, encryptedContext);
            }
            catch
            {
                // Silent fail
            }
        }

        private string LoadContextFromFile(string name)
        {
            try
            {
                string filePath = Path.Combine(AppDataPath, $"context_{name}.dat");
                if (File.Exists(filePath))
                {
                    string encryptedContext = File.ReadAllText(filePath);
                    return DecryptString(encryptedContext);
                }
            }
            catch
            {
                // Silent fail
            }

            return null;
        }

        #endregion

        #region Utility Methods


        // Local data is encrypted with Windows DPAPI: only this Windows user on this PC can read it.
        private static readonly byte[] LocalEntropy = Encoding.UTF8.GetBytes("YokAI-local-v1");

        private string EncryptString(string plainText)
        {
            byte[] data = ProtectedData.Protect(Encoding.UTF8.GetBytes(plainText), LocalEntropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(data);
        }

        private string DecryptString(string cipherText)
        {
            byte[] data = ProtectedData.Unprotect(Convert.FromBase64String(cipherText), LocalEntropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }

        #endregion

        #region Message Display

        // Add a message to the chat history with bubble formatting
        private void AddMessage(string messageType, string message)
        {
            if (messageType == "Debug" && !aiSettings.ShowDebugMessages) return;
            Dispatcher.InvokeAsync(() =>
            {
                CreateMessageBubble(messageType, message, false);
            });
        }

        // Core method to create message bubbles
        private void CreateMessageBubble(string messageType, string message, bool isFormatted = false)
        {
            try
            {
                // Get bubble style and text color
                Style bubbleStyle = GetBubbleStyle(messageType);
                SolidColorBrush textColor = GetTextColor(messageType);

                // Create the bubble container
                Border bubble = new Border
                {
                    Style = bubbleStyle
                };

                // Create content container
                StackPanel contentPanel = new StackPanel();

                // Handle formatted content or regular text
                if (isFormatted)
                {
                    CreateFormattedContent(contentPanel, message, textColor, "");
                }
                else
                {
                    // Regular text content - use RichTextBox for text selection
                    RichTextBox messageText = CreateSelectableTextBox(message, textColor,
                        messageType == "debug" ? 8 : 12);

                    // Center align debug messages within the content panel
                    if (messageType.ToLower() == "debug")
                    {
                        messageText.HorizontalAlignment = HorizontalAlignment.Center;
                    }

                    contentPanel.Children.Add(messageText);
                }

                bubble.Child = contentPanel;

                // Add to chat container
                ChatBubbleContainer.Children.Add(bubble);

                // Auto-scroll to bottom
                ScrollToBottom();
            }
            catch (Exception ex)
            {
                // Fallback to simple text if bubble creation fails
                CreateSimpleFallbackBubble(messageType, message);
            }
        }

        // Helper method to determine bubble style
        private Style GetBubbleStyle(string messageType)
        {
            switch (messageType?.ToLower())
            {
                case "user":
                    return (Style)FindResource("UserBubbleStyle");
                case "ai":
                    return (Style)FindResource("AIBubbleStyle");
                case "debug":
                default:
                    return (Style)FindResource("DebugBubbleStyle");
            }
        }

        // Helper method to determine text color
        private SolidColorBrush GetTextColor(string messageType)
        {
            switch (messageType?.ToLower())
            {
                case "user":
                    return new SolidColorBrush(Colors.White);
                case "ai":
                    return new SolidColorBrush(Colors.White);
                case "debug":
                default:
                    return new SolidColorBrush(Color.FromRgb(150, 150, 150));
            }
        }

        // Helper method to scroll to bottom
        private void ScrollToBottom()
        {
            ChatScrollViewer.ScrollToEnd();
            ChatScrollViewer.UpdateLayout();
        }

        // Fallback method for error cases
        private void CreateSimpleFallbackBubble(string sender, string message)
        {
            try
            {
                Border fallbackBubble = new Border
                {
                    Background = (SolidColorBrush)FindResource("BackgroundLightBrush"),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(8, 4, 60, 4),
                    HorizontalAlignment = HorizontalAlignment.Left
                };

                string displayText = string.IsNullOrEmpty(sender) ? message : $"{sender}: {message}";
                RichTextBox fallbackText = CreateSelectableTextBox(displayText,
                    (SolidColorBrush)FindResource("TextPrimaryBrush"), 12);

                fallbackBubble.Child = fallbackText;
                ChatBubbleContainer.Children.Add(fallbackBubble);
                ScrollToBottom();
            }
            catch
            {
                // Ultimate fallback - do nothing to prevent crashes
            }
        }

        // Add a formatted message to the chat history with code highlighting
        private void AddFormattedMessage(string messageType, string message)
        {
            Dispatcher.InvokeAsync(() =>
            {
                CreateMessageBubble(messageType, message, true);
            });
        }

        // Blue function - for prompts going TO the AI
        private void AddPromptMessage(string promptText)
        {
            Dispatcher.InvokeAsync(() =>
            {
                // Create blue bubble (right-aligned, user style)
                Border bubble = new Border
                {
                    Style = (Style)FindResource("UserBubbleStyle")
                };

                RichTextBox messageText = CreateSelectableTextBox(promptText,
                    new SolidColorBrush(Colors.White), 12);
                bubble.Child = messageText;

                ChatBubbleContainer.Children.Add(bubble);
                ScrollToBottom();
            });
        }

        // Dark function - for responses coming FROM the AI  
        private void AddResponseMessage(string responseText)
        {
            Dispatcher.InvokeAsync(() =>
            {
                string messageId = "ai_" + Guid.NewGuid().ToString("N").Substring(0, 8);

                Border bubble = new Border
                {
                    Style = (Style)FindResource("AIBubbleStyle")
                };

                StackPanel contentPanel = new StackPanel();
                CreateFormattedContent(contentPanel, responseText, new SolidColorBrush(Colors.White), messageId);

                bubble.Child = contentPanel;
                ChatBubbleContainer.Children.Add(bubble);
                ScrollToBottom();
            });
        }

        // Helper method to extract code blocks from message
        private MarkdownContent ParseMarkdownContent(string message)
        {
            var content = new MarkdownContent();
            var parts = new List<MarkdownPart>();

            int pos = 0;

            while (pos < message.Length)
            {
                // Find code blocks first
                int codeStart = message.IndexOf("```", pos);

                if (codeStart >= 0)
                {
                    // Add text before code block
                    if (codeStart > pos)
                    {
                        string textBeforeCode = message.Substring(pos, codeStart - pos);
                        parts.AddRange(ParseTextMarkdown(textBeforeCode));
                    }

                    // Find code block end
                    int codeEnd = message.IndexOf("```", codeStart + 3);
                    if (codeEnd > codeStart)
                    {
                        string codeContent = message.Substring(codeStart + 3, codeEnd - codeStart - 3);

                        // Skip language specifier
                        int newlinePos = codeContent.IndexOf('\n');
                        if (newlinePos >= 0 && newlinePos < 20)
                        {
                            codeContent = codeContent.Substring(newlinePos + 1);
                        }

                        parts.Add(new MarkdownPart { Type = "code", Content = codeContent.Trim() });
                        pos = codeEnd + 3;
                    }
                    else
                    {
                        break;
                    }
                }
                else
                {
                    // No more code blocks, parse remaining text
                    string remainingText = message.Substring(pos);
                    parts.AddRange(ParseTextMarkdown(remainingText));
                    break;
                }
            }

            content.Parts = parts;
            return content;
        }

        private List<MarkdownPart> ParseTextMarkdown(string text)
        {
            var parts = new List<MarkdownPart>();
            var lines = text.Split('\n');

            foreach (string line in lines)
            {
                string trimmedLine = line.Trim();

                if (string.IsNullOrEmpty(trimmedLine))
                {
                    parts.Add(new MarkdownPart { Type = "paragraph", Content = "" });
                    continue;
                }

                // Headers
                if (trimmedLine.StartsWith("### "))
                {
                    parts.Add(new MarkdownPart { Type = "h3", Content = trimmedLine.Substring(4) });
                }
                else if (trimmedLine.StartsWith("## "))
                {
                    parts.Add(new MarkdownPart { Type = "h2", Content = trimmedLine.Substring(3) });
                }
                else if (trimmedLine.StartsWith("# "))
                {
                    parts.Add(new MarkdownPart { Type = "h1", Content = trimmedLine.Substring(2) });
                }
                // Lists
                else if (trimmedLine.StartsWith("- ") || trimmedLine.StartsWith("* "))
                {
                    parts.Add(new MarkdownPart { Type = "list", Content = trimmedLine.Substring(2) });
                }
                else if (Regex.IsMatch(trimmedLine, @"^\d+\.\s"))
                {
                    string listContent = Regex.Replace(trimmedLine, @"^\d+\.\s", "");
                    parts.Add(new MarkdownPart { Type = "numbered-list", Content = listContent });
                }
                // Regular paragraph with inline formatting
                else
                {
                    parts.Add(new MarkdownPart { Type = "paragraph", Content = trimmedLine });
                }
            }

            return parts;
        }
        // Helper method to create formatted content with code blocks
        private void CreateFormattedContent(StackPanel container, string message, SolidColorBrush textColor, string messageId = "")
        {
            var markdownContent = ParseMarkdownContent(message);

            foreach (var part in markdownContent.Parts)
            {
                switch (part.Type)
                {
                    case "h1":
                        container.Children.Add(CreateHeading(part.Content, 18, textColor));
                        break;
                    case "h2":
                        container.Children.Add(CreateHeading(part.Content, 16, textColor));
                        break;
                    case "h3":
                        container.Children.Add(CreateHeading(part.Content, 14, textColor));
                        break;
                    case "list":
                        container.Children.Add(CreateListItem(part.Content, textColor, "•"));
                        break;
                    case "numbered-list":
                        container.Children.Add(CreateListItem(part.Content, textColor, "1."));
                        break;
                    case "code":
                        container.Children.Add(CreateCodeBlock(part.Content));
                        break;
                    case "paragraph":
                        if (!string.IsNullOrEmpty(part.Content))
                        {
                            container.Children.Add(CreateFormattedParagraph(part.Content, textColor));
                        }
                        else
                        {
                            container.Children.Add(CreateSpacing());
                        }
                        break;
                }
            }

            // Add reply button for AI messages
            if (!string.IsNullOrEmpty(messageId) && !messageId.StartsWith("user_") && !messageId.StartsWith("debug_"))
            {
                container.Children.Add(CreateReplyButton(messageId, message));
            }

            // Fallback if no parts were created
            if (container.Children.Count == 0)
            {
                container.Children.Add(CreateSelectableTextBox(message, textColor, 12));
            }
        }

        private RichTextBox CreateHeading(string text, double fontSize, SolidColorBrush textColor)
        {
            RichTextBox richTextBox = new RichTextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                IsReadOnly = true,
                FontSize = fontSize,
                Foreground = textColor,
                Cursor = Cursors.Arrow,
                Margin = new Thickness(0, 8, 0, 4),
                Padding = new Thickness(0),
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            FlowDocument doc = new FlowDocument();
            doc.PagePadding = new Thickness(0);

            Paragraph para = new Paragraph();
            para.Margin = new Thickness(0);

            Run run = new Run(text);
            run.FontWeight = FontWeights.Bold;
            para.Inlines.Add(run);

            doc.Blocks.Add(para);
            richTextBox.Document = doc;

            return richTextBox;
        }

        private RichTextBox CreateListItem(string text, SolidColorBrush textColor, string bullet)
        {
            RichTextBox richTextBox = new RichTextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                IsReadOnly = true,
                FontSize = 12,
                Foreground = textColor,
                Cursor = Cursors.Arrow,
                Margin = new Thickness(16, 2, 0, 2),
                Padding = new Thickness(0),
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            FlowDocument doc = new FlowDocument();
            doc.PagePadding = new Thickness(0);

            Paragraph para = new Paragraph();
            para.Margin = new Thickness(0);

            // Add bullet
            // Add bullet
            Run bulletRun = new Run(bullet == "1." ? "• " : bullet + " ");
            bulletRun.Foreground = textColor;
            para.Inlines.Add(bulletRun);

            // Add formatted text
            para.Inlines.AddRange(ParseInlineFormatting(text, textColor));

            doc.Blocks.Add(para);
            richTextBox.Document = doc;

            return richTextBox;
        }

        private RichTextBox CreateFormattedParagraph(string text, SolidColorBrush textColor)
        {
            RichTextBox richTextBox = new RichTextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                IsReadOnly = true,
                FontSize = 12,
                Foreground = textColor,
                Cursor = Cursors.Arrow,
                Margin = new Thickness(0, 2, 0, 6),
                Padding = new Thickness(0),
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            FlowDocument doc = new FlowDocument();
            doc.PagePadding = new Thickness(0);

            Paragraph para = new Paragraph();
            para.Margin = new Thickness(0);
            para.Inlines.AddRange(ParseInlineFormatting(text, textColor));

            doc.Blocks.Add(para);
            richTextBox.Document = doc;

            return richTextBox;
        }

        private Border CreateCodeBlock(string code)
        {
            Border codeContainer = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 4, 0, 8)
            };

            RichTextBox codeText = CreateSelectableTextBox(code,
                new SolidColorBrush(Color.FromRgb(0, 191, 255)), 11);
            codeText.FontFamily = new FontFamily("Consolas");
            codeText.Margin = new Thickness(0);

            codeContainer.Child = codeText;
            return codeContainer;
        }

        private UIElement CreateSpacing()
        {
            return new Border { Height = 8 };
        }

        private Border CreateReplyButton(string messageId, string messageContent)
        {
            // Container for both buttons
            Border buttonContainer = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -8, -8, 0),
                Background = Brushes.Transparent,
                Width = 48, // Increased width for two buttons
                Height = 24
            };

            // Container for the two buttons side by side
            StackPanel buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            // The reply button
            Button replyButton = new Button
            {
                Width = 20,
                Height = 20,
                Background = new SolidColorBrush(Color.FromArgb(0, 255, 255, 255)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Content = "↩️",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.7,
                Margin = new Thickness(0, 0, 2, 0)
            };

            // The focus button
            Button focusButton = new Button
            {
                Width = 20,
                Height = 20,
                Background = new SolidColorBrush(Color.FromArgb(0, 255, 255, 255)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Content = "📌",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.7
            };

            // Store message content for reply functionality
            if (!messageContentMap.ContainsKey(messageId))
            {
                string cleanContent = CleanMessageForReply(messageContent);
                messageContentMap[messageId] = cleanContent;
            }

            // Set button commands
            replyButton.Tag = messageId;
            replyButton.Click += ReplyButton_Click;

            focusButton.Tag = messageId;
            focusButton.Click += FocusButton_Click;

            // Hover effects for reply button
            replyButton.MouseEnter += (s, e) =>
            {
                replyButton.Opacity = 1;
                replyButton.Background = new SolidColorBrush(Color.FromArgb(180, 60, 60, 60));
            };

            replyButton.MouseLeave += (s, e) =>
            {
                replyButton.Opacity = 0.4;
                replyButton.Background = new SolidColorBrush(Color.FromArgb(100, 80, 80, 80));
            };

            // Hover effects for focus button
            focusButton.MouseEnter += (s, e) =>
            {
                focusButton.Opacity = 1;
                focusButton.Background = new SolidColorBrush(Color.FromArgb(180, 60, 60, 60));
            };

            focusButton.MouseLeave += (s, e) =>
            {
                focusButton.Opacity = 0.4;
                focusButton.Background = new SolidColorBrush(Color.FromArgb(100, 80, 80, 80));
            };

            // Show buttons on container hover
            buttonContainer.MouseEnter += (s, e) =>
            {
                replyButton.Opacity = 0.8;
                focusButton.Opacity = 0.8;
                replyButton.Background = new SolidColorBrush(Color.FromArgb(180, 60, 60, 60));
                focusButton.Background = new SolidColorBrush(Color.FromArgb(180, 60, 60, 60));
            };

            buttonContainer.MouseLeave += (s, e) =>
            {
                replyButton.Opacity = 0.7;
                focusButton.Opacity = 0.7;
            };

            // Add buttons to panel
            buttonPanel.Children.Add(replyButton);
            buttonPanel.Children.Add(focusButton);

            buttonContainer.Child = buttonPanel;
            return buttonContainer;
        }

        private string CleanMessageForReply(string content)
        {
            // Remove excessive whitespace and line breaks
            string cleaned = Regex.Replace(content, @"\s+", " ").Trim();

            // Truncate if too long
            if (cleaned.Length > 150)
            {
                cleaned = cleaned.Substring(0, 147) + "...";
            }

            return cleaned;
        }

        private List<Inline> ParseInlineFormatting(string text, SolidColorBrush textColor)
        {
            var inlines = new List<Inline>();
            int pos = 0;

            while (pos < text.Length)
            {
                // Find bold **text**
                int boldStart = text.IndexOf("**", pos);
                if (boldStart >= 0 && boldStart < text.Length - 2)
                {
                    int boldEnd = text.IndexOf("**", boldStart + 2);
                    if (boldEnd > boldStart)
                    {
                        // Add text before bold
                        if (boldStart > pos)
                        {
                            inlines.Add(new Run(text.Substring(pos, boldStart - pos)));
                        }

                        // Add bold text
                        Run boldRun = new Run(text.Substring(boldStart + 2, boldEnd - boldStart - 2));
                        boldRun.FontWeight = FontWeights.Bold;
                        inlines.Add(boldRun);

                        pos = boldEnd + 2;
                        continue;
                    }
                }

                // Find italic *text*
                int italicStart = text.IndexOf("*", pos);
                if (italicStart >= 0 && italicStart < text.Length - 1 && italicStart != boldStart)
                {
                    int italicEnd = text.IndexOf("*", italicStart + 1);
                    if (italicEnd > italicStart)
                    {
                        // Add text before italic
                        if (italicStart > pos)
                        {
                            inlines.Add(new Run(text.Substring(pos, italicStart - pos)));
                        }

                        // Add italic text
                        Run italicRun = new Run(text.Substring(italicStart + 1, italicEnd - italicStart - 1));
                        italicRun.FontStyle = FontStyles.Italic;
                        inlines.Add(italicRun);

                        pos = italicEnd + 1;
                        continue;
                    }
                }

                // Find inline code `text`
                int codeStart = text.IndexOf("`", pos);
                if (codeStart >= 0 && codeStart < text.Length - 1)
                {
                    int codeEnd = text.IndexOf("`", codeStart + 1);
                    if (codeEnd > codeStart)
                    {
                        // Add text before code
                        if (codeStart > pos)
                        {
                            inlines.Add(new Run(text.Substring(pos, codeStart - pos)));
                        }

                        // Add inline code
                        Run codeRun = new Run(text.Substring(codeStart + 1, codeEnd - codeStart - 1));
                        codeRun.Background = new SolidColorBrush(Color.FromRgb(40, 40, 40));
                        codeRun.FontFamily = new FontFamily("Consolas");
                        codeRun.Foreground = new SolidColorBrush(Color.FromRgb(0, 191, 255));
                        inlines.Add(codeRun);

                        pos = codeEnd + 1;
                        continue;
                    }
                }

                // No more formatting found, add remaining text
                inlines.Add(new Run(text.Substring(pos)));
                break;
            }

            // Fallback if no inlines were created
            if (inlines.Count == 0)
            {
                inlines.Add(new Run(text));
            }

            return inlines;
        }

        private void EnterScreenshotMode()
        {
            try
            {
                // Show thumbnail panel
                ThumbnailPreviewArea.Visibility = Visibility.Visible;

                // Change input placeholder
                UserInput.Text = defaultScreenshotPrompt;

                // Update placeholder (you might need to use a watermark technique)
                // For now, we'll just ensure the default prompt is there
                UserInput.Focus();
                UserInput.CaretIndex = UserInput.Text.Length;

                // Set flag
                isScreenshotModeActive = true;

                AddMessage("Yok-AI", $"Screenshot mode active ({screenshotBuffer.Count}/{maxScreenshots})");
            }
            catch (Exception ex)
            {
                AddMessage("Debug", $"Enter screenshot mode error: {ex.Message}");
            }
        }

        private void ExitScreenshotMode()
        {
            try
            {
                // Clear screenshots
                screenshotBuffer.Clear();
                ClearAllThumbnails();

                // Hide thumbnail panel
                ThumbnailPreviewArea.Visibility = Visibility.Collapsed;

                // Reset input
                UserInput.Clear();

                // Clear flag
                isScreenshotModeActive = false;

                AddMessage("Yok-AI", "Screenshot mode exited.");
            }
            catch (Exception ex)
            {
                AddMessage("Debug", $"Exit screenshot mode error: {ex.Message}");
            }
        }

        private bool IsInScreenshotMode()
        {
            return screenshotBuffer.Count > 0;
        }

        // Send a message to the AI
        private async void SendMessage()
        {
            if (!isLoggedIn)
            {
                AddMessage("Yok-AI", "Please log in to use Yok-AI.");
                return;
            }

            // Check if we're in screenshot mode
            if (IsInScreenshotMode())
            {
                await ProcessScreenshotMessage();
                return;
            }

            // Normal text message processing
            if (string.IsNullOrWhiteSpace(UserInput.Text))
                return;

            // Check usage limits first
            if (!CheckCredits())
                return;

            string userMessage = UserInput.Text.Trim();

            // If in reply mode, add context to hidden chat history
            if (isInReplyMode && currentReplyContext != null)
            {
                string contextualMessage = $"[Replying to: \"{currentReplyContext.OriginalContent}\"]\n\n{userMessage}";

                // Exit reply mode after sending
                ExitReplyMode();
            }
            else
            {
            }

            // Show in UI
            AddPromptMessage(userMessage);
            UserInput.Clear();

            // Show typing indicator
            AddResponseMessage("Thinking...");

            try
            {
                // Get response with memory context
                string response = await SendMessageToAPI(userMessage, "text");

                // Remove the "thinking" message
                RemoveThinkingMessage();

                // Add the formatted response
                AddResponseMessage(response);
            }
            catch (Exception ex)
            {
                // Remove the "thinking" message and add error message
                RemoveThinkingMessage();
                AddMessage("Yok-AI", "Please check your internet connection.");
            }
        }

        private async Task ProcessScreenshotMessage()
        {
            try
            {
                if (screenshotBuffer.Count == 0)
                {
                    AddMessage("Yok-AI", "No screenshots to analyze.");
                    return;
                }

                // Check usage limits
                if (!CheckCredits())
                    return;

                // Get custom prompt from main input
                string customPrompt = UserInput.Text.Trim();

                // Create a copy of screenshots for processing BEFORE clearing
                List<string> screenshotsCopy = new List<string>(screenshotBuffer);

                // Build final prompt: [custom prompt] + [default prompt]
                string finalPrompt = string.IsNullOrWhiteSpace(customPrompt)
                    ? defaultScreenshotPrompt
                    : customPrompt + "\n\n" + defaultScreenshotPrompt;

                // Show in UI
                AddPromptMessage($"📷 Analyzing {screenshotsCopy.Count} screenshot(s): {customPrompt}");

                // **EXIT SCREENSHOT MODE IMMEDIATELY** ← This is the key change
                ExitScreenshotMode();

                // Show typing indicator
                AddResponseMessage("Thinking...");

                // Process screenshots using unified method
                string aiResponse = await SendMessageToAPI(finalPrompt, "image", screenshotsCopy);

                if (!string.IsNullOrEmpty(aiResponse))
                {
                    // Remove thinking message and show response
                    RemoveThinkingMessage();
                    AddResponseMessage(aiResponse);
                }
            }
            catch (Exception ex)
            {
                RemoveThinkingMessage();
                AddMessage("Yok-AI", $"Error processing screenshots: {ex.Message}");
            }
        }

        private void RemoveThinkingMessage()
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    // Find and remove the last "Thinking..." bubble
                    for (int i = ChatBubbleContainer.Children.Count - 1; i >= 0; i--)
                    {
                        if (ChatBubbleContainer.Children[i] is Border bubble)
                        {
                            if (FindTextInBubble(bubble, "Thinking..."))
                            {
                                ChatBubbleContainer.Children.RemoveAt(i);
                                return;
                            }
                        }
                    }
                }
                catch
                {
                    // Silent fail
                }
            });
        }

        // Helper method to find text within a bubble
        private bool FindTextInBubble(Border bubble, string searchText)
        {
            return FindTextInUIElement(bubble, searchText);
        }

        // Recursive helper to search for text in UI elements
        private bool FindTextInUIElement(DependencyObject element, string searchText)
        {
            if (element is TextBlock textBlock)
            {
                return textBlock.Text?.Contains(searchText) == true;
            }

            // ADD THIS: Check RichTextBox content
            if (element is RichTextBox richTextBox)
            {
                try
                {
                    TextRange textRange = new TextRange(
                        richTextBox.Document.ContentStart,
                        richTextBox.Document.ContentEnd);
                    return textRange.Text?.Contains(searchText) == true;
                }
                catch
                {
                    return false;
                }
            }

            // Search children
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(element, i);
                if (FindTextInUIElement(child, searchText))
                    return true;
            }

            // Also check logical children for panels
            if (element is Panel panel)
            {
                foreach (UIElement child in panel.Children)
                {
                    if (FindTextInUIElement(child, searchText))
                        return true;
                }
            }

            return false;
        }
        #endregion

        // Add these missing methods to your MainWindow class

        private void StartHybridAudioProcessing()
        {
            try
            {
                // Start the audio capture for hybrid processing
                StartAudioForwarding();
                AddMessage("Debug", "Hybrid audio processing started");
            }
            catch (Exception ex)
            {
                AddMessage("Debug", $"Failed to start hybrid audio processing: {ex.Message}");
            }
        }

        private float CalculateRMSLevel(byte[] buffer, int bytesRecorded)
        {
            try
            {
                double sum = 0;
                int sampleCount = 0;

                for (int i = 0; i < bytesRecorded; i += 2)
                {
                    if (i + 1 < bytesRecorded)
                    {
                        // Convert two bytes to 16-bit sample
                        short sample = (short)(buffer[i] | (buffer[i + 1] << 8));
                        sum += (sample * sample);
                        sampleCount++;
                    }
                }

                if (sampleCount == 0) return 0;

                // Calculate RMS
                double rms = Math.Sqrt(sum / sampleCount);
                return (float)(rms / short.MaxValue);
            }
            catch
            {
                return 0; // Default to no audio on error
            }
        }

        private void RemoveLastMessage(string senderName)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    // Find and remove the last message from the specified sender
                    for (int i = ChatBubbleContainer.Children.Count - 1; i >= 0; i--)
                    {
                        if (ChatBubbleContainer.Children[i] is Border bubble)
                        {
                            if (FindTextInBubble(bubble, senderName))
                            {
                                ChatBubbleContainer.Children.RemoveAt(i);
                                return;
                            }
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                AddMessage("Debug", $"Error removing message: {ex.Message}");
            }
        }

        // Unified method to send all types of messages to the API
        private async Task<string> SendMessageToAPI(string message, string requestType, List<string> images = null, string audioBase64 = null, string language = "en")
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(60);

                    // STEP 1: ALWAYS store the user message first (regardless of memory setting)
                    StoreChatMessage("User", message, images, requestType);

                    // STEP 2: Prepare the message with or without chat history based on memory setting
                    string finalMessage = message;

                    if (isChatMemoryEnabled && chatHistory.Count > 1 && requestType == "text")
                    {
                        // Build conversation history (exclude the current message we just added)
                        StringBuilder conversationBuilder = new StringBuilder();
                        conversationBuilder.AppendLine("=== Previous Conversation ===");

                        // Get all messages EXCEPT the current one (chatHistory.Count - 1)
                        for (int i = 0; i < chatHistory.Count - 1; i++)
                        {
                            var chatMsg = chatHistory[i];
                            string role = chatMsg.MessageType == "User" ? "User" : "Assistant";

                            // Include image info in context
                            if (chatMsg.Images.Count > 0)
                            {
                                conversationBuilder.AppendLine($"{role}: {chatMsg.Content} [with {chatMsg.Images.Count} image(s)]");
                            }
                            else
                            {
                                conversationBuilder.AppendLine($"{role}: {chatMsg.Content}");
                            }
                            conversationBuilder.AppendLine();
                        }

                        conversationBuilder.AppendLine("=== Current Message ===");
                        conversationBuilder.Append(message);

                        finalMessage = conversationBuilder.ToString();
                    }

                    // STEP 3: Ask the AI the user picked (their own key or their own Claude Code)
                    var ai = new AiClient(aiSettings);
                    string aiResponse = await ai.AskAsync(finalMessage, requestType == "image" ? images : null);

                    // STEP 4: ALWAYS store AI response (regardless of memory setting)
                    StoreChatMessage("AI", aiResponse, null, requestType);

                    return aiResponse;
                }
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        // Helper classes for audio processing
        public class AudioChunk
        {
            public byte[] Data { get; set; }
            public DateTime Timestamp { get; set; }
            public float RMSLevel { get; set; }
            public bool IsSpeech { get; set; }
            public int SampleRate { get; set; }

            public AudioChunk(byte[] data, float rmsLevel, bool isSpeech)
            {
                Data = new byte[data.Length];
                Array.Copy(data, Data, data.Length);
                Timestamp = DateTime.Now;
                RMSLevel = rmsLevel;
                IsSpeech = isSpeech;
                SampleRate = 16000;
            }
        }

        public class MovingAverage
        {
            private Queue<double> values = new Queue<double>();
            private int maxSize;
            private double sum = 0;

            public MovingAverage(int size)
            {
                maxSize = size;
            }

            public void Add(double value)
            {
                values.Enqueue(value);
                sum += value;

                while (values.Count > maxSize)
                {
                    sum -= values.Dequeue();
                }
            }

            public double Average => values.Count > 0 ? sum / values.Count : 0;
            public int Count => values.Count;
        }

        public class CircularBuffer<T>
        {
            private T[] buffer;
            private int head = 0;
            private int tail = 0;
            private int count = 0;
            private readonly object lockObj = new object();

            public CircularBuffer(int capacity)
            {
                buffer = new T[capacity];
            }

            public void Add(T item)
            {
                lock (lockObj)
                {
                    buffer[head] = item;
                    head = (head + 1) % buffer.Length;

                    if (count < buffer.Length)
                    {
                        count++;
                    }
                    else
                    {
                        tail = (tail + 1) % buffer.Length;
                    }
                }
            }

            public T[] GetAll()
            {
                lock (lockObj)
                {
                    T[] result = new T[count];
                    for (int i = 0; i < count; i++)
                    {
                        result[i] = buffer[(tail + i) % buffer.Length];
                    }
                    return result;
                }
            }

            public void Clear()
            {
                lock (lockObj)
                {
                    head = tail = count = 0;
                }
            }
        }

        private string FormatBilledTime(TimeSpan billedTime)
        {
            if (billedTime.TotalHours >= 1)
            {
                return $"{(int)billedTime.TotalHours}h {billedTime.Minutes}m {billedTime.Seconds}s";
            }
            else if (billedTime.TotalMinutes >= 1)
            {
                return $"{billedTime.Minutes}m {billedTime.Seconds}s";
            }
            else
            {
                return $"{billedTime.Seconds}s";
            }
        }

        private string FormatSessionDuration()
        {
            if (isRealtimeTranscriptionActive && sessionStartTime != DateTime.MinValue)
            {
                TimeSpan sessionDuration = DateTime.Now - sessionStartTime;
                return FormatTimeSpan(sessionDuration);
            }
            else if (sessionStartTime != DateTime.MinValue)
            {
                // For completed sessions, calculate from start to when it was stopped
                TimeSpan sessionDuration = DateTime.Now - sessionStartTime;
                return FormatTimeSpan(sessionDuration);
            }
            return "00:00:00";
        }

        private string FormatTimeSpan(TimeSpan timeSpan)
        {
            return $"{timeSpan.Hours:D2}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
        }

        private void InitializeTextSelectionFeature()
        {
            try
            {
                // Initialize debounce timer for selection
                selectionDelayTimer = new DispatcherTimer();
                selectionDelayTimer.Interval = TimeSpan.FromMilliseconds(300); // 300ms delay
                selectionDelayTimer.Tick += SelectionDelayTimer_Tick;

                // Attach selection event
                RealtimeTranscriptionDisplay.SelectionChanged += TranscriptionDisplay_SelectionChanged;

                // Monitor chat input changes to detect manual edits
                UserInput.TextChanged += UserInput_TextChanged;
                
                //Listen for Enter key on RichTextBox
                RealtimeTranscriptionDisplay.PreviewKeyDown += TranscriptionDisplay_KeyDown;
                AddMessage("Debug", "Perfect text selection initialized");
            }
            catch (Exception ex)
            {
                AddMessage("Debug", $"Error initializing selection: {ex.Message}");
            }
        }

        private void TranscriptionDisplay_SelectionChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                // Stop and restart the timer on every selection change
                selectionDelayTimer.Stop();
                selectionDelayTimer.Start();
            }
            catch (Exception ex)
            {
                AddMessage("Debug", $"Selection change error: {ex.Message}");
            }
        }

        private void SelectionDelayTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                // Stop the timer
                selectionDelayTimer.Stop();

                // Get current selection after delay
                string currentSelection = GetCleanSelectedText();
                currentSelectedText = currentSelection;
                if (currentSelection.Length > 5) // Minimum meaningful text
                {
                    // Clear any previous auto-filled text first
                    if (isAutoFilled && !string.IsNullOrEmpty(lastAutoFilledText))
                    {
                        string currentText = UserInput.Text;
                        if (currentText.Contains(lastAutoFilledText))
                        {
                            UserInput.Text = currentText.Replace(lastAutoFilledText, "").Trim();
                        }
                    }

                    // Create new prompt using default selection prompt
                    string newPrompt = $"\"{currentSelection}\": {defaultSelectionPrompt}";
                    
                    // Add to chat input
                    string existingText = UserInput.Text.Trim();
                    if (string.IsNullOrEmpty(existingText))
                    {
                        UserInput.Text = newPrompt;
                    }
                    else
                    {
                        UserInput.Text = existingText + " " + newPrompt;
                    }

                    // Store auto-filled text for future reference
                    lastAutoFilledText = newPrompt;
                    isAutoFilled = true;

                    // Focus and position cursor
                    //UserInput.Focus();
                    //UserInput.CaretIndex = UserInput.Text.Length;

                    // Visual feedback
                    AddMessage("Yok-AI", $"✓ Selected text added to chat input ({currentSelection.Length} characters)");
                }
                else if (currentSelection.Length == 0 && isAutoFilled)
                {
                    // Clear selection when deselected
                    currentSelectedText = "";

                    // Selection cleared - remove auto-filled text
                    ClearAutoFilledText();
                    AddMessage("Yok-AI", "Selection cleared");
                }
            }
            catch (Exception ex)
            {
                AddMessage("Debug", $"Timer tick error: {ex.Message}");
            }
        }

        private async void TranscriptionDisplay_KeyDown(object sender, KeyEventArgs e)
        {
            // In TranscriptionDisplay_KeyDown method, replace the try block after the prompt creation:
            try
            {
                // Create prompt for selected text using default selection prompt
                string prompt = $"\"{currentSelectedText}\": {defaultSelectionPrompt}";

                // Check login and credits
                if (!isLoggedIn)
                {
                    AddMessage("Yok-AI", "Please log in to use Yok-AI.");
                    return;
                }

                if (!CheckCredits())
                    return;

                // Add user message to chat
                AddPromptMessage(prompt);
                AddResponseMessage("Thinking...");
                // Clear the chat input box
                UserInput.Clear();

                try
                {
                    // Send to API
                    string response = await SendMessageToAPI(prompt, "text");

                    // Remove thinking message and add response
                    RemoveThinkingMessage();
                    AddResponseMessage(response);

                    // Clear the selection after successful send
                    RealtimeTranscriptionDisplay.Selection.Select(
                        RealtimeTranscriptionDisplay.Document.ContentStart,
                        RealtimeTranscriptionDisplay.Document.ContentStart);
                    currentSelectedText = "";

                    // Visual feedback
                    AddMessage("Yok-AI", "✓ Selected text analyzed via Enter key");
                }
                catch (Exception apiEx)
                {
                    RemoveThinkingMessage();
                    AddMessage("Yok-AI", "Analysis failed. Please try again.");
                    AddMessage("Debug", $"API error: {apiEx.Message}");
                }
            }
            catch (Exception ex)
            {
                AddMessage("Debug", $"KeyDown error: {ex.Message}");
            }
        }

        private void UserInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                // If user manually edits the input, clear auto-fill tracking
                if (isAutoFilled)
                {
                    // Check if the auto-filled text is still present
                    string currentText = UserInput.Text;
                    if (!currentText.Contains(lastAutoFilledText))
                    {
                        // User has manually edited/removed auto-filled text
                        ResetAutoFillState();
                    }
                }
            }
            catch (Exception ex)
            {
                AddMessage("Debug", $"Input change error: {ex.Message}");
            }
        }

        private void ClearAutoFilledText()
        {
            try
            {
                if (isAutoFilled && !string.IsNullOrEmpty(lastAutoFilledText))
                {
                    string currentText = UserInput.Text;
                    if (currentText.Contains(lastAutoFilledText))
                    {
                        UserInput.Text = currentText.Replace(lastAutoFilledText, "").Trim();
                    }
                    ResetAutoFillState();
                }
            }
            catch (Exception ex)
            {
                AddMessage("Debug", $"Clear auto-fill error: {ex.Message}");
            }
        }

        private void ResetAutoFillState()
        {
            lastAutoFilledText = "";
            isAutoFilled = false;
            currentSelectedText = "";
        }

        private string GetCleanSelectedText()
        {
            try
            {
                TextSelection selection = RealtimeTranscriptionDisplay.Selection;
                if (selection.IsEmpty)
                    return "";

                // Get the raw selected text
                string selectedText = selection.Text;

                if (string.IsNullOrEmpty(selectedText))
                    return "";

                // Remove timestamp patterns [HH:mm:ss] using regex
                // This pattern matches: [12:34:56] followed by optional space
                string cleanText = System.Text.RegularExpressions.Regex.Replace(
                    selectedText,
                    @"\[\d{2}:\d{2}:\d{2}\]\s*",
                    "",
                    System.Text.RegularExpressions.RegexOptions.Multiline
                );

                // Clean up multiple spaces and newlines
                cleanText = System.Text.RegularExpressions.Regex.Replace(cleanText, @"\s+", " ");

                return cleanText.Trim();
            }
            catch (Exception ex)
            {
                // Fallback to original selection if regex fails
                AddMessage("Debug", $"Clean selection error: {ex.Message}");
                return RealtimeTranscriptionDisplay.Selection.Text?.Trim() ?? "";
            }
        }

        // Add this new helper class for speaker detection
        public class AudioSegment
        {
            public DateTime Timestamp { get; set; }
            public float AudioLevel { get; set; }
            public bool IsSilence { get; set; }
            public string TranscriptionText { get; set; }

            public AudioSegment(float audioLevel, bool isSilence, string text = "")
            {
                Timestamp = DateTime.Now;
                AudioLevel = audioLevel;
                IsSilence = isSilence;
                TranscriptionText = text ?? "";
            }
        }

        // Helper method to create selectable text using RichTextBox
        private RichTextBox CreateSelectableTextBox(string text, SolidColorBrush textColor, double fontSize = 12)
        {
            RichTextBox richTextBox = new RichTextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                IsReadOnly = true,
                FontSize = fontSize,
                Foreground = textColor,
                Cursor = Cursors.Arrow,
                Margin = new Thickness(0),
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = fontSize <= 9 ? HorizontalAlignment.Center : HorizontalAlignment.Stretch,
                IsDocumentEnabled = true,
                AcceptsReturn = false,
                AcceptsTab = false,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            // Create FlowDocument with the text
            FlowDocument doc = new FlowDocument();

            // Set document properties for text wrapping
            doc.PagePadding = new Thickness(0);
            doc.FontSize = fontSize;
            doc.PageWidth = double.NaN; // This allows the document to use available width
            doc.ColumnWidth = double.PositiveInfinity; // This prevents column layout

            Paragraph para = new Paragraph(new Run(text))
            {
                Margin = new Thickness(0),
                Padding = new Thickness(0),
                LineHeight = 16,
                TextAlignment = fontSize <= 9 ? TextAlignment.Center : TextAlignment.Left
            };

            doc.Blocks.Add(para);
            richTextBox.Document = doc;

            // Disable context menu to make it cleaner
            richTextBox.ContextMenu = null;

            return richTextBox;
        }

        private void MemoryToggleButton_Click(object sender, RoutedEventArgs e)
        {
            isChatMemoryEnabled = !isChatMemoryEnabled;

            // Update visual state only
            if (isChatMemoryEnabled)
            {
                MemoryToggleButton.Foreground = (SolidColorBrush)FindResource("AccentBlueBrush");
                MemoryToggleButton.ToolTip = "Chat Memory: ON";
            }
            else
            {
                MemoryToggleButton.Foreground = (SolidColorBrush)FindResource("TextSecondaryBrush");
                MemoryToggleButton.ToolTip = "Chat Memory: OFF";
            }
        }

        private void ReplyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Button button = sender as Button;
                string messageId = button?.Tag as string;

                if (string.IsNullOrEmpty(messageId) || !messageContentMap.ContainsKey(messageId))
                    return;

                string originalContent = messageContentMap[messageId];

                // Enter reply mode
                EnterReplyMode(messageId, originalContent);
            }
            catch (Exception ex)
            {
                AddMessage("Debug", $"Reply button error: {ex.Message}");
            }
        }

        private void FocusButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Button button = sender as Button;
                string messageId = button?.Tag as string;

                if (string.IsNullOrEmpty(messageId))
                    return;

                // Find the message bubble that contains this button
                Border targetBubble = FindMessageBubbleByButton(button);

                if (targetBubble != null)
                {
                    if (!isFocusModeActive)
                    {
                        EnterFocusMode(targetBubble);
                    }
                    else
                    {
                        ExitFocusMode();
                    }
                }
            }
            catch (Exception ex)
            {
                AddMessage("Debug", $"Focus button error: {ex.Message}");
            }
        }

        private void EnterReplyMode(string messageId, string originalContent)
        {
            try
            {
                // Set reply state
                isInReplyMode = true;
                replyMessageId = messageId;
                currentReplyContext = new ReplyContext(messageId, originalContent, "AI");

                // Show reply context bar
                ReplyContextBar.Visibility = Visibility.Visible;

                // Set preview text
                ReplyPreviewText.Text = originalContent.Length > 80
                    ? originalContent.Substring(0, 77) + "..."
                    : originalContent;

                // Format the input with quoted text
                string quotedText = FormatQuotedReply(originalContent);
                UserInput.Text = quotedText;

                // Position cursor at the end
                UserInput.Focus();
                UserInput.CaretIndex = UserInput.Text.Length;

                // Visual feedback
                UserInput.BorderBrush = (SolidColorBrush)FindResource("AccentBlueBrush");
                UserInput.BorderThickness = new Thickness(2);
            }
            catch (Exception ex)
            {
                AddMessage("Debug", $"Enter reply mode error: {ex.Message}");
            }
        }

        private string FormatQuotedReply(string originalContent)
        {
            // Create a clean quoted format
            string[] lines = originalContent.Split('\n');
            StringBuilder quotedBuilder = new StringBuilder();

            foreach (string line in lines)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    quotedBuilder.AppendLine("> " + line.Trim());
                }
            }

            quotedBuilder.AppendLine(); // Add blank line
            quotedBuilder.AppendLine(); // Position cursor here

            return quotedBuilder.ToString();
        }

        private void CloseReplyButton_Click(object sender, RoutedEventArgs e)
        {
            ExitReplyMode();
        }

        private void ExitReplyMode()
        {
            try
            {
                // Reset state
                isInReplyMode = false;
                replyMessageId = "";
                currentReplyContext = null;

                // Hide context bar
                ReplyContextBar.Visibility = Visibility.Collapsed;

                // Reset input styling
                UserInput.BorderBrush = (SolidColorBrush)FindResource("BorderDarkBrush");
                UserInput.BorderThickness = new Thickness(1);

                // Clear input (optional - you might want to keep non-quoted text)
                UserInput.Clear();

                // Reset preview
                ReplyPreviewText.Text = "";
            }
            catch (Exception ex)
            {
                AddMessage("Debug", $"Exit reply mode error: {ex.Message}");
            }
        }

        // Helper method to store chat messages (hidden from UI)
        private void StoreChatMessage(string messageType, string content, List<string> images = null, string requestType = "text")
        {
            chatHistory.Add(new ChatMessage(messageType, content, images, requestType));
        }

        private Border FindMessageBubbleByButton(Button button)
        {
            try
            {
                // Walk up the visual tree to find the message bubble
                DependencyObject current = button;

                while (current != null)
                {
                    if (current is Border border)
                    {
                        // Check if this border has the bubble styles
                        if (border.Style == (Style)FindResource("AIBubbleStyle") ||
                            border.Style == (Style)FindResource("UserBubbleStyle"))
                        {
                            return border;
                        }
                    }
                    current = VisualTreeHelper.GetParent(current);
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private void EnterFocusMode(Border targetBubble)
        {
            try
            {
                isFocusModeActive = true;
                focusedMessageBubble = targetBubble;
                hiddenElements.Clear();

                // Store original window properties
                originalMainBorderMargin = MainBorder.Margin;
                originalWindowWidth = this.Width;
                originalWindowHeight = this.Height;

                // Hide all main UI elements from the Grid
                Grid mainGrid = (Grid)MainBorder.Child;

                for (int i = 0; i < mainGrid.Children.Count; i++)
                {
                    UIElement child = mainGrid.Children[i];
                    if (child.Visibility == Visibility.Visible)
                    {
                        hiddenElements.Add(child);
                        child.Visibility = Visibility.Collapsed;
                    }
                }

                // Hide all other chat messages except the focused one
                foreach (UIElement child in ChatBubbleContainer.Children)
                {
                    if (child != targetBubble && child.Visibility == Visibility.Visible)
                    {
                        hiddenElements.Add(child);
                        child.Visibility = Visibility.Collapsed;
                    }
                }

                // Make the chat scroll viewer visible again and show only focused message
                ChatScrollViewer.Visibility = Visibility.Visible;

                // Center the focused message with minimal frame
                targetBubble.HorizontalAlignment = HorizontalAlignment.Center;
                targetBubble.VerticalAlignment = VerticalAlignment.Center;
                targetBubble.Margin = new Thickness(10); // Reduced from 20 to 10

                // Adjust window and border for focus mode with minimal padding
                MainBorder.Margin = new Thickness(0);

                // Resize window to fit content with minimal frame
                this.Width = Math.Max(200, targetBubble.ActualWidth + 40); // Reduced from +100 to +40
                this.Height = Math.Max(100, targetBubble.ActualHeight + 60); // Reduced from +100 to +60
                
                // Ensure stealth properties are maintained
                MaintainStealthInFocusMode();

                // Add ESC key handler for focus mode
                this.KeyDown += FocusMode_KeyDown;

                AddMessage("Debug", "Focus mode activated - Press ESC to exit");
            }
            catch (Exception ex)
            {
                AddMessage("Debug", $"Enter focus mode error: {ex.Message}");
                // Fallback: exit focus mode if something goes wrong
                ExitFocusMode();
            }
        }

        private void ExitFocusMode()
        {
            try
            {
                if (!isFocusModeActive) return;

                isFocusModeActive = false;

                // Remove ESC key handler
                this.KeyDown -= FocusMode_KeyDown;

                // Restore focused message properties
                if (focusedMessageBubble != null)
                {
                    focusedMessageBubble.HorizontalAlignment = HorizontalAlignment.Left; // or Right for user messages
                    focusedMessageBubble.VerticalAlignment = VerticalAlignment.Top;
                    focusedMessageBubble.Margin = new Thickness(8, 4, 8, 4); // Default bubble margin
                }

                // Restore all hidden elements
                foreach (UIElement element in hiddenElements)
                {
                    element.Visibility = Visibility.Visible;
                }

                // Restore window properties
                MainBorder.Margin = originalMainBorderMargin;
                this.Width = originalWindowWidth;
                this.Height = originalWindowHeight;

                // Clear state
                hiddenElements.Clear();
                focusedMessageBubble = null;

                // Ensure stealth properties are maintained
                MaintainStealthInFocusMode();

                AddMessage("Debug", "Focus mode deactivated");
            }
            catch (Exception ex)
            {
                AddMessage("Debug", $"Exit focus mode error: {ex.Message}");
            }
        }

        private void FocusMode_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && isFocusModeActive)
            {
                ExitFocusMode();
                e.Handled = true;
            }
        }

        private void MaintainStealthInFocusMode()
        {
            try
            {
                // Reapply all stealth measures to ensure they're not broken
                if (_windowHandle != IntPtr.Zero)
                {
                    // Reapply window stealth properties
                    int exStyle = Utilities.NativeMethods.GetWindowLong(_windowHandle, Utilities.NativeMethods.GWL_EXSTYLE);
                    exStyle |= Utilities.NativeMethods.WS_EX_LAYERED | Utilities.NativeMethods.WS_EX_TOOLWINDOW;
                    Utilities.NativeMethods.SetWindowLong(_windowHandle, Utilities.NativeMethods.GWL_EXSTYLE, exStyle);

                    // Reapply transparency
                    SetLayeredWindowAttributes(_windowHandle, 0, 245, LWA_ALPHA);

                    // Reapply display affinity (anti-capture)
                    Utilities.NativeMethods.SetWindowDisplayAffinity(_windowHandle, Utilities.NativeMethods.WDA_EXCLUDEFROMCAPTURE);

                    // Ensure topmost
                    this.Topmost = true;
                    this.ShowInTaskbar = false;
                }
            }
            catch
            {
                // Silent fail to maintain stealth
            }
        }

        private void CreateThumbnail(string base64Image, int screenshotNumber)
        {
            try
            {
                // Convert base64 to BitmapImage for display
                byte[] imageBytes = Convert.FromBase64String(base64Image);

                using (MemoryStream ms = new MemoryStream(imageBytes))
                {
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = ms;
                    bitmap.DecodePixelWidth = 120; // Thumbnail width
                    bitmap.EndInit();
                    bitmap.Freeze();

                    // Create thumbnail container
                    Border thumbnailBorder = new Border
                    {
                        Width = 120,
                        Height = 70,
                        Margin = new Thickness(0, 0, 8, 0),
                        BorderBrush = (SolidColorBrush)FindResource("BorderLightBrush"),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Background = (SolidColorBrush)FindResource("BackgroundDarkBrush")
                    };

                    // Create image display
                    Image thumbnailImage = new Image
                    {
                        Source = bitmap,
                        Stretch = Stretch.UniformToFill,
                        Width = 118,
                        Height = 68
                    };

                    // Main overlay grid
                    Grid overlayGrid = new Grid();
                    overlayGrid.Children.Add(thumbnailImage);

                    // Number badge (bottom left)
                    Border numberBadge = new Border
                    {
                        Background = (SolidColorBrush)FindResource("AccentBlueBrush"),
                        CornerRadius = new CornerRadius(8),
                        Width = 16,
                        Height = 16,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(2, 0, 0, 2)
                    };

                    TextBlock numberText = new TextBlock
                    {
                        Text = screenshotNumber.ToString(),
                        Foreground = Brushes.White,
                        FontSize = 9,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    numberBadge.Child = numberText;
                    overlayGrid.Children.Add(numberBadge);

                    // Remove button (top right) - using Border + Button approach
                    Border removeButtonBorder = new Border
                    {
                        Width = 18,
                        Height = 18,
                        Background = new SolidColorBrush(Color.FromArgb(200, 239, 68, 68)), // Semi-transparent red
                        CornerRadius = new CornerRadius(9),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(0, 2, 2, 0),
                        Cursor = Cursors.Hand
                    };

                    Button removeButton = new Button
                    {
                        Background = Brushes.Transparent,
                        BorderThickness = new Thickness(0),
                        Width = 18,
                        Height = 18,
                        Cursor = Cursors.Hand,
                        ToolTip = "Remove screenshot"
                    };

                    // Remove button content
                    TextBlock removeIcon = new TextBlock
                    {
                        Text = "✕",
                        Foreground = Brushes.White,
                        FontSize = 10,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    removeButton.Content = removeIcon;

                    // Add click handler for remove button
                    int capturedIndex = screenshotNumber - 1;
                    removeButton.Click += (s, e) => {
                        e.Handled = true; // Prevent event bubbling
                        RemoveThumbnail(thumbnailBorder, capturedIndex);
                    };

                    // Hover effects for remove button
                    removeButton.MouseEnter += (s, e) => {
                        removeButton.Background = new SolidColorBrush(Color.FromArgb(255, 239, 68, 68)); // Full red
                    };
                    removeButton.MouseLeave += (s, e) => {
                        removeButton.Background = new SolidColorBrush(Color.FromArgb(200, 239, 68, 68)); // Semi-transparent red
                    };

                    overlayGrid.Children.Add(removeButton);
                    thumbnailBorder.Child = overlayGrid;

                    // Remove the old click handler for the entire thumbnail
                    thumbnailBorder.ToolTip = $"Screenshot {screenshotNumber}";

                    // Add to container
                    ThumbnailContainer.Children.Add(thumbnailBorder);
                }
            }
            catch (Exception ex)
            {
                AddMessage("Debug", $"Error creating thumbnail: {ex.Message}");
            }
        }

        private void RemoveThumbnail(Border thumbnailBorder, int index)
        {
            try
            {
                if (index >= 0 && index < screenshotBuffer.Count)
                {
                    // Remove from buffer
                    screenshotBuffer.RemoveAt(index);

                    // Remove from UI
                    ThumbnailContainer.Children.Remove(thumbnailBorder);

                    // Update numbering for remaining thumbnails
                    UpdateThumbnailNumbers();

                    // Check if we should exit screenshot mode
                    if (screenshotBuffer.Count == 0)
                    {
                        ExitScreenshotMode();
                    }
                    else
                    {
                        AddMessage("Yok-AI", $"Screenshot removed. {screenshotBuffer.Count} remaining.");
                    }
                }
            }
            catch (Exception ex)
            {
                AddMessage("Debug", $"Error removing thumbnail: {ex.Message}");
            }
        }

        private void UpdateThumbnailNumbers()
        {
            try
            {
                for (int i = 0; i < ThumbnailContainer.Children.Count; i++)
                {
                    if (ThumbnailContainer.Children[i] is Border border)
                    {
                        if (border.Child is Grid grid)
                        {
                            // Find the number badge
                            foreach (UIElement child in grid.Children)
                            {
                                if (child is Border badge && badge.Child is TextBlock textBlock)
                                {
                                    textBlock.Text = (i + 1).ToString();
                                    border.ToolTip = $"Screenshot {i + 1} - Click to remove";
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AddMessage("Debug", $"Error updating thumbnail numbers: {ex.Message}");
            }
        }

        private void ClearAllThumbnails()
        {
            try
            {
                ThumbnailContainer.Children.Clear();
                ThumbnailPreviewArea.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                AddMessage("Debug", $"Error clearing thumbnails: {ex.Message}");
            }
        }

        private void ClearAllScreenshotsButton_Click(object sender, RoutedEventArgs e)
        {
            ExitScreenshotMode();
        }

    }
}