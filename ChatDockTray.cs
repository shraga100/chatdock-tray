// ChatDock tray helper: a small Windows program that Chrome starts through
// native messaging (chrome.runtime.connectNative) while "tray icon" is on.
//
// * Shows ChatDock's icon (with the unread count) in the notification area.
// * Keeps the windows the extension hands it (ChatDock's hidden windows: the
//   ready copy left after the X, pre-loaded alert windows) off the taskbar
//   (ITaskbarList.DeleteTab), fully hidden while minimized. Shown windows are
//   released back to the taskbar by the extension. A click on the tray icon
//   tells the extension to show the main window; the menu lists the windows.
// * When Chrome closes the connection (extension off, Chrome closing), every
//   window is put back on the taskbar, so nothing is ever lost.
//
// Messages are JSON, each preceded by its length (4 bytes, native order),
// as Chrome's native messaging protocol requires.
// * Shows the unread count on the taskbar button of ChatDock's shown windows
//   (ITaskbarList3.SetOverlayIcon), as chat apps do.
//
//   from the extension: {cmd:"status", text, tooltip, connected}
//                       {cmd:"track", id, marker}   find the window whose title is marker
//                       {cmd:"release", id}         shown again: back on the taskbar, with the count
//                       {cmd:"untrack", id}         closed / no longer ChatDock's
//   to the extension:   {event:"hello", version}       at start (2 = knows "release")
//                       {event:"tracked", id, ok}
//                       {event:"click"} / {event:"options"} / {event:"show", id}
//
// Build: install.ps1 (uses the C# compiler that ships with Windows).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ChatDock
{
    // ITaskbarList3, declared with every method in vtable order (ITaskbarList,
    // ITaskbarList2, then ITaskbarList3) up to the one used, SetOverlayIcon.
    [ComImport, Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ITaskbarList3
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
        void SetProgressState(IntPtr hwnd, int state);
        void RegisterTab(IntPtr tab, IntPtr mdi);
        void UnregisterTab(IntPtr tab);
        void SetTabOrder(IntPtr tab, IntPtr insertBefore);
        void SetTabActive(IntPtr tab, IntPtr mdi, uint flags);
        void ThumbBarAddButtons(IntPtr hwnd, uint count, IntPtr buttons);
        void ThumbBarUpdateButtons(IntPtr hwnd, uint count, IntPtr buttons);
        void ThumbBarSetImageList(IntPtr hwnd, IntPtr imageList);
        void SetOverlayIcon(IntPtr hwnd, IntPtr icon, [MarshalAs(UnmanagedType.LPWStr)] string description);
    }

    [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    class TaskbarListClass { }

    static class Native
    {
        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
        public delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int pid);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc cb, uint pid, uint thread, uint flags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);

        public const int SW_HIDE = 0, SW_SHOWMINNOACTIVE = 7, SW_RESTORE = 9;
        public const int ASFW_ANY = -1;
        public const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016, EVENT_OBJECT_SHOW = 0x8002;
        public const uint WINEVENT_OUTOFCONTEXT = 0;
    }

    class TrayApp : ApplicationContext
    {
        readonly Control ui = new Control();
        readonly NotifyIcon icon = new NotifyIcon();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly Dictionary<long, IntPtr> windows = new Dictionary<long, IntPtr>();
        readonly HashSet<IntPtr> hidden = new HashSet<IntPtr>();
        // Known windows that are shown (on the taskbar, with the count).
        readonly HashSet<long> released = new HashSet<long>();
        string count = "";
        IntPtr overlayHandle = IntPtr.Zero;
        int overlayTick = 0;
        readonly JavaScriptSerializer json = new JavaScriptSerializer();
        readonly object writeLock = new object();
        readonly Stream stdout = Console.OpenStandardOutput();
        readonly System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer();
        readonly Native.WinEventProc hookProc;
        readonly List<IntPtr> hooks = new List<IntPtr>();
        ITaskbarList3 taskbar;
        Bitmap onBase, offBase;
        IntPtr iconHandle = IntPtr.Zero;

        public TrayApp()
        {
            ui.CreateControl(); // so BeginInvoke works from the reader thread
            try { taskbar = (ITaskbarList3)new TaskbarListClass(); taskbar.HrInit(); } catch { taskbar = null; }
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            onBase = Load(Path.Combine(dir, "icon.png"));
            offBase = Load(Path.Combine(dir, "icon-off.png")) ?? onBase;
            SetIcon(false, "");
            icon.Text = "ChatDock";
            icon.Visible = true;
            icon.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) Click(); };
            menu.Opening += (s, e) => BuildMenu();
            menu.RightToLeft = RightToLeft.Yes;
            icon.ContextMenuStrip = menu;

            // Minimized ChatDock windows go to the tray at once; a window that
            // shows again is taken off the taskbar again.
            hookProc = OnWinEvent;
            hooks.Add(Native.SetWinEventHook(Native.EVENT_SYSTEM_MINIMIZESTART, Native.EVENT_SYSTEM_MINIMIZESTART, IntPtr.Zero, hookProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT));
            hooks.Add(Native.SetWinEventHook(Native.EVENT_OBJECT_SHOW, Native.EVENT_OBJECT_SHOW, IntPtr.Zero, hookProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT));
            // And a slow check, in case an event was missed.
            tick.Interval = 1000;
            tick.Tick += (s, e) => Sweep();
            tick.Start();

            new Thread(ReadLoop) { IsBackground = true }.Start();
            Send(new { @event = "hello", version = 2 });
        }

        static Bitmap Load(string path)
        {
            try { return File.Exists(path) ? new Bitmap(path) : null; } catch { return null; }
        }

        // ------------------------------------------------------- messages

        void ReadLoop()
        {
            var stdin = Console.OpenStandardInput();
            var len = new byte[4];
            try
            {
                while (true)
                {
                    if (!ReadExactly(stdin, len, 4)) break;
                    int n = BitConverter.ToInt32(len, 0);
                    if (n <= 0 || n > 1024 * 1024) break;
                    var buf = new byte[n];
                    if (!ReadExactly(stdin, buf, n)) break;
                    var msg = json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(buf));
                    ui.BeginInvoke((Action)(() => Handle(msg)));
                }
            }
            catch { }
            ui.BeginInvoke((Action)Quit); // Chrome closed the connection
        }

        static bool ReadExactly(Stream s, byte[] buf, int n)
        {
            int got = 0;
            while (got < n)
            {
                int r = s.Read(buf, got, n - got);
                if (r <= 0) return false;
                got += r;
            }
            return true;
        }

        void Send(object msg)
        {
            var bytes = Encoding.UTF8.GetBytes(json.Serialize(msg));
            lock (writeLock)
            {
                try
                {
                    stdout.Write(BitConverter.GetBytes(bytes.Length), 0, 4);
                    stdout.Write(bytes, 0, bytes.Length);
                    stdout.Flush();
                }
                catch { }
            }
        }

        static string Str(Dictionary<string, object> m, string k)
        {
            object v;
            return m.TryGetValue(k, out v) && v != null ? Convert.ToString(v) : "";
        }

        static long Num(Dictionary<string, object> m, string k)
        {
            object v;
            try { return m.TryGetValue(k, out v) && v != null ? Convert.ToInt64(v) : 0; } catch { return 0; }
        }

        void Handle(Dictionary<string, object> msg)
        {
            switch (Str(msg, "cmd"))
            {
                case "status":
                    SetIcon(Str(msg, "connected") == "True", Str(msg, "text"));
                    string tip = Str(msg, "tooltip");
                    if (tip.Length == 0) tip = "ChatDock";
                    icon.Text = tip.Length > 63 ? tip.Substring(0, 62) + "…" : tip;
                    string text = Str(msg, "connected") == "True" ? Str(msg, "text") : "";
                    if (text != count) { count = text; MakeOverlay(); }
                    UpdateOverlays();
                    break;
                case "track":
                    released.Remove(Num(msg, "id"));
                    Track(Num(msg, "id"), Str(msg, "marker"), 0);
                    break;
                case "release":
                    Release(Num(msg, "id"));
                    break;
                case "untrack":
                    Untrack(Num(msg, "id"), false);
                    break;
            }
        }

        // ------------------------------------------------------- windows

        // The extension briefly sets the page title to a unique marker; the
        // window with that title is ChatDock's.
        void Track(long id, string marker, int attempt)
        {
            if (marker.Length == 0) return;
            IntPtr found = IntPtr.Zero;
            Native.EnumWindows((h, l) =>
            {
                var sb = new StringBuilder(512);
                Native.GetWindowText(h, sb, sb.Capacity);
                if (sb.ToString().Contains(marker)) { found = h; return false; }
                return true;
            }, IntPtr.Zero);
            if (found == IntPtr.Zero)
            {
                if (attempt >= 30) { Send(new { @event = "tracked", id = id, ok = false }); return; }
                var t = new System.Windows.Forms.Timer { Interval = 100 };
                t.Tick += (s, e) => { t.Stop(); t.Dispose(); Track(id, marker, attempt + 1); };
                t.Start();
                return;
            }
            windows[id] = found;
            if (released.Contains(id)) SetOverlay(found);
            else Apply(found);
            Send(new { @event = "tracked", id = id, ok = true });
        }

        // Shown: back on the taskbar, and it keeps the count on its button.
        void Release(long id)
        {
            released.Add(id);
            IntPtr h;
            if (!windows.TryGetValue(id, out h)) return; // found later (Track)
            Restore(h);
            SetOverlay(h);
        }

        void Untrack(long id, bool keep)
        {
            released.Remove(id);
            IntPtr h;
            if (!windows.TryGetValue(id, out h)) return;
            windows.Remove(id);
            ClearOverlay(h);
            Restore(h);
        }

        // Hidden ChatDock windows (tracked and not released) stay off the taskbar.
        bool IsTracked(IntPtr h)
        {
            foreach (var kv in windows) if (kv.Value == h && !released.Contains(kv.Key)) return true;
            return false;
        }

        // ------------------------------------------------- taskbar count

        // The count as a small red disc, the size Windows draws overlays (16 px).
        void MakeOverlay()
        {
            IntPtr old = overlayHandle;
            overlayHandle = IntPtr.Zero;
            if (count.Length > 0)
            {
                using (var bmp = new Bitmap(16, 16))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                        using (var red = new SolidBrush(Color.FromArgb(217, 48, 37))) g.FillEllipse(red, 0, 0, 15, 15);
                        string t = count.Length > 2 ? "+" : count;
                        using (var f = new Font("Segoe UI", t.Length > 1 ? 7.5f : 10f, FontStyle.Bold, GraphicsUnit.Pixel))
                        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        {
                            g.DrawString(t, f, Brushes.White, new RectangleF(0, 0.5f, 16, 16), sf);
                        }
                    }
                    overlayHandle = bmp.GetHicon();
                }
            }
            UpdateOverlays();
            if (old != IntPtr.Zero) Native.DestroyIcon(old);
        }

        void SetOverlay(IntPtr h)
        {
            if (taskbar == null || !Native.IsWindow(h)) return;
            string what = count.Length == 0 ? null : (Hebrew ? count + " הודעות שלא נקראו" : count + " unread");
            try { taskbar.SetOverlayIcon(h, overlayHandle, what); } catch { }
        }

        void ClearOverlay(IntPtr h)
        {
            if (taskbar == null || !Native.IsWindow(h)) return;
            try { taskbar.SetOverlayIcon(h, IntPtr.Zero, null); } catch { }
        }

        void UpdateOverlays()
        {
            foreach (var kv in windows) if (released.Contains(kv.Key)) SetOverlay(kv.Value);
        }

        void Apply(IntPtr h)
        {
            if (!Native.IsWindow(h)) return;
            if (Native.IsIconic(h) && Native.IsWindowVisible(h))
            {
                Native.ShowWindow(h, Native.SW_HIDE);
                hidden.Add(h);
            }
            if (taskbar != null) { try { taskbar.DeleteTab(h); } catch { } }
        }

        // Back to how Chrome had it: on the taskbar, minimized if it was hidden.
        void Restore(IntPtr h)
        {
            if (!Native.IsWindow(h)) return;
            if (!Native.IsWindowVisible(h)) Native.ShowWindow(h, Native.SW_SHOWMINNOACTIVE);
            hidden.Remove(h);
            if (taskbar != null) { try { taskbar.AddTab(h); } catch { } }
        }

        void OnWinEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (idObject != 0 || !IsTracked(hwnd)) return;
            if (ev == Native.EVENT_SYSTEM_MINIMIZESTART)
            {
                // After the minimize has happened (hiding mid-way confuses it).
                ui.BeginInvoke((Action)(() =>
                {
                    var t = new System.Windows.Forms.Timer { Interval = 50 };
                    t.Tick += (s, e) => { t.Stop(); t.Dispose(); Apply(hwnd); };
                    t.Start();
                }));
            }
            else
            {
                hidden.Remove(hwnd);
                ui.BeginInvoke((Action)(() => Apply(hwnd)));
            }
        }

        void Sweep()
        {
            var gone = new List<long>();
            foreach (var kv in windows)
            {
                if (!Native.IsWindow(kv.Value)) gone.Add(kv.Key);
                else if (!released.Contains(kv.Key)) Apply(kv.Value);
            }
            foreach (var id in gone) { hidden.Remove(windows[id]); windows.Remove(id); released.Remove(id); }
            // Chrome may draw its own overlay (its profile picture) over ours: put
            // the count back now and then.
            if (++overlayTick % 5 == 0) UpdateOverlays();
        }

        // ---------------------------------------------------------- tray

        void Click()
        {
            // The click is the user's: let Chrome bring its window to the front.
            Native.AllowSetForegroundWindow(Native.ASFW_ANY);
            Send(new { @event = "click" });
        }

        // Menu words follow the Windows display language (Hebrew or English).
        static readonly bool Hebrew =
            System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "he";

        void BuildMenu()
        {
            menu.Items.Clear();
            menu.RightToLeft = Hebrew ? RightToLeft.Yes : RightToLeft.No;
            menu.Items.Add(Hebrew ? "פתיחת ChatDock" : "Open ChatDock", null, (s, e) => Click());
            var list = new List<KeyValuePair<long, IntPtr>>();
            foreach (var kv in windows) if (!released.Contains(kv.Key)) list.Add(kv);
            if (list.Count > 0) menu.Items.Add(new ToolStripSeparator());
            foreach (var kv in list)
            {
                if (!Native.IsWindow(kv.Value)) continue;
                var sb = new StringBuilder(256);
                Native.GetWindowText(kv.Value, sb, sb.Capacity);
                string title = sb.ToString();
                if (title.Length == 0) continue;
                if (title.Length > 60) title = title.Substring(0, 59) + "…";
                long id = kv.Key;
                var item = menu.Items.Add(title, null, (s, e) =>
                {
                    Native.AllowSetForegroundWindow(Native.ASFW_ANY);
                    Send(new { @event = "show", id = id });
                });
                if (!Native.IsWindowVisible(kv.Value) || Native.IsIconic(kv.Value)) item.Font = new Font(item.Font, FontStyle.Italic);
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Hebrew ? "הגדרות ChatDock" : "ChatDock options", null, (s, e) => { Native.AllowSetForegroundWindow(Native.ASFW_ANY); Send(new { @event = "options" }); });
        }

        void SetIcon(bool connected, string text)
        {
            var size = SystemInformation.SmallIconSize;
            using (var bmp = new Bitmap(size.Width, size.Height))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    var src = connected ? onBase : offBase;
                    if (src != null) g.DrawImage(src, 0, 0, size.Width, size.Height);
                    else g.FillEllipse(Brushes.SeaGreen, 0, 0, size.Width - 1, size.Height - 1);
                    if (!string.IsNullOrEmpty(text))
                    {
                        // The unread count in a red circle, bottom corner (like the toolbar badge).
                        float d = size.Width * 0.62f;
                        var box = new RectangleF(size.Width - d, size.Height - d, d, d);
                        using (var red = new SolidBrush(Color.FromArgb(217, 48, 37))) g.FillEllipse(red, box);
                        string t = text.Length > 2 ? "+" : text;
                        using (var f = new Font("Segoe UI", d * (t.Length > 1 ? 0.48f : 0.62f), FontStyle.Bold, GraphicsUnit.Pixel))
                        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        {
                            g.DrawString(t, f, Brushes.White, box, sf);
                        }
                    }
                }
                IntPtr h = bmp.GetHicon();
                icon.Icon = Icon.FromHandle(h);
                if (iconHandle != IntPtr.Zero) Native.DestroyIcon(iconHandle);
                iconHandle = h;
            }
        }

        void Quit()
        {
            foreach (var h in windows.Values) { ClearOverlay(h); Restore(h); }
            windows.Clear();
            foreach (var hook in hooks) Native.UnhookWinEvent(hook);
            icon.Visible = false;
            icon.Dispose();
            ExitThread();
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.Run(new TrayApp());
        }
    }
}
