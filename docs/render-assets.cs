using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;

// README 展示图渲染器：复用 src/Ui.cs、TaskbarStrip.cs、PopupForm.cs、TrayApp.cs 的
// 配色 / 布局常量与字体，伪造会话数据离屏绘制，输出 docs/img/*.png（2x）。
// 编译运行见同目录 render-assets.cmd（系统自带 csc，无需 SDK）。
static class RenderAssets
{
    const int K = 2;                       // 输出倍率（逻辑 px * K）
    const string NF = "Maple Mono NF CN";  // 与应用一致的等宽中文字体（含 Nerd Font 图标）
    const string ShellFont = "Segoe UI Variable Text";

    const string IconCache = "\uF1C0";     // 数据库（缓存）
    const string IconIn = "\uF0AA";        // 入
    const string IconOut = "\uF0AB";       // 出

    // ---- 相位配色（src/Ui.cs PhaseColor）----
    static Color PhaseColor(string p)
    {
        switch (p)
        {
            case "thinking": return Color.FromArgb(76, 194, 255);
            case "tool": return Color.FromArgb(255, 185, 0);
            case "wait": return Color.FromArgb(108, 203, 95);
            case "done": return Color.FromArgb(126, 178, 143);
            case "error": return Color.FromArgb(255, 96, 89);
            default: return Color.FromArgb(138, 138, 138);
        }
    }
    static readonly Color TextMain = Color.FromArgb(238, 238, 242);
    static readonly Color TextDim = Color.FromArgb(158, 158, 166);
    static readonly Color PanelBg = Color.FromArgb(36, 36, 41);

    static string outDir;

    static Font NFx(float px, bool bold)
    {
        // 逻辑像素字号（0.5 取整，同应用 GetFont）；位图整体 ScaleTransform(K) 负责放大
        return new Font(NF, (float)Math.Round(px * 2f) / 2f, bold ? FontStyle.Bold : FontStyle.Regular,
            GraphicsUnit.Pixel);
    }

    static float TextW(Graphics g, string s, Font f)
    {
        return g.MeasureString(s, f, int.MaxValue, StringFormat.GenericTypographic).Width;
    }

    static void DrawT(Graphics g, string s, Font f, Brush b, float x, float yTop, float h)
    {
        StringFormat sf = new StringFormat(StringFormat.GenericTypographic);
        sf.LineAlignment = StringAlignment.Center;
        sf.FormatFlags |= StringFormatFlags.NoWrap;
        float w = g.MeasureString(s, f, int.MaxValue, StringFormat.GenericTypographic).Width;
        g.DrawString(s, f, b, new RectangleF(x, yTop, w + 2f, h), sf);
    }

