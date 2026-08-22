using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AgentStatusBar
{
    /// <summary>托盘主程序：图标/提示/菜单 + 任务栏状态条 + 详情面板 的装配与刷新。</summary>
    class TrayApp : ApplicationContext
    {
        readonly StatusEngine engine;
        readonly NotifyIcon tray;
        readonly PopupForm popup;
        readonly TaskbarStripForm strip;
        readonly System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer();
        ToolStripMenuItem miStrip;

        AgentStatus last;
        Icon currentIcon;
        int frame;
        string lastIconKey = "";
        string lastTip = "";
        DateTime popupHiddenAt = DateTime.MinValue;
        const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        const string AppName = "AgentStatusBar";

        public TrayApp()
        {
            engine = new StatusEngine();
            engine.Changed += OnEngineChanged;

            popup = new PopupForm(engine.Compute);
            popup.VisibleChanged += delegate { if (!popup.Visible) popupHiddenAt = DateTime.Now; };

            strip = new TaskbarStripForm();
            strip.LeftClick = delegate { TogglePopup(strip.DockRect); };
            strip.MenuProvider = delegate { return tray.ContextMenuStrip; };

            tray = new NotifyIcon();
            tray.Text = "Agent Status — 初始化";
            SetIcon(Phase.NoData, 0);
            tray.ContextMenuStrip = BuildMenu();
            tray.MouseClick += OnTrayClick;
            tray.MouseMove += delegate { if (last != null) UpdateTooltip(last); };
            tray.Visible = true;

            if (miStrip.Checked) SetStripVisible(true);

            anim.Interval = 400;
            anim.Tick += delegate
            {
                frame++;
                if (last != null && last.Running) SetIcon(last.Overall, frame);
            };
            anim.Start();

            // 订阅系统主题变更广播（WM_SETTINGCHANGE / ImmersiveColorSet），切换深浅模式即时重绘
            SystemEvents.UserPreferenceChanged += OnPreferenceChanged;

            RefreshAll();
        }

        void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General) return;
            try { StyleMenu(tray.ContextMenuStrip); }
            catch { }
        }

        ContextMenuStrip BuildMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();

            ToolStripMenuItem miPanel = new ToolStripMenuItem("状态面板", null, delegate { TogglePopup(strip.DockRect); });
            miStrip = new ToolStripMenuItem("任务栏状态条");
            miStrip.CheckOnClick = true;
            miStrip.Checked = Settings.GetBool("TaskbarStrip", true);
            miStrip.CheckedChanged += delegate { SetStripVisible(miStrip.Checked); };

            ToolStripMenuItem miDir = new ToolStripMenuItem("打开数据目录", null, delegate
            {
                try
                {
                    if (Directory.Exists(engine.LogDir))
                        System.Diagnostics.Process.Start("explorer.exe", "\"" + engine.LogDir + "\"");
                }
                catch { }
            });

            ToolStripMenuItem miAuto = new ToolStripMenuItem("开机自动启动");
            miAuto.CheckOnClick = true;
            miAuto.Checked = GetAutoStart();
            miAuto.CheckedChanged += delegate
            {
                try { SetAutoStart(miAuto.Checked); }
                catch { miAuto.Checked = GetAutoStart(); }
            };

            ToolStripMenuItem miRefresh = new ToolStripMenuItem("重新扫描日志", null, delegate
            {
                engine.ForceRescan();
                RefreshAll();
            });

            ToolStripMenuItem miExit = new ToolStripMenuItem("退出", null, delegate
            {
                tray.Visible = false;
                Application.Exit();
            });

            menu.Items.Add(miStrip);
            menu.Items.Add(miPanel);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miDir);
            menu.Items.Add(miAuto);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miRefresh);
            menu.Items.Add(miExit);

            // 每次打开时按系统主题应用样式（可实时跟随明暗切换）
            menu.Opening += delegate { StyleMenu(menu); };
            StyleMenu(menu);
            return menu;
        }

        // ---------- 菜单主题适配 ----------

        static bool IsSystemDark()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return k != null && Convert.ToInt32(k.GetValue("AppsUseLightTheme", 1)) == 0;
            }
            catch { return false; }
        }

        static void StyleMenu(ContextMenuStrip menu)
        {
            if (IsSystemDark())
            {
                menu.Renderer = new DarkMenuRenderer();
                menu.BackColor = Color.FromArgb(43, 43, 49);
                menu.ForeColor = Color.FromArgb(238, 238, 242);
            }
            else
            {
                menu.Renderer = new ToolStripProfessionalRenderer();
                menu.BackColor = SystemColors.Menu;
                menu.ForeColor = SystemColors.MenuText;
            }

            // 统一内部间距（两种主题一致的度量）
            menu.Font = Ui.Text;
            menu.Padding = new Padding(6, 6, 8, 6);
            menu.ImageScalingSize = new Size(20, 20);
            foreach (ToolStripItem it in menu.Items)
            {
                ToolStripSeparator sep = it as ToolStripSeparator;
                if (sep != null)
                {
                    sep.AutoSize = false;
                    sep.Height = 2;
                    sep.Margin = new Padding(10, 6, 10, 6);
                }
                else
                {
                    it.Padding = new Padding(4, 7, 16, 7);
                    it.Margin = new Padding(0);
                }
            }
        }

        class DarkMenuRenderer : ToolStripProfessionalRenderer
        {
            public DarkMenuRenderer() : base(new DarkColorTable()) { }

            // 自绘亮色勾选标记（默认勾为深色系，在深底上不可见）
            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
            {
                Graphics g = e.Graphics;
                Rectangle r = e.ImageRectangle;
                if (r.Width < 4 || r.Height < 4) return;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (Pen pen = new Pen(Color.FromArgb(235, 240, 240, 246), 1.8f))
                {
                    pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                    PointF[] pts = new PointF[]
                    {
                        new PointF(r.X + r.Width * 0.18f, r.Y + r.Height * 0.52f),
                        new PointF(r.X + r.Width * 0.42f, r.Y + r.Height * 0.76f),
                        new PointF(r.X + r.Width * 0.82f, r.Y + r.Height * 0.26f)
                    };
                    g.DrawLines(pen, pts);
                }
            }
        }

        class DarkColorTable : ProfessionalColorTable
        {
            static readonly Color Bg = Color.FromArgb(43, 43, 49);
            static readonly Color Hover = Color.FromArgb(62, 62, 70);
            static readonly Color Border = Color.FromArgb(66, 66, 74);

            public override Color ToolStripDropDownBackground { get { return Bg; } }
            public override Color MenuBorder { get { return Border; } }
            public override Color MenuItemBorder { get { return Border; } }
            public override Color MenuItemSelected { get { return Hover; } }
            public override Color MenuItemSelectedGradientBegin { get { return Hover; } }
            public override Color MenuItemSelectedGradientEnd { get { return Hover; } }
            public override Color MenuItemPressedGradientBegin { get { return Hover; } }
            public override Color MenuItemPressedGradientEnd { get { return Hover; } }
            public override Color ImageMarginGradientBegin { get { return Bg; } }
            public override Color ImageMarginGradientMiddle { get { return Bg; } }
            public override Color ImageMarginGradientEnd { get { return Bg; } }
            public override Color SeparatorDark { get { return Border; } }
            public override Color SeparatorLight { get { return Border; } }
            public override Color CheckBackground { get { return Hover; } }
            public override Color CheckSelectedBackground { get { return Hover; } }
            public override Color CheckPressedBackground { get { return Hover; } }
        }

        void SetStripVisible(bool v)
        {
            strip.SetEnabled(v);
            Settings.Set("TaskbarStrip", v ? "1" : "0");
        }

        void OnTrayClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) TogglePopup(Rectangle.Empty);
        }

        void TogglePopup(Rectangle invoker)
        {
            if (popup.Visible)
            {
                popup.Hide();
                return;
            }
            // 刚被“点击外部/失焦”隐藏时不立刻重开，避免关闭动作变成重开
            if ((DateTime.Now - popupHiddenAt).TotalMilliseconds < 300) return;
            popup.UpdateData(last != null ? last : engine.Compute());
            popup.ShowFlyout(invoker);
        }

        void OnEngineChanged()
        {
            try { RefreshAll(); }
            catch { }
        }

        void RefreshAll()
        {
            AgentStatus s = engine.Compute();
            last = s;

            UpdateTooltip(s);

            string key = s.Overall.ToString() + (s.Running ? "|run" : "|idle");
            if (key != lastIconKey || s.Running)
            {
                lastIconKey = key;
                SetIcon(s.Overall, frame);
            }

            if (popup.Visible) popup.UpdateData(s);
            strip.Update(s);
        }

        void UpdateTooltip(AgentStatus s)
        {
            string tip = "ZCode: " + s.StateText;
            if (s.Sessions.Count > 0) tip += " · " + s.Sessions.Count + " 个会话";
            DateTime now = DateTime.Now;
            SessionState run = s.FirstRunning(now);
            if (run != null)
            {
                if (run.CurrentTool.Length > 0)
                    tip += " · " + run.CurrentTool + " " + Ui.Dur(now - run.ToolStart);
                if (run.ModelShort.Length > 0) tip += " · " + run.ModelShort;
            }
            else if (s.ErrorsToday > 0) tip += " · 今日 " + s.ErrorsToday + " 错误";
            if (tip.Length > 126) tip = tip.Substring(0, 126);
            if (tip != lastTip)
            {
                lastTip = tip;
                tray.Text = tip;
            }
        }

        // ---------- 图标绘制 ----------

        void SetIcon(Phase overall, int f)
        {
            using (Bitmap bmp = RenderIcon(overall, f))
            {
                IntPtr h = bmp.GetHicon();
                Icon cloned = (Icon)Icon.FromHandle(h).Clone();
                Native.DestroyIconSafe(h);
                Icon old = currentIcon;
                currentIcon = cloned;
                tray.Icon = cloned;
                if (old != null) old.Dispose();
            }
        }

        static Bitmap RenderIcon(Phase overall, int frame)
        {
            Bitmap bmp = new Bitmap(32, 32);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                using (GraphicsPath bg = Ui.RoundRect(new RectangleF(1f, 1f, 30f, 30f), 8f))
                {
                    // 不透明深色底：保证彩色状态图形在任务栏上的可读性
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 34, 34, 39)))
                        g.FillPath(b, bg);
                    using (Pen p = new Pen(Color.FromArgb(255, 96, 96, 104), 1f))
                        g.DrawPath(p, bg);
                }

                Color c = Ui.PhaseColor(overall);
                switch (overall)
                {
                    case Phase.Thinking:
                        // 轨道 + 旋转亮点的“思考”动画
                        using (Pen orbit = new Pen(Color.FromArgb(110, c), 2.4f))
                            g.DrawEllipse(orbit, 7f, 7f, 18f, 18f);
                        double a = frame * (Math.PI / 6.0);
                        float dx = 16f + 9f * (float)Math.Cos(a);
                        float dy = 16f + 9f * (float)Math.Sin(a);
                        using (SolidBrush b = new SolidBrush(c))
                        {
                            g.FillEllipse(b, dx - 3f, dy - 3f, 6f, 6f);
                            g.FillEllipse(b, 13f, 13f, 6f, 6f);
                        }
                        break;
                    case Phase.ToolRunning:
                        using (SolidBrush b = new SolidBrush(c))
                        {
                            using (GraphicsPath sq = Ui.RoundRect(new RectangleF(9f, 9f, 14f, 14f), 3f))
                                g.FillPath(b, sq);
                        }
                        using (Pen w = new Pen(Color.FromArgb(32, 32, 32), 2f))
                            g.DrawRectangle(w, 13f, 15f, 6f, 2.5f);
                        break;
                    case Phase.WaitingInput:
                        using (Pen p = new Pen(c, 3.2f))
                            g.DrawEllipse(p, 9f, 9f, 14f, 14f);
                        break;
                    case Phase.Completed:
                        using (Pen p = new Pen(c, 3f))
                        {
                            g.DrawLines(p, new PointF[] {
                                new PointF(10f, 16.5f), new PointF(14f, 20.5f), new PointF(22.5f, 11.5f) });
                        }
                        break;
                    case Phase.Error:
                        using (SolidBrush b = new SolidBrush(c))
                            g.FillEllipse(b, 8f, 8f, 16f, 16f);
                        using (StringFormat sf = new StringFormat())
                        {
                            sf.Alignment = StringAlignment.Center;
                            sf.LineAlignment = StringAlignment.Center;
                            using (SolidBrush w = new SolidBrush(Color.White))
                                g.DrawString("!", new Font("Segoe UI", 12f, FontStyle.Bold), w, new RectangleF(0, -1f, 32, 32), sf);
                        }
                        break;
                    case Phase.Idle:
                        using (SolidBrush b = new SolidBrush(c))
                            g.FillEllipse(b, 10f, 10f, 12f, 12f);
                        break;
                    default: // NoData
                        using (Pen p = new Pen(c, 2.4f))
                            g.DrawEllipse(p, 10f, 10f, 12f, 12f);
                        break;
                }
            }
            return bmp;
        }

        // ---------- 自启动 ----------

        static string ExePath()
        {
            return Assembly.GetExecutingAssembly().Location;
        }

        static bool GetAutoStart()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(AppName) != null;
            }
            catch { return false; }
        }

        static void SetAutoStart(bool on)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue(AppName, "\"" + ExePath() + "\"");
                else if (k.GetValue(AppName) != null) k.DeleteValue(AppName);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
                try { if (tray != null) { tray.Visible = false; tray.Dispose(); } } catch { }
                if (anim != null) anim.Dispose();
                if (strip != null) strip.Dispose();
                if (popup != null) popup.Dispose();
                if (engine != null) engine.Dispose();
                if (currentIcon != null) currentIcon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
