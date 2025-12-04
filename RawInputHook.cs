using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ApmTracker
{
    // TODO: Consider using Raw Input API for mouse as well (currently using Low-Level Hook)
    // TODO: Add support for gamepad/controller input
    // TODO: Implement input filtering (ignore certain keys/combinations)
    
    public class RawInputHook : IDisposable
    {
        private const int WM_INPUT = 0x00FF;
        private const uint RID_INPUT = 0x10000003;
        private const uint RIM_TYPEKEYBOARD = 1;
        private const uint RIDEV_INPUTSINK = 0x00000100;
        private const ushort RI_KEY_BREAK = 0x01;

        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_XBUTTONDOWN = 0x020B;
        private const int WM_MOUSEWHEEL = 0x020A;

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        private HwndSource? _hwndSource;
        private readonly System.Collections.Generic.HashSet<ushort> _pressedKeys = new();
        private IntPtr _mouseHookId = IntPtr.Zero;
        private readonly LowLevelMouseProc _mouseProc;

        public event Action<InputType>? OnInput;

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTHEADER
        {
            public uint dwType;
            public uint dwSize;
            public IntPtr hDevice;
            public IntPtr wParam;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, byte[] pData, ref uint pcbSize, uint cbSizeHeader);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        public RawInputHook()
        {
            _mouseProc = MouseHookCallback;
        }

        public void Start(Window window)
        {
            var helper = new WindowInteropHelper(window);
            var hwnd = helper.Handle;

            _hwndSource = HwndSource.FromHwnd(hwnd);
            _hwndSource?.AddHook(WndProc);

            var devices = new RAWINPUTDEVICE[1];
            devices[0].usUsagePage = 0x01;
            devices[0].usUsage = 0x06;
            devices[0].dwFlags = RIDEV_INPUTSINK;
            devices[0].hwndTarget = hwnd;

            RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());

            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule!;
            _mouseHookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle(curModule.ModuleName), 0);
        }

        public void Stop()
        {
            _hwndSource?.RemoveHook(WndProc);
            _pressedKeys.Clear();

            if (_mouseHookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHookId);
                _mouseHookId = IntPtr.Zero;
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_INPUT)
            {
                ProcessRawInput(lParam);
            }
            return IntPtr.Zero;
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var inputType = (int)wParam switch
                {
                    WM_LBUTTONDOWN => InputType.MouseLeft,
                    WM_RBUTTONDOWN => InputType.MouseRight,
                    WM_MBUTTONDOWN => InputType.MouseMiddle,
                    WM_XBUTTONDOWN => InputType.MouseExtra,
                    WM_MOUSEWHEEL => InputType.MouseWheel,
                    _ => InputType.None
                };

                if (inputType != InputType.None)
                {
                    OnInput?.Invoke(inputType);
                }
            }
            return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
        }

        private void ProcessRawInput(IntPtr lParam)
        {
            uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
            uint size = 0;

            if (GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, headerSize) != 0)
                return;

            if (size == 0) return;

            byte[] data = new byte[size];
            if (GetRawInputData(lParam, RID_INPUT, data, ref size, headerSize) != size)
                return;

            uint dwType = BitConverter.ToUInt32(data, 0);

            if (dwType == RIM_TYPEKEYBOARD)
            {
                ProcessKeyboardData(data, headerSize);
            }
        }

        private void ProcessKeyboardData(byte[] data, uint headerSize)
        {
            int offset = (int)headerSize;
            
            if (data.Length < offset + 8) return;

            ushort flags = BitConverter.ToUInt16(data, offset + 2);
            ushort vKey = BitConverter.ToUInt16(data, offset + 6);

            bool isKeyDown = (flags & RI_KEY_BREAK) == 0;

            if (isKeyDown)
            {
                if (!_pressedKeys.Contains(vKey))
                {
                    _pressedKeys.Add(vKey);
                    OnInput?.Invoke(InputType.Keyboard);
                }
            }
            else
            {
                _pressedKeys.Remove(vKey);
            }
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }
    }

    public enum InputType
    {
        None,
        Keyboard,
        MouseLeft,
        MouseRight,
        MouseMiddle,
        MouseExtra,
        MouseWheel
    }
}
