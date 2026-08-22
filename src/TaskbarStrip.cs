using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace AgentStatusBar
{
    /// <summary>
    /// 停靠在 Win11 任务栏左侧空白区的状态条。
    /// Win11 不再支持 DeskBand，这里用分层窗口覆盖在任务栏上方：
    /// 逐像素 alpha 透明（液态玻璃胶囊），经 UpdateLayeredWindow 提交。
    /// 稳定性设计：WinEvent 钩子即时恢复 z 序（被任务栏盖住时立刻插回其上方）、
    /// 全屏判定看任务栏是否被顶出屏幕（开始菜单等不算）、锚点位置变化防抖 + 消失宽限。
    /// </summary>
    class TaskbarStripForm : Form
    {
        public Action LeftClick;
        public Func<ContextMenuStrip> MenuProvider;

        bool enabled;
        readonly Timer dock;
        readonly Timer animT = new Timer();
        int tick;
        Phase lastPhase = Phase.NoData;
        string lastText = "";
        string renderKey = "";
        string layoutKey = "";
        Rectangle layout;
        bool hasLayout;
        bool contentActive;   // 有值得显示的内容（空闲/无数据时整个隐藏）
        bool animating;       // 运行中：呼吸 + 波浪点
        int startTick;        // 短语轮换基准
        string prevText;      // 文字过渡动效：切换前的文本
        int textChangedAt;    // 文字切换时刻
        AgentStatus lastStatus; // 缓存状态，供秒数本地刷新
        int lastSecRefresh;
        // token 用量展示见 tokenVals（{图标, 数值} 分组）

        static readonly int[] PhraseOrder = BuildShuffledOrder();

        static int[] BuildShuffledOrder()
        {
            int[] a = new int[Phrases.All.Length];
            for (int i = 0; i < a.Length; i++) a[i] = i;
            Random r = new Random();
            for (int i = a.Length - 1; i > 0; i--)
            {
                int j = r.Next(i + 1);
                int t = a[i]; a[i] = a[j]; a[j] = t;
            }
            return a;
        }

        // 停靠状态
        IntPtr cachedTaskbar = IntPtr.Zero;
        int activeAnchor = -1;      // 当前生效的左锚点（防抖后的）
        int pendingAnchor;          // 候选锚点
        int pendingCount;           // 候选连续出现次数
        DateTime lastAnchorOk = DateTime.MinValue;

        // z 序即时恢复
        Native.WinEventDelegate winEvt;
        IntPtr winEvtHook;
        int lastZFix;

        int msgTaskbarCreated;
        readonly object renderGate = new object();

        static Bitmap measureBmp;
        static Graphics measurer;
        static Dictionary<string, Font> fontMap;

        const uint EVENT_MIN = 0x0003;      // EVENT_SYSTEM_FOREGROUND
        const uint EVENT_MAX = 0x8005;      // EVENT_OBJECT_FOCUS（覆盖 SHOW/HIDE/REORDER）
        const uint WINEVENT_OUTOFCONTEXT = 0;

        public bool StripEnabled { get { return enabled; } }

        /// <summary>当前停靠矩形（供弹窗等定位）。</summary>
        public Rectangle DockRect
        {
            get { return hasLayout ? layout : Rectangle.Empty; }
        }

        public TaskbarStripForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Size = new Size(10, 10);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint, true);

            dock = new Timer();
            dock.Interval = 500;
            dock.Tick += delegate { DockTick(); };
            dock.Start();

            startTick = Environment.TickCount;
            animT.Interval = 40; // 25fps：旋转指示需要流畅帧率
            animT.Tick += delegate
            {
                if (!contentActive) return;
                if (animating)
                {
                    // 每约 1s 本地重算文本（秒数及时刷新），短语轮换经 ApplyText 走过渡动效判断
                    if (Environment.TickCount - lastSecRefresh >= 1000)
                    {
                        lastSecRefresh = Environment.TickCount;
                        if (lastStatus != null) ApplyText(BuildDisplayText(lastStatus));
                    }
                    RenderIfChanged(true);
                }
            };

            MouseClick += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    Action a = LeftClick;
                    if (a != null) a();
                }
                else if (e.Button == MouseButtons.Right)
                {
                    ContextMenuStrip m = MenuProvider != null ? MenuProvider() : null;
                    if (m != null) m.Show(this, e.Location);
                }
            };

            try { msgTaskbarCreated = Native.RegisterWindowMessage("TaskbarCreated"); }
            catch { }
            try
            {
                winEvt = OnWinEvent;
                winEvtHook = Native.SetWinEventHook(EVENT_MIN, EVENT_MAX, IntPtr.Zero,
                    winEvt, 0, 0, WINEVENT_OUTOFCONTEXT);
            }
            catch { }

            try
            {
                // 必须持有 Bitmap 引用，否则其被 GC 回收后 Graphics 失效（“参数无效”）
                measureBmp = new Bitmap(1, 1);
                measurer = Graphics.FromImage(measureBmp);
            }
            catch { }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80 | 0x80000 | 0x8000000; // TOOLWINDOW | LAYERED | NOACTIVATE
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void WndProc(ref Message m)
        {
            // explorer 重启后任务栏重建，立即重新停靠
            if (msgTaskbarCreated != 0 && m.Msg == msgTaskbarCreated)
            {
                layoutKey = "";
                activeAnchor = -1;
                DockTick();
            }
            base.WndProc(ref m);
        }

        public void SetEnabled(bool v)
        {
            enabled = v;
            if (!v) { Visible = false; return; }
            layoutKey = "";
            activeAnchor = -1;
            DockTick();
        }

        // Nerd Font 图标（Maple Mono NF 内置，已验证存在）
        const int S = 3; // 超采样倍数
        const string IconCache = "\uF1C0";   // 数据库（缓存）
        const string IconInput = "\uF0AA";   // ↑ 出圈（输入，用户上行）
        const string IconOutput = "\uF0AB";  // ↓ 入圈（输出，模型下行）
        // 固定宽度模板：{图标, 数值} 交替
        static readonly string[] TokenTpl = new string[]
        {
            IconCache, "999.9M", IconInput, "999.9M", IconOutput, "999.9M"
        };
        string[] tokenVals; // 当前的 {图标, 数值} 序列；null = 无数据

        public void Update(AgentStatus s)
        {
            if (s == null || !enabled) return;
            lastStatus = s;
            lastPhase = s.Overall;
            long cache = s.CacheRead + s.CacheWrite;
            tokenVals = (s.TokensIn > 0 || s.TokensOut > 0 || cache > 0)
                ? new string[] { IconCache, Ui.KT(cache), IconInput, Ui.KT(s.TokensIn), IconOutput, Ui.KT(s.TokensOut) }
                : null;
            ApplyText(BuildDisplayText(s));

            bool active = lastText != null;
            if (active != contentActive)
            {
                contentActive = active;
                if (active)
                {
                    renderKey = "";
                    layoutKey = ""; // 重新应用布局并显示
                }
                else if (Visible) Visible = false; // 空闲：整个状态条隐藏
            }
            animating = active && s.Running;
            bool inTransition = active && prevText != null &&
                (Environment.TickCount - textChangedAt) < 320;
            animT.Enabled = animating || inTransition;
            RenderIfChanged(false);
        }

        /// <summary>
        /// 状态条文本：会话数（多会话时）在前，俏皮话居中，运行提示（工具+耗时）在后；
        /// 等待输入/出错显示简要信息；空闲/无数据返回 null（隐藏整个状态条）。
        /// </summary>
        string BuildDisplayText(AgentStatus s)
        {
            if (s.Overall == Phase.Idle || s.Overall == Phase.NoData) return null;
            DateTime now = DateTime.Now;
            if (s.Running)
            {
                long seq = (Environment.TickCount - startTick) / 4000;
                string phrase = Phrases.All[PhraseOrder[(int)(seq % PhraseOrder.Length)]];
                string text = s.Sessions.Count > 1 ? s.Sessions.Count + " 会话 · " : "";
                text += phrase;
                SessionState run = s.FirstRunning(now);
                if (s.Overall == Phase.ToolRunning && run != null && run.CurrentTool.Length > 0)
                    text += " · " + run.CurrentTool + " " + Ui.Dur(now - run.ToolStart);
                return text;
            }
            if (s.Overall == Phase.WaitingInput)
                return s.Sessions.Count > 1
                    ? "等待输入 · " + s.Sessions.Count + " 个会话"
                    : "等待输入";
            return "出错 · 今日 " + s.ErrorsToday + " 个错误";
        }

        // ---------- 停靠 ----------

        void DockTick()
        {
            if (!enabled) return;
            tick++;

            IntPtr tb = Native.FindWindow("Shell_TrayWnd", null);
            cachedTaskbar = tb;
            if (tb == IntPtr.Zero)
            {
                HideStrip("taskbar not found");
                hasLayout = false;
                activeAnchor = -1;
                return;
            }

            Native.RECT tr;
            if (!Native.GetWindowRect(tb, out tr) || tr.Width < 200)
            {
                HideStrip("bad taskbar rect");
                return;
            }
            if (tr.Height > tr.Width) // 竖向任务栏不支持
            {
                HideStrip("vertical taskbar");
                return;
            }
            if (IsTaskbarHidden(tb, tr)) // 真全屏把任务栏顶出屏幕（开始菜单不算）
            {
                HideStrip("taskbar pushed off-screen (fullscreen)");
                return;
            }
            if (!contentActive) // 空闲/无数据：不显示
            {
                HideStrip("idle");
                return;
            }

            LayoutInfo li = ComputeLayout(tb, tr);
            if (li.GapRight - li.GapLeft < 100)
            {
                // 无空隙：先维持 5 秒（播放器歌词移动等瞬态），仍无则隐藏
                if (hasLayout && (DateTime.Now - lastAnchorOk).TotalSeconds < 5.0)
                {
                    FixZOrder();
                    return;
                }
                hasLayout = false;
                layoutKey = "";
                activeAnchor = -1;
                HideStrip("no gap");
                return;
            }

            // 锚点防抖：新位置需连续 3 tick（1.5s）确认才迁移
            int anchor = li.GapLeft;
            if (!hasLayout || activeAnchor < 0)
            {
                activeAnchor = anchor;
                pendingAnchor = anchor;
                pendingCount = 99;
            }
            else if (anchor != pendingAnchor)
            {
                pendingAnchor = anchor;
                pendingCount = 1;
            }
            else pendingCount++;

            if (pendingCount >= 3 && anchor == pendingAnchor && activeAnchor != anchor)
            {
                Dbg("ANCHOR: " + activeAnchor + " -> " + anchor);
                activeAnchor = anchor;
            }
            lastAnchorOk = DateTime.Now;

            int w = Math.Min(li.NeedWidth, li.GapRight - activeAnchor);
            Rectangle target = new Rectangle(activeAnchor, li.Y, w, li.StripH);
            string key = target.X + "," + target.Y + "," + target.Width + "," + target.Height;
            if (key != layoutKey)
            {
                bool wasHidden = !Visible;
                layoutKey = key;
                layout = target;
                hasLayout = true;
                // 先同步 WinForms Bounds，否则 Visible=true 时会用旧 Bounds 覆盖 ULW 的位置
                SetBounds(target.X, target.Y, target.Width, target.Height);
                RenderIfChanged(true);
                if (wasHidden) { Visible = true; Dbg("SHOW: layout " + target); }
            }

            FixZOrder();

            // 周期性强制重绘，防内容意外丢失
            if (tick % 10 == 0 && hasLayout) RenderIfChanged(true);
        }

        class LayoutInfo
        {
            public int GapLeft, GapRight, Y, StripH, NeedWidth;
        }

        /// <summary>
        /// 计算空隙：把开始按钮、任务栏子窗口、以及“贴在任务栏上的第三方顶层控件”
        /// （播放器任务栏歌词等）逐块吸收为占用区，返回第一个真正的空隙。
        /// </summary>
        LayoutInfo ComputeLayout(IntPtr tb, Native.RECT tr)
        {
            LayoutInfo li = new LayoutInfo();
            int tbH = tr.Height;
            int tbW = tr.Width;
            li.StripH = 0;
            if (tbH < 30) return li;

            int stripH = tbH; // 与任务栏同高
            if (stripH < 18) return li;
            li.StripH = stripH;
            li.Y = tr.Top + (tbH - stripH) / 2;

            List<Native.RECT> blocks = new List<Native.RECT>();
            Native.EnumChildWindows(tb, delegate(IntPtr h, IntPtr lp)
            {
                if (Native.IsWindowVisible(h))
                {
                    Native.RECT r;
                    if (Native.GetWindowRect(h, out r))
                    {
                        if (r.Width > 4 && r.Width < tbW * 0.8) blocks.Add(r); // 忽略零尺寸与整条容器
                    }
                }
                return true;
            }, IntPtr.Zero);

            // 贴在任务栏上的第三方顶层控件（如播放器任务栏歌词）
            Native.EnumWindows(delegate(IntPtr h, IntPtr lp)
            {
                if (h == Handle || h == tb) return true;   // 排除自身与任务栏
                if (!Native.IsWindowVisible(h)) return true;
                Native.RECT r;
                if (!Native.GetWindowRect(h, out r)) return true;
                if (r.Right <= tr.Left || r.Left >= tr.Right) return true;        // 水平不相交
                int vOverlap = Math.Min(r.Bottom, tr.Bottom) - Math.Max(r.Top, tr.Top);
                if (vOverlap < tbH / 2) return true;                              // 垂直重叠不足
                if (r.Top > tr.Top + 8) return true;                              // 顶边必须贴住任务栏顶
                if (r.Height > tbH * 3) return true;                               // 大窗口（开始菜单等）
                blocks.Add(r);
                return true;
            }, IntPtr.Zero);

            // 从任务栏左缘贪心吸收连续占用块（如贴左的播放器歌词），得到占用区右缘。
            // 不预留“开始按钮”宽度：本布局下开始按钮不在最左角。
            blocks.Sort(delegate(Native.RECT a, Native.RECT b) { return a.Left.CompareTo(b.Left); });
            int cursor = tr.Left;
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (Native.RECT b in blocks)
                {
                    if (b.Left <= cursor + 24 && b.Right > cursor)
                    {
                        cursor = b.Right;
                        grew = true;
                    }
                }
            }
            li.GapLeft = cursor + 10; // 距任务栏左缘留出边距

            // 第一个未被吸收的块（居中图标组）的左缘即空隙右界
            int blockLeft = Int32.MaxValue;
            foreach (Native.RECT b in blocks)
                if (b.Left > cursor && b.Left < blockLeft) blockLeft = b.Left;
            li.GapRight = blockLeft != Int32.MaxValue
                ? blockLeft - 6
                : tr.Left + tbW / 2 - 220; // 探测失败时保守估计

            // 内容宽度（与渲染同一超采样字号/字体测量）：token 前缀 + 圆环 + 主文字
            float fontPx = stripH * 0.40f;
            int textW = 0;
            Font f = GetFont(Ui.CjkFontFamily, fontPx, 3);
            Font fl = GetFont(
                Ui.UnifiedFont ? Ui.CjkFontFamily : Ui.DisplayFontFamily,
                fontPx * (Ui.UnifiedFont ? 1f : LatinScale), 3);
            if (measurer != null)
            {
                if (tokenVals != null)
                    textW += (int)Math.Ceiling(
                        RenderTokens(measurer, TokenTpl, f, fl, null, 0f, 0f, 0f, null) / 3f) + 8; // 固定预留宽
                if (animating) textW += (int)(stripH * 0.42f) + 8;
                textW += (int)Math.Ceiling(MixedWidth(measurer, lastText, f, fl) / 3f);
            }
            else textW = 220;
            li.NeedWidth = 9 + textW + 6;
            return li;
        }

        // ---------- z 序 ----------

        void OnWinEvent(IntPtr hHook, uint eventType, IntPtr hwnd, int idObject, int idChild,
            uint dwEventThread, uint dwmsEventTime)
        {
            if (idObject != 0) return; // 只关心窗口级事件（OBJID_WINDOW）
            int now = Environment.TickCount;
            if (now - lastZFix < 60) return; // 节流
            lastZFix = now;
            FixZOrder();
        }

        /// <summary>若被任务栏盖住，立刻插回任务栏正上方（不做盲目置顶，避免浮到开始菜单上面）。</summary>
        static bool IsAboveHandle(IntPtr me, IntPtr anchor)
        {
            IntPtr h = anchor;
            for (int i = 0; i < 80; i++)
            {
                h = Native.GetWindow(h, 3 /* GW_HWNDPREV，向顶部方向 */);
                if (h == IntPtr.Zero) return false;
                if (h == me) return true;
            }
            return false;
        }

        /// <summary>任务栏被顶出屏幕（真全屏自动隐藏）才隐藏；开始菜单/搜索打开不算。</summary>
        static bool IsTaskbarHidden(IntPtr tb, Native.RECT tr)
        {
            try
            {
                IntPtr mon = Native.MonitorFromWindow(tb, 1 /* MONITOR_DEFAULTTONEAREST */);
                Native.MONITORINFO mi = new Native.MONITORINFO();
                mi.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO));
                if (!Native.GetMonitorInfo(mon, ref mi)) return false;
                int visTop = Math.Max(tr.Top, mi.rcMonitor.Top);
                int visBottom = Math.Min(tr.Bottom, mi.rcMonitor.Bottom);
                return (visBottom - visTop) < (tr.Bottom - tr.Top) * 4 / 10; // 可见不足 40%
            }
            catch { return false; }
        }

        // ---------- 渲染 ----------

        void RenderIfChanged(bool force)
        {
            if (!hasLayout) return;
            string key = layout.Width + "x" + layout.Height + "|" + (int)lastPhase + "|" + lastText;
            if (!force && key == renderKey) return;
            renderKey = key;
            lock (renderGate)
            {
                try { RenderNow(); }
                catch (Exception ex) { Dbg("render ex: " + ex.Message); }
            }
        }

        static int dbgCount;
        static void Dbg(string s)
        {
            if (dbgCount > 400) return;
            dbgCount++;
            try
            {
                System.IO.File.AppendAllText(System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "render.log"),
                    "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + s + "\r\n");
            }
            catch { }
        }

        void HideStrip(string reason)
        {
            if (Visible) { Visible = false; Dbg("HIDE: " + reason); }
        }

        int lastZLog;

        void FixZOrder()
        {
            if (!hasLayout || !Visible) return;
            IntPtr tb = cachedTaskbar != IntPtr.Zero ? cachedTaskbar : Native.FindWindow("Shell_TrayWnd", null);
            cachedTaskbar = tb;
            if (tb == IntPtr.Zero || tb == Handle) return;
            if (!IsAboveHandle(Handle, tb))
            {
                // 注意：插入任务栏上方（hWndInsertAfter=tb）实测无效（返回 TRUE 但 z 序不变），
                // 必须用 HWND_TOPMOST；开始菜单等系统浮层不覆盖任务栏条区域，无视觉冲突。
                Native.SetWindowPos(Handle, new IntPtr(-1) /* HWND_TOPMOST */, 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
                int now = Environment.TickCount;
                if (now - lastZLog > 2000)
                {
                    lastZLog = now;
                    Dbg("ZFIX: was below taskbar, re-asserted TOPMOST");
                }
            }
        }

        /// <summary>
        /// 液态玻璃渲染：3 倍超采样绘制（胶囊玻璃体 + 高光边缘 + 状态点辉光 + 文字投影），
        /// 高质量缩小后预乘 alpha，经 CreateDIBSection（32bpp，保 alpha）提交 UpdateLayeredWindow。
        /// </summary>
        void RenderNow()
        {
            int w = layout.Width, h = layout.Height;
            if (w < 10 || h < 8) return;

            int W = w * S, H = h * S;

            using (Bitmap lo = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Bitmap hi = new Bitmap(W, H, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(hi))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                        // ---- 底板：极淡底色，与任务栏同高 ----
                        float pad = 1f * S;
                        RectangleF cap = new RectangleF(pad, pad, W - pad * 2f, H - pad * 2f);
                        float rad = Math.Min(6f * S, cap.Height * 0.35f);
                        using (GraphicsPath pill = Ui.RoundRect(cap, rad))
                        {
                            using (LinearGradientBrush body = new LinearGradientBrush(
                                cap, Color.FromArgb(22, 255, 255, 255), Color.FromArgb(6, 255, 255, 255), 90f))
                                g.FillPath(body, pill);
                        }

                        // 字体：Maple Mono NF CN 为拉丁+中文一体（等宽），无需分字号；否则中文/拉丁分字号
                        Font fC = GetFont(Ui.CjkFontFamily, h * 0.40f, S);
                        Font fL = GetFont(
                            Ui.UnifiedFont ? Ui.CjkFontFamily : Ui.DisplayFontFamily,
                            h * 0.40f * (Ui.UnifiedFont ? 1f : LatinScale), S);
                        using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
                        {
                            sf.LineAlignment = StringAlignment.Center;
                            sf.FormatFlags |= StringFormatFlags.NoWrap;

                            float mx = cap.X + 8f * S;

                            // ---- 今日 token 用量（图标+数值分组，暗色，固定宽度，最前）----
                            if (tokenVals != null)
                            {
                                using (SolidBrush pb = new SolidBrush(Color.FromArgb(185, 198, 205, 214)))
                                    mx += RenderTokens(g, tokenVals, fC, fL, pb, mx, cap.Y, cap.Height, sf) + 8f * S;
                            }

                            // ---- 旋转圆环加载指示（运行中）----
                            float spR = cap.Height * 0.21f;
                            if (animating)
                            {
                                float spCx = mx + spR;
                                float spCy = cap.Y + cap.Height / 2f;
                                float ang = (float)((Environment.TickCount % 1400) / 1400.0 * 360.0);
                                Color pc = Ui.PhaseColor(lastPhase);
                                float penW = Math.Max(2.2f, cap.Height * 0.075f);
                                RectangleF spBox = new RectangleF(spCx - spR, spCy - spR, spR * 2f, spR * 2f);
                                using (Pen trk = new Pen(Color.FromArgb(52, pc), penW))
                                    g.DrawArc(trk, spBox, 0f, 360f);
                                using (Pen arc = new Pen(Color.FromArgb(235, pc), penW))
                                {
                                    arc.StartCap = LineCap.Round;
                                    arc.EndCap = LineCap.Round;
                                    g.DrawArc(arc, spBox, ang, 95f);
                                }
                                mx = spCx + spR + 8f * S;
                            }

                            // ---- 主文字：切换时交叉淡入淡出 + 横向滑动（260ms，smoothstep）----
                            float tx = mx;
                            double tp = (Environment.TickCount - textChangedAt) / 260.0;
                            bool inTrans = prevText != null && tp < 1.0;
                            if (inTrans)
                            {
                                float p = (float)Math.Max(0.0, Math.Min(1.0, tp));
                                float e = p * p * (3f - 2f * p); // smoothstep
                                float slide = 9f * S;
                                using (SolidBrush ob = new SolidBrush(Color.FromArgb((int)((1f - e) * 225), 250, 250, 252)))
                                    DrawMixed(g, prevText, fC, fL, ob, tx - e * slide, cap.Y, cap.Height, sf);
                                using (SolidBrush nb = new SolidBrush(Color.FromArgb((int)(e * 248), 250, 250, 252)))
                                    DrawMixed(g, lastText, fC, fL, nb, tx + (1f - e) * slide, cap.Y, cap.Height, sf);
                            }
                            else
                            {
                                using (SolidBrush tb = new SolidBrush(Color.FromArgb(248, 250, 250, 252)))
                                    DrawMixed(g, lastText, fC, fL, tb, tx, cap.Y, cap.Height, sf);
                            }
                        }
                    }

                    // ---- 高质量缩小 ----
                    using (Graphics gl = Graphics.FromImage(lo))
                    {
                        gl.CompositingQuality = CompositingQuality.HighQuality;
                        gl.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        gl.SmoothingMode = SmoothingMode.AntiAlias;
                        gl.DrawImage(hi, 0, 0, w, h);
                    }
                }

                Premultiply(lo);
                SubmitLayered(lo, w, h);
            }
        }

        /// <summary>把预乘后的位图经 32bpp DIB 提交给 ULW（GetHbitmap 转 DDB 有丢 alpha 风险）。</summary>
        void SubmitLayered(Bitmap lo, int w, int h)
        {
            Native.BITMAPINFO bmi = new Native.BITMAPINFO();
            bmi.bmiHeader.biSize = 40;
            bmi.bmiHeader.biWidth = (uint)w;
            bmi.bmiHeader.biHeight = (uint)(-h); // top-down
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = 0; // BI_RGB

            IntPtr bits;
            IntPtr hbmp = Native.CreateDIBSection(IntPtr.Zero, ref bmi, 0, out bits, IntPtr.Zero, 0);
            if (hbmp == IntPtr.Zero)
            {
                Dbg("CreateDIBSection FAIL err=" + Marshal.GetLastWin32Error());
                return;
            }

            Rectangle r = new Rectangle(0, 0, w, h);
            BitmapData bd = lo.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int len = bd.Stride * h;
                byte[] px = new byte[len];
                Marshal.Copy(bd.Scan0, px, 0, len);
                Marshal.Copy(px, 0, bits, len);
            }
            finally { lo.UnlockBits(bd); }

            IntPtr dc = Native.CreateCompatibleDC(IntPtr.Zero);
            IntPtr old = Native.SelectObject(dc, hbmp);
            try
            {
                Native.SIZE size = new Native.SIZE(w, h);
                Native.POINT src = new Native.POINT(0, 0);
                Native.POINT dst = new Native.POINT(layout.X, layout.Y);
                Native.BLENDFUNCTION blend = new Native.BLENDFUNCTION();
                blend.BlendOp = 0;          // AC_SRC_OVER
                blend.SourceConstantAlpha = 255;
                blend.AlphaFormat = 1;       // AC_SRC_ALPHA
                bool ok = Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref size,
                    dc, ref src, 0, ref blend, 2 /* ULW_ALPHA */);
                if (!ok)
                    Dbg("ULW FAIL err=" + Marshal.GetLastWin32Error() +
                        " hwnd=" + Handle + " rect=" + layout.X + "," + layout.Y + " " + w + "x" + h);
            }
            finally
            {
                Native.SelectObject(dc, old);
                Native.DeleteDC(dc);
                Native.DeleteObject(hbmp);
            }
        }

        static void Premultiply(Bitmap bmp)
        {
            Rectangle r = new Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData bd = bmp.LockBits(r, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int len = bd.Stride * bd.Height;
                byte[] px = new byte[len];
                Marshal.Copy(bd.Scan0, px, 0, len);
                for (int i = 0; i < len; i += 4)
                {
                    byte a = px[i + 3];
                    if (a == 255) continue;
                    px[i] = (byte)(px[i] * a / 255);
                    px[i + 1] = (byte)(px[i + 1] * a / 255);
                    px[i + 2] = (byte)(px[i + 2] * a / 255);
                }
                Marshal.Copy(px, 0, bd.Scan0, len);
            }
            finally { bmp.UnlockBits(bd); }
        }

        /// <summary>
        /// 应用新文本：实质性变化（短语更换/会话数变化）触发切换动效；
        /// 纯尾部的秒数跳动等长前缀微调静默更新。
        /// </summary>
        void ApplyText(string newText)
        {
            if (newText == lastText) return;
            if (ShouldAnimateChange(lastText, newText))
            {
                prevText = lastText;
                textChangedAt = Environment.TickCount;
            }
            lastText = newText;
        }

        /// <summary>共同前缀不足较短文本的 60% 视为实质性变化，需要切换动效。</summary>
        static bool ShouldAnimateChange(string a, string b)
        {
            if (a == null || b == null || a == b) return false;
            int n = Math.Min(a.Length, b.Length), i = 0;
            while (i < n && a[i] == b[i]) i++;
            return i < Math.Min(a.Length, b.Length) * 0.6;
        }

        // ---------- 中英混排分字号：拉丁/数字字形天然比中文矮，放大 1.2 倍对齐视觉高度 ----------

        const float LatinScale = 1.2f;

        static bool IsCjk(char c) { return c >= 0x2E80; }

        static void SplitRuns(string text, Action<string, bool> emit)
        {
            int i = 0;
            while (i < text.Length)
            {
                bool cjk = IsCjk(text[i]);
                int j = i;
                while (j < text.Length && IsCjk(text[j]) == cjk) j++;
                emit(text.Substring(i, j - i), cjk);
                i = j;
            }
        }

        static float MixedWidth(Graphics g, string text, Font fCjk, Font fLat)
        {
            float w = 0f;
            SplitRuns(text, delegate(string seg, bool cjk)
            {
                w += g.MeasureString(seg, cjk ? fCjk : fLat, Int32.MaxValue,
                    StringFormat.GenericTypographic).Width;
            });
            return w;
        }

        static void DrawMixed(Graphics g, string text, Font fCjk, Font fLat, Brush brush,
            float x, float yTop, float h, StringFormat sf)
        {
            SplitRuns(text, delegate(string seg, bool cjk)
            {
                Font f = cjk ? fCjk : fLat;
                float sw = g.MeasureString(seg, f, Int32.MaxValue,
                    StringFormat.GenericTypographic).Width;
                g.DrawString(seg, f, brush, new RectangleF(x, yTop, sw + 2f, h), sf);
                x += sw;
            });
        }

        /// <summary>
        /// 渲染 token 分组：图标统一方形槽（列对齐），数值按 "999.9M" 宽度固定占位，
        /// 组间一律 8px；brush 为 null 时仅测宽。返回内容总宽。
        /// </summary>
        float RenderTokens(Graphics g, string[] vals, Font fC, Font fL, Brush brush,
            float x, float yTop, float h, StringFormat sf)
        {
            float valSlot = MixedWidth(g, "999.9M", fC, fL); // 每组数值固定槽宽
            float iconSlot = fC.Size;                          // 图标统一方形槽宽
            float ix = x;
            for (int i = 0; i + 1 < vals.Length; i += 2)
            {
                if (brush != null)
                    g.DrawString(vals[i], fC, brush, new RectangleF(ix, yTop, iconSlot + 2f, h), sf);
                ix += iconSlot + 3f * S;

                if (brush != null)
                    DrawMixed(g, vals[i + 1], fC, fL, brush, ix, yTop, h, sf);
                ix += valSlot + 8f * S;
            }
            return ix - x - 8f * S; // 最后一组的组间距不计
        }

        /// <summary>按字体族/逻辑像素/超采样倍数取共享缓存字体，调用方不得 Dispose。</summary>
        static Font GetFont(string family, float px, int scale)
        {
            px = Math.Max(8f, (float)Math.Round(px * 2f) / 2f);
            string key = family + "|" + (px * scale).ToString(CultureInfo.InvariantCulture);
            if (fontMap == null) fontMap = new Dictionary<string, Font>();
            Font f;
            if (!fontMap.TryGetValue(key, out f) || f == null)
            {
                f = new Font(family, px * scale, FontStyle.Regular, GraphicsUnit.Pixel);
                fontMap[key] = f;
            }
            return f;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (winEvtHook != IntPtr.Zero)
                {
                    try { Native.UnhookWinEvent(winEvtHook); } catch { }
                    winEvtHook = IntPtr.Zero;
                }
                if (dock != null) dock.Dispose();
                if (animT != null) animT.Dispose();
                if (fontMap != null)
                {
                    foreach (Font f in fontMap.Values) if (f != null) f.Dispose();
                    fontMap = null;
                }
                if (measurer != null) { measurer.Dispose(); measurer = null; }
                if (measureBmp != null) { measureBmp.Dispose(); measureBmp = null; }
            }
            base.Dispose(disposing);
        }
    }
}
