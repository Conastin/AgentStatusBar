using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace AgentStatusBar
{
    static class Ui
    {
        public static readonly Color Bg = Color.FromArgb(36, 36, 41);
        public static readonly Color BgBorder = Color.FromArgb(74, 74, 82);
        public static readonly Color BgCard = Color.FromArgb(36, 36, 41);
        public static readonly Color TextMain = Color.FromArgb(238, 238, 242);
        public static readonly Color TextDim = Color.FromArgb(158, 158, 166);

        /// <summary>Win11 的可变字体，小字号下比 Segoe UI 清晰；无则回退。</summary>
        public static readonly string DisplayFontFamily = DetectFamily();

        /// <summary>中文字体链：Maple Mono NF CN（拉丁+中文一体等宽）> 苹方 > Noto Sans SC > 微软雅黑。</summary>
        public static readonly string CjkFontFamily = DetectCjk();

        /// <summary>当前是否为拉丁/中文一体的字体（无需混排分字号补偿）。</summary>
        public static readonly bool UnifiedFont = CjkFontFamily == "Maple Mono NF CN";

        static string DetectFamily()
        {
            try
            {
                foreach (FontFamily ff in FontFamily.Families)
                    if (ff.Name == "Segoe UI Variable Text") return "Segoe UI Variable Text";
            }
            catch { }
            return "Segoe UI";
        }

        static string DetectCjk()
        {
            try
            {
                string[] prefer = { "Maple Mono NF CN", "PingFang SC", "Noto Sans SC", "MiSans", "HarmonyOS Sans SC", "Microsoft YaHei UI" };
                List<string> names = new List<string>();
                foreach (FontFamily ff in FontFamily.Families) names.Add(ff.Name);
                foreach (string p in prefer)
                    if (names.Contains(p)) return p;
            }
            catch { }
            return DisplayFontFamily;
        }

        public static readonly Font Text = new Font(CjkFontFamily, 9f);
        public static readonly Font TextBold = new Font(CjkFontFamily, 9f, FontStyle.Bold);
        public static readonly Font Small = new Font(CjkFontFamily, 8.25f);

        public static Color PhaseColor(Phase p)
        {
            switch (p)
            {
                case Phase.Thinking: return Color.FromArgb(76, 194, 255);
                case Phase.ToolRunning: return Color.FromArgb(255, 185, 0);
                case Phase.WaitingInput: return Color.FromArgb(108, 203, 95);
                case Phase.Completed: return Color.FromArgb(126, 178, 143); // 灰绿：干完活了
                case Phase.Error: return Color.FromArgb(255, 96, 89);
                case Phase.Idle: return Color.FromArgb(138, 138, 138);
                default: return Color.FromArgb(90, 90, 90);
            }
        }

        public static string PhaseText(Phase p)
        {
            switch (p)
            {
                case Phase.Thinking: return "思考中";
                case Phase.ToolRunning: return "工具执行";
                case Phase.WaitingInput: return "等待输入";
                case Phase.Completed: return "已完成";
                case Phase.Error: return "出错";
                case Phase.Idle: return "空闲";
                default: return "无数据";
            }
        }

        public static string Ago(DateTime t)
        {
            if (t == DateTime.MinValue) return "从未";
            TimeSpan d = DateTime.Now - t;
            if (d.TotalSeconds < 0) d = TimeSpan.Zero;
            if (d.TotalMinutes < 1) return "刚刚";
            if (d.TotalHours < 1) return ((int)d.TotalMinutes) + " 分钟前";
            if (d.TotalDays < 1) return ((int)d.TotalHours) + " 小时前";
            return ((int)d.TotalDays) + " 天前";
        }

        /// <summary>token 数量缩写：1234 → 1.2k，1234567 → 1.2M，1.2B。</summary>
        public static string KT(long v)
        {
            if (v <= 0) return "0";
            if (v < 1000) return v.ToString(CultureInfo.InvariantCulture);
            if (v < 1000000) return (v / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "k";
            if (v < 1000000000L) return (v / 1000000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M";
            return (v / 1000000000.0).ToString("0.#", CultureInfo.InvariantCulture) + "B";
        }

        public static string Dur(TimeSpan t)
        {
            if (t.TotalSeconds < 0) t = TimeSpan.Zero;
            if (t.TotalSeconds < 60) return ((int)t.TotalSeconds) + "s";
            return ((int)t.TotalMinutes) + "m" + ((int)(t.TotalSeconds % 60)).ToString("00") + "s";
        }

        public static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            var path = new GraphicsPath();
            if (rad < 0.5f || r.Width < 1 || r.Height < 1)
            {
                path.AddRectangle(r);
                return path;
            }
            float d = rad * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
