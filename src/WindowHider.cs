// WinHide —— 一键隐藏 / 恢复指定程序窗口（托盘常驻 + 全局热键）
// 编译：csc /target:winexe /out:WinHide.exe WindowHider.cs /r:System.Windows.Forms.dll /r:System.Drawing.dll

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WinHide
{
    internal class WinInfo
    {
        public IntPtr Handle;
        public string Process;
        public string Title;
        public override string ToString()
        {
            return Process + " | " + Title;
        }
    }

    internal class Target
    {
        public string Process;
        public string Title;
    }

    internal static class Win32
    {
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        public const int SW_HIDE = 0;
        public const int SW_SHOW = 5;
        public const int SW_RESTORE = 9;
        public const int WM_HOTKEY = 0x0312;
        public const uint MOD_ALT = 1;
        public const uint MOD_CONTROL = 2;
        public const uint MOD_SHIFT = 4;
        public const uint MOD_WIN = 8;
        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_APPWINDOW = 0x00040000;
        public const int WS_CAPTION = 0x00C00000;
    }

    internal static class Store
    {
        public static string Dir
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinHide");
                if (!Directory.Exists(d)) Directory.CreateDirectory(d);
                return d;
            }
        }
        public static string ConfigPath
        {
            get
            {
                // 允许用环境变量指定配置文件，便于多套配置或便携使用
                string env = Environment.GetEnvironmentVariable("WINHIDE_CONFIG");
                if (!string.IsNullOrEmpty(env)) return env;
                return Path.Combine(Dir, "targets.txt");
            }
        }
        public static string LogPath
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("WINHIDE_LOG");
                if (!string.IsNullOrEmpty(env)) return env;
                return Path.Combine(Dir, "winhide.log");
            }
        }

        public static void Log(string msg)
        {
            try
            {
                File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        public static List<Target> Load()
        {
            var list = new List<Target>();
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    File.WriteAllText(ConfigPath,
                        "# WinHide 目标列表" + Environment.NewLine +
                        "# 每行一个目标，格式：进程名|窗口标题关键字" + Environment.NewLine +
                        "# 窗口标题留空表示匹配该进程的所有窗口（推荐）" + Environment.NewLine +
                        "# 以 # 开头的行为注释。示例：" + Environment.NewLine +
                        "# notepad.exe|" + Environment.NewLine +
                        "# chrome.exe|哔哩哔哩" + Environment.NewLine,
                        Encoding.UTF8);
                    return list;
                }
                string[] lines = File.ReadAllLines(ConfigPath, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    string proc = line;
                    string title = "";
                    int idx = line.IndexOf('|');
                    if (idx >= 0)
                    {
                        proc = line.Substring(0, idx).Trim();
                        title = line.Substring(idx + 1).Trim();
                    }
                    if (proc.Length == 0) continue;
                    var t = new Target();
                    t.Process = proc;
                    t.Title = title;
                    list.Add(t);
                }
            }
            catch (Exception ex) { Log("读取配置失败: " + ex.Message); }
            return list;
        }

        public static void Save(List<Target> targets)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# WinHide 目标列表");
                sb.AppendLine("# 每行一个目标，格式：进程名|窗口标题关键字");
                sb.AppendLine("# 窗口标题留空表示匹配该进程的所有窗口");
                foreach (Target t in targets)
                {
                    sb.AppendLine(t.Process + "|" + t.Title);
                }
                File.WriteAllText(ConfigPath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex) { Log("保存配置失败: " + ex.Message); }
        }
        public static void SaveHotkeys(string hide, string show, bool sameKey)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# 热键配置（手工改完需重启工具；托盘菜单「设置热键…」改则即时生效）");
                sb.AppendLine("# 规则：必须含 Ctrl/Alt/Shift/Win 之一；samekey=1 时隐藏与恢复共用 hide 的热键");
                sb.AppendLine("hide=" + hide);
                sb.AppendLine("show=" + show);
                sb.AppendLine("samekey=" + (sameKey ? "1" : "0"));
                File.WriteAllText(HotkeysPath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex) { Log("保存热键失败: " + ex.Message); }
        }

        public static bool GetFlag(string name, bool def)
        {
            string v = GetHotkey(name, null);
            if (string.IsNullOrEmpty(v)) return def;
            v = v.Trim();
            return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase) || v.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        public static string GetHotkey(string name, string def)
        {
            try
            {
                if (!File.Exists(HotkeysPath)) return def;
                string[] lines = File.ReadAllLines(HotkeysPath, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int idx = line.IndexOf('=');
                    if (idx <= 0) continue;
                    if (string.Equals(line.Substring(0, idx).Trim(), name, StringComparison.OrdinalIgnoreCase))
                        return line.Substring(idx + 1).Trim();
                }
            }
            catch (Exception ex) { Log("读取热键失败: " + ex.Message); }
            return def;
        }

        public static string HotkeysPath
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("WINHIDE_HOTKEYS");
                if (!string.IsNullOrEmpty(env)) return env;
                return Path.Combine(Dir, "hotkeys.txt");
            }
        }

        public static string HiddenPath
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("WINHIDE_STATE");
                if (!string.IsNullOrEmpty(env)) return env;
                return Path.Combine(Dir, "hidden.txt");
            }
        }

        /// <summary>记录"本工具亲手隐藏的窗口句柄"，恢复时只还原这些，避免把进程内部的
        /// 隐藏辅助窗口（如微信的 WxTrayIconMessageWindow）一并弹出来。</summary>
        public static void SaveHidden(List<string> entries)
        {
            try
            {
                if (entries == null || entries.Count == 0)
                {
                    if (File.Exists(HiddenPath)) File.Delete(HiddenPath);
                    return;
                }
                File.WriteAllLines(HiddenPath, entries.ToArray(), Encoding.UTF8);
            }
            catch (Exception ex) { Log("保存隐藏列表失败: " + ex.Message); }
        }

        public static List<string> LoadHidden()
        {
            var list = new List<string>();
            try
            {
                if (!File.Exists(HiddenPath)) return list;
                string[] lines = File.ReadAllLines(HiddenPath, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length > 0) list.Add(line);
                }
            }
            catch (Exception ex) { Log("读取隐藏列表失败: " + ex.Message); }
            return list;
        }
    }

    internal class Hotkey
    {
        public uint Mods;
        public uint Vk;
        public string Text;

        public static Hotkey Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            string[] parts = s.Split('+');
            uint mods = 0;
            uint vk = 0;
            bool hasKey = false;
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim().ToLowerInvariant().Replace(" ", "");
                if (p.Length == 0) continue;
                if (p == "ctrl" || p == "control") { mods |= Win32.MOD_CONTROL; continue; }
                if (p == "alt") { mods |= Win32.MOD_ALT; continue; }
                if (p == "shift") { mods |= Win32.MOD_SHIFT; continue; }
                if (p == "win" || p == "windows") { mods |= Win32.MOD_WIN; continue; }

                Keys k;
                if (!TryParseKey(p, out k)) return null;
                vk = (uint)k;
                hasKey = true;
            }
            if (!hasKey) return null;
            if (mods == 0) return null;   // 必须带修饰键，避免单键抢占
            Hotkey h = new Hotkey();
            h.Mods = mods;
            h.Vk = vk;
            h.Text = ModsToText(mods) + KeyToText((Keys)vk);
            return h;
        }

        public static Hotkey FromKeys(Keys mods, Keys key)
        {
            if (IsModifierKey(key)) return null;
            uint m = 0;
            if ((mods & Keys.Control) == Keys.Control) m |= Win32.MOD_CONTROL;
            if ((mods & Keys.Alt) == Keys.Alt) m |= Win32.MOD_ALT;
            if ((mods & Keys.Shift) == Keys.Shift) m |= Win32.MOD_SHIFT;
            if (m == 0) return null;

            string name = KeyToText(key);
            if (name == null) return null;
            Hotkey h = new Hotkey();
            h.Mods = m;
            h.Vk = (uint)key;
            h.Text = ModsToText(m) + name;
            return h;
        }

        private static bool IsModifierKey(Keys k)
        {
            return k == Keys.ControlKey || k == Keys.ShiftKey || k == Keys.Menu ||
                   k == Keys.LControlKey || k == Keys.RControlKey ||
                   k == Keys.LShiftKey || k == Keys.RShiftKey ||
                   k == Keys.LMenu || k == Keys.RMenu ||
                   k == Keys.LWin || k == Keys.RWin;
        }

        public static string ModsToText(uint mods)
        {
            StringBuilder sb = new StringBuilder();
            if ((mods & Win32.MOD_CONTROL) != 0) sb.Append("Ctrl+");
            if ((mods & Win32.MOD_ALT) != 0) sb.Append("Alt+");
            if ((mods & Win32.MOD_SHIFT) != 0) sb.Append("Shift+");
            if ((mods & Win32.MOD_WIN) != 0) sb.Append("Win+");
            return sb.ToString();
        }

        /// <summary>生成给用户看的操作提示，跟随实际热键而非写死默认值。</summary>
        public static string HintText(Hotkey hide, Hotkey show, bool sameKey)
        {
            if (hide == null) return "";
            if (sameKey) return "按 " + hide.Text + " 切换隐藏/恢复";
            string s = show != null ? show.Text : "";
            if (s.Length == 0 || string.Equals(s, hide.Text, StringComparison.OrdinalIgnoreCase))
                return "按 " + hide.Text + " 隐藏";
            return "隐藏 " + hide.Text + "，恢复 " + s;
        }

        public static string KeyToText(Keys k)
        {
            if (k >= Keys.A && k <= Keys.Z) return ((char)('A' + (k - Keys.A))).ToString();
            if (k >= Keys.D0 && k <= Keys.D9) return ((char)('0' + (k - Keys.D0))).ToString();
            if (k >= Keys.F1 && k <= Keys.F24) return "F" + (k - Keys.F1 + 1);
            switch (k)
            {
                case Keys.Space: return "Space";
                case Keys.Tab: return "Tab";
                case Keys.Enter: return "Enter";
                case Keys.Escape: return "Esc";
                case Keys.Back: return "Backspace";
                case Keys.Delete: return "Delete";
                case Keys.Insert: return "Insert";
                case Keys.Home: return "Home";
                case Keys.End: return "End";
                case Keys.PageUp: return "PageUp";
                case Keys.PageDown: return "PageDown";
                case Keys.Up: return "Up";
                case Keys.Down: return "Down";
                case Keys.Left: return "Left";
                case Keys.Right: return "Right";
            }
            return null;
        }

        public static bool TryParseKey(string p, out Keys key)
        {
            key = Keys.None;
            if (p.Length == 1)
            {
                char c = char.ToUpperInvariant(p[0]);
                if (c >= 'A' && c <= 'Z') { key = (Keys)(int)c; return true; }
                if (c >= '0' && c <= '9') { key = (Keys)(int)Keys.D0 + (c - '0'); return true; }
            }
            if (p.Length >= 2 && p[0] == 'f')
            {
                int n;
                if (int.TryParse(p.Substring(1), out n) && n >= 1 && n <= 24)
                {
                    key = (Keys)(int)Keys.F1 + (n - 1);
                    return true;
                }
            }
            switch (p)
            {
                case "space": key = Keys.Space; return true;
                case "tab": key = Keys.Tab; return true;
                case "enter": case "return": key = Keys.Enter; return true;
                case "esc": case "escape": key = Keys.Escape; return true;
                case "backspace": key = Keys.Back; return true;
                case "delete": case "del": key = Keys.Delete; return true;
                case "insert": case "ins": key = Keys.Insert; return true;
                case "home": key = Keys.Home; return true;
                case "end": key = Keys.End; return true;
                case "pageup": case "pgup": key = Keys.PageUp; return true;
                case "pagedown": case "pgdn": key = Keys.PageDown; return true;
                case "up": key = Keys.Up; return true;
                case "down": key = Keys.Down; return true;
                case "left": key = Keys.Left; return true;
                case "right": key = Keys.Right; return true;
            }
            return false;
        }
    }

    internal static class Windows
    {
        private static readonly string[] HelperClasses = new string[]
        {
            "IME", "MSCTFIME", "CiceroUIWndFrame", "Progman", "WorkerW",
            "Shell_TrayWnd", "TrayNotifyWnd", "GDI+", "SysShadow"
        };

        private static readonly string[] HelperTitles = new string[]
        {
            "MSCTFIME UI", "Default IME", "CiceroUIWndFrame", "Program Manager"
        };

        /// <summary>输入法窗口、桌面窗口、托盘/阴影等系统辅助窗口：列表里不显示，
        /// 隐藏/恢复时也不去动它们。</summary>
        public static bool IsHelperWindow(IntPtr h)
        {
            int ex = Win32.GetWindowLong(h, Win32.GWL_EXSTYLE);
            if ((ex & Win32.WS_EX_TOOLWINDOW) != 0) return true;

            string cls = GetClassNameOf(h);
            for (int i = 0; i < HelperClasses.Length; i++)
            {
                if (string.Equals(cls, HelperClasses[i], StringComparison.OrdinalIgnoreCase)) return true;
            }

            string title = GetTitle(h);
            for (int i = 0; i < HelperTitles.Length; i++)
            {
                if (string.Equals(title, HelperTitles[i], StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public static List<WinInfo> Enumerate(bool visibleOnly, string excludeProcess)
        {
            var list = new List<WinInfo>();
            Win32.EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                int len = Win32.GetWindowTextLength(h);
                if (len <= 0) return true;
                if (visibleOnly && !Win32.IsWindowVisible(h)) return true;
                if (IsHelperWindow(h)) return true;   // 过滤输入法/桌面/托盘等系统窗口

                var sb = new StringBuilder(len + 1);
                Win32.GetWindowText(h, sb, sb.Capacity);
                string title = sb.ToString();
                if (title.Trim().Length == 0) return true;

                uint pid;
                Win32.GetWindowThreadProcessId(h, out pid);
                string proc = "";
                try
                {
                    using (Process p = Process.GetProcessById((int)pid)) { proc = p.ProcessName; }
                }
                catch { }
                if (proc.Length == 0) return true;
                if (excludeProcess != null && string.Equals(proc, excludeProcess, StringComparison.OrdinalIgnoreCase)) return true;

                var w = new WinInfo();
                w.Handle = h;
                w.Process = proc;
                w.Title = title;
                list.Add(w);
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static bool Matches(Target t, WinInfo w)
        {
            string tp = t.Process;
            if (tp.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) tp = tp.Substring(0, tp.Length - 4);
            if (!string.Equals(w.Process, tp, StringComparison.OrdinalIgnoreCase)) return false;
            if (t.Title.Length > 0 && w.Title.IndexOf(t.Title, StringComparison.OrdinalIgnoreCase) < 0) return false;
            return true;
        }

        public static int CountVisible(List<Target> targets)
        {
            Dictionary<int, List<Target>> map = ResolveTargets(targets);
            if (map.Count == 0) return 0;
            int n = 0;
            Win32.EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                uint pidU;
                Win32.GetWindowThreadProcessId(h, out pidU);
                List<Target> ts;
                if (!map.TryGetValue((int)pidU, out ts)) return true;
                if (!Win32.IsWindowVisible(h)) return true;
                if (IsHelperWindow(h)) return true;
                if (!AnyTitleMatch(ts, h)) return true;
                n++;
                return true;
            }, IntPtr.Zero);
            return n;
        }

        /// <summary>按进程名一次性拿到 PID 列表，建立 PID -> 目标规则 的映射。
        /// 这样枚举窗口时只需 GetWindowThreadProcessId，不用为每个窗口创建 Process 对象。</summary>
        private static Dictionary<int, List<Target>> ResolveTargets(List<Target> targets)
        {
            var map = new Dictionary<int, List<Target>>();
            var seen = new Dictionary<string, List<Target>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < targets.Count; i++)
            {
                string name = NormalizeProcess(targets[i].Process);
                if (name.Length == 0) continue;
                List<Target> rules;
                if (!seen.TryGetValue(name, out rules)) { rules = new List<Target>(); seen[name] = rules; }
                rules.Add(targets[i]);
            }
            foreach (KeyValuePair<string, List<Target>> kv in seen)
            {
                Process[] ps;
                try { ps = Process.GetProcessesByName(kv.Key); }
                catch { continue; }
                for (int i = 0; i < ps.Length; i++)
                {
                    int pid = ps[i].Id;
                    List<Target> list;
                    if (!map.TryGetValue(pid, out list)) { list = new List<Target>(); map[pid] = list; }
                    list.AddRange(kv.Value);
                    ps[i].Dispose();
                }
            }
            return map;
        }

        private static string NormalizeProcess(string p)
        {
            if (string.IsNullOrEmpty(p)) return "";
            string t = p.Trim();
            if (t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) t = t.Substring(0, t.Length - 4);
            return t;
        }

        private static string GetTitle(IntPtr h)
        {
            int len = Win32.GetWindowTextLength(h);
            if (len <= 0) return "";
            var sb = new StringBuilder(len + 1);
            Win32.GetWindowText(h, sb, sb.Capacity);
            return sb.ToString();
        }

        private static string GetClassNameOf(IntPtr h)
        {
            var sb = new StringBuilder(256);
            int n = Win32.GetClassName(h, sb, sb.Capacity);
            if (n <= 0) return "";
            return sb.ToString();
        }

        private static bool AnyTitleMatch(List<Target> rules, IntPtr h)
        {
            string title = null;
            for (int i = 0; i < rules.Count; i++)
            {
                if (rules[i].Title.Length == 0) return true;   // 该进程的所有窗口都算
                if (title == null) title = GetTitle(h);
                if (title.Length > 0 && title.IndexOf(rules[i].Title, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        /// <summary>判断是不是"真正的应用窗口"。托盘消息窗、输入法窗、广播窗这类
        /// 辅助窗口通常是 WS_EX_TOOLWINDOW，或者没有标题，恢复时不应被弹出来。</summary>
        public static bool IsRealWindow(IntPtr h)
        {
            int ex = Win32.GetWindowLong(h, Win32.GWL_EXSTYLE);
            if ((ex & Win32.WS_EX_TOOLWINDOW) != 0) return false;
            if (Win32.GetWindowTextLength(h) <= 0) return false;
            int style = Win32.GetWindowLong(h, Win32.GWL_STYLE);
            if ((style & Win32.WS_CAPTION) == 0 && (ex & Win32.WS_EX_APPWINDOW) == 0) return false;
            return true;
        }

        public static int Hide(List<Target> targets)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Dictionary<int, List<Target>> map = ResolveTargets(targets);
            if (map.Count == 0)
            {
                Store.Log("隐藏: 目标进程未运行 (0 个窗口, " + sw.ElapsedMilliseconds + "ms)");
                return 0;
            }

            int n = 0;
            var hid = new List<string>();
            Win32.EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                uint pidU;
                Win32.GetWindowThreadProcessId(h, out pidU);
                List<Target> ts;
                if (!map.TryGetValue((int)pidU, out ts)) return true;
                if (!Win32.IsWindowVisible(h)) return true;      // 已经隐藏的跳过
                if (IsHelperWindow(h)) return true;             // 输入法/托盘等系统窗口不动
                if (!AnyTitleMatch(ts, h)) return true;
                Win32.ShowWindow(h, Win32.SW_HIDE);
                if (!Win32.IsWindowVisible(h))
                {
                    n++;                                          // UIPI 拒绝时仍可见，不计入
                    hid.Add(h.ToInt64().ToString() + "|" + pidU);
                }
                return true;
            }, IntPtr.Zero);

            Store.SaveHidden(hid);
            Store.Log("隐藏: 成功 " + n + " 个窗口, " + sw.ElapsedMilliseconds + "ms");
            return n;
        }

        public static int Show(List<Target> targets)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int n = 0;
            IntPtr last = IntPtr.Zero;

            // 首选：只还原本工具之前亲手隐藏的句柄，绝不会碰到进程内部的隐藏辅助窗口
            List<string> cached = Store.LoadHidden();
            for (int i = 0; i < cached.Count; i++)
            {
                string[] parts = cached[i].Split('|');
                if (parts.Length < 2) continue;
                long hv;
                int pid;
                if (!long.TryParse(parts[0], out hv)) continue;
                if (!int.TryParse(parts[1], out pid)) continue;

                IntPtr h = new IntPtr(hv);
                if (!Win32.IsWindow(h)) continue;
                uint ownerPid;
                Win32.GetWindowThreadProcessId(h, out ownerPid);
                if ((int)ownerPid != pid) continue;               // 句柄已复用，丢弃
                if (Win32.IsWindowVisible(h)) { n++; continue; }  // 已经露着了
                if (Win32.IsIconic(h)) Win32.ShowWindow(h, Win32.SW_RESTORE);
                Win32.ShowWindow(h, Win32.SW_SHOW);
                if (Win32.IsWindowVisible(h)) { n++; last = h; }
            }
            if (n > 0)
            {
                Store.SaveHidden(null);
                if (last != IntPtr.Zero) Win32.SetForegroundWindow(last);
                Store.Log("恢复: 成功 " + n + " 个窗口（按记录还原）, " + sw.ElapsedMilliseconds + "ms");
                return n;
            }

            // 兜底：没有记录时（例如中途重启过工具），只还原各目标进程的主窗口，
            // 绝不去枚举"所有不可见窗口"，否则会把托盘消息窗之类的内部窗口弹出来。
            Dictionary<int, List<Target>> map = ResolveTargets(targets);
            if (map.Count == 0)
            {
                Store.Log("恢复: 目标进程未运行 (0 个窗口, " + sw.ElapsedMilliseconds + "ms)");
                return 0;
            }

            foreach (int pid in map.Keys)
            {
                IntPtr main = IntPtr.Zero;
                try
                {
                    using (Process p = Process.GetProcessById(pid)) { main = p.MainWindowHandle; }
                }
                catch { continue; }

                if (main == IntPtr.Zero || !Win32.IsWindow(main)) continue;
                if (Win32.IsWindowVisible(main)) { n++; continue; }
                if (!IsRealWindow(main)) continue;
                if (Win32.IsIconic(main)) Win32.ShowWindow(main, Win32.SW_RESTORE);
                Win32.ShowWindow(main, Win32.SW_SHOW);
                if (Win32.IsWindowVisible(main)) { n++; last = main; }
            }

            if (last != IntPtr.Zero) Win32.SetForegroundWindow(last);
            Store.Log("恢复: 成功 " + n + " 个窗口（兜底：主窗口）, " + sw.ElapsedMilliseconds + "ms");
            return n;
        }
    }

    internal class TrayApp : Form
    {
        private NotifyIcon icon;
        private const int HOTKEY_HIDE = 1;
        private const int HOTKEY_SHOW = 2;
        private ToolStripMenuItem miHide;
        private ToolStripMenuItem miShow;
        private ToolStripMenuItem miToggle;
        private Hotkey curHide;
        private Hotkey curShow;
        private bool sameKey;

        public TrayApp()
        {
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
            FormBorderStyle = FormBorderStyle.None;
            Opacity = 0;
            Size = new Size(1, 1);
            ShowIcon = false;

            BuildTray();

            bool same = Store.GetFlag("samekey", false);
            Hotkey h = Hotkey.Parse(Store.GetHotkey("hide", "Ctrl+Alt+H"));
            Hotkey s = Hotkey.Parse(Store.GetHotkey("show", "Ctrl+Alt+R"));
            if (h == null) h = Hotkey.Parse("Ctrl+Alt+H");
            if (s == null) s = Hotkey.Parse("Ctrl+Alt+R");
            if (same) s = h;
            bool ok = ApplyHotkeys(h, s, same, true);
            if (ok) ScheduleStartupBalloon();
        }

        /// <summary>启动提示要等消息循环跑起来才能显示，用一次性定时器延后触发。</summary>
        private void ScheduleStartupBalloon()
        {
            var timer = new System.Windows.Forms.Timer();
            timer.Interval = 1200;
            timer.Tick += delegate
            {
                timer.Stop();
                timer.Dispose();
                Balloon("WinHide 已启动", HotkeyHint() + "。右键托盘图标可调整。");
            };
            timer.Start();
        }

        /// <summary>按当前真实热键生成提示文案，避免提示里写死 Ctrl+Alt+H。</summary>
        private string HotkeyHint()
        {
            return Hotkey.HintText(curHide, curShow, sameKey);
        }

        private void UpdateMenuTexts()
        {
            if (miHide != null && curHide != null)
                miHide.Text = sameKey ? "隐藏目标窗口" : "隐藏目标窗口    " + curHide.Text;
            if (miShow != null && curShow != null)
                miShow.Text = sameKey ? "恢复目标窗口" : "恢复目标窗口    " + curShow.Text;
            if (miToggle != null && curHide != null)
            {
                miToggle.Checked = sameKey;
                miToggle.Text = sameKey
                    ? "隐藏/恢复同键切换    " + curHide.Text
                    : "隐藏/恢复同键切换（未启用）";
            }
            // 托盘悬停提示也跟随当前热键
            if (icon != null && curHide != null)
            {
                string tip = "WinHide：" + HotkeyHint();
                if (tip.Length > 62) tip = tip.Substring(0, 62);
                icon.Text = tip;
            }
        }

        private bool ApplyHotkeys(Hotkey h, Hotkey s, bool same, bool initial)
        {
            Win32.UnregisterHotKey(Handle, HOTKEY_HIDE);
            Win32.UnregisterHotKey(Handle, HOTKEY_SHOW);

            bool ok1;
            bool ok2 = true;
            if (same)
            {
                ok1 = Win32.RegisterHotKey(Handle, HOTKEY_HIDE, h.Mods, h.Vk);
            }
            else
            {
                ok1 = Win32.RegisterHotKey(Handle, HOTKEY_HIDE, h.Mods, h.Vk);
                ok2 = Win32.RegisterHotKey(Handle, HOTKEY_SHOW, s.Mods, s.Vk);
            }

            if (!ok1 || !ok2)
            {
                Store.Log("热键注册失败 hide=" + ok1 + " show=" + ok2 + " (" + h.Text + " / " + s.Text + ", same=" + same + ")");
                if (!initial && curHide != null && curShow != null)
                {
                    Win32.UnregisterHotKey(Handle, HOTKEY_HIDE);
                    Win32.UnregisterHotKey(Handle, HOTKEY_SHOW);
                    Win32.RegisterHotKey(Handle, HOTKEY_HIDE, curHide.Mods, curHide.Vk);
                    if (!sameKey) Win32.RegisterHotKey(Handle, HOTKEY_SHOW, curShow.Mods, curShow.Vk);
                }
                Balloon("热键被占用", h.Text + (same ? "" : " 或 " + s.Text) + " 无法注册，可能与其他程序冲突。");
                return false;
            }

            sameKey = same;
            curHide = h;
            curShow = same ? h : s;
            UpdateMenuTexts();
            return true;
        }

        private void DoToggle()
        {
            List<Target> targets = Store.Load();
            if (targets.Count == 0)
            {
                Balloon("还没有设置目标", "右键托盘图标 → 选择目标程序…");
                return;
            }
            if (Windows.CountVisible(targets) > 0) Windows.Hide(targets);
            else Windows.Show(targets);
        }

        private void ToggleSameKey()
        {
            bool want = !sameKey;
            Hotkey s = curShow;
            if (!want && (s == null || string.Equals(s.Text, curHide.Text, StringComparison.OrdinalIgnoreCase)))
                s = Hotkey.Parse("Ctrl+Alt+R");
            if (ApplyHotkeys(curHide, s, want, false))
            {
                Store.SaveHotkeys(curHide.Text, curShow.Text, sameKey);
                Balloon("已切换", want
                    ? "隐藏与恢复共用 " + curHide.Text + "，按一次切换状态。"
                    : "已分开：隐藏 " + curHide.Text + "，恢复 " + curShow.Text);
            }
        }

        private void DoHotkeySettings()
        {
            using (var f = new HotkeyForm(curHide, curShow, sameKey))
            {
                if (f.ShowDialog() != DialogResult.OK) return;
                Hotkey h = Hotkey.Parse(f.HideText);
                Hotkey s = f.SameKey ? h : Hotkey.Parse(f.ShowText);
                if (h == null || s == null)
                {
                    MessageBox.Show("热键格式无效：必须包含 Ctrl/Alt/Shift/Win 中至少一个修饰键，并带一个主键。",
                        "WinHide", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (!f.SameKey && string.Equals(h.Text, s.Text, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("未启用同键模式时，隐藏和恢复不能使用同一个热键。\n勾选「隐藏与恢复使用同一个热键」即可。",
                        "WinHide", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (ApplyHotkeys(h, s, f.SameKey, false))
                {
                    Store.SaveHotkeys(h.Text, s.Text, f.SameKey);
                    Balloon("热键已更新", f.SameKey
                        ? "同键切换：" + h.Text
                        : "隐藏：" + h.Text + "　恢复：" + s.Text);
                }
            }
        }

        private void Balloon(string title, string text)
        {
            try
            {
                icon.ShowBalloonTip(3000, title, text, ToolTipIcon.Info);
            }
            catch { }
        }

        private void BuildTray()
        {
            icon = new NotifyIcon();
            icon.Icon = MakeIcon();
            icon.Text = "WinHide —— 隐藏/恢复指定窗口";
            icon.Visible = true;

            var menu = new ContextMenuStrip();
            miHide = new ToolStripMenuItem("隐藏目标窗口");
            miHide.Click += delegate { DoHide(); };
            miShow = new ToolStripMenuItem("恢复目标窗口");
            miShow.Click += delegate { DoShow(); };
            var miHotkey = new ToolStripMenuItem("设置热键…");
            miHotkey.Click += delegate { DoHotkeySettings(); };
            miToggle = new ToolStripMenuItem("隐藏/恢复同键切换");
            miToggle.Click += delegate { ToggleSameKey(); };
            var miPick = new ToolStripMenuItem("选择目标程序…");
            miPick.Click += delegate { DoPick(); };
            var miEdit = new ToolStripMenuItem("编辑目标列表（记事本）");
            miEdit.Click += delegate { OpenConfig(); };
            var miAuto = new ToolStripMenuItem("开机自动启动");
            miAuto.CheckOnClick = true;
            miAuto.Checked = Autostart.IsEnabled();
            miAuto.Click += delegate { Autostart.Set(miAuto.Checked); };
            var miLog = new ToolStripMenuItem("打开数据目录");
            miLog.Click += delegate { Process.Start("explorer.exe", Store.Dir); };
            var miExit = new ToolStripMenuItem("退出");
            miExit.Click += delegate { Close(); };

            menu.Items.Add(miHide);
            menu.Items.Add(miShow);
            menu.Items.Add(miToggle);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miHotkey);
            menu.Items.Add(miPick);
            menu.Items.Add(miEdit);
            menu.Items.Add(miAuto);
            menu.Items.Add(miLog);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miExit);

            icon.ContextMenuStrip = menu;
            icon.DoubleClick += delegate { DoHide(); };
        }

        private static Icon MakeIcon()
        {
            // 优先用编译时嵌入的 app.ico，保证托盘与 exe 文件图标一致
            try
            {
                Stream s = typeof(TrayApp).Assembly.GetManifestResourceStream("app.ico");
                if (s != null)
                {
                    using (s) { return new Icon(s); }
                }
            }
            catch { }

            // 兜底：运行时绘制
            var bmp = new Bitmap(32, 32);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var brush = new SolidBrush(Color.FromArgb(37, 99, 235)))
                {
                    g.FillEllipse(brush, 1, 1, 30, 30);
                }
                using (var pen = new Pen(Color.White, 3))
                {
                    g.DrawLine(pen, 8, 11, 24, 11);
                    g.DrawLine(pen, 8, 21, 24, 21);
                    g.DrawLine(pen, 16, 13, 16, 19);
                }
            }
            IntPtr h = bmp.GetHicon();
            return Icon.FromHandle(h);
        }

        private void DoHide()
        {
            List<Target> targets = Store.Load();
            if (targets.Count == 0)
            {
                Balloon("还没有设置目标", "右键托盘图标 → 选择目标程序…");
                return;
            }
            int n = Windows.Hide(targets);
            if (n == 0)
            {
                if (Windows.CountVisible(targets) == 0)
                    Balloon("已经是隐藏状态", "目标窗口当前都不可见，按恢复键即可回来。");
                else
                    Balloon("没有隐藏成功", "目标可能未运行，或它是以管理员身份运行的（本工具也需以管理员身份启动）。");
            }
        }

        private void DoShow()
        {
            List<Target> targets = Store.Load();
            if (targets.Count == 0) return;
            int n = Windows.Show(targets);
            if (n == 0) Balloon("没有找到目标窗口", "目标程序可能已经关闭。");
        }

        private void DoPick()
        {
            using (var f = new PickerForm())
            {
                f.ShowDialog();
            }
            string hint = HotkeyHint();
            Balloon("目标已更新", "共 " + Store.Load().Count + " 项目标" +
                (hint.Length > 0 ? "；" + hint : "") + "。");
        }

        private static void OpenConfig()
        {
            Store.Load();
            Process.Start("notepad.exe", "\"" + Store.ConfigPath + "\"");
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Win32.WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HOTKEY_HIDE) { if (sameKey) DoToggle(); else DoHide(); }
                else if (id == HOTKEY_SHOW) DoShow();
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            Win32.UnregisterHotKey(Handle, HOTKEY_HIDE);
            Win32.UnregisterHotKey(Handle, HOTKEY_SHOW);
            if (icon != null) { icon.Visible = false; icon.Dispose(); }
            base.OnFormClosing(e);
        }
    }

    internal static class Autostart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "WinHide";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (k == null) return false;
                    object v = k.GetValue(ValueName);
                    return v != null;
                }
            }
            catch { return false; }
        }

        public static void Set(bool enable)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (enable)
                    {
                        string exe = Application.ExecutablePath;
                        k.SetValue(ValueName, "\"" + exe + "\" --tray");
                    }
                    else
                    {
                        if (k.GetValue(ValueName) != null) k.DeleteValue(ValueName, false);
                    }
                }
            }
            catch (Exception ex) { Store.Log("设置开机启动失败: " + ex.Message); }
        }
    }

    internal class PickerForm : Form
    {
        private ListView list;
        private List<WinInfo> windows;

        public PickerForm()
        {
            Text = "选择要隐藏的目标程序";
            Width = 620;
            Height = 460;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            ShowInTaskbar = true;

            // 用 TableLayoutPanel 固定三段布局，避免 Dock 顺序导致的遮挡问题
            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

            var hint = new Label();
            hint.Text = "勾选要管理的窗口，确定后保存。（同一程序的全部窗口都会被一起隐藏/恢复）";
            hint.Dock = DockStyle.Fill;
            hint.TextAlign = ContentAlignment.MiddleLeft;
            hint.Padding = new Padding(8, 0, 8, 0);

            list = new ListView();
            list.Dock = DockStyle.Fill;
            list.View = View.Details;
            list.CheckBoxes = true;
            list.FullRowSelect = true;
            list.Columns.Add("程序", 150);
            list.Columns.Add("窗口标题", 420);

            var panel = new Panel();
            panel.Dock = DockStyle.Fill;

            var ok = new Button();
            ok.Text = "确定";
            ok.DialogResult = DialogResult.OK;
            ok.Width = 88;
            ok.Height = 30;
            ok.Left = Width - 230;
            ok.Top = 9;

            var cancel = new Button();
            cancel.Text = "取消";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Width = 88;
            cancel.Height = 30;
            cancel.Left = Width - 130;
            cancel.Top = 9;

            panel.Controls.Add(ok);
            panel.Controls.Add(cancel);

            root.Controls.Add(hint, 0, 0);
            root.Controls.Add(list, 0, 1);
            root.Controls.Add(panel, 0, 2);

            Controls.Add(root);
            AcceptButton = ok;
            CancelButton = cancel;

            Load += delegate { Fill(); };
        }

        private void Fill()
        {
            list.BeginUpdate();
            list.Items.Clear();
            windows = Windows.Enumerate(true, "WinHide");
            List<Target> current = Store.Load();
            for (int i = 0; i < windows.Count; i++)
            {
                WinInfo w = windows[i];
                var item = new ListViewItem(w.Process);
                item.SubItems.Add(w.Title);
                item.Tag = w;
                for (int k = 0; k < current.Count; k++)
                {
                    if (Windows.Matches(current[k], w)) { item.Checked = true; break; }
                }
                list.Items.Add(item);
            }
            list.EndUpdate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult == DialogResult.OK)
            {
                var result = new List<Target>();
                for (int i = 0; i < list.Items.Count; i++)
                {
                    if (!list.Items[i].Checked) continue;
                    var t = new Target();
                    t.Process = list.Items[i].Text;
                    // 同一进程多个窗口时只保留一条（按进程匹配全部窗口）
                    bool exists = false;
                    for (int k = 0; k < result.Count; k++)
                    {
                        if (string.Equals(result[k].Process, t.Process, StringComparison.OrdinalIgnoreCase)) { exists = true; break; }
                    }
                    if (!exists) result.Add(t);
                }
                Store.Save(result);
            }
            base.OnFormClosing(e);
        }
    }

    internal class HotkeyForm : Form
    {
        public string HideText { get { return boxHide.Text; } }
        public string ShowText { get { return cbSame.Checked ? boxHide.Text : boxShow.Text; } }
        public bool SameKey { get { return cbSame.Checked; } }

        private TextBox boxHide;
        private TextBox boxShow;
        private CheckBox cbSame;

        public HotkeyForm(Hotkey hide, Hotkey show, bool sameKey)
        {
            Text = "设置热键";
            Width = 442;
            Height = 258;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;

            var tip = new Label();
            tip.Text = "点进输入框后直接按组合键（需含 Ctrl / Alt / Shift / Win 至少一个）。";
            tip.SetBounds(14, 10, 400, 32);

            var l1 = new Label();
            l1.Text = "隐藏：";
            l1.SetBounds(14, 56, 60, 24);
            boxHide = new TextBox();
            boxHide.SetBounds(80, 54, 340, 24);
            boxHide.ReadOnly = true;
            boxHide.Text = hide != null ? hide.Text : "Ctrl+Alt+H";
            boxHide.KeyDown += CaptureKey;

            var l2 = new Label();
            l2.Text = "恢复：";
            l2.SetBounds(14, 92, 60, 24);
            boxShow = new TextBox();
            boxShow.SetBounds(80, 90, 340, 24);
            boxShow.ReadOnly = true;
            boxShow.Text = show != null ? show.Text : "Ctrl+Alt+R";
            boxShow.KeyDown += CaptureKey;

            cbSame = new CheckBox();
            cbSame.Text = "隐藏与恢复使用同一个热键（按一次切换状态）";
            cbSame.SetBounds(80, 124, 340, 24);
            cbSame.Checked = sameKey;
            cbSame.CheckedChanged += delegate
            {
                boxShow.Enabled = !cbSame.Checked;
                if (cbSame.Checked) boxShow.Text = boxHide.Text;
            };
            boxShow.Enabled = !sameKey;
            if (sameKey) boxShow.Text = boxHide.Text;

            var reset = new Button();
            reset.Text = "恢复默认";
            reset.SetBounds(14, 162, 92, 30);
            reset.Click += delegate
            {
                cbSame.Checked = false;
                boxHide.Text = "Ctrl+Alt+H";
                boxShow.Text = "Ctrl+Alt+R";
            };

            var ok = new Button();
            ok.Text = "确定";
            ok.DialogResult = DialogResult.OK;
            ok.SetBounds(222, 162, 92, 30);

            var cancel = new Button();
            cancel.Text = "取消";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.SetBounds(322, 162, 92, 30);

            Controls.Add(tip);
            Controls.Add(l1);
            Controls.Add(boxHide);
            Controls.Add(l2);
            Controls.Add(boxShow);
            Controls.Add(cbSame);
            Controls.Add(reset);
            Controls.Add(ok);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        private void CaptureKey(object sender, KeyEventArgs e)
        {
            TextBox tb = sender as TextBox;
            if (tb == null) return;
            e.Handled = true;
            e.SuppressKeyPress = true;

            Hotkey h = Hotkey.FromKeys(e.Modifiers, e.KeyCode);
            if (h == null) return;   // 只按了修饰键，或是不支持的键
            tb.Text = h.Text;
            if (cbSame.Checked && tb == boxHide) boxShow.Text = h.Text;
        }
    }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "--tray";

            if (mode == "--hide")
            {
                List<Target> t = Store.Load();
                if (t.Count == 0) { Store.Log("--hide: 未配置目标"); return 1; }
                int n = Windows.Hide(t);
                return n > 0 ? 0 : 2;
            }
            if (mode == "--show")
            {
                List<Target> t = Store.Load();
                if (t.Count == 0) { Store.Log("--show: 未配置目标"); return 1; }
                int n = Windows.Show(t);
                return n > 0 ? 0 : 2;
            }
            if (mode == "--list")
            {
                List<WinInfo> ws = Windows.Enumerate(true, null);
                var sb = new StringBuilder();
                for (int i = 0; i < ws.Count; i++)
                {
                    sb.AppendLine(ws[i].Process + " | " + ws[i].Title);
                }
                File.WriteAllText(Path.Combine(Store.Dir, "windows.txt"), sb.ToString(), Encoding.UTF8);
                Store.Log("--list: " + ws.Count + " 个窗口 -> windows.txt");
                return 0;
            }
            if (mode == "--pick")
            {
                using (var f = new PickerForm()) { f.ShowDialog(); }
                return 0;
            }

            // 默认：托盘常驻
            bool created;
            using (new Mutex(true, "WinHide_Tray_SingleInstance", out created))
            {
                if (!created) return 0;
                Store.Log("启动托盘");
                Application.Run(new TrayApp());
            }
            return 0;
        }
    }
}
