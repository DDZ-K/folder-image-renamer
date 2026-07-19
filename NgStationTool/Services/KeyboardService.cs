using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;

namespace NgStationTool.Services;

/// <summary>发送数字小键盘等按键（SendInput）。</summary>
public sealed class KeyboardService
{
    private readonly AppLogger _log;

    public KeyboardService(AppLogger log) => _log = log;

    public bool SendKey(string keyName, int repeat, int delayMs, string? titleContains, string? processName, int activateDelayMs)
    {
        if (!TryMapKey(keyName, out var vk, out var label))
        {
            _log.Error("键盘", $"未知按键配置: {keyName}");
            return false;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(titleContains) || !string.IsNullOrWhiteSpace(processName))
            {
                if (!ActivateTarget(titleContains, processName))
                    _log.Warn("键盘", "未找到目标窗口，仍尝试向前台发送");
                else if (activateDelayMs > 0)
                    Thread.Sleep(activateDelayMs);
            }

            for (var i = 0; i < Math.Max(1, repeat); i++)
            {
                if (!SendVk(vk))
                {
                    _log.Error("键盘", $"SendInput 失败: {label}");
                    return false;
                }
                if (delayMs > 0 && i + 1 < repeat)
                    Thread.Sleep(delayMs);
            }

            _log.Success("键盘", $"已发送 {label} x{Math.Max(1, repeat)}");
            return true;
        }
        catch (Exception ex)
        {
            _log.Error("键盘", ex.Message);
            return false;
        }
    }

    public static bool TryMapKey(string name, out ushort vk, out string label)
    {
        vk = 0;
        label = name;
        if (string.IsNullOrWhiteSpace(name)) return false;
        var n = name.Trim();

        // 标准别名
        var map = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            // 数字小键盘（NumLock 开时为数字；关时系统常映射成导航键，但 VK 仍是 NUMPAD）
            ["NumPad0"] = 0x60,
            ["NumPad1"] = 0x61,
            ["NumPad2"] = 0x62,
            ["NumPad3"] = 0x63,
            ["NumPad4"] = 0x64,
            ["NumPad5"] = 0x65,
            ["NumPad6"] = 0x66,
            ["NumPad7"] = 0x67,
            ["NumPad8"] = 0x68,
            ["NumPad9"] = 0x69,
            ["Num7"] = 0x67,
            ["Num9"] = 0x69,
            // 主键盘区数字
            ["D0"] = 0x30,
            ["D1"] = 0x31,
            ["D2"] = 0x32,
            ["D3"] = 0x33,
            ["D4"] = 0x34,
            ["D5"] = 0x35,
            ["D6"] = 0x36,
            ["D7"] = 0x37,
            ["D8"] = 0x38,
            ["D9"] = 0x39,
            // 默认 7/9 仍映射小键盘（兼容旧配置）
            ["7"] = 0x67,
            ["9"] = 0x69,
            // 导航键（与「小键盘关 NumLock 时 7/9 的体感」对应，但是独立 VK）
            ["Home"] = 0x24,      // VK_HOME
            ["End"] = 0x23,       // VK_END
            ["PageUp"] = 0x21,    // VK_PRIOR
            ["PgUp"] = 0x21,
            ["Prior"] = 0x21,
            ["PageDown"] = 0x22,  // VK_NEXT
            ["PgDn"] = 0x22,
            ["PgDown"] = 0x22,
            ["Next"] = 0x22,
            ["Insert"] = 0x2D,
            ["Ins"] = 0x2D,
            ["Delete"] = 0x2E,
            ["Del"] = 0x2E,
            ["Left"] = 0x25,
            ["Up"] = 0x26,
            ["Right"] = 0x27,
            ["Down"] = 0x28,
            ["Enter"] = 0x0D,
            ["Return"] = 0x0D,
            ["Escape"] = 0x1B,
            ["Esc"] = 0x1B,
            ["Space"] = 0x20,
            ["Tab"] = 0x09,
            ["Backspace"] = 0x08,
            ["F1"] = 0x70,
            ["F2"] = 0x71,
            ["F3"] = 0x72,
            ["F4"] = 0x73,
            ["F5"] = 0x74,
            ["F6"] = 0x75,
            ["F7"] = 0x76,
            ["F8"] = 0x77,
            ["F9"] = 0x78,
            ["F10"] = 0x79,
            ["F11"] = 0x7A,
            ["F12"] = 0x7B,
        };

        if (map.TryGetValue(n, out vk))
        {
            label = n;
            return true;
        }

        // VK_xx 十六进制
        if (n.StartsWith("VK_", StringComparison.OrdinalIgnoreCase)
            && ushort.TryParse(n.AsSpan(3), System.Globalization.NumberStyles.HexNumber, null, out vk))
        {
            label = n;
            return true;
        }

        return false;
    }

    private static bool SendVk(ushort vk)
    {
        var inputs = new INPUT[2];
        inputs[0].type = 1; // INPUT_KEYBOARD
        inputs[0].U.ki = new KEYBDINPUT { wVk = vk, dwFlags = 0 };
        inputs[1].type = 1;
        inputs[1].U.ki = new KEYBDINPUT { wVk = vk, dwFlags = 2 }; // KEYEVENTF_KEYUP
        var size = Marshal.SizeOf<INPUT>();
        var sent = SendInput(2, inputs, size);
        return sent == 2;
    }

    private bool ActivateTarget(string? titleContains, string? processName)
    {
        try
        {
            var procs = System.Diagnostics.Process.GetProcesses();
            foreach (var p in procs)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(processName))
                    {
                        if (!string.Equals(p.ProcessName, processName.Trim(), StringComparison.OrdinalIgnoreCase))
                            continue;
                    }

                    var h = p.MainWindowHandle;
                    if (h == IntPtr.Zero) continue;

                    if (!string.IsNullOrWhiteSpace(titleContains))
                    {
                        var title = p.MainWindowTitle ?? "";
                        if (title.IndexOf(titleContains.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                    }
                    else if (string.IsNullOrWhiteSpace(processName))
                    {
                        continue;
                    }

                    ShowWindow(h, 9); // SW_RESTORE
                    SetForegroundWindow(h);
                    return true;
                }
                catch { /* next */ }
                finally
                {
                    try { p.Dispose(); } catch { /* ignore */ }
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warn("键盘", "激活窗口异常: " + ex.Message);
        }
        return false;
    }

    #region P/Invoke

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    #endregion
}