    static GraphicsPath Round(RectangleF r, float rad)
    {
        GraphicsPath p = new GraphicsPath();
        if (rad < 0.5f || r.Width < 1 || r.Height < 1) { p.AddRectangle(r); return p; }
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static void Save(Bitmap bmp, string name)
    {
        string path = Path.Combine(outDir, name);
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine("OK " + path);
    }

    // ---------- 任务栏状态条（src/TaskbarStrip.cs RenderNow 复刻）----------

    /// <summary>渲染一条状态条，返回位图（含透明边距）。w 由内容测量决定。</summary>
    static Bitmap RenderStrip(int h, string[] tokens, string phase, bool spinning, string text)
    {
        using (Bitmap measure = new Bitmap(1, 1))
        using (Graphics mg = Graphics.FromImage(measure))
        {
            float fontPx = h * 0.40f;
            Font f = NFx(fontPx, false);

            // token 固定槽宽：数值按 "999.9M"、图标按字号方形（RenderTokens）
            float valSlot = TextW(mg, "999.9M", f);
            float iconSlot = f.Size;
            float tokensW = 0f;
            if (tokens != null)
                tokensW = 3f * (iconSlot + 3f) + 3f * valSlot + 2f * 8f;

            float spinW = spinning ? h * 0.42f + 8f : 0f;
            float textW = TextW(mg, text, f);
            int w = (int)Math.Ceiling(9f + tokensW + (tokens != null ? 8f : 0f) + spinW + textW + 6f);

            Bitmap bmp = new Bitmap(w * K, h * K, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.ScaleTransform(K, K);

                // 极淡底色玻璃胶囊（同高、圆角 6）
                RectangleF cap = new RectangleF(1f, 1f, w - 2f, h - 2f);
                float rad = Math.Min(6f, cap.Height * 0.35f);
                using (GraphicsPath pill = Round(cap, rad))
                using (LinearGradientBrush body = new LinearGradientBrush(cap,
                    Color.FromArgb(22, 255, 255, 255), Color.FromArgb(6, 255, 255, 255), 90f))
                    g.FillPath(body, pill);

                float mx = cap.X + 8f;

                // token 用量分组（暗色，图标+定宽数值）
                if (tokens != null)
                {
                    Color ic = Color.FromArgb(185, 198, 205, 214);
                    using (SolidBrush pb = new SolidBrush(ic))
                    {
                        float ix = mx;
                        for (int i = 0; i + 1 < tokens.Length; i += 2)
                        {
                            DrawT(g, tokens[i], f, pb, ix, cap.Y, cap.Height);
                            ix += iconSlot + 3f;
                            DrawT(g, tokens[i + 1], f, pb, ix, cap.Y, cap.Height);
                            ix += valSlot + 8f;
                        }
                    }
                    mx += tokensW + 8f;
                }

                // 旋转圆环（运行中）
                if (spinning)
                {
                    float r = cap.Height * 0.21f;
                    float cx = mx + r, cy = cap.Y + cap.Height / 2f;
                    Color pc = PhaseColor(phase);
                    float penW = Math.Max(2.2f, cap.Height * 0.075f);
                    RectangleF box = new RectangleF(cx - r, cy - r, r * 2f, r * 2f);
                    using (Pen trk = new Pen(Color.FromArgb(52, pc), penW))
                        g.DrawArc(trk, box, 0f, 360f);
                    using (Pen arc = new Pen(Color.FromArgb(235, pc), penW))
                    {
                        arc.StartCap = LineCap.Round;
                        arc.EndCap = LineCap.Round;
                        g.DrawArc(arc, box, 210f, 95f);
                    }
                    mx = cx + r + 8f;
                }

                using (SolidBrush tb = new SolidBrush(Color.FromArgb(248, 250, 250, 252)))
                    DrawT(g, text, f, tb, mx, cap.Y, cap.Height);
            }
            return bmp;
        }
    }

    // ---------- 伪造的 Win11 任务栏背景 ----------

    static void DrawTaskbarBg(Graphics g, int w, int h)
    {
        RectangleF r = new RectangleF(0, 0, w, h);
        using (LinearGradientBrush b = new LinearGradientBrush(r,
            Color.FromArgb(30, 30, 32), Color.FromArgb(24, 24, 26), 90f))
            g.FillRectangle(b, r);
        using (SolidBrush b = new SolidBrush(Color.FromArgb(28, 255, 255, 255)))
            g.FillRectangle(b, 0, 0, w, 1);
    }

    // 24×24 应用图标（几何近似，仅求氛围）
    static void AppIcon(Graphics g, int kind, float x, float y)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TranslateTransform(x, y);
        switch (kind)
        {
            case 0: // 开始：四块蓝
                using (SolidBrush b = new SolidBrush(Color.FromArgb(46, 140, 228)))
                {
                    g.FillRectangle(b, 2, 2, 9, 9);
                    g.FillRectangle(b, 13, 2, 9, 9);
                    g.FillRectangle(b, 2, 13, 9, 9);
                    g.FillRectangle(b, 13, 13, 9, 9);
                }
                break;
            case 1: // 搜索
                using (Pen p = new Pen(Color.FromArgb(216, 216, 220), 1.8f))
                {
                    g.DrawEllipse(p, 4, 4, 10, 10);
                    g.DrawLine(p, 13.5f, 13.5f, 19, 19);
                }
                break;
            case 2: // 任务视图：双矩形
                using (Pen p = new Pen(Color.FromArgb(200, 204, 210), 1.6f))
                {
                    using (GraphicsPath q = Round(new RectangleF(3, 6, 12, 12), 2)) g.DrawPath(p, q);
                    g.DrawLines(p, new PointF[] { new PointF(8, 4), new PointF(19, 4), new PointF(19, 15) });
                }
                break;
            case 3: // 资源管理器文件夹
                using (SolidBrush b = new SolidBrush(Color.FromArgb(232, 163, 61)))
                    g.FillRectangle(b, 2, 6, 20, 14);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 210, 112)))
                using (GraphicsPath q = Round(new RectangleF(4, 9, 18, 11), 1.5f))
                    g.FillPath(b, q);
                break;
            case 4: // Edge：双色圆
                using (SolidBrush b = new SolidBrush(Color.FromArgb(46, 159, 230)))
                    g.FillEllipse(b, 3, 3, 18, 18);
                using (Pen p = new Pen(Color.FromArgb(14, 94, 158), 3.4f))
                    g.DrawArc(p, 5, 5, 14, 14, -40, 200);
                break;
            case 5: // 终端
                using (GraphicsPath q = Round(new RectangleF(2, 4, 20, 16), 2))
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(43, 43, 48))) g.FillPath(b, q);
                    using (Pen p = new Pen(Color.FromArgb(84, 84, 92), 1f)) g.DrawPath(p, q);
                }
                using (Font f = new Font("Consolas", 7f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(232, 232, 236)))
                    g.DrawString(">_", f, b, 4, 8);
                break;
            case 6: // VS Code：蓝色块+斜杠
                using (GraphicsPath q = Round(new RectangleF(3, 3, 18, 18), 3))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(30, 111, 208)))
                    g.FillPath(b, q);
                using (Pen p = new Pen(Color.FromArgb(150, 214, 240), 2.6f))
                    g.DrawLines(p, new PointF[] { new PointF(8, 16), new PointF(15, 7) });
                break;
            case 7: // 聊天（绿）
                using (GraphicsPath q = Round(new RectangleF(3, 3, 18, 18), 4))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(91, 208, 138)))
                    g.FillPath(b, q);
                using (SolidBrush b = new SolidBrush(Color.White))
                {
                    g.FillEllipse(b, 7, 10, 3, 3);
                    g.FillEllipse(b, 11, 10, 3, 3);
                    g.FillEllipse(b, 15, 10, 3, 3);
                }
                break;
            case 8: // 音乐（紫）
                using (GraphicsPath q = Round(new RectangleF(3, 3, 18, 18), 4))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(160, 107, 232)))
                    g.FillPath(b, q);
                using (Pen p = new Pen(Color.White, 1.8f))
                {
                    g.DrawLine(p, 10, 15.5f, 10, 7.5f);
                    g.DrawLine(p, 10, 7.5f, 16, 6);
                    g.DrawLine(p, 16, 6, 16, 13.5f);
                }
                using (SolidBrush b = new SolidBrush(Color.White))
                {
                    g.FillEllipse(b, 7.6f, 14, 3.6f, 3.2f);
                    g.FillEllipse(b, 13.6f, 12, 3.6f, 3.2f);
                }
                break;
            case 9: // 照片（四色）
                using (SolidBrush b = new SolidBrush(Color.FromArgb(240, 240, 244)))
                using (GraphicsPath q = Round(new RectangleF(3, 3, 18, 18), 3))
                    g.FillPath(b, q);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(240, 96, 89)))
                    g.FillRectangle(b, 5, 5, 6.5f, 6.5f);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(108, 203, 95)))
                    g.FillRectangle(b, 12.5f, 5, 6.5f, 6.5f);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(76, 194, 255)))
                    g.FillRectangle(b, 5, 12.5f, 6.5f, 6.5f);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 185, 0)))
                    g.FillRectangle(b, 12.5f, 12.5f, 6.5f, 6.5f);
                break;
            case 10: // 邮件（蓝）
                using (GraphicsPath q = Round(new RectangleF(2, 5, 20, 14), 2))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(76, 143, 232)))
                    g.FillPath(b, q);
                using (Pen p = new Pen(Color.White, 1.4f))
                    g.DrawLines(p, new PointF[] { new PointF(4, 8), new PointF(12, 13.5f), new PointF(20, 8) });
                break;
            case 11: // 设置齿轮（灰）
                using (SolidBrush b = new SolidBrush(Color.FromArgb(160, 164, 172)))
                {
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4.0;
                        g.FillRectangle(b, 11f + 7f * (float)Math.Cos(a) - 2f,
                            11f + 7f * (float)Math.Sin(a) - 2f, 4f, 4f);
                    }
                    g.FillEllipse(b, 5, 5, 12, 12);
                }
                using (SolidBrush b = new SolidBrush(Color.FromArgb(30, 30, 32)))
                    g.FillEllipse(b, 8.5f, 8.5f, 5f, 5f);
                break;
        }
        g.TranslateTransform(-x, -y); // 恢复平移（不能 ResetTransform，会清掉外层 K 倍缩放）
    }

    static void DrawTrayCluster(Graphics g, int w, int h)
    {
        Font f = new Font(ShellFont, 12f, FontStyle.Regular, GraphicsUnit.Pixel);
        using (SolidBrush tb = new SolidBrush(Color.FromArgb(235, 240, 240, 245)))
        {
            float cy = h / 2f;
            float right = w - 14f;

            // 时间
            float tw = TextW(g, "22:28:33", f);
            DrawT(g, "22:28:33", f, tb, right - tw, 0, h);
            right -= tw + 12f;

            // 电池 + 79%
            float bw = TextW(g, "79%", f);
            DrawT(g, "79%", f, tb, right - bw, 0, h);
            right -= bw + 5f;
            using (Pen p = new Pen(tb, 1.4f))
            {
                g.DrawRectangle(p, right - 22, cy - 5.5f, 20, 11);
                g.FillRectangle(tb, right - 1.5f, cy - 2.5f, 2, 5);
                g.FillRectangle(tb, right - 20.5f, cy - 4, 13, 8);
            }
            right -= 26f;

            // WiFi：两段弧 + 点
            using (Pen p = new Pen(tb, 1.5f))
            {
                g.DrawArc(p, right - 14, cy - 6, 12, 12, -45, 90);
                g.DrawArc(p, right - 10, cy - 2.5f, 5, 5, -45, 90);
                g.FillEllipse(tb, right - 8.4f, cy + 1.6f, 2.2f, 2.2f);
            }
            right -= 20f;

            // 音量（tb 为共享画刷，不能再用 using 包装别名，否则会把 tb 一并 Dispose）
            PointF[] sp = new PointF[] {
                new PointF(right - 12, cy - 3), new PointF(right - 8, cy - 3),
                new PointF(right - 4, cy - 7), new PointF(right - 4, cy + 7),
                new PointF(right - 8, cy + 3), new PointF(right - 12, cy + 3) };
            g.FillPolygon(tb, sp);
            right -= 20f;

            // 输入法「中」
            float zw = TextW(g, "中", f);
            DrawT(g, "中", f, tb, right - zw, 0, h);
            right -= zw + 12f;

            // 折叠箭头
            using (Pen p = new Pen(tb, 1.4f))
            {
                g.DrawLine(p, right - 8, cy - 1.5f, right - 4.5f, cy + 2f);
                g.DrawLine(p, right - 4.5f, cy + 2f, right - 1, cy - 1.5f);
            }
        }
    }

    static void RenderHero()
    {
        int w = 1920, h = 48;
        Bitmap hero = new Bitmap(w * K, h * K, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(hero))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.ScaleTransform(K, K);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            DrawTaskbarBg(g, w, h);

            // 状态条停靠在最左空隙（距左缘 10px）
            string[] tokens = new string[] { IconCache, "228.0M", IconIn, "232.1M", IconOut, "979.4k" };
            using (Bitmap strip = RenderStrip(h, tokens, "tool", true, "3 会话 · 翻搅中 · Bash 4s"))
                g.DrawImage(strip, 10, 0, strip.Width / K, h);

            // 居中应用图标组
            int[] icons = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
            float total = icons.Length * 24f + (icons.Length - 1) * 18f;
            float ix = (w - total) / 2f;
            for (int i = 0; i < icons.Length; i++)
            {
                AppIcon(g, icons[i], ix, 12);
                ix += 42f;
                if (i == 3 || i == 7) // 分隔线
                    using (Pen p = new Pen(Color.FromArgb(58, 58, 60), 1f))
                        g.DrawLine(p, ix - 9, 13, ix - 9, 35);
            }

            DrawTrayCluster(g, w, h);
        }
        Save(hero, "strip-hero.png");
    }

    /// <summary>四种状态的状态条特写（2x）。</summary>
    static void RenderStates()
    {
        string[][] rows = new string[][]
        {
            new string[] { "thinking", "琢磨中" },
            new string[] { "tool", "3 会话 · 翻搅中 · Bash 4s" },
            new string[] { "wait", "等待输入 · 2 个会话" },
            new string[] { "error", "出错 · 今日 2 个错误" },
        };
        string[][] tokens = new string[][]
        {
            new string[] { IconCache, "41.7M", IconIn, "38.2M", IconOut, "212.5k" },
            new string[] { IconCache, "228.0M", IconIn, "232.1M", IconOut, "979.4k" },
            new string[] { IconCache, "96.4M", IconIn, "88.1M", IconOut, "431.0k" },
            new string[] { IconCache, "12.9M", IconIn, "10.3M", IconOut, "48.7k" },
        };
        int h = 48, pad = 26, gap = 22;
        Bitmap canvas = null;
        int y = pad;
        Bitmap[] strips = new Bitmap[rows.Length];
        int maxW = 0;
        for (int i = 0; i < rows.Length; i++)
        {
            strips[i] = RenderStrip(h, tokens[i], rows[i][0], rows[i][0] == "thinking" || rows[i][0] == "tool", rows[i][1]);
            if (strips[i].Width > maxW) maxW = strips[i].Width;
        }
        canvas = new Bitmap(maxW + pad * 2, rows.Length * h + (rows.Length + 1) * gap,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(canvas))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(25, 25, 27)))
                g.FillRectangle(bg, 0, 0, canvas.Width, canvas.Height);
            y = gap;
            for (int i = 0; i < strips.Length; i++)
            {
                g.DrawImage(strips[i], (canvas.Width - strips[i].Width) / 2, y, strips[i].Width / K, h);
                y += h + gap;
                strips[i].Dispose();
            }
        }
        Save(canvas, "strip-states.png");
    }

    // ---------- 详情面板（src/PopupForm.cs 复刻，384 逻辑宽，2x）----------

    class FakeSession
    {
        public string Phase, Title, L2, Time;
        public FakeSession(string p, string t, string l2, string tm)
        { Phase = p; Title = t; L2 = l2; Time = tm; }
    }

    static void RenderPopup()
    {
        FakeSession[] sess = new FakeSession[]
        {
            new FakeSession("tool",   "修复日志轮转竞态",     "工具执行 · Bash 1m23s · GLM-5.3", "刚刚"),
            new FakeSession("thinking", "重构任务栏停靠逻辑", "思考中 · GLM-5.3",                "刚刚"),
            new FakeSession("wait",   "编写 README 与展示图", "GLM-5.3 · 12 请求 · 34 工具 · 后台 2", "1 分钟前"),
            new FakeSession("error",  "排查渲染异常",         "出错 · GLM-5.3 · 上次出错 5 分钟前",   "26 分钟前"),
            new FakeSession("done",   "批量重命名脚本",       "GLM-5.3 · 8 请求 · 15 工具",          "2 小时前"),
        };

        const int PanelW = 384, RowH = 48;
        int bodyH = sess.Length * (RowH + 4);
        int totalH = 38 + bodyH + 48;

        Bitmap panel = new Bitmap(PanelW * K, totalH * K, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(panel))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.ScaleTransform(K, K);

            RectangleF full = new RectangleF(0, 0, PanelW, totalH);
            using (GraphicsPath frame = Round(full, 8))
            {
                using (SolidBrush b = new SolidBrush(PanelBg)) g.FillPath(b, frame);
                // 高光描边：上 45% 亮、其余暗（PaintBorder）
                g.SetClip(new RectangleF(0, 0, PanelW, totalH * 0.45f));
                using (Pen pen = new Pen(Color.FromArgb(150, 255, 255, 255), 1f)) g.DrawPath(pen, frame);
                g.ResetClip();
                using (Pen pen = new Pen(Color.FromArgb(70, 255, 255, 255), 1f)) g.DrawPath(pen, frame);
            }

            Font fTitle = NFx(12, true), fL1 = NFx(12, true), fL2 = NFx(11, false);

            DrawT(g, "Agent 状态", fTitle, new SolidBrush(TextMain), 16, 8, 22);

            float y = 38f;
            foreach (FakeSession s in sess)
            {
                RectangleF row = new RectangleF(8, y, PanelW - 16, RowH);
                using (GraphicsPath card = Round(new RectangleF(row.X + 0.5f, row.Y + 0.5f, row.Width - 1, row.Height - 1), 8))
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(26, 255, 255, 255))) g.FillPath(b, card);
                    using (Pen pen = new Pen(Color.FromArgb(28, 255, 255, 255), 1f)) g.DrawPath(pen, card);
                }
                using (SolidBrush db = new SolidBrush(PhaseColor(s.Phase)))
                    g.FillEllipse(db, 8 + 12, y + 18, 12, 12);

                SizeF l1 = g.MeasureString(s.Title, fL1, int.MaxValue, StringFormat.GenericTypographic);
                DrawT(g, s.Title, fL1, new SolidBrush(TextMain), 8 + 32, y + 5, 22);
                DrawT(g, s.Time, fL2, new SolidBrush(TextDim), 8 + PanelW - 16 - 12 - 58, y + 9, 18);
                DrawT(g, s.L2, fL2, new SolidBrush(TextDim), 8 + 32, y + 24, 20);
                y += RowH + 4;
            }

            float fy = totalH - 42f;
            DrawT(g, "今日 81 次请求 · 54 次工具调用 · 1 个错误", fL2, new SolidBrush(TextDim), 16, fy, 16);
            DrawT(g, "Token 入 101.3M · 出 388.6k · 缓存 99.3M", fL2, new SolidBrush(TextDim), 16, fy + 16, 16);
        }

        // 铺在深色渐变桌面上，带投影（画布与变换均按逻辑像素 × K）
        int m = 34;
        Bitmap canvas = new Bitmap(panel.Width + m * 2 * K, panel.Height + m * 2 * K,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(canvas))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.ScaleTransform(K, K);
            RectangleF bgR = new RectangleF(0, 0, canvas.Width / K, canvas.Height / K);
            using (LinearGradientBrush b = new LinearGradientBrush(bgR,
                Color.FromArgb(38, 43, 56), Color.FromArgb(17, 19, 25), 55f))
                g.FillRectangle(b, bgR);

            float pw = panel.Width / K, ph = panel.Height / K;
            g.TranslateTransform(m, m);
            for (int i = 6; i >= 1; i--)
            {
                using (GraphicsPath sh = Round(new RectangleF(-i, -i + 3, pw + i * 2, ph + i * 2), 10))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(9, 0, 0, 0)))
                    g.FillPath(b, sh);
            }
            g.DrawImage(panel, 0, 0, pw, ph);
            g.ResetTransform();
        }
        Save(canvas, "popup.png");
    }

    // ---------- 托盘图标（src/TrayApp.cs RenderIcon 复刻，32 单位坐标）----------

    static Bitmap RenderTrayIcon(string phase, int frame)
    {
        Bitmap bmp = new Bitmap(32 * K * 3 / 2, 32 * K * 3 / 2); // 1.5x 逻辑 32px → 96px
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.ScaleTransform(bmp.Width / 32f, bmp.Width / 32f);

            using (GraphicsPath bg = Round(new RectangleF(1, 1, 30, 30), 8))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 34, 34, 39))) g.FillPath(b, bg);
                using (Pen p = new Pen(Color.FromArgb(255, 96, 96, 104), 1f)) g.DrawPath(p, bg);
            }

            Color c = PhaseColor(phase);
            switch (phase)
            {
                case "thinking":
                    using (Pen orbit = new Pen(Color.FromArgb(110, c), 2.4f))
                        g.DrawEllipse(orbit, 7, 7, 18, 18);
                    double a = frame * (Math.PI / 6.0);
                    float dx = 16f + 9f * (float)Math.Cos(a);
                    float dy = 16f + 9f * (float)Math.Sin(a);
                    using (SolidBrush b = new SolidBrush(c))
                    {
                        g.FillEllipse(b, dx - 3f, dy - 3f, 6f, 6f);
                        g.FillEllipse(b, 13, 13, 6, 6);
                    }
                    break;
                case "tool":
                    using (GraphicsPath sq = Round(new RectangleF(9, 9, 14, 14), 3))
                    using (SolidBrush b = new SolidBrush(c))
                        g.FillPath(b, sq);
                    using (Pen w = new Pen(Color.FromArgb(32, 32, 32), 2f))
                        g.DrawRectangle(w, 13, 15, 6, 2.5f);
                    break;
                case "wait":
                    using (Pen p = new Pen(c, 3.2f)) g.DrawEllipse(p, 9, 9, 14, 14);
                    break;
                case "done":
                    using (Pen p = new Pen(c, 3f))
                        g.DrawLines(p, new PointF[] {
                            new PointF(10, 16.5f), new PointF(14, 20.5f), new PointF(22.5f, 11.5f) });
                    break;
                case "error":
                    using (SolidBrush b = new SolidBrush(c)) g.FillEllipse(b, 8, 8, 16, 16);
                    using (StringFormat sf = new StringFormat())
                    {
                        sf.Alignment = StringAlignment.Center;
                        sf.LineAlignment = StringAlignment.Center;
                        using (SolidBrush w = new SolidBrush(Color.White))
                            g.DrawString("!", new Font("Segoe UI", 12f, FontStyle.Bold), w,
                                new RectangleF(0, -1f, 32, 32), sf);
                    }
                    break;
                default:
                    using (SolidBrush b = new SolidBrush(c)) g.FillEllipse(b, 10, 10, 12, 12);
                    break;
            }
        }
        return bmp;
    }

    static void RenderTrayRow()
    {
        string[] phases = { "thinking", "tool", "wait", "done", "error", "idle" };
        string[] names = { "思考中", "工具执行", "等待输入", "已完成", "出错", "空闲" };
        int tile = 96, gap = 26, margin = 30, capH = 24;
        Bitmap canvas = new Bitmap(margin * 2 + phases.Length * tile + (phases.Length - 1) * gap,
            margin + tile + 6 + capH + margin - 14,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(canvas))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            RectangleF bgR = new RectangleF(0, 0, canvas.Width, canvas.Height);
            using (LinearGradientBrush b = new LinearGradientBrush(bgR,
                Color.FromArgb(32, 32, 35), Color.FromArgb(24, 24, 27), 90f))
                g.FillRectangle(b, bgR);

            Font cap = NFx(13, false);
            using (SolidBrush cb = new SolidBrush(TextDim))
            {
                float x = margin;
                for (int i = 0; i < phases.Length; i++)
                {
                    using (Bitmap ic = RenderTrayIcon(phases[i], 2))
                        g.DrawImage(ic, x, margin, tile, tile);
                    float cw = TextW(g, names[i], cap);
                    g.DrawString(names[i], cap, cb, x + (tile - cw) / 2f, margin + tile + 6);
                    x += tile + gap;
                }
            }
        }
        Save(canvas, "tray-icons.png");
    }

    static void Main(string[] args)
    {
        outDir = args.Length > 0 ? args[0] : "img";
        Directory.CreateDirectory(outDir);
        RenderHero();
        RenderStates();
        RenderPopup();
        RenderTrayRow();
    }
}
