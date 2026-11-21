using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MyDock
{
    // 分层菜单窗口：DockForm 是 WS_EX_NOACTIVATE 的，系统 ContextMenuStrip 挂在这种
    // 窗口上只能显示、点不动，所以菜单自绘在一张真 alpha 画布上，命中判定自己做。
    internal class DockMenu : Form
    {
        internal class Entry
        {
            public string Text;
            public Action Act;
            public bool Sep;
            public Rectangle R;
        }

        public event EventHandler Dismissed;

        private List<Entry> _items = new List<Entry>();
        private Bitmap _buf;
        private int _hover = -1;
        private int _pressed = -1;
        private Timer _watch;
        private int _outMs;
        private int _shownAt;

        private const int MARGIN = 12;     // 阴影余量（窗口比菜单大一圈）
        private const int ROW = 34;
        private const int SEPH = 9;
        private const int PADX = 10;       // 左右内边距
        private const int TEXTX = 18;      // 文字左缩进
        private const int PADY = 8;

        private Font _font;

        public DockMenu()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            DoubleBuffered = false;
            _font = new Font("Microsoft YaHei UI", 9.5f);
            _watch = new Timer();
            _watch.Interval = 40;
            _watch.Tick += delegate { Watch(); };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080;   // WS_EX_TOOLWINDOW
                cp.ExStyle |= 0x08000000;   // WS_EX_NOACTIVATE
                cp.ExStyle |= 0x00080000;   // WS_EX_LAYERED
                cp.ExStyle |= 0x00000008;   // WS_EX_TOPMOST
                return cp;
            }
        }

        // ---------------------------------------------------------- 显示
        public void ShowAt(Point anchor, List<Entry> items)
        {
            _items = items;
            _hover = -1;
            _pressed = -1;
            _outMs = 0;
            _shownAt = Environment.TickCount;

            int textW = 0;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                StringFormat sf = new StringFormat(StringFormatFlags.NoWrap | StringFormatFlags.NoClip);
                sf.Trimming = StringTrimming.EllipsisCharacter;
                foreach (Entry e in _items)
                {
                    if (e.Sep || e.Text == null) continue;
                    SizeF s = g.MeasureString(e.Text, _font, new PointF(0, 0), sf);
                    if ((int)Math.Ceiling(s.Width) > textW) textW = (int)Math.Ceiling(s.Width);
                }
                sf.Dispose();
            }
            int bodyW = Math.Max(150, textW + TEXTX + PADX + 8);

            // 先算行位置（相对窗口）
            int y = MARGIN + PADY;
            foreach (Entry e in _items)
            {
                int h = e.Sep ? SEPH : ROW;
                e.R = new Rectangle(MARGIN + PADX, y, bodyW - PADX * 2, h);
                y += h;
            }
            int bodyH = y + PADY - MARGIN;

            Size size = new Size(bodyW + MARGIN * 2, bodyH + MARGIN * 2);

            // 位置：默认贴着锚点往右，超出工作区就翻到左侧
            Rectangle work = Screen.FromPoint(anchor).WorkingArea;
            int x = anchor.X;
            int yy = anchor.Y - ROW / 2;
            if (x + size.Width > work.Right) x = work.Right - size.Width;
            if (x < work.Left) x = work.Left;
            if (yy + size.Height > work.Bottom) yy = work.Bottom - size.Height;
            if (yy < work.Top) yy = work.Top;

            Size = size;
            Location = new Point(x, yy);
            Log.Write("menu show size=" + size + " loc=" + Location + " rows=" + _items.Count);
            if (_buf != null) { _buf.Dispose(); _buf = null; }
            Visible = true;
            RaiseTopMost();
            Render();
            _watch.Start();
        }

        public void HideMenu()
        {
            if (!Visible) return;
            _watch.Stop();
            _hover = -1;
            _pressed = -1;
            Visible = false;
            if (Dismissed != null) Dismissed(this, EventArgs.Empty);
        }

        private void Watch()
        {
            // 刚弹出时鼠标多半还在白条上，先给个宽容期，别一出来就自我关闭
            if (Environment.TickCount - _shownAt < 650) return;
            Point p = Cursor.Position;
            if (!Bounds.Contains(p))
            {
                _outMs += _watch.Interval;
                if (_outMs > 300) { HideMenu(); }
            }
            else _outMs = 0;

            if (!Visible) _watch.Stop();
            // 快捷栏本身也是置顶窗口，菜单要持续压在它上面，否则会被图标盖住
            RaiseTopMost();
        }

        private void RaiseTopMost()
        {
            if (!IsHandleCreated) return;
            SetWindowPos(Handle, (IntPtr)(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        }

        // ---------------------------------------------------------- 命中
        private int HitIndex(Point pt)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Sep) continue;
                if (_items[i].R.Contains(pt)) return i;
            }
            return -1;
        }

        private void UpdateHover(Point p)
        {
            int idx = HitIndex(p);
            if (idx != _hover)
            {
                _hover = idx;
                Render();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            UpdateHover(e.Location);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1) { _hover = -1; Render(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) _pressed = HitIndex(e.Location);
            else if (e.Button == MouseButtons.Right) HideMenu();
            UpdateHover(e.Location);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            int idx = HitIndex(e.Location);
            Action act = null;
            if (idx >= 0 && idx == _pressed) act = _items[idx].Act;
            _pressed = -1;
            HideMenu();
            if (act != null) { try { act(); } catch { } }
        }

        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnResize(EventArgs e) { base.OnResize(e); if (Visible) Render(); }

        // ---------------------------------------------------------- 绘制
        private void Render()
        {
            if (!IsHandleCreated || Width <= 0 || Height <= 0) return;
            try
            {
                bool light = Theme.IsLight();
                if (_buf == null || _buf.Width != Width || _buf.Height != Height)
                {
                    if (_buf != null) _buf.Dispose();
                    _buf = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                }

                Graphics g = Graphics.FromImage(_buf);
                try
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                    // 菜单底色（RGB 值，用于逐行的不透明小画布）
                    Color bodyRgb = light ? Color.FromArgb(251, 252, 253) : Color.FromArgb(32, 34, 39);

                    Rectangle body = new Rectangle(MARGIN, MARGIN, Width - MARGIN * 2 - 1, Height - MARGIN * 2 - 1);

                    // 柔和外阴影
                    for (int i = 9; i >= 1; i--)
                    {
                        Rectangle sh = body;
                        sh.Inflate(i, i);
                        sh.Offset(0, 2);
                        int a = light ? 8 : 12;
                        using (GraphicsPath p = Gfx.Round(sh, 12 + i))
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(a, 0, 0, 0)))
                            g.FillPath(b, p);
                    }

                    // 主体
                    using (GraphicsPath p = Gfx.Round(body, 12))
                    {
                        Color fill = light ? Color.FromArgb(250, 251, 252, 253) : Color.FromArgb(248, 32, 34, 39);
                        using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                        using (Pen pen = new Pen(light ? Color.FromArgb(34, 0, 0, 0) : Color.FromArgb(30, 255, 255, 255)))
                            g.DrawPath(pen, p);
                    }

                    Color ink = light ? Color.FromArgb(32, 34, 38) : Color.FromArgb(236, 238, 243);
                    Color accent = light ? Color.FromArgb(0, 120, 212) : Color.FromArgb(96, 165, 250);

                    foreach (Entry e in _items)
                    {
                        if (e.Sep)
                        {
                            using (Pen pen = new Pen(light ? Color.FromArgb(24, 0, 0, 0) : Color.FromArgb(26, 255, 255, 255)))
                                g.DrawLine(pen, body.X + 14, e.R.Y + e.R.Height / 2, body.Right - 14, e.R.Y + e.R.Height / 2);
                            continue;
                        }

                        bool hot = _items.IndexOf(e) == _hover;

                        // 每一行先画到一张“不透明”小画布上，再上字：
                        // 直接往预乘 alpha 画布上写 GDI 文字会留下彩色描边（发虚的根源）；
                        // 这里用 GDI+ 的灰度抗锯齿（GridFit）在整数坐标上画，笔画干净又不显粗。
                        using (Bitmap row = new Bitmap(e.R.Width, e.R.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
                        {
                            using (Graphics rg = Graphics.FromImage(row))
                            {
                                rg.Clear(bodyRgb);
                                rg.SmoothingMode = SmoothingMode.AntiAlias;
                                rg.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                                if (hot)
                                {
                                    Rectangle hr = new Rectangle(0, 0, row.Width - 1, row.Height - 1);
                                    using (GraphicsPath p = Gfx.Round(hr, 7))
                                    using (SolidBrush b = new SolidBrush(Color.FromArgb(light ? 26 : 40, accent.R, accent.G, accent.B)))
                                        rg.FillPath(b, p);
                                }

                                int tx = TEXTX - PADX;
                                SizeF sz = rg.MeasureString(e.Text, _font);
                                int ty = (int)Math.Round((row.Height - sz.Height) / 2f) + 1;
                                using (SolidBrush brush = new SolidBrush(hot ? accent : ink))
                                    rg.DrawString(e.Text, _font, brush, new PointF(tx, ty));
                            }
                            g.DrawImageUnscaled(row, e.R.X, e.R.Y);
                        }
                    }
                }
                finally { g.Dispose(); }

                PushLayered();
            }
            catch (Exception ex) { Log.Write("menu render: " + ex.GetType().Name + " " + ex.Message); }
        }

        // ---------------------------------------------------------- 分层推送
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLEND { public byte Op, Flags, Alpha, Format; }

        [DllImport("user32.dll")]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref Point pos, ref Size size,
            IntPtr hdcSrc, ref Point src, int key, ref BLEND blend, int flags);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);

        private void PushLayered()
        {
            if (_buf == null) return;
            IntPtr scr = GetDC(IntPtr.Zero);
            IntPtr mem = CreateCompatibleDC(scr);
            IntPtr hb = _buf.GetHbitmap(Color.FromArgb(0, 0, 0, 0));
            IntPtr old = SelectObject(mem, hb);

            Point pos = new Point(Left, Top);
            Size size = new Size(_buf.Width, _buf.Height);
            Point src = new Point(0, 0);
            BLEND bl = new BLEND();
            bl.Op = 0;
            bl.Flags = 0;
            bl.Alpha = 255;
            bl.Format = 1;
            UpdateLayeredWindow(Handle, scr, ref pos, ref size, mem, ref src, 0, ref bl, 2);

            SelectObject(mem, old);
            DeleteObject(hb);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, scr);
        }
    }
}
