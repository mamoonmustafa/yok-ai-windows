using System;
using System.Runtime.InteropServices;
using System.Text;

namespace StealthAssistWPF.Utilities
{
    public static class NativeMethods
    {
        // Add to your NativeMethods.cs class

        // Window message constants
        public const int WM_GETTEXT = 0x000D;
        public const int WM_GETTEXTLENGTH = 0x000E;

        // Window style constants - you already have these but ensure they're correct
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_LAYERED = 0x80000;
        public const int WS_EX_TOOLWINDOW = 0x80;
        public const int WDA_NONE = 0x00000000;
        public const int WDA_MONITOR = 0x00000001;
        public const int WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        // LayeredWindowAttributes flags
        public const uint LWA_ALPHA = 0x2;
        public const uint LWA_COLORKEY = 0x1;

        // Add these additional constants for enhanced stealth
        public const int HWND_TOPMOST = -1;
        public const int HWND_NOTOPMOST = -2;
        public const int SWP_NOMOVE = 0x0002;
        public const int SWP_NOSIZE = 0x0001;
        public const int SWP_NOACTIVATE = 0x0010;
        public const int SWP_SHOWWINDOW = 0x0040;

        // Import additional methods for window manipulation
        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("user32.dll", EntryPoint = "SetWindowDisplayAffinity", SetLastError = true)]
        private static extern bool SetWindowDisplayAffinityNative(IntPtr hWnd, uint dwAffinity);

        /// <summary>
        /// True when started with --screenshot-mode: the window stays visible to screenshots and screen sharing
        /// (for documentation and debugging). Off by default.
        /// </summary>
        public static bool ScreenshotMode =
            Array.Exists(Environment.GetCommandLineArgs(), a => string.Equals(a, "--screenshot-mode", StringComparison.OrdinalIgnoreCase));

        public static bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity)
        {
            return SetWindowDisplayAffinityNative(hWnd, ScreenshotMode ? (uint)WDA_NONE : dwAffinity);
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        // DWM constants for Windows 10/11 exclusive capture blocking
        public const int DWMWA_CLOAK = 13;
        public const int DWMWA_EXCLUDED_FROM_PEEK = 12;
        public const int DWM_CLOAKED_APP = 0x00000001;


        public static void EnhanceStealthForRelease(IntPtr windowHandle)
        {
            try
            {
                // Apply all available stealth techniques
                if (windowHandle != IntPtr.Zero)
                {
                    // 1. Make window layered and hide from taskbar
                    int exStyle = GetWindowLong(windowHandle, GWL_EXSTYLE);
                    exStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW;
                    SetWindowLong(windowHandle, GWL_EXSTYLE, exStyle);

                    // 2. Apply transparency using SetLayeredWindowAttributes 
                    // (instead of AllowsTransparency which conflicts with WDA_EXCLUDEFROMCAPTURE)
                    SetLayeredWindowAttributes(windowHandle, 0, 245, LWA_ALPHA);

                    // 3. Try to exclude from capture first (Windows 10 v2004+)
                    bool result = SetWindowDisplayAffinity(windowHandle, WDA_EXCLUDEFROMCAPTURE);

                    // 4. If that fails, fall back to WDA_MONITOR (black box in capture)
                    if (!result)
                    {
                        SetWindowDisplayAffinity(windowHandle, WDA_MONITOR);
                    }

                    // 5. Add DWM cloaking (Windows 10+)
                    // This helps with more modern screen sharing tech
                    int cloakValue = DWM_CLOAKED_APP;
                    DwmSetWindowAttribute(windowHandle, DWMWA_CLOAK, ref cloakValue, sizeof(int));

                    // 6. Exclude from Windows Peek
                    int excludeFromPeek = 1;
                    DwmSetWindowAttribute(windowHandle, DWMWA_EXCLUDED_FROM_PEEK, ref excludeFromPeek, sizeof(int));

                    // 7. Set topmost but don't activate
                    SetWindowPos(
                        windowHandle,
                        (IntPtr)HWND_TOPMOST,
                        0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE
                    );
                }
            }
            catch
            {
                // Silent fail
            }
        }

        // For detecting window enumeration
        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        // For hiding from window enumeration
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        // For thread manipulation to avoid hooks
        [DllImport("kernel32.dll")]
        public static extern IntPtr OpenThread(ThreadAccess dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

        [DllImport("kernel32.dll")]
        public static extern uint SuspendThread(IntPtr hThread);

        [DllImport("kernel32.dll")]
        public static extern uint ResumeThread(IntPtr hThread);

        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr hObject);

        [Flags]
        public enum ThreadAccess : int
        {
            TERMINATE = (0x0001),
            SUSPEND_RESUME = (0x0002),
            GET_CONTEXT = (0x0008),
            SET_CONTEXT = (0x0010),
            SET_INFORMATION = (0x0020),
            QUERY_INFORMATION = (0x0040),
            SET_THREAD_TOKEN = (0x0080),
            IMPERSONATE = (0x0100),
            DIRECT_IMPERSONATION = (0x0200)
        }

        // For window appearance manipulation
        [DllImport("user32.dll")]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        // For injecting input directly
        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;
            [FieldOffset(0)]
            public KEYBDINPUT ki;
            [FieldOffset(0)]
            public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        // Constants for SendInput
        public const uint INPUT_MOUSE = 0;
        public const uint INPUT_KEYBOARD = 1;
        public const uint INPUT_HARDWARE = 2;
        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const uint KEYEVENTF_UNICODE = 0x0004;
        public const uint KEYEVENTF_SCANCODE = 0x0008;





        // Window styles
        public const int WS_EX_TRANSPARENT = 0x20;



        // For window transparency
        [DllImport("user32.dll")]
        public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, int crKey, byte bAlpha, uint dwFlags);


        // Hotkey registration
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // Cursor methods
        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int x, int y);

        // Keyboard hook methods
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        // Keyboard hook delegate
        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        // Keyboard hook constants
        public const int WH_KEYBOARD_LL = 13;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;

        // Hot key constants
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint VK_SPACE = 0x20;
        public const uint VK_C = 0x43;
        public const uint VK_X = 0x58;
        public const uint VK_V = 0x56;

        // Window dragging
        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HT_CAPTION = 0x2;

        // Hotkey IDs
        public const int HOTKEY_TOGGLE_UI = 1;
        public const int HOTKEY_SCREEN_CAPTURE = 2;
        public const int HOTKEY_EMERGENCY_EXIT = 3;
        public const int HOTKEY_VOICE_TOGGLE = 4;
        public const int HOTKEY_MULTI_CAPTURE_START = 5;
        public const int HOTKEY_MULTI_CAPTURE_FINISH = 6;
        public const int HOTKEY_KEYSTROKE_HIDE = 7;
        public const int HOTKEY_CURSOR_HIDE = 8;
        public const int HOTKEY_STEALTH_MODE = 9;
        public const int HOTKEY_OPACITY_TOGGLE = 10;  // Use a number that isn't already used
        public const uint VK_O = 0x4F;                // Virtual key code for 'O'
    }
}