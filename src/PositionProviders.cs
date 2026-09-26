using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace MaskOver
{
    internal sealed class GlobalMouseWheelHook : IDisposable
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEWHEEL = 0x020A;
        private readonly HookCallback callback;
        private IntPtr hook;

        public event Action<int> Wheel;

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseHookData
        {
            public NativeMethods.POINT Point;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        private delegate IntPtr HookCallback(int code, IntPtr message, IntPtr data);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int hookType, HookCallback callback, IntPtr module, uint threadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string moduleName);

        public GlobalMouseWheelHook()
        {
            callback = HandleMouse;
            hook = SetWindowsHookEx(WH_MOUSE_LL, callback, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), Locale.T("Nie mozna uruchomic globalnej obslugi rolki myszy.", "Could not start the global mouse wheel handler."));
        }

        private IntPtr HandleMouse(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0 && message.ToInt32() == WM_MOUSEWHEEL)
            {
                MouseHookData details = (MouseHookData)Marshal.PtrToStructure(data, typeof(MouseHookData));
                int delta = (short)((details.MouseData >> 16) & 65535);
                Action<int> handler = Wheel;
                if (handler != null)
                    handler(delta);
            }
            return CallNextHookEx(hook, code, message, data);
        }

        public void Dispose()
        {
            if (hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(hook);
                hook = IntPtr.Zero;
            }
        }
    }

    internal static class PositionParser
    {
        private static readonly Regex NumberRegex = new Regex(@"-?\d+(?:[\.,]\d+)?", RegexOptions.Compiled);
        public static bool TryParse(string text, string source, out WorldPosition position)
        {
            position = new WorldPosition();
            if (String.IsNullOrWhiteSpace(text))
                return false;
            int start = text.IndexOf("Pos", StringComparison.OrdinalIgnoreCase);
            string relevant = start >= 0 ? text.Substring(start) : text;
            MatchCollection matches = NumberRegex.Matches(relevant);
            if (matches.Count < 3)
                return false;
            double x;
            double y;
            double z;
            if (!TryNumber(matches[0].Value, out x) || !TryNumber(matches[1].Value, out y) || !TryNumber(matches[2].Value, out z))
                return false;
            position = new WorldPosition(x, y, z, source);
            if (source.Equals("bridge", StringComparison.OrdinalIgnoreCase) && matches.Count >= 6)
            {
                double directionX;
                double ignoredDirectionY;
                double directionZ;
                if (TryNumber(matches[3].Value, out directionX) && TryNumber(matches[4].Value, out ignoredDirectionY) && TryNumber(matches[5].Value, out directionZ))
                {
                    double horizontalLength = Math.Sqrt((directionX * directionX) + (directionZ * directionZ));
                    if (horizontalLength > 0.0001)
                    {
                        position.DirectionX = directionX / horizontalLength;
                        position.DirectionZ = directionZ / horizontalLength;
                        position.HasDirection = true;
                    }
                }
            }
            return true;
        }

        private static bool TryNumber(string text, out double value)
        {
            return Double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }

    internal sealed class BridgePositionProvider
    {
        private readonly string path;

        public BridgePositionProvider(string path)
        {
            this.path = path;
        }

        public bool TryRead(out WorldPosition position)
        {
            position = new WorldPosition();
            try
            {
                if (!File.Exists(path))
                    return false;
                DateTime writeTime = File.GetLastWriteTimeUtc(path);
                if ((DateTime.UtcNow - writeTime).TotalSeconds > 2.0)
                    return false;
                string text = File.ReadAllText(path);
                return PositionParser.TryParse(text, "bridge", out position);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    internal static class NativeMethods
    {
        public delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr handle, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr handle);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr handle, uint flags);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int key);

        public static IntPtr FindBuldozerWindow()
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows(delegate(IntPtr handle, IntPtr parameter)
            {
                if (!IsWindowVisible(handle))
                    return true;
                StringBuilder title = new StringBuilder(512);
                GetWindowText(handle, title, title.Capacity);
                if (title.ToString().Equals("Buldozer", StringComparison.OrdinalIgnoreCase))
                {
                    found = handle;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        public static bool IsBuldozerForeground()
        {
            IntPtr buldozer = FindBuldozerWindow();
            IntPtr foreground = GetForegroundWindow();
            if (buldozer == IntPtr.Zero || foreground == IntPtr.Zero)
                return false;

            IntPtr buldozerRoot = GetAncestor(buldozer, 2);
            IntPtr foregroundRoot = GetAncestor(foreground, 2);
            if (buldozerRoot == IntPtr.Zero)
                buldozerRoot = buldozer;
            if (foregroundRoot == IntPtr.Zero)
                foregroundRoot = foreground;
            if (buldozerRoot == foregroundRoot)
                return true;

            uint buldozerProcess;
            uint foregroundProcess;
            GetWindowThreadProcessId(buldozerRoot, out buldozerProcess);
            GetWindowThreadProcessId(foregroundRoot, out foregroundProcess);
            return buldozerProcess != 0 && buldozerProcess == foregroundProcess;
        }

    }
}
