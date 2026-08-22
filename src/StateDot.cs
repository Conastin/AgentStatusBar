using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AgentStatusBar
{
    /// <summary>按相位着色的小圆点。</summary>
    class StateDot : Control
    {
        Phase phase = Phase.Idle;

        public Phase Phase
        {
            get { return phase; }
            set { phase = value; Invalidate(); }
        }

        public StateDot()
        {
            Width = 12;
            Height = 12;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush b = new SolidBrush(Ui.PhaseColor(phase)))
                e.Graphics.FillEllipse(b, 0, 0, Width - 1, Height - 1);
        }
    }
}
