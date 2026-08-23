using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace AgentStatusBar
{
    /// <summary>
    /// 详情面板：在状态条正上方展开（托盘触发时在右下角）。
    /// 布局为固定常量的纯手动排版（不依赖构建时序/Anchor），
    /// 原地刷新（仅会话集合变化才重建行），WS_EX_COMPOSITED 防闪烁。
    /// </summary>
    class PopupForm : Form
    {
        class RowUi
        {
            public Panel P;
            public StateDot Dot;
            public Label L1, L2, Time;
        }

        readonly Panel listPanel;
        readonly Label footer;
        readonly Label emptyLabel;
        readonly Timer refresh = new Timer();
        readonly Func<AgentStatus> compute;
        readonly List<RowUi> rows = new List<RowUi>();

        Point anchorBR;
        string lastIds = "";
        string lastFooter = "";

        const int PanelW = 368; // = 384 - 8*2
        const int RowH = 48;

        public PopupForm(Func<AgentStatus> compute)
        {
            this.compute = compute;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(S(384), S(160));
            BackColor = Ui.Bg;
            ForeColor = Ui.TextMain;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            Font = Ui.Text;

            Label title = new Label();
            title.Text = "Agent 状态";
            title.AutoSize = true;
            title.Font = Ui.TextBold;
            title.ForeColor = Ui.TextMain;
            title.BackColor = Ui.Bg;
            title.Location = new Point(S(16), S(12));
            Controls.Add(title);

            listPanel = new Panel();
            listPanel.Location = new Point(S(8), S(38));
            listPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            listPanel.BackColor = Ui.Bg;
            listPanel.Size = new Size(S(PanelW), S(100));
            SetDoubleBuffered(listPanel);
            Controls.Add(listPanel);

            emptyLabel = new Label();
            emptyLabel.Text = "暂无活动会话（ZCode 未运行或今天还没有日志）";
            emptyLabel.Font = Ui.Small;
            emptyLabel.ForeColor = Ui.TextDim;
            emptyLabel.BackColor = Ui.Bg;
            emptyLabel.AutoSize = false;
            emptyLabel.Size = new Size(S(PanelW - 16), S(28));
            emptyLabel.Location = new Point(S(8), S(6));
            emptyLabel.Visible = false;
            listPanel.Controls.Add(emptyLabel);

            footer = new Label();
            footer.AutoSize = false;
            footer.Size = new Size(S(PanelW - 16), S(34));
            footer.Font = Ui.Small;
            footer.ForeColor = Ui.TextDim;
            footer.BackColor = Ui.Bg;
            footer.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            Controls.Add(footer);

            refresh.Interval = 1000;
            refresh.Tick += delegate { if (Visible) SafeUpdate(); };

            KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Hide(); };
            Deactivate += delegate { if (Visible) Hide(); };
            Paint += PaintBorder;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80;         // WS_EX_TOOLWINDOW：不出现在 Alt-Tab
                cp.ExStyle |= 0x02000000;   // WS_EX_COMPOSITED：整窗（含子控件）双缓冲
                return cp;
            }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            refresh.Enabled = Visible;
            if (Visible)
            {
                Native.EnableRoundedCorners(Handle);
                Native.EnableDarkFrame(Handle);
            }
        }

        int S(int v) { return v * DeviceDpi / 96; }

        static void SetDoubleBuffered(Control c)
        {
            try
            {
                typeof(Control).GetProperty("DoubleBuffered",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .SetValue(c, true, null);
            }
            catch { }
        }

        void PaintBorder(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            RectangleF full = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using (System.Drawing.Drawing2D.GraphicsPath p = Ui.RoundRect(full, 8f))
            {
                using (Pen pen = new Pen(Color.FromArgb(150, 255, 255, 255)))
                {
                    g.SetClip(new RectangleF(0, 0, Width, Height * 0.45f));
                    g.DrawPath(pen, p);
                    g.ResetClip();
                }
                using (Pen pen = new Pen(Color.FromArgb(70, 255, 255, 255)))
                    g.DrawPath(pen, p);
            }
        }

        /// <summary>
        /// 锚定触发控件展开：invoker 为状态条时，弹窗在其左上方（垂直贴任务栏上缘）；
        /// invoker 为空（托盘图标触发）时锚定光标所在工作区右下角。
        /// </summary>
        public void ShowFlyout(Rectangle invoker)
        {
            Point ref_ = invoker.Width > 0
                ? new Point(invoker.X, invoker.Y)
                : Cursor.Position;
            Screen sc = Screen.FromPoint(ref_);
            Rectangle wa = sc.WorkingArea;
            int w = S(384);
            int left;
            if (invoker.Width > 0)
                left = invoker.X - S(10);
            else
                left = Cursor.Position.X + S(16) - w;
            if (left < wa.Left + S(8)) left = wa.Left + S(8);
            if (left > wa.Right - w - S(8)) left = wa.Right - w - S(8);

            anchorBR = new Point(left + w, wa.Bottom - S(8));
            if (Height < S(100)) Height = S(160);
            Location = new Point(anchorBR.X - Width, anchorBR.Y - Height);
            Show();
            Activate();
        }

        void SafeUpdate()
        {
            try { UpdateData(compute()); }
            catch { }
        }

        public void UpdateData(AgentStatus s)
        {
            if (s == null) return;
            DateTime now = DateTime.Now;

            // 引擎已限 8 个，全量展示（不再截断到 6 行）
            int max = s.Sessions.Count;

            // 会话集合（含顺序）变化才重建行，否则原地更新文本
            StringBuilder ids = new StringBuilder();
            for (int i = 0; i < max; i++) ids.Append(s.Sessions[i].Id).Append('|');
            string idSig = ids.ToString();
            if (idSig != lastIds)
            {
                lastIds = idSig;
                RebuildRows(s, max, now);
            }
            else
            {
                for (int i = 0; i < max; i++) UpdateRow(rows[i], s.Sessions[i], now);
            }

            bool empty = s.Sessions.Count == 0;
            if (emptyLabel.Visible != empty) emptyLabel.Visible = empty;

            int bodyH = empty ? S(40) : max * (S(RowH) + S(4));
            int totalH = S(38) + bodyH + S(48);

            SuspendLayout();
            try
            {
                listPanel.Size = new Size(S(PanelW), bodyH);
                footer.Location = new Point(S(16), totalH - S(42));
                Size = new Size(S(384), totalH);
                Location = new Point(anchorBR.X - Width, anchorBR.Y - Height);
            }
            finally { ResumeLayout(false); }

            long cache = s.CacheRead + s.CacheWrite;
            string ft = String.Format("今日 {0} 次请求 · {1} 次工具调用 · {2} 个错误",
                s.RequestsToday, s.ToolCallsToday, s.ErrorsToday);
            if (s.TokensIn > 0 || s.TokensOut > 0 || cache > 0)
                ft += String.Format("\r\nToken 入 {0} · 出 {1} · 缓存 {2}",
                    Ui.KT(s.TokensIn), Ui.KT(s.TokensOut), Ui.KT(cache));
            if (ft != lastFooter)
            {
                lastFooter = ft;
                footer.Text = ft;
            }
        }

        void RebuildRows(AgentStatus s, int max, DateTime now)
        {
            SuspendLayout();
            try
            {
                rows.Clear();
                for (int i = listPanel.Controls.Count - 1; i >= 0; i--)
                    listPanel.Controls[i].Dispose();
                listPanel.Controls.Clear();
                listPanel.Controls.Add(emptyLabel);

                for (int i = 0; i < max; i++)
                    rows.Add(BuildRow(s.Sessions[i], i * (S(RowH) + S(4)), now));
            }
            finally { ResumeLayout(false); }
        }

        RowUi BuildRow(SessionState sess, int y, DateTime now)
        {
            int rowH = S(RowH);
            int rowW = S(PanelW);
            RowUi ui = new RowUi();

            ui.P = new Panel();
            ui.P.Size = new Size(rowW, rowH);
            ui.P.Location = new Point(0, y);
            ui.P.BackColor = Ui.Bg;
            SetDoubleBuffered(ui.P);
            ui.P.Paint += delegate(object s, PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (System.Drawing.Drawing2D.GraphicsPath card = Ui.RoundRect(
                    new RectangleF(0.5f, 0.5f, ui.P.Width - 1f, ui.P.Height - 1f), 8f))
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(26, 255, 255, 255)))
                        g.FillPath(b, card);
                    using (Pen pen = new Pen(Color.FromArgb(28, 255, 255, 255)))
                        g.DrawPath(pen, card);
                }
            };
            listPanel.Controls.Add(ui.P);

            ui.Dot = new StateDot();
            ui.Dot.Location = new Point(S(12), rowH / 2 - S(6));
            ui.P.Controls.Add(ui.Dot);

            // 标题行：固定宽度 + 省略号，避免 AutoSize 长文本伸进时间区
            ui.L1 = new Label();
            ui.L1.Font = Ui.TextBold;
            ui.L1.ForeColor = Ui.TextMain;
            ui.L1.BackColor = Color.Transparent;
            ui.L1.AutoSize = false;
            ui.L1.AutoEllipsis = true;
            ui.L1.Size = new Size(rowW - S(32) - S(64), S(20));
            ui.L1.Location = new Point(S(32), S(7));
            ui.P.Controls.Add(ui.L1);

            ui.L2 = new Label();
            ui.L2.Font = Ui.Small;
            ui.L2.ForeColor = Ui.TextDim;
            ui.L2.BackColor = Color.Transparent;
            ui.L2.AutoSize = false;
            ui.L2.AutoEllipsis = true;
            // 时间挪到标题行右上角后，第二行可占满整行宽
            ui.L2.Size = new Size(rowW - S(32) - S(8), S(18));
            ui.L2.Location = new Point(S(32), S(26));
            ui.P.Controls.Add(ui.L2);

            ui.Time = new Label();
            ui.Time.Font = Ui.Small;
            ui.Time.ForeColor = Ui.TextDim;
            ui.Time.BackColor = Color.Transparent;
            ui.Time.AutoSize = false;
            ui.Time.AutoEllipsis = true;
            ui.Time.Size = new Size(S(58), S(16));
            ui.Time.TextAlign = ContentAlignment.MiddleRight;
            ui.Time.Location = new Point(rowW - S(12) - S(58), S(9));
            ui.P.Controls.Add(ui.Time);

            UpdateRow(ui, sess, now);
            return ui;
        }

        void UpdateRow(RowUi ui, SessionState sess, DateTime now)
        {
            Phase p = sess.EffectivePhase(now);

            if (ui.Dot.Phase != p) ui.Dot.Phase = p;

            string l1 = sess.TitleDisplay;
            if (ui.L1.Text != l1) ui.L1.Text = l1;

            string model = sess.ModelShort.Length > 0 ? sess.ModelShort : "未知模型";
            // Agent 名只在非 ZCode 时才显示（多 Agent 适配预留），给长文案让位
            string prefix = (sess.Agent != null && sess.Agent.Length > 0 && sess.Agent != "ZCode")
                ? sess.Agent + " · " : "";
            string l2;
            if (p == Phase.ToolRunning && sess.CurrentTool.Length > 0)
                l2 = prefix + Ui.PhaseText(p) + " · " + sess.ToolDisplay + " " + Ui.Dur(now - sess.ToolStart) + " · " + model;
            else if (p == Phase.Thinking)
                l2 = prefix + Ui.PhaseText(p) + " · " + model;
            else if (p == Phase.Error)
                l2 = prefix + Ui.PhaseText(p) + " · " + model + " · 上次出错 " + Ui.Ago(sess.LastError);
            else
            {
                l2 = prefix + model + " · " + sess.Requests + " 请求 · " + sess.Tools + " 工具";
                if (sess.Errors > 0) l2 += " · " + sess.Errors + " 错";
                if (sess.BackgroundTasks > 0) l2 += " · 后台 " + sess.BackgroundTasks;
            }
            if (ui.L2.Text != l2) ui.L2.Text = l2;

            string tm = Ui.Ago(sess.LastActivity);
            if (ui.Time.Text != tm) ui.Time.Text = tm;
        }
    }
}
