using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MyDock
{
    public enum DockEdge { None = 0, Left = 1, Top = 2, Right = 3, Bottom = 4 }

    internal static class Log
    {
        public static string FilePath = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "dock.log");
        public static void Write(string s)
        {
            try { File.AppendAllText(FilePath, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s + "\r\n"); }
            catch { }
        }
    }

    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("winmm.dll")]
        private static extern int timeBeginPeriod(int period);

        [DllImport("winmm.dll")]
        private static extern int timeEndPeriod(int period);

        [DllImport("winmm.dll")]
        internal static extern int timeSetEvent(int delay, int res, TimerProc proc, IntPtr user, int flags);

        [DllImport("winmm.dll")]
        internal static extern int timeKillEvent(int id);

        internal delegate void TimerProc(int id, int msg, IntPtr user, IntPtr p1, IntPtr p2);

        [STAThread]
        private static void Main()
        {
            bool created = false;
            using (Mutex m = new Mutex(true, "Global\\MyDock_SingleInstance_2026", out created))
            {
                if (!created) return;
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { Log.Write("THREAD EXC: " + e.Exception); };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e) { Log.Write("UNHANDLED: " + e.ExceptionObject); };
                try { SetProcessDPIAware(); }
                catch { }
                // 把系统定时器精度提到 1ms，动画才能真正跑满帧
                try { timeBeginPeriod(1); }
                catch { }
                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new DockForm());
                }
                finally
                {
                    try { timeEndPeriod(1); }
                    catch { }
                }
            }
        }
    }

    // ---------------------------------------------------------------- 画笔工具
    internal static class Gfx
    {
        public static GraphicsPath Circle(Rectangle r)
        {
            GraphicsPath p = new GraphicsPath();
            p.AddEllipse(r);
            return p;
        }

        public static GraphicsPath Circle(RectangleF r)
        {
            GraphicsPath p = new GraphicsPath();
            p.AddEllipse(r);
            return p;
        }

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            if (radius <= 0 || r.Width <= radius * 2 || r.Height <= radius * 2)
            {
                p.AddRectangle(r);
                p.CloseFigure();
                return p;
            }
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // ---------------------------------------------------------------- 配色
    internal static class Pal
    {
        public static readonly Color Panel = Color.FromArgb(26, 28, 32);      // 面板底色（无背景模式时兼作透明键）
        public static readonly Color Bar = Color.FromArgb(238, 242, 246);     // 白条
    }

    // ---------------------------------------------------------------- 白色拖动手柄
    internal class HandleStrip : Control
    {
        public bool Plain;   // 无背景模式下用直角绘制，避免透明键边缘出现紫边
        public float Reveal = 1f;   // 0~1：胶囊从中间往两边长出来，进出不硬闪

        public HandleStrip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Cursor = Cursors.SizeAll;
            BackColor = Pal.Panel;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int w = Math.Max(2, Width);
            int h = Math.Max(2, Height);

            // 无背景模式：先把控件底色铺满透明键颜色，再画胶囊，避免残留直角
            if (Plain)
            {
                using (SolidBrush bg = new SolidBrush(BackColor))
                    g.FillRectangle(bg, ClientRectangle);
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // 按 Reveal 收缩胶囊长度，中心对齐，进出都像“长出来/缩回去”
            float rv = Reveal;
            if (rv < 0f) rv = 0f;
            if (rv > 1f) rv = 1f;
            if (rv <= 0.01f) return;

            Rectangle box = new Rectangle(0, 0, w, h);
            if (w >= h)
            {
                int cw = (int)Math.Round(w * rv);
                if (cw < h) cw = Math.Min(h, w);   // 再短就是个圆点，够用了
                box = new Rectangle((w - cw) / 2, 0, cw, h);
            }
            else
            {
                int ch = (int)Math.Round(h * rv);
                if (ch < w) ch = Math.Min(w, h);
                box = new Rectangle(0, (h - ch) / 2, w, ch);
            }

            int rad = Math.Min(box.Width, box.Height) / 2;      // 半圆端头 = 胶囊形，最圆滑
            if (rad < 1) rad = 1;

            GraphicsPath p = new GraphicsPath();
            p.AddArc(box.X, box.Y, rad * 2, rad * 2, 180, 90);
            p.AddArc(box.Right - rad * 2, box.Y, rad * 2, rad * 2, 270, 90);
            p.AddArc(box.Right - rad * 2, box.Bottom - rad * 2, rad * 2, rad * 2, 0, 90);
            p.AddArc(box.X, box.Bottom - rad * 2, rad * 2, rad * 2, 90, 90);
            p.CloseFigure();
            using (p)
            using (SolidBrush b = new SolidBrush(Pal.Bar))
                g.FillPath(b, p);
        }
    }

    // ---------------------------------------------------------------- 顶端“桌面文件夹”入口 / 底端“设置”入口
    internal class SpecialButton : Control
    {
        public Action Press;
        private readonly Image _img;
        private readonly bool _gear;
        private readonly Color _bg;
        private bool _hover;

        public SpecialButton(Image img, bool gear, Color bg, int size)
        {
            _img = img;
            _gear = gear;
            _bg = bg;
            BackColor = bg;
            Size = new Size(size, size);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location))
            {
                if (Press != null) Press();
            }
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            using (SolidBrush b = new SolidBrush(_bg))
                g.FillRectangle(b, ClientRectangle);

            if (_hover)
            {
                Rectangle hr = new Rectangle(0, 0, Width - 1, Height - 1);
                using (GraphicsPath p = Gfx.Round(hr, 9))
                {
                    using (SolidBrush hb = new SolidBrush(Color.FromArgb(64, 255, 255, 255)))
                        g.FillPath(hb, p);
                    using (Pen pen = new Pen(Color.FromArgb(120, 255, 255, 255)))
                        g.DrawPath(pen, p);
                }
            }

            if (_img != null)
            {
                Rectangle dr = ClientRectangle;
                dr.Inflate(-2, -2);
                int rad = (int)Math.Round(Math.Min(dr.Width, dr.Height) * 0.235);
                if (rad < 4) rad = 4;
                using (GraphicsPath p = Gfx.Round(dr, rad))
                {
                    GraphicsState st = g.Save();
                    g.SetClip(p, CombineMode.Replace);
                    g.DrawImage(_img, dr);
                    g.Restore(st);
                }
            }
            else if (_gear)
            {
                DrawGear(g);
            }
        }

        private void DrawGear(Graphics g)
        {
            Rectangle r = ClientRectangle;
            r.Inflate(-3, -3);
            float cx = r.X + r.Width / 2f;
            float cy = r.Y + r.Height / 2f;
            float outer = Math.Min(r.Width, r.Height) * 0.46f;
            float body = outer * 0.66f;

            Color c = _hover ? Color.FromArgb(255, 255, 255) : Color.FromArgb(206, 212, 220);
            using (SolidBrush br = new SolidBrush(c))
            {
                for (int i = 0; i < 8; i++)
                {
                    GraphicsState st = g.Save();
                    g.TranslateTransform(cx, cy);
                    g.RotateTransform(i * 45f);
                    g.FillRectangle(br, -outer * 0.11f, -outer, outer * 0.22f, outer - body * 0.85f);
                    g.Restore(st);
                }
                g.FillEllipse(br, cx - body, cy - body, body * 2, body * 2);

                GraphicsPath ring = new GraphicsPath();
                ring.AddEllipse(cx - body, cy - body, body * 2, body * 2);
                using (Pen pen = new Pen(c, 1.2f)) g.DrawPath(pen, ring);
                ring.Dispose();
            }
            using (SolidBrush hole = new SolidBrush(_bg))
                g.FillEllipse(hole, cx - body * 0.42f, cy - body * 0.42f, body * 0.84f, body * 0.84f);
        }
    }

    // ---------------------------------------------------------------- 单个快捷方式（纯数据 + 自绘）
    // ---------------------------------------------------------------- 拖动浮影
    // 主面板只有“图标总宽 + 一点余量”这么大，图标一旦被拖出窗口就会被裁掉。
    // 所以拖动时另开一块跟随鼠标的分层小窗：鼠标穿透、不抢焦点，松手立刻消失，
    // 图标回到白条里它该在的格子。
    internal class DragGhost : Form
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLEND { public byte Op, Flags, Alpha, Format; }

        [DllImport("user32.dll")]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref Point pos, ref Size size,
            IntPtr hdcSrc, ref Point src, int key, ref BLEND blend, int flags);
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);

        private Bitmap _buf;
        private bool _shown;

        public DragGhost()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            SetStyle(ControlStyles.UserPaint | ControlStyles.Opaque, true);
            Bounds = new Rectangle(-2400, -2400, 1, 1);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080;   // TOOLWINDOW
                cp.ExStyle |= 0x08000000;   // NOACTIVATE
                cp.ExStyle |= 0x00080000;   // LAYERED
                cp.ExStyle |= 0x00000020;   // TRANSPARENT：鼠标事件全部穿透
                return cp;
            }
        }

        public void ShowAt(Bitmap disc, int size, Point center)
        {
            if (disc == null) return;

            int s = (int)Math.Round(size * 1.14f);
            int pad = 14;
            int c = s + pad * 2;
            if (_buf == null || _buf.Width != c)
            {
                if (_buf != null) _buf.Dispose();
                _buf = new Bitmap(c, c, PixelFormat.Format32bppPArgb);
            }

            using (Graphics g = Graphics.FromImage(_buf))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;

                int x = (c - s) / 2, y = (c - s) / 2 - 3;

                // 落地影子（两层，外浅内深），拖动时看起来是“拿起来”的
                using (SolidBrush b = new SolidBrush(Color.FromArgb(30, 0, 0, 0)))
                    g.FillEllipse(b, x + 1, y + s - 12, s - 2, 16);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(48, 0, 0, 0)))
                    g.FillEllipse(b, x + 5, y + s - 10, s - 10, 11);

                InterpolationMode im = g.InterpolationMode;
                g.DrawImage(disc, new Rectangle(x, y, s, s));
                g.InterpolationMode = im;
            }

            IntPtr h = Handle;   // 强制建句柄（不显示就不会闪一下）
            if (!_shown) { _shown = true; Show(); }
            Push(h, new Point(center.X - c / 2, center.Y - c / 2));
        }

        public void HideGhost()
        {
            if (!_shown) return;
            Push(Handle, new Point(-2400, -2400));
        }

        private void Push(IntPtr hwnd, Point pos)
        {
            if (_buf == null) return;
            IntPtr scr = GetDC(IntPtr.Zero);
            IntPtr mem = CreateCompatibleDC(scr);
            IntPtr hb = _buf.GetHbitmap(Color.FromArgb(0, 0, 0, 0));
            IntPtr old = SelectObject(mem, hb);

            Size size = new Size(_buf.Width, _buf.Height);
            Point src = new Point(0, 0);
            BLEND bl = new BLEND();
            bl.Op = 0;
            bl.Flags = 0;
            bl.Alpha = 255;
            bl.Format = 1;
            UpdateLayeredWindow(hwnd, scr, ref pos, ref size, mem, ref src, 0, ref bl, 2);

            SelectObject(mem, old);
            DeleteObject(hb);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, scr);
        }
    }

    internal class DockItem
    {
        public readonly string TargetPath;
        public Rectangle R;          // 在面板里的位置
        public bool Hover;
        public float H;              // 悬停放大进度：0 常态 / 1 放大到位
        public float HTarget;        // 放大动画目标
        public float HFrom;          // 本段动画起点
        public int HStart;           // 本段动画起始时刻（ms）
        private readonly Bitmap _disc;

        public DockItem(string path, Image img, int size)
        {
            TargetPath = path;
            // 圆盘按“悬停放大后的最大尺寸”烘焙（含回弹的 5% 余量）：放大时接近 1:1 贴图，不发虚
            int bake = (int)Math.Ceiling(size * (1f + HOVER_ZOOM * 1.08f));
            if (bake < size) bake = size;
            _disc = MakeDisc(img, TintOf(img), bake);
        }

        public bool Hit(Point p) { return R.Contains(p); }

        public Bitmap Disc { get { return _disc; } }

        // 取图标自身的平均色，作为圆形底盘的颜色（透明底图标也不会露出方角）
        private static Color TintOf(Image img)
        {
            if (img == null) return Pal.Panel;
            Color avg = Pal.Panel;
            try
            {
                using (Bitmap bmp = new Bitmap(img))
                {
                    int r = 0, g = 0, b = 0, n = 0;
                    int step = bmp.Width > 96 ? 2 : 1;
                    for (int y = 0; y < bmp.Height; y += step)
                    {
                        for (int x = 0; x < bmp.Width; x += step)
                        {
                            Color c = bmp.GetPixel(x, y);
                            if (c.A < 210) continue;
                            r += c.R; g += c.G; b += c.B; n++;
                        }
                    }
                    if (n > 0) avg = Color.FromArgb(r / n, g / n, b / n);
                }
            }
            catch { }
            return avg;
        }

        // 烘一张真正带 alpha 的圆盘：4×4 超采样同时求“颜色”和“覆盖度”，
        // 得到正确的半透明边缘。窗口是分层窗口，DWM 直接和桌面合成，
        // 所以既没有透明键混出来的黑边，也没有二值化产生的锯齿。
        private static Bitmap MakeDisc(Image src, Color tint, int size)
        {
            if (src == null || size <= 0) return null;
            const int SS = 4;
            int big = size * SS;

            Bitmap art = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                using (Graphics g = Graphics.FromImage(art))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    g.DrawImage(src, new Rectangle(0, 0, size, size));
                }
            }
            catch { }

            Bitmap big2 = new Bitmap(big, big, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(big2))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.Clear(Color.Transparent);

                float c = big / 2f;
                float rOut = (size / 2f - 0.5f) * SS;

                // 圆盘：先铺图标主色，再把图标裁进圆里
                using (GraphicsPath p = Gfx.Circle(new RectangleF(c - rOut, c - rOut, rOut * 2, rOut * 2)))
                {
                    using (SolidBrush tb = new SolidBrush(tint))
                        g.FillPath(tb, p);
                    GraphicsState st = g.Save();
                    g.SetClip(p, CombineMode.Replace);
                    g.DrawImage(art, new Rectangle(0, 0, big, big));
                    g.Restore(st);
                }
            }
            art.Dispose();

            // 4×4 平均下采样：颜色按 alpha 加权，alpha 取平均，边缘就是一条平滑过渡
            Bitmap disc = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int sa = 0, sr = 0, sg = 0, sb2 = 0;
                    for (int sy = 0; sy < SS; sy++)
                    {
                        for (int sx = 0; sx < SS; sx++)
                        {
                            Color p = big2.GetPixel(x * SS + sx, y * SS + sy);
                            int a = p.A;
                            sa += a; sr += p.R * a; sg += p.G * a; sb2 += p.B * a;
                        }
                    }
                    if (sa == 0) disc.SetPixel(x, y, Color.Transparent);
                    else
                    {
                        int alpha = (int)Math.Round(sa / (float)(SS * SS));
                        disc.SetPixel(x, y, Color.FromArgb(alpha, sr / sa, sg / sa, sb2 / sa));
                    }
                }
            }
            big2.Dispose();
            return disc;
        }

        // 悬停放大倍数：0.28 相当于图标长到 82px 左右（半径各涨约 9px），
        // 竖向 9 颗时首尾正好还能塞进面板（面板上下各留 10px 边距）
        public const float HOVER_ZOOM = 0.28f;

        // k = 本帧的缩放倍数（1 为常态，由悬停进度驱动）；shift = 沿栏方向的位移（邻居被“推开”的涟漪）
        public void Paint(Graphics g, int size, float alpha, float k, float shift, bool vert)
        {
            if (_disc == null || alpha <= 0.01f) return;
            float a = alpha > 1f ? 1f : alpha;
            if (k < 0.5f) k = 0.5f;
            float d = size * k;
            float dx = R.X + (size - d) * 0.5f + (vert ? 0f : shift);
            float dy = R.Y + (size - d) * 0.5f + (vert ? shift : 0f);

            using (ImageAttributes ia = new ImageAttributes())
            {
                ColorMatrix cm = new ColorMatrix();
                if (a < 0.999f) cm.Matrix33 = a;
                ia.SetColorMatrix(cm, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);

                // 关键：用浮点缩放矩阵来画，缩放全程连续亚像素变化。
                // 之前按 Math.Round 取整尺寸，放大过程会一像素一像素地跳，那就是“抖动”的来源。
                GraphicsState st = g.Save();
                g.TranslateTransform(dx, dy);
                float sc = d / _disc.Width;
                g.ScaleTransform(sc, sc);
                g.DrawImage(_disc, new Rectangle(0, 0, _disc.Width, _disc.Height),
                            0, 0, _disc.Width, _disc.Height, GraphicsUnit.Pixel, ia);
                g.Restore(st);
            }
        }

        public void Dispose()
        {
            if (_disc != null) _disc.Dispose();
        }

        public void Launch()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(TargetPath);
                psi.UseShellExecute = true;
                string dir = Path.GetDirectoryName(TargetPath);
                if (dir != null && Directory.Exists(dir)) psi.WorkingDirectory = dir;
                Process.Start(psi);
            }
            catch { }
        }
    }

    // ---------------------------------------------------------------- 悬停名字小气泡
    internal class NameChip : Form
    {
        private string _txt = "";

        public NameChip()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(32, 33, 36);
            Font = new Font("Microsoft YaHei UI", 9f);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000;   // WS_EX_NOACTIVATE
                cp.ExStyle |= 0x00000080;   // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        public void SetText(string text)
        {
            _txt = text == null ? "" : text;
            Size sz = TextRenderer.MeasureText(_txt, Font, new Size(400, 60), TextFormatFlags.NoPadding);
            int w = sz.Width + 24;
            int h = sz.Height + 10;
            if (w < 40) w = 40;
            ClientSize = new Size(w, h);
            Invalidate();
        }

        public void ShowNoActivate()
        {
            if (!Visible) Show();
            else { Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Gfx.Round(new Rectangle(0, 0, Width - 1, Height - 1), Math.Min(9, Height / 2)))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(238, 32, 33, 36)))
                g.FillPath(b, p);
            TextRenderer.DrawText(g, _txt, Font, new Rectangle(0, 0, Width, Height), Color.FromArgb(240, 240, 240),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    // ---------------------------------------------------------------- 主体
    internal class DockForm : Form
    {
        private const int HANDLE_T = 10;   // 白条宽度（比原来粗一点，更好抓）
        private const int GAP_OUT = 14;    // 白条到屏幕边、以及白条到底卡的空隙（两处一样宽）
        private const int CARD_PAD = 10;   // 底卡在图标外侧留的边
        private int _gap = 6;              // 图标间距（设置里可调 0~24）
        private const int PAD = 10;        // 两端留白
        private const int MIN_LEN = 150;   // 最小长度
        private const int SNAP_DIST = 36;  // 靠近边缘多少像素以内才吸附
        private const int CENTER_SNAP_DIST = 110; // 离边的中点这么近就吸到正中
        private const float PUSH_PX = 5f;  // 悬停时邻居沿栏方向被推开的最大像素（macOS Dock 式的“让位”）

        private int _iconSize = 40;        // 图标尺寸（设置里可调：小/中/大/超大）
        private int _bgStyle = 1;          // 背景色：0=黑 1=白 2=跟随系统（都靠 alpha 做玻璃/透明）
        private int _barPct = 25;          // 白条长度：占面板长度的百分比
        private int _alphaPct = 30;        // 背景「透明度」：0=纯色实心，30=毛玻璃（默认），100=完全不显示背景
        private Color _tintColor = Pal.Panel;

        private static readonly Color PanelColor = Pal.Panel;
        private static readonly Color BorderColor = Color.FromArgb(70, 255, 255, 255);
        private static readonly int[] IconSizes = new int[] { 32, 40, 48, 64 };
        private static readonly string[] IconSizeLabels = new string[] { "小", "中", "大", "超大" };

        private DockEdge _edge = DockEdge.Right;   // 吸附边 / 悬浮时的朝向
        private bool _floating;                    // 自由悬浮在屏幕任意位置
        private int _floatX, _floatY;
        private double _ratio = 0.5;
        private float _t = 0f;
        private float _targetT;              // 动画目标（0 收起 / 1 展开）
        private int _animStartMs;            // 本段动画的起始时刻
        private float _animFrom;             // 本段动画的起始进度
        private bool _wantShow;
        private bool _dragging;
        private bool _menuOpen;
        private bool _dragOver;
        private int _leaveMs;

        // 白条有自己的一套显示/隐藏动画，和图标面板互不干扰
        private float _tb = 1f;
        private float _barTarget;
        private int _barAnimStartMs;
        private float _barAnimFrom;
        private bool _wantBar;
        private int _barLeaveMs;
        private int _statLast, _statN, _statSum;   // 动画帧间隔统计
        private double _statWorkSum; private int _statWorkN;
        private System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        private DateTime _graceUntil = DateTime.MinValue;
        private bool _wasWant = true;
        private Point _dragOffset;

        // 连续放大（macOS Dock 手感）：_mag 是总强度 0~1，_magCoord 是光标在沿栏方向的坐标
        private float _mag;
        private float _magTarget;
        private float _magFrom;
        private int _magStart;
        private int _magCoord;
        private bool _locked;   // 锁定吸附位置：白条不能再拖动换边，图标仍可拖动排序

        private System.Windows.Forms.Timer _tick;
        private Thread _animThread;
        private volatile bool _animRun;
        private volatile bool _mmPending;
        private int _prevTickMs;
        private const int FRAME_MS = 10;                 // 动画帧间隔（≈100fps）
        private Rectangle _barRect;                     // 白条所在区域（自绘 + 右键命中）
        private int _barClickTick;                      // 上一次点白条的时刻，用来判定双击
        private Rectangle _bgRect;                      // 背景底卡：只包住图标那一排，白条在它外面
        private Bitmap _buf;                            // 分层窗口的离屏画布
        private Bitmap _gloss;                          // 底卡顶部高光（带抖动，消除灰度阶梯）
        private long _glossKey;
        private bool _passR;                            // 右键穿透到桌面中
        private int _passUntil;
        private System.Windows.Forms.Timer _passTimer;  // 穿透样式恢复（不能依赖帧循环）
        private string _desktopDir;
        private bool _fsLock;              // 全屏时锁定，不响应呼出
        private int _lastFsCheck;
        private string _itemsSig;          // items 目录指纹，外部增删时自动刷新
        private int _lastSigCheck;
        private System.Windows.Forms.Timer _pvTimer;   // 设置窗口实时预览的节流器
        private int _pvSize, _pvBg, _pvBar, _pvGap, _pvAlpha;
        private List<string> _pvPaths;
        private bool _pvOn;
        private readonly List<DockItem> _items = new List<DockItem>();
        private readonly ToolTip _tip = new ToolTip();
        private int _tipIdx = -1;
        private DockMenu _menu;

        private string _rootDir;
        private string _itemsDir;
        private string _iniPath;
        private Rectangle _workArea;

        // 图标拖动排序状态
        private DockItem _pressItem;
        private bool _itemDrag;
        private DragGhost _ghost;
        private Point _pressPoint;
        private Point _grab;

        public DockForm()
        {
            _rootDir = Path.GetDirectoryName(Application.ExecutablePath);
            _itemsDir = Path.Combine(_rootDir, "items");
            _iniPath = Path.Combine(_rootDir, "dock.ini");
            _desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (string.IsNullOrEmpty(_desktopDir))
                _desktopDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop");

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            // 不置顶：只压在桌面/壁纸之上，普通窗口永远盖在它前面
            TopMost = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = PanelColor;
            Text = "MyDock";
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;

            AllowDrop = true;
            DragEnter += OnDockDragEnter;
            DragLeave += OnDockDragLeave;
            DragDrop += OnDockDragDrop;

            MouseDown += OnBarDown;
            MouseMove += OnBarMove;
            MouseUp += OnBarUp;

            BuildMenu();
            LoadSettings();
            UpdateWorkArea();
            ApplyLook();

            Reload();

            _t = 1f;
            _tb = 1f;
            _wantShow = true;
            _wantBar = true;
            _graceUntil = DateTime.Now.AddMilliseconds(2600);
            UpdateWorkArea();
            ApplyPosition();

            // 调试用：带 --settings 启动就直接打开设置窗口，方便截图验收
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--settings") > 0)
            {
                System.Windows.Forms.Timer t0 = new System.Windows.Forms.Timer();
                t0.Interval = 900;
                t0.Tick += delegate { t0.Stop(); t0.Dispose(); ShowSettings(); };
                t0.Start();
            }

            _tick = new System.Windows.Forms.Timer();
            _tick.Interval = FRAME_MS;
            _tick.Tick += OnTick;

            // 右键穿透只持续 260ms，必须有独立定时器负责恢复样式：
            // 帧循环只在动画期间投帧，靠它会永久卡在“鼠标透明”状态。
            _passTimer = new System.Windows.Forms.Timer();
            _passTimer.Interval = 30;
            _passTimer.Tick += delegate
            {
                if (!_passR) { _passTimer.Stop(); return; }
                if ((Environment.TickCount - _passUntil) >= 0)
                {
                    _passR = false;
                    ApplyPassStyle(false);
                    _passTimer.Stop();
                }
            };

            // WinForms 的 Timer 底层是 WM_TIMER，Windows 会把它的节拍并到 15.6ms，
            // 8ms 的设实际只能跑到 ~64fps，动画就一顿一顿；这台机器上多媒体定时器也不可用。
            // 所以用一条后台线程按 FRAME_MS 往窗口投消息，稳定跑到 ~150fps。
            _animRun = true;
            _animThread = new Thread(AnimLoop);
            _animThread.IsBackground = true;
            _animThread.Priority = ThreadPriority.AboveNormal;
            _animThread.Start();

            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        }

        private void AnimLoop()
        {
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            long next = 0;
            while (_animRun)
            {
                next += FRAME_MS;
                long now = sw.ElapsedMilliseconds;
                long wait = next - now;
                if (wait > 0) { try { Thread.Sleep((int)wait); } catch { } }
                else next = now;              // 落后太多就重新对齐，不追帧

                if (_mmPending) continue;     // 上一帧还没画完，跳过这帧
                _mmPending = true;
                try { PostMessage(Handle, WM_APP_TICK, IntPtr.Zero, IntPtr.Zero); }
                catch { _mmPending = false; }
            }
        }

        // ------------------------------------------------------------ 窗口样式
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW 不在 Alt+Tab 出现
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE 不抢焦点
                // WS_EX_LAYERED：整窗自己画成一张 32 位带 alpha 的图交给系统合成，
                // 图标边缘是真正的半透明（没有黑边/锯齿），淡入淡出也不会露杂色
                cp.ExStyle |= 0x00080000;
                return cp;
            }
        }

        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int MA_NOACTIVATE = 0x0003;
        private const int WM_APP_TICK = 0x0400 + 11;

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr w, IntPtr l);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        // ---- z 序：按 EnumWindows 的 z 序（从最上层往下）找桌面/壁纸层
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr param);
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr param);
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        // ---- 分层窗口合成
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLEND { public byte Op, Flags, Alpha, Format; }

        [DllImport("user32.dll")]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref Point pos, ref Size size,
            IntPtr hdcSrc, ref Point src, int key, ref BLEND blend, int flags);
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);

        // ---- 右键穿透
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr h, int idx);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr h, int idx, int val);
        [DllImport("user32.dll")]
        private static extern void mouse_event(uint flags, int dx, int dy, uint data, IntPtr extra);
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;

        private void AssertTopMost()
        {
            // 菜单/设置开着的时候不再动自己，否则会把它们盖住
            if (_menu != null && _menu.Visible) return;
            try
            {
                // z 序：找到最上层的那块桌面/壁纸层（Progman / WorkerW），把自己插在它上面。
                // 这样快捷栏只浮在壁纸之上，任何普通窗口都在它前面——不会挡住别人的窗口。
                IntPtr anchor = IntPtr.Zero;
                EnumWindows(delegate(IntPtr h, IntPtr p)
                {
                    if (h == Handle || !IsWindowVisible(h)) return true;
                    if (WindowClass(h) == "Progman" || WindowClass(h) == "WorkerW")
                    {
                        anchor = h;
                        return false;
                    }
                    return true;
                }, IntPtr.Zero);

                if (anchor != IntPtr.Zero)
                    SetWindowPos(Handle, anchor, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
            catch { }
        }

        private static string WindowClass(IntPtr h)
        {
            StringBuilder sb = new StringBuilder(256);
            try { GetClassName(h, sb, sb.Capacity); }
            catch { }
            return sb.ToString();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_APP_TICK)
            {
                _mmPending = false;
                long w0 = _clock.ElapsedTicks;
                OnTick(null, EventArgs.Empty);
                _statWorkSum += (_clock.ElapsedTicks - w0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                _statWorkN++;
                return;
            }
            if (m.Msg == WM_MOUSEACTIVATE)
            {
                m.Result = (IntPtr)MA_NOACTIVATE;
                return;
            }
            base.WndProc(ref m);
        }

        // ------------------------------------------------------------ 绘制（分层窗口，自己合成一张 ARGB 图）
        protected override void OnPaint(PaintEventArgs e)
        {
            // 分层窗口的画面全靠 UpdateLayeredWindow，这里不用画
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }

        // 生成一张带 Bayer 抖动的高光图：8bit alpha 渐变在深色半透明底上会出现“阶梯”，
        // 用有序抖动把每一级 alpha 打散，肉眼看就是连续过渡
        private Bitmap Gloss(int w, int h, int hi, bool horiz)
        {
            if (w < 1 || h < 1 || w > 4000 || h > 4000) return null;
            long key = ((long)w << 40) | ((long)h << 20) | ((long)hi << 2) | (horiz ? 1L : 0L);
            if (_gloss != null && _glossKey == key) return _gloss;
            if (_gloss != null) { _gloss.Dispose(); _gloss = null; }
            Bitmap b = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            System.Drawing.Imaging.BitmapData bd = b.LockBits(new Rectangle(0, 0, w, h),
                System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            int stride = bd.Stride;
            byte[] px = new byte[stride * h];
            int[,] bay = new int[,] { { 0, 8, 2, 10 }, { 12, 4, 14, 6 }, { 3, 11, 1, 9 }, { 15, 7, 13, 5 } };
            for (int y = 0; y < h; y++)
            {
                int row = y * stride;
                for (int x = 0; x < w; x++)
                {
                    float t = horiz ? (w <= 1 ? 0f : x / (float)(w - 1)) : (h <= 1 ? 0f : y / (float)(h - 1));
                    double a = hi * (1.0 - t);
                    int ai = (int)a;
                    double frac = a - ai;
                    if (frac * 16.0 > bay[y & 3, x & 3]) ai++;
                    if (ai < 0) ai = 0;
                    if (ai > 255) ai = 255;
                    int o = row + x * 4;
                    px[o] = 255;
                    px[o + 1] = 255;
                    px[o + 2] = 255;
                    px[o + 3] = (byte)ai;
                }
            }
            System.Runtime.InteropServices.Marshal.Copy(px, 0, bd.Scan0, px.Length);
            b.UnlockBits(bd);
            _gloss = b;
            _glossKey = key;
            return b;
        }

        private void Render()
        {
            if (!IsHandleCreated || Width <= 0 || Height <= 0) return;
            try
            {
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
                    g.CompositingQuality = CompositingQuality.HighQuality;

                    float aPanel = Ease(_t);
                    float aBar = Ease(_tb);

                    // 面板底（无背景模式不画）：只包住图标那一排，白条独立待在它外面
                    if (aPanel > 0.01f && _alphaPct < 100)
                    {
                        Rectangle r = _bgRect;
                        if (r.Width < 4 || r.Height < 4) r = new Rectangle(0, 0, Width - 1, Height - 1);
                        using (GraphicsPath p = Gfx.Round(r, 13))
                        {
                            {
                                // 背景色 + 透明度（只作用在底卡上；半透明时带一点顶部高光，像玻璃）
                                int alpha = (int)Math.Round(aPanel * (100 - _alphaPct) * 255.0 / 100.0);
                                if (alpha < 0) alpha = 0;
                                if (alpha > 255) alpha = 255;
                                Color pc = PanelFill();
                                using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, pc.R, pc.G, pc.B)))
                                    g.FillPath(b, p);

                                // 玻璃高光只属于“半透明”状态：0% 纯色时完全没有渐变，
                                // 越透明高光越强，到 100%（不显示背景）自然收掉
                                int hi = 0;
                                if (_alphaPct > 0)
                                {
                                    int k = Math.Min(_alphaPct, 45) * 100 / 45;   // 45% 及以上满强度
                                    hi = (int)Math.Round(aPanel * (100 - _alphaPct) * 62.0 / 100.0 * k / 100.0);
                                    if (_bgStyle == 0) hi = (int)Math.Round(hi * 0.45);   // 深色底上白高光要更克制
                                }
                                if (hi > 0)
                                {
                                    Bitmap gl = Gloss(Math.Max(1, r.Width), Math.Max(1, r.Height), hi, IsVertical(_edge));
                                    if (gl != null)
                                    {
                                        g.SetClip(p);
                                        g.DrawImageUnscaled(gl, r.X, r.Y);
                                        g.ResetClip();
                                    }
                                }

                                // 描边：纯色状态不要白边，深色底更淡，避免出现“灰白轮廓”
                                float bf = _bgStyle == 0 ? 0.10f : 0.26f;
                                int ba = (int)(alpha * bf * (0.35f + 0.65f * Math.Min(_alphaPct, 100) / 100f));
                                if (ba > 0)
                                {
                                    using (Pen pen = new Pen(Color.FromArgb(ba, 255, 255, 255)))
                                        g.DrawPath(pen, p);
                                }
                            }
                        }
                    }

                    // 白条（胶囊）
                    if (aBar > 0.01f && _barRect.Width > 0 && _barRect.Height > 0)
                    {
                        Rectangle box = _barRect;
                        int rad = Math.Min(box.Width, box.Height) / 2;
                        if (rad < 1) rad = 1;
                        using (GraphicsPath p = new GraphicsPath())
                        {
                            p.AddArc(box.X, box.Y, rad * 2, rad * 2, 180, 90);
                            p.AddArc(box.Right - rad * 2, box.Y, rad * 2, rad * 2, 270, 90);
                            p.AddArc(box.Right - rad * 2, box.Bottom - rad * 2, rad * 2, rad * 2, 0, 90);
                            p.AddArc(box.X, box.Bottom - rad * 2, rad * 2, rad * 2, 90, 90);
                            p.CloseFigure();
                            using (SolidBrush b = new SolidBrush(Color.FromArgb((int)Math.Round(aBar * 255), Pal.Bar)))
                                g.FillPath(b, p);
                        }
                    }

                    // 图标：macOS Dock 式的连续放大。光标换算成“第几颗”的浮点坐标，
                    // 每颗的放大/推开量都是光标位置的距离函数——所以从一颗滑到下一颗是
                    // 连续过渡的，不会像逐颗切换那样一顿一顿。
                    if (aPanel > 0.01f && _items.Count > 0)
                    {
                        bool vert = IsVertical(_edge);
                        int n = _items.Count;
                        float[] ks = new float[n];
                        float[] sh = new float[n];
                        float[] ord = new float[n];
                        const float ZOOM = DockItem.HOVER_ZOOM;

                        int step = _iconSize + _gap;
                        if (step < 1) step = 1;
                        Rectangle r0 = _items[0].R;
                        int baseC = vert ? (r0.Y + r0.Height / 2) : (r0.X + r0.Width / 2);
                        float cc = (_magCoord - baseC) / (float)step;   // 0 = 第一颗中心

                        for (int i = 0; i < n; i++)
                        {
                            float d = i - cc;
                            float ad = Math.Abs(d);
                            float w = (float)Math.Exp(-(ad * ad) / 0.85f);   // 高斯的“圆顶”，连续无跳变
                            ks[i] = 1f + ZOOM * _mag * w;

                            // 推开量：正侧往正方向推、负侧往负方向推，离得越近推得越多
                            float push = (float)Math.Exp(-((ad - 1f) * (ad - 1f)) / 1.0f);
                            sh[i] = PUSH_PX * _mag * push * (d > 0f ? 1f : -1f);
                            ord[i] = ks[i];
                        }

                        // 大的后画（压在邻居之上）
                        for (int pass = 0; pass < 2; pass++)
                        {
                            for (int i = 0; i < n; i++)
                            {
                                // 正在拖的那颗不在这里画：它由 DragGhost 跟着鼠标显示
                                if (_itemDrag && _pressItem != null && _items[i] == _pressItem) continue;
                                bool big = ord[i] > 1.08f;
                                if ((pass == 0) == big) continue;
                                _items[i].Paint(g, _iconSize, aPanel, ks[i], sh[i], vert);
                            }
                        }
                    }
                }
                finally { g.Dispose(); }

                PushLayered();
            }
            catch (Exception ex) { Log.Write("render err: " + ex.GetType().Name + " " + ex.Message); }
        }

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
            bl.Op = 0;      // AC_SRC_OVER
            bl.Flags = 0;
            bl.Alpha = 255; // 整体不透明度交给渲染里的「背景透明度」，这里保持 1.0
            bl.Format = 1;  // AC_SRC_ALPHA：像素自带 alpha
            UpdateLayeredWindow(Handle, scr, ref pos, ref size, mem, ref src, 0, ref bl, 2);

            SelectObject(mem, old);
            DeleteObject(hb);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, scr);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Width <= 0 || Height <= 0) return;
            Render();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Log.Write("shown size=" + Size + " loc=" + Location + " free=" + _floating + " edge=" + _edge);
        }

        // ------------------------------------------------------------ 菜单
        private void BuildMenu()
        {
            // 菜单是自绘的分层窗口（DockForm 是 NoActivate 的，系统菜单点不动）
            _menu = new DockMenu();
            _menu.Dismissed += delegate
            {
                _menuOpen = false;
                _leaveMs = 0;
                _barLeaveMs = 0;
                _wantBar = true;
            };
        }

        private List<DockMenu.Entry> MenuEntries()
        {
            List<DockMenu.Entry> list = new List<DockMenu.Entry>();
            // 只留两条：设置 + 退出。刷新图标 / 打开文件夹 / 开机自启 / 立即收起 都不在菜单里了
            list.Add(new DockMenu.Entry { Text = "桌面", Act = delegate { OpenDesktopFolder(); } });
            list.Add(new DockMenu.Entry { Text = _locked ? "解锁位置" : "锁定位置", Act = delegate
            {
                _locked = !_locked;
                SaveSettings();
                Log.Write("lock=" + _locked);
            } });
            list.Add(new DockMenu.Entry { Text = "设置…", Act = delegate { ShowSettings(); } });
            list.Add(new DockMenu.Entry { Text = "退出", Act = delegate { Close(); } });
            return list;
        }

        // ------------------------------------------------------------ 顶部/底部入口
        // 右键菜单「桌面」：打开系统真正的桌面文件夹（不是 items 目录）
        private void OpenDesktopFolder()
        {
            try
            {
                string dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (string.IsNullOrEmpty(dir)) dir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                if (!string.IsNullOrEmpty(dir)) Process.Start("explorer.exe", "\"" + dir + "\"");
            }
            catch { }
        }

        private void OpenDesktop()
        {
            try { Process.Start("explorer.exe", "\"" + _desktopDir + "\""); }
            catch { }
        }

        // 白条长度上限 = 图标那一排的总长占面板长度的百分比：
        // 白条最宽也就和图标排一样长，图标越多/越大，上限越高，设置里的滑条跟着变。
        private static int MaxBarPct(int count, int icon, int gap)
        {
            int slots = count < 1 ? 1 : count;
            int content = slots * (icon + gap) - gap;
            int slack = (int)Math.Ceiling(icon * (DockItem.HOVER_ZOOM * 1.08f) / 2f) + (int)PUSH_PX + 6;
            int len = content + (PAD + slack) * 2;   // 面板长度，和 PanelSizeFor 同一套算法
            if (len < MIN_LEN) len = MIN_LEN;
            int pct = (int)Math.Round(100.0 * content / len);
            if (pct > 100) pct = 100;
            if (pct < 10) pct = 10;
            return pct;
        }

        private void ShowSettings()
        {
            bool oldMenu = _menuOpen;
            _menuOpen = true;
            int oldSize = _iconSize, oldBg = _bgStyle, oldBar = _barPct, oldGap = _gap, oldAlpha = _alphaPct;
            string oldDesk = _desktopDir;
            try
            {
                using (SettingsForm f = new SettingsForm(_desktopDir, _itemsDir, _iconSize, _bgStyle, _barPct, _gap, CollectItemFiles(), IsAutoStart(),
                        delegate(int count, int icon, int gap) { return MaxBarPct(count, icon, gap); }, _alphaPct))
                {
                    f.Changed += delegate { QueuePreview(f); };
                    // 设置窗打开期间自己先不置顶，不然顶在最上面会把设置窗压住；关掉后恢复
                    TopMost = false;
                    DialogResult r;
                    try { r = f.ShowDialog(); }
                    finally { TopMost = true; }
                    if (_pvTimer != null) _pvTimer.Stop();
                    _pvOn = false;
                    if (r == DialogResult.OK)
                    {
                        _iconSize = f.IconSize;
                        _bgStyle = f.BgStyle;
                        _barPct = f.BarPct;
                        _alphaPct = f.AlphaPct;
                        _gap = f.Gap;
                        _desktopDir = f.DesktopDir;
                        if (f.AutoStart != IsAutoStart()) SetAutoStart(f.AutoStart);
                        CommitItems(f.ItemPaths);
                        SaveSettings();
                        ApplyLook();
                        Reload();
                    }
                    else
                    {
                        _iconSize = oldSize;
                        _bgStyle = oldBg;
                        _barPct = oldBar;
                        _alphaPct = oldAlpha;
                        _gap = oldGap;
                        _desktopDir = oldDesk;
                        ApplyLook();
                        Reload();
                    }
                    _t = 1f;
                    _wantShow = true;
                    _leaveMs = 0; _barLeaveMs = 0; _wantBar = true;
                    ApplyPosition();
                }
            }
            catch (Exception ex)
            {
                Log.Write("settings err: " + ex);
            }
            finally
            {
                _menuOpen = oldMenu;
                _wantShow = true;
                _leaveMs = 0; _barLeaveMs = 0; _wantBar = true;
                ApplyPosition();
            }
        }

        // ------------------------------------------------------------ 设置窗口的实时预览
        private bool _pvPending;
        private void QueuePreview(SettingsForm f)
        {
            _pvOn = true;
            _pvSize = f.IconSize;
            _pvBg = f.BgStyle;
            _pvBar = f.BarPct;
            _pvAlpha = f.AlphaPct;
            _pvGap = f.Gap;
            _pvPaths = new List<string>(f.ItemPaths);
            _pvPending = true;
            if (_pvTimer == null)
            {
                _pvTimer = new System.Windows.Forms.Timer();
                _pvTimer.Interval = 60;   // 节流而不是防抖：一直拖也一直刷，不等松手
                _pvTimer.Tick += delegate
                {
                    _pvTimer.Stop();
                    if (!_pvPending) return;
                    _pvPending = false;
                    ApplyPreview();
                };
            }
            if (!_pvTimer.Enabled) _pvTimer.Start();
        }

        private void ApplyPreview()
        {
            if (!_pvOn || _pvPaths == null) return;
            bool rebuild = (_pvSize != _iconSize) || (_pvPaths.Count != _items.Count);
            if (!rebuild)
            {
                for (int i = 0; i < _pvPaths.Count; i++)
                {
                    if (!string.Equals(_items[i].TargetPath, _pvPaths[i], StringComparison.OrdinalIgnoreCase))
                    {
                        rebuild = true;
                        break;
                    }
                }
            }
            _iconSize = _pvSize;
            _bgStyle = _pvBg;
            _barPct = _pvBar;
            _alphaPct = _pvAlpha;
            _gap = _pvGap;
            if (rebuild) BuildItemsFrom(_pvPaths);
            ApplyLook();
            ApplyLayout(null);
            ApplyPosition();
        }

        // 把设置窗口里的目标清单落盘：缺的建快捷方式，多的删掉，再写顺序
        private void CommitItems(List<string> sources)
        {
            try { if (!Directory.Exists(_itemsDir)) Directory.CreateDirectory(_itemsDir); }
            catch { }

            List<string> names = new List<string>();
            foreach (string src in sources)
            {
                if (!File.Exists(src) && !Directory.Exists(src)) continue;
                string name = Path.GetFileName(src);
                string noExt = Path.GetFileNameWithoutExtension(name);
                string lnk = Path.Combine(_itemsDir, noExt + ".lnk");
                string plain = Path.Combine(_itemsDir, name);

                bool exists = File.Exists(lnk) || File.Exists(plain) || Directory.Exists(plain);
                if (!exists)
                {
                    try
                    {
                        string ext = Path.GetExtension(name).ToLowerInvariant();
                        if (ext == ".lnk" || ext == ".url") File.Copy(src, plain, true);
                        else Shortcut.Create(src, lnk);
                    }
                    catch { }
                }
                if (File.Exists(lnk)) names.Add(Path.GetFileName(lnk));
                else if (File.Exists(plain)) names.Add(Path.GetFileName(plain));
                else if (Directory.Exists(plain)) names.Add(Path.GetFileName(plain));
            }

            // 删掉已经不在名单里的旧项目
            List<string> keep = new List<string>();
            foreach (string n in names) keep.Add(n);
            try
            {
                foreach (string f in Directory.GetFiles(_itemsDir))
                {
                    string n = Path.GetFileName(f);
                    if (string.Equals(n, "order.txt", StringComparison.OrdinalIgnoreCase)) continue;
                    bool inList = false;
                    foreach (string k in keep)
                    {
                        if (string.Equals(k, n, StringComparison.OrdinalIgnoreCase)) { inList = true; break; }
                    }
                    if (!inList) { try { File.Delete(f); } catch { } }
                }
                foreach (string d in Directory.GetDirectories(_itemsDir))
                {
                    string n = Path.GetFileName(d);
                    bool inList = false;
                    foreach (string k in keep)
                    {
                        if (string.Equals(k, n, StringComparison.OrdinalIgnoreCase)) { inList = true; break; }
                    }
                    if (!inList) { try { Directory.Delete(d, true); } catch { } }
                }
            }
            catch { }

            try { File.WriteAllLines(Path.Combine(_itemsDir, "order.txt"), names.ToArray()); }
            catch { }
        }

        // ------------------------------------------------------------ 外观（背景 / 白条 / 图标大小）
        // 图标尺寸现在是滑条：32（原「小」）～100，中间连续可调
        private static int SnapIconSize(int v)
        {
            if (v < 32) return 32;
            if (v > 100) return 100;
            return v;
        }

        private Color ItemBg()
        {
            return Pal.Panel;
        }

        // 背景颜色：黑 / 白 / 跟随系统
        private Color PanelFill()
        {
            if (_bgStyle == 0) return Color.FromArgb(18, 18, 22);
            if (_bgStyle == 1) return Color.FromArgb(250, 250, 253);
            return Pal.Panel;
        }

        // 在快捷栏往里挪一点的位置抓一小块屏幕，取平均色当背景色
        private void SampleTintColor()
        {
            try
            {
                Rectangle b = Bounds;
                if (b.Width < 10 || b.Height < 10) return;
                Rectangle r;
                if (IsVertical(_edge))
                {
                    int x = (_edge == DockEdge.Left) ? b.Right + 24 : b.Left - 84;
                    r = new Rectangle(x, b.Top + b.Height / 2 - 140, 60, 280);
                }
                else
                {
                    int y = (_edge == DockEdge.Top) ? b.Bottom + 24 : b.Top - 84;
                    r = new Rectangle(b.Left + b.Width / 2 - 140, y, 280, 60);
                }
                Rectangle wa = _workArea;
                r.Intersect(wa);
                if (r.Width < 4 || r.Height < 4) return;

                using (Bitmap t = new Bitmap(r.Width, r.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(t))
                        g.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(r.Width, r.Height));
                    long sr = 0, sg = 0, sb = 0, n = 0;
                    for (int y = 0; y < t.Height; y += 6)
                    {
                        for (int x = 0; x < t.Width; x += 6)
                        {
                            Color c = t.GetPixel(x, y);
                            sr += c.R; sg += c.G; sb += c.B; n++;
                        }
                    }
                    if (n == 0) return;
                    _tintColor = Color.FromArgb((int)(sr / n), (int)(sg / n), (int)(sb / n));
                }
            }
            catch { }
        }

        private void ApplyLook()
        {
            BackColor = PanelFill();
            // 分层窗口不需要透明键：画面自己带 alpha，背景模式在 Render 里画
            try { TransparencyKey = Color.Empty; }
            catch { }
            try { Opacity = 1.0; }
            catch { }
            Render();
        }

        // ------------------------------------------------------------ 设置
        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(_iniPath)) return;
                foreach (string line in File.ReadAllLines(_iniPath))
                {
                    string s = line.Trim();
                    if (s.Length == 0 || s.StartsWith("#")) continue;
                    int eq = s.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = s.Substring(0, eq).Trim().ToLowerInvariant();
                    string v = s.Substring(eq + 1).Trim();
                    if (k == "edge")
                    {
                        if (string.Equals(v, "free", StringComparison.OrdinalIgnoreCase)) _floating = true;
                        else
                        {
                            DockEdge tmp;
                            if (Enum.TryParse<DockEdge>(v, true, out tmp) && tmp != DockEdge.None)
                            {
                                _edge = tmp;
                                _floating = false;
                            }
                        }
                    }
                    else if (k == "ratio")
                    {
                        double d;
                        if (double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d))
                            _ratio = Math.Min(1.0, Math.Max(0.0, d));
                    }
                    else if (k == "x")
                    {
                        int n;
                        if (int.TryParse(v, out n)) _floatX = n;
                    }
                    else if (k == "y")
                    {
                        int n;
                        if (int.TryParse(v, out n)) _floatY = n;
                    }
                    else if (k == "icon")
                    {
                        int n;
                        if (int.TryParse(v, out n) && n >= 20 && n <= 100) _iconSize = SnapIconSize(n);
                    }
                    else if (k == "bgstyle")
                    {
                        int n;
                        if (int.TryParse(v, out n)) { if (n > 2) n = 2; if (n < 0) n = 0; _bgStyle = n; }
                    }
                    else if (k == "bg")
                    {
                        // 旧配置：0=深色面板 1=半透明 2=无背景 → 折成「黑 / 白 / 跟随系统 + 透明度」
                        int n;
                        if (int.TryParse(v, out n))
                        {
                            if (n == 2) { _bgStyle = 1; _alphaPct = 100; }
                            else if (n == 1) _bgStyle = 1;
                            else _bgStyle = 2;
                        }
                    }
                    else if (k == "gap")
                    {
                        int n;
                        if (int.TryParse(v, out n) && n >= 0 && n <= 40) _gap = n;
                    }
                    else if (k == "desktop")
                    {
                        if (v.Length > 2 && Directory.Exists(v)) _desktopDir = v;
                    }
                    else if (k == "bar")
                    {                        int n;
                        if (int.TryParse(v, out n))
                        {
                            if (n > 100) n = 100;      // 旧配置里的像素值统一收到 100%
                            if (n == 0) n = 100;       // 旧配置里 0 = 全长
                            if (n < 5) n = 5;
                            _barPct = n;
                        }
                    }
                    else if (k == "lock")
                    {
                        _locked = v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (k == "alpha")
                    {
                        int n;
                        if (int.TryParse(v, out n))
                        {
                            if (n < 0) n = 0;
                            if (n > 100) n = 100;
                            _alphaPct = n;
                        }
                    }
                }
            }
            catch { }
            if (_edge == DockEdge.None) _edge = DockEdge.Right;
        }

        private void SaveSettings()
        {
            try
            {
                File.WriteAllText(_iniPath,
                    "# MyDock 配置：edge=Left/Top/Right/Bottom，或 free(自由悬浮)  ratio=沿边位置0~1  x/y=悬浮坐标\r\n" +
                    "edge=" + (_floating ? "Free" : _edge.ToString()) + "\r\n" +
                    "ratio=" + _ratio.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "\r\n" +
                    "x=" + _floatX + "\r\n" +
                    "y=" + _floatY + "\r\n" +
                    "icon=" + _iconSize + "\r\n" +
                    "gap=" + _gap + "\r\n" +
                    "bgstyle=" + _bgStyle + "\r\n" +
                    "bar=" + _barPct + "\r\n" +
                    "alpha=" + _alphaPct + "\r\n" +
                    "lock=" + (_locked ? 1 : 0) + "\r\n" +
                    "desktop=" + _desktopDir + "\r\n");
            }
            catch { }
        }

        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunName = "MyDock";

        private bool IsAutoStart()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (k == null) return false;
                    object v = k.GetValue(RunName);
                    return v != null && v.ToString().Length > 0;
                }
            }
            catch { return false; }
        }

        private void SetAutoStart(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (on) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue(RunName, false);
                }
            }
            catch { }
        }

        // ------------------------------------------------------------ 加载图标
        private void Reload()
        {
            try { if (!Directory.Exists(_itemsDir)) Directory.CreateDirectory(_itemsDir); }
            catch { }
            BuildItemsFrom(CollectItemFiles());
            ApplyLayout(null);
            UpdateWorkArea();
            ApplyPosition();
            _itemsSig = ItemsSig();
            Log.Write("reload n=" + _items.Count);
        }

        // 按 items 目录 + order.txt 得出“当前应该在栏里”的文件列表
        private List<string> CollectItemFiles()
        {
            List<string> files = new List<string>();
            try
            {
                if (!Directory.Exists(_itemsDir)) Directory.CreateDirectory(_itemsDir);
                files.AddRange(Directory.GetFiles(_itemsDir));
                files.AddRange(Directory.GetDirectories(_itemsDir));
            }
            catch { }

            List<string> order = ReadOrder();
            List<string> picked = new List<string>();
            foreach (string name in order)
            {
                foreach (string f in files)
                {
                    if (string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase) && !picked.Contains(f))
                    {
                        picked.Add(f);
                        break;
                    }
                }
            }
            files.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string f in files)
            {
                string fn = Path.GetFileName(f);
                if (string.Equals(fn, "order.txt", StringComparison.OrdinalIgnoreCase)) continue;
                if (!picked.Contains(f)) picked.Add(f);
            }
            return picked;
        }

        // 用给定文件列表重建面板上的图标（正式加载与设置窗口的实时预览共用）
        private void BuildItemsFrom(List<string> files)
        {
            foreach (DockItem it in _items) it.Dispose();
            _items.Clear();

            foreach (string f in files)
            {
                if (!File.Exists(f) && !Directory.Exists(f)) continue;
                Image img = IconLoader.Load(f, _iconSize);
                if (img == null) Log.Write("icon null: " + Path.GetFileName(f));
                DockItem item = new DockItem(f, img, _iconSize);
                _items.Add(item);
            }
        }

        private string ItemsSig()
        {
            try
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                string[] fs = Directory.GetFiles(_itemsDir);
                Array.Sort(fs, StringComparer.OrdinalIgnoreCase);
                foreach (string f in fs)
                {
                    if (string.Equals(Path.GetFileName(f), "order.txt", StringComparison.OrdinalIgnoreCase)) continue;
                    sb.Append(Path.GetFileName(f)).Append('|');
                    sb.Append(new FileInfo(f).LastWriteTimeUtc.Ticks);
                    sb.Append(';');
                }
                string[] ds = Directory.GetDirectories(_itemsDir);
                Array.Sort(ds, StringComparer.OrdinalIgnoreCase);
                foreach (string d in ds) sb.Append(Path.GetFileName(d)).Append("\\;");
                return sb.ToString();
            }
            catch { return null; }
        }

        private List<string> ReadOrder()
        {
            List<string> order = new List<string>();
            string orderPath = Path.Combine(_itemsDir, "order.txt");
            try
            {
                if (File.Exists(orderPath))
                {
                    foreach (string line in File.ReadAllLines(orderPath))
                    {
                        string s = line.Trim();
                        if (s.Length > 0 && !s.StartsWith("#")) order.Add(s);
                    }
                }
            }
            catch { }
            return order;
        }

        private void SaveOrder()
        {
            try
            {
                List<string> lines = new List<string>();
                foreach (DockItem it in _items) lines.Add(Path.GetFileName(it.TargetPath));
                File.WriteAllLines(Path.Combine(_itemsDir, "order.txt"), lines.ToArray());
            }
            catch { }
        }

        // ------------------------------------------------------------ 布局
        private bool IsVertical(DockEdge e)
        {
            return e == DockEdge.Left || e == DockEdge.Right;
        }

        private Size PanelSizeFor(DockEdge e)
        {
            // 厚度方向从“贴边”往外排：边缘留 GAP_OUT → 白条 → GAP_OUT → 图标 → 放大余量
            int zoomRoom = (int)Math.Ceiling(_iconSize * (DockItem.HOVER_ZOOM * 1.08f) / 2f) + (int)PUSH_PX + 2;
            int thick = GAP_OUT + HANDLE_T + GAP_OUT + CARD_PAD + _iconSize + zoomRoom;
            int slots = _items.Count < 1 ? 1 : _items.Count;
            int content = slots * (_iconSize + _gap) - _gap;
            // 长度方向留出“悬停放大 + 回弹 + 邻居推开”的余量，否则最外侧那颗放大时会被窗口裁掉
            int slack = (int)Math.Ceiling(_iconSize * (DockItem.HOVER_ZOOM * 1.08f) / 2f) + (int)PUSH_PX + 6;
            int len = content + (PAD + slack) * 2;
            if (len < MIN_LEN) len = MIN_LEN;
            return IsVertical(e) ? new Size(thick, len) : new Size(len, thick);
        }
        // 滑入距离：以“白条所在的位置”为界，收起时图标排正压在白条上，
        // 展开时从白条那条线滑出来（再叠加 alpha 淡入淡出），不再从屏幕边框外整块推进来
        private int SlideOffset(Size s)
        {
            if (_floating) return 0;
            float vis = Math.Max(Ease(_t), Ease(_tb));
            if (vis > 1f) vis = 1f;
            int dist = GAP_OUT + HANDLE_T;   // 图标排到白条起点的距离
            return (int)Math.Round((1f - vis) * dist);
        }

        private void OffsetByEdge(ref Rectangle r, int d)
        {
            if (d <= 0) return;
            if (_edge == DockEdge.Left) r.X -= d;
            else if (_edge == DockEdge.Right) r.X += d;
            else if (_edge == DockEdge.Top) r.Y -= d;
            else r.Y += d;
        }

        // 面板尺寸固定，动画期间只改透明度 + 沿法线方向的滑动偏移
        private void ApplyLayout(DockItem skip)
        {
            Size s = PanelSizeFor(_edge);
            Rectangle eb = ExpandedBounds();
            int slide = SlideOffset(s);
            OffsetByEdge(ref eb, slide);
            if (!_dragging && Bounds != eb) Bounds = eb;
            LayoutRects(s.Width, s.Height, skip);
            Render();
        }

        // 每帧更新位置（滑动）+ 重画透明图
        private void ApplyFrame()
        {
            if (!_dragging && !_itemDrag)
            {
                Size s = PanelSizeFor(_edge);
                Rectangle eb = ExpandedBounds();
                int slide = SlideOffset(s);
                OffsetByEdge(ref eb, slide);
                if (Bounds != eb) Bounds = eb;
            }
            Render();
        }

        private void LayoutRects(int w, int h, DockItem skip)
        {
            bool vert = IsVertical(_edge);

            int total = vert ? h : w;
            int slots = _items.Count;
            int content = slots * (_iconSize + _gap) - _gap;
            if (content < 24) content = 24;

            // 白条长度 = 面板长度的百分比；上限永远压在“最外那颗图标的边缘”上，不会伸出去
            int barLen = (int)Math.Round(total * _barPct / 100.0);
            int maxLen = Math.Max(24, total - 20);
            if (maxLen > content) maxLen = content;
            if (barLen > maxLen) barLen = maxLen;
            if (barLen < 24) barLen = 24;
            int barStart = (total - barLen) / 2;

            if (vert)
            {
                // 白条离屏幕边 GAP_OUT，和它到图标的间距一样
                int hx = (_edge == DockEdge.Right) ? w - GAP_OUT - HANDLE_T : GAP_OUT;
                if (hx < 0) hx = 0;
                _barRect = new Rectangle(hx, barStart, HANDLE_T, barLen);
            }
            else
            {
                int hy = (_edge == DockEdge.Top) ? GAP_OUT : h - GAP_OUT - HANDLE_T;
                if (hy < 0) hy = 0;
                _barRect = new Rectangle(barStart, hy, barLen, HANDLE_T);
            }

            int start = (total - content) / 2;
            // 图标排离边的距离：边 → GAP_OUT → 白条 → GAP_OUT → 底卡边 → CARD_PAD → 图标
            int iconOff = GAP_OUT + HANDLE_T + GAP_OUT + CARD_PAD;

            for (int i = 0; i < slots; i++)
            {
                DockItem it = _items[i];
                if (it == skip) continue;

                Rectangle r;
                if (vert)
                {
                    int x = (_edge == DockEdge.Right)
                        ? w - iconOff - _iconSize
                        : iconOff;
                    r = new Rectangle(x, start + i * (_iconSize + _gap), _iconSize, _iconSize);
                }
                else
                {
                    int y = (_edge == DockEdge.Top)
                        ? iconOff
                        : h - iconOff - _iconSize;
                    r = new Rectangle(start + i * (_iconSize + _gap), y, _iconSize, _iconSize);
                }
                it.R = r;
            }

            // 背景底卡：只包住图标那一排（两端各留 CARD_PAD），白条独立待在它外面，
            // 白条到底卡的缝隙 = 白条到屏幕边的缝隙（都是 GAP_OUT）
            int bp = CARD_PAD;
            if (vert)
            {
                int x0 = (_edge == DockEdge.Right) ? w - iconOff - _iconSize : iconOff;
                _bgRect = new Rectangle(x0 - bp, start - bp, _iconSize + bp * 2, content + bp * 2);
            }
            else
            {
                int y0 = (_edge == DockEdge.Top) ? iconOff : h - iconOff - _iconSize;
                _bgRect = new Rectangle(start - bp, y0 - bp, content + bp * 2, _iconSize + bp * 2);
            }
            if (_bgRect.X < 0) _bgRect.X = 0;
            if (_bgRect.Y < 0) _bgRect.Y = 0;
            if (_bgRect.Right > w - 1) _bgRect.Width = w - 1 - _bgRect.X;
            if (_bgRect.Bottom > h - 1) _bgRect.Height = h - 1 - _bgRect.Y;
        }


        private Rectangle ExpandedBounds()
        {
            Size s = PanelSizeFor(_edge);
            if (_floating)
            {
                Rectangle fa = _workArea;
                int fx = Math.Max(fa.Left, Math.Min(fa.Right - s.Width, _floatX));
                int fy = Math.Max(fa.Top, Math.Min(fa.Bottom - s.Height, _floatY));
                return new Rectangle(fx, fy, s.Width, s.Height);
            }
            Rectangle wa = _workArea;
            int x, y;
            if (_edge == DockEdge.Left)
            {
                x = wa.Left;
                y = wa.Top + (int)Math.Round((wa.Height - s.Height) * _ratio);
            }
            else if (_edge == DockEdge.Right)
            {
                x = wa.Right - s.Width;
                y = wa.Top + (int)Math.Round((wa.Height - s.Height) * _ratio);
            }
            else if (_edge == DockEdge.Top)
            {
                x = wa.Left + (int)Math.Round((wa.Width - s.Width) * _ratio);
                y = wa.Top;
            }
            else
            {
                x = wa.Left + (int)Math.Round((wa.Width - s.Width) * _ratio);
                y = wa.Bottom - s.Height;
            }
            return new Rectangle(x, y, s.Width, s.Height);
        }

        private static float Ease(float t)
        {
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            return t * t * (3f - 2f * t);
        }

        // 放大用“轻微回弹”的缓动（末段略微超过 1 再落回），手感更接近 macOS Dock：
        // 起始有力、收尾带回弹，而不是匀速涨上去。峰值约 1.05，靠 10px 边距兜住。
        private static float EaseBack(float t)
        {
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            const float s = 1.15f;
            float u = t - 1f;
            return 1f + (s + 1f) * u * u * u + s * u * u;
        }

        private void ApplyPosition()
        {
            ApplyLayout(null);
        }

        // ------------------------------------------------------------ 全屏锁定
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

        private bool IsFullscreenAppActive()
        {
            try
            {
                IntPtr fg = GetForegroundWindow();
                if (fg == IntPtr.Zero || fg == Handle) return false;

                int pid = 0;
                GetWindowThreadProcessId(fg, out pid);
                if (pid == Process.GetCurrentProcess().Id) return false;

                System.Text.StringBuilder sb = new System.Text.StringBuilder(160);
                GetClassName(fg, sb, 160);
                string cls = sb.ToString();
                if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" ||
                    cls == "Shell_SecondaryTrayWnd" || cls == "TaskManagerWindow") return false;

                RECT r = new RECT();
                if (!GetWindowRect(fg, out r)) return false;

                Rectangle wr = new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
                if (wr.Width < 300 || wr.Height < 300) return false;

                Screen sc = Screen.FromPoint(new Point(wr.X + wr.Width / 2, wr.Y + wr.Height / 2));
                Rectangle b = sc.Bounds;
                return Math.Abs(wr.Left - b.Left) <= 2 && Math.Abs(wr.Top - b.Top) <= 2 &&
                       Math.Abs(wr.Right - b.Right) <= 2 && Math.Abs(wr.Bottom - b.Bottom) <= 2;
            }
            catch { return false; }
        }

        // ------------------------------------------------------------ 自动隐藏逻辑
        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                int nowMs0 = Environment.TickCount;
                int el = (_prevTickMs == 0) ? FRAME_MS : (nowMs0 - _prevTickMs);
                _prevTickMs = nowMs0;
                if (el < 1) el = 1;
                if (el > 60) el = 60;

                // 全屏检测：全屏程序在前台时锁定，不呼出、不显示
                if (Environment.TickCount - _lastFsCheck > 250)
                {
                    _lastFsCheck = Environment.TickCount;
                    bool fs = IsFullscreenAppActive();
                    if (fs != _fsLock)
                    {
                        _fsLock = fs;
                        if (fs)
                        {
                            _wantShow = false;
                            _wantBar = false;
                            _t = 0f;
                            _tb = 0f;
                            ApplyLayout(null);
                            Visible = false;
                        }
                        else
                        {
                            Visible = true;
                            _wantShow = false;
                            _wantBar = false;
                            _leaveMs = 0;
                            _barLeaveMs = 0;
                            _t = 0f;
                            _tb = 0f;
                            ApplyLayout(null);
                            AssertTopMost();
                        }
                        Log.Write("fs lock=" + fs);
                    }
                }
                if (_fsLock) return;

                // 目录指纹变化（设置里增删、外部往 items 里丢文件）就自动重载
                if (!_dragging && !_itemDrag && !_menuOpen && !_dragOver &&
                    Environment.TickCount - _lastSigCheck > 1000)
                {
                    _lastSigCheck = Environment.TickCount;
                    string sig = ItemsSig();
                    if (sig != null && !string.Equals(sig, _itemsSig, StringComparison.Ordinal))
                    {
                        _itemsSig = sig;
                        Reload();
                    }
                }

                if (_floating)
                {
                    if (_t != 1f) { _t = 1f; }
                    if (_tb != 1f) { _tb = 1f; }
                    return;
                }

                Point p = Cursor.Position;

                // 悬停高亮 + 右键穿透样式到点恢复（分层窗口没有子控件，全靠这里算）
                UpdateHover(PointToClient(p));

                if (_passR && (Environment.TickCount - _passUntil) >= 0)
                {
                    _passR = false;
                    ApplyPassStyle(false);
                }

                if (_dragging || _menuOpen || _dragOver || _itemDrag)
                {
                    _wantShow = true;
                    _leaveMs = 0; _barLeaveMs = 0; _wantBar = true;
                }
                else if (DateTime.Now < _graceUntil)
                {
                    _wantShow = true;
                    _leaveMs = 0; _barLeaveMs = 0; _wantBar = true;
                }
                else
                {
                    // 白条和图标各认一块热区：碰边只出白条，再往里走才出图标
                    bool onBar = EdgeHot().Contains(p);

                    Rectangle panelHit;
                    if (_t > 0.05f)
                    {
                        panelHit = ExpandedBounds();
                        panelHit.Inflate(6, 6);
                    }
                    else panelHit = PanelHot();
                    bool onPanel = panelHit.Contains(p);

                    if (onPanel)
                    {
                        _wantShow = true;
                        _leaveMs = 0;
                    }
                    else if (_wantShow)
                    {
                        _leaveMs += el;
                        if (_leaveMs > 700) _wantShow = false;
                    }

                    // 白条只听自己那块热区，图标展开的时候它默认隐身（要右键进设置时再把鼠标移到白条位置）
                    if (onBar)
                    {
                        _wantBar = true;
                        _barLeaveMs = 0;
                    }
                    else if (_wantBar)
                    {
                        _barLeaveMs += el;
                        int lim = _wantShow ? 380 : 550;   // 图标已经在的时候，白条收得更干脆
                        if (_barLeaveMs > lim) _wantBar = false;
                    }
                }

                float target = _wantShow ? 1f : 0f;
                float barTarget = _wantBar ? 1f : 0f;
                if (_wantShow && !_wasWant) AssertTopMost();
                _wasWant = _wantShow;

                bool needApply = false;
                if (AnimateHover()) needApply = true;

                // 时间驱动的缓动：不受 timer 抖动影响，展开/收起都平顺
                if (target != _targetT)
                {
                    _targetT = target;
                    _animFrom = _t;
                    _animStartMs = Environment.TickCount;
                }
                if (_t != _targetT)
                {
                    int dur = _targetT > 0.5f ? 180 : 210;
                    float dt = (Environment.TickCount - _animStartMs) / (float)dur;
                    if (dt < 0f) dt = 0f;
                    if (dt > 1f) dt = 1f;
                    _t = _animFrom + (_targetT - _animFrom) * dt;
                    if (dt >= 1f) _t = _targetT;
                    needApply = true;
                }

                if (barTarget != _barTarget)
                {
                    _barTarget = barTarget;
                    _barAnimFrom = _tb;
                    _barAnimStartMs = Environment.TickCount;
                }
                if (_tb != _barTarget)
                {
                    int dur2 = _barTarget > 0.5f ? 150 : 190;
                    float dt2 = (Environment.TickCount - _barAnimStartMs) / (float)dur2;
                    if (dt2 < 0f) dt2 = 0f;
                    if (dt2 > 1f) dt2 = 1f;
                    _tb = _barAnimFrom + (_barTarget - _barAnimFrom) * dt2;
                    if (dt2 >= 1f) _tb = _barTarget;
                    needApply = true;
                }

                if (needApply)
                {
                    // 统计一下实测帧间隔，方便判断动画到底跑到多少 fps
                    int now2 = Environment.TickCount;
                    if (_statLast != 0) { _statN++; _statSum += (now2 - _statLast); }
                    _statLast = now2;
                    ApplyFrame();      // 位置（滑入/滑出）+ 重画透明图
                }
                else if (_statLast != 0)
                {
                    if (_statN > 3)
                        Log.Write(string.Format("anim frames={0} avg={1:F1}ms work={2:F2}ms",
                            _statN, _statSum / (float)_statN,
                            _statWorkN > 0 ? _statWorkSum / _statWorkN : 0));
                    _statLast = 0; _statN = 0; _statSum = 0; _statWorkSum = 0; _statWorkN = 0;
                }
            }
            catch { }
        }

        // 悬停：只记录“鼠标在沿栏方向走到哪个坐标”，放大程度完全由这个坐标连续推出
        // 注意入参必须是“窗口客户区坐标”，R 是客户区坐标
        private void UpdateHover(Point client)
        {
            int hit = -1;
            if (_t > 0.05f && _items.Count > 0 && !_dragging && !_itemDrag)
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    if (_items[i].R.Contains(client)) { hit = i; break; }
                }
            }

            if (hit >= 0) _magCoord = IsVertical(_edge) ? client.Y : client.X;

            float tgt = hit >= 0 ? 1f : 0f;
            if (_magTarget != tgt)
            {
                _magTarget = tgt;
                _magFrom = _mag;
                _magStart = Environment.TickCount;
            }

            for (int i = 0; i < _items.Count; i++)
                _items[i].Hover = (i == hit);

            // 悬停时在图标旁边报出名字，文件夹也能看出是哪个
            if (hit != _tipIdx)
            {
                _tipIdx = hit;
                try
                {
                    if (hit >= 0)
                    {
                        string nm = Path.GetFileNameWithoutExtension(_items[hit].TargetPath);
                        ShowNameChip(nm, _items[hit].R);
                    }
                    else HideNameChip();
                }
                catch { }
            }
        }

        private NameChip _chip;

        private void ShowNameChip(string text, Rectangle iconClient)
        {
            if (_chip == null) _chip = new NameChip();
            _chip.SetText(text);
            Point sp = PointToScreen(new Point(iconClient.X, iconClient.Y));
            Rectangle work = Screen.FromPoint(sp).WorkingArea;
            int x = sp.X + iconClient.Width / 2 - _chip.Width / 2;
            int y = (_edge == DockEdge.Top) ? sp.Y + iconClient.Height + 6 : sp.Y - _chip.Height - 6;
            if (x < work.Left + 2) x = work.Left + 2;
            if (x + _chip.Width > work.Right) x = work.Right - _chip.Width;
            if (y < work.Top + 2) y = work.Top + 2;
            if (y + _chip.Height > work.Bottom) y = work.Bottom - _chip.Height;
            _chip.SetBounds(x, y, _chip.Width, _chip.Height);
            _chip.ShowNoActivate();
        }

        private void HideNameChip()
        {
            try { if (_chip != null && _chip.Visible) _chip.Hide(); }
            catch { }
        }

        // 只推进“放大总强度”这一个 0~1 的标量：进出时平滑升降，
        // 具体每颗放大多少由 Render 按光标坐标连续计算，切换图标不会再跳。
        private bool AnimateHover()
        {
            if (_mag == _magTarget) return false;
            int dur = _magTarget > 0.5f ? 150 : 210;
            float dt = (Environment.TickCount - _magStart) / (float)dur;
            if (dt < 0f) dt = 0f;
            if (dt > 1f) dt = 1f;
            float e = _magTarget > 0.5f ? EaseBack(dt) : Ease(dt);
            _mag = _magFrom + (_magTarget - _magFrom) * e;
            if (dt >= 1f) _mag = _magTarget;
            return true;
        }

        // 贴边那条窄热区：整条边都算，方便随便碰到就出白条
        private Rectangle EdgeHot()        {
            Rectangle eb = ExpandedBounds();
            if (_floating) return eb;
            int band = GAP_OUT + HANDLE_T + 8;   // 白条离边 GAP_OUT，热区要把这段也算进去
            if (_edge == DockEdge.Left)
                return new Rectangle(eb.X - 2, eb.Y - 24, band + 2, eb.Height + 48);
            if (_edge == DockEdge.Right)
                return new Rectangle(eb.Right - band, eb.Y - 24, band + 2, eb.Height + 48);
            if (_edge == DockEdge.Top)
                return new Rectangle(eb.X - 24, eb.Y - 2, eb.Width + 48, band + 2);
            return new Rectangle(eb.X - 24, eb.Bottom - band, eb.Width + 48, band + 2);
        }

        // 图标热区：让开白条 + 空隙，只有瞄到图标那一段才展开图标
        private Rectangle PanelHot()
        {
            Rectangle eb = ExpandedBounds();
            if (_floating) return eb;
            int inset = GAP_OUT + HANDLE_T + GAP_OUT;
            Rectangle r;
            if (_edge == DockEdge.Left) r = new Rectangle(eb.X + inset, eb.Y, eb.Width - inset, eb.Height);
            else if (_edge == DockEdge.Right) r = new Rectangle(eb.X, eb.Y, eb.Width - inset, eb.Height);
            else if (_edge == DockEdge.Top) r = new Rectangle(eb.X, eb.Y + inset, eb.Width, eb.Height - inset);
            else r = new Rectangle(eb.X, eb.Y, eb.Width, eb.Height - inset);
            r.Inflate(3, 3);
            return r;
        }

        private void UpdateWorkArea()
        {
            try
            {
                Rectangle vs = SystemInformation.VirtualScreen;
                int cx = Left + Width / 2;
                int cy = Top + Height / 2;
                cx = Math.Max(vs.Left + 1, Math.Min(vs.Right - 1, cx));
                cy = Math.Max(vs.Top + 1, Math.Min(vs.Bottom - 1, cy));
                Screen sc = Screen.FromPoint(new Point(cx, cy));
                _workArea = sc.WorkingArea;
            }
            catch
            {
                _workArea = Screen.PrimaryScreen.WorkingArea;
            }
        }

        private void OnDisplayChanged(object sender, EventArgs e)
        {
            UpdateWorkArea();
            ApplyLayout(null);
            ApplyPosition();
        }

        // ------------------------------------------------------------ 鼠标（分层窗口：全部自己判位置）
        private bool _rOnBar;

        private DockItem ItemAt(Point p)
        {
            if (_t < 0.05f) return null;
            foreach (DockItem it in _items) if (it.R.Contains(p)) return it;
            return null;
        }

        private void ShowMenu()
        {
            if (_menu == null) return;
            try
            {
                _menuOpen = true;
                // 就在鼠标点击的位置弹出（ShowAt 里的锚点含 12px 阴影留白，先抵消掉）
                Point cur = Cursor.Position;
                Point anchor = new Point(cur.X - 12, cur.Y - 10);
                _menu.ShowAt(anchor, MenuEntries());
            }
            catch (Exception ex) { Log.Write("menu err: " + ex.Message); }
        }

        private void ApplyPassStyle(bool on)
        {
            try
            {
                int ex = GetWindowLong(Handle, GWL_EXSTYLE);
                if (on) ex |= WS_EX_TRANSPARENT;
                else ex &= ~WS_EX_TRANSPARENT;
                SetWindowLong(Handle, GWL_EXSTYLE, ex);
            }
            catch { }
        }

        // 右键落在白条以外：先让自己对鼠标透明，再把一次真正的右键送给下面的桌面
        private void PassRightToDesktop()
        {
            _passR = true;
            _passUntil = Environment.TickCount + 260;
            ApplyPassStyle(true);
            if (_passTimer != null) { _passTimer.Stop(); _passTimer.Start(); }
            try
            {
                mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, IntPtr.Zero);
                mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, IntPtr.Zero);
            }
            catch { }
        }

        private void OnBarDown(object sender, MouseEventArgs e)
        {
            if (_fsLock) return;

            if (e.Button == MouseButtons.Right)
            {
                // 右键：白条和底卡上任何位置都弹菜单，只有真正透明的区域才交给桌面
                _rOnBar = _barRect.Contains(e.Location) || _bgRect.Contains(e.Location);
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            _wantShow = true;
            _leaveMs = 0; _barLeaveMs = 0; _wantBar = true;

            DockItem hit = ItemAt(e.Location);
            if (hit != null)
            {
                _pressItem = hit;
                _itemDrag = false;
                _pressPoint = Cursor.Position;
                _grab = new Point(e.X - hit.R.X, e.Y - hit.R.Y);
                Capture = true;
                return;
            }

            if (_barRect.Contains(e.Location))
            {
                // 双击白条 = 打开桌面图标文件夹（锁定位置时也照样能双击）
                int now = Environment.TickCount;
                if (_barClickTick != 0 && now - _barClickTick < 420 && now >= _barClickTick)
                {
                    _barClickTick = 0;
                    _t = 1f; _tb = 1f;
                    Render();
                    OpenDesktop();
                    Log.Write("bar dblclick open desktop");
                    return;
                }
                _barClickTick = now;

                if (_locked) return;   // 锁定后白条不再响应拖动，位置固定
                _dragging = true;
                _t = 1f; _tb = 1f;
                Render();
                _dragOffset = new Point(Cursor.Position.X - Left, Cursor.Position.Y - Top);
                Capture = true;
                Log.Write("drag start edge=" + _edge + " free=" + _floating + " loc=" + Location);
            }
        }

        private void OnBarMove(object sender, MouseEventArgs e)
        {
            if (_dragging)
            {
                Point p = Cursor.Position;
                Location = new Point(p.X - _dragOffset.X, p.Y - _dragOffset.Y);
                PushLayered();
                return;
            }
            if (_pressItem != null) ItemDragMove();
        }

        private void OnBarUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                if (_rOnBar) ShowMenu();
                else PassRightToDesktop();
                _rOnBar = false;
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            if (_dragging)
            {
                _dragging = false;
                Capture = false;
                FinishDrag();
                return;
            }

            if (_pressItem != null)
            {
                DockItem it = _pressItem;
                bool wasDrag = _itemDrag;
                _itemDrag = false;
                _pressItem = null;
                Capture = false;
                if (wasDrag)
                {
                    _itemDrag = false;
                    if (_ghost != null) _ghost.HideGhost();
                    ApplyLayout(null);
                    SaveOrder();
                    Log.Write("reorder saved");
                }
                else if (it.R.Contains(e.Location))
                {
                    it.Launch();
                }
                Render();
            }
        }

        private void FinishDrag()
        {
            UpdateWorkArea();
            Rectangle wa = _workArea;
            Rectangle b = Bounds;

            if (b.Right > wa.Right) b.X = wa.Right - b.Width;
            if (b.Bottom > wa.Bottom) b.Y = wa.Bottom - b.Height;
            if (b.Left < wa.Left) b.X = wa.Left;
            if (b.Top < wa.Top) b.Y = wa.Top;
            Location = b.Location;
            b = Bounds;

            int dLeft = b.Left - wa.Left;
            int dRight = wa.Right - b.Right;
            int dTop = b.Top - wa.Top;
            int dBottom = wa.Bottom - b.Bottom;
            int dist = Math.Min(Math.Min(dLeft, dRight), Math.Min(dTop, dBottom));

            int cx = b.X + b.Width / 2;
            int cy = b.Y + b.Height / 2;

            if (dist <= SNAP_DIST)
            {
                DockEdge ne;
                int min = dist;
                if (dLeft == min) ne = DockEdge.Left;
                else if (dRight == min) ne = DockEdge.Right;
                else if (dTop == min) ne = DockEdge.Top;
                else ne = DockEdge.Bottom;

                Size ns = PanelSizeFor(ne);
                if (IsVertical(ne))
                {
                    double span = wa.Height - ns.Height;
                    _ratio = span > 0 ? ((cy - wa.Top) - ns.Height / 2.0) / span : 0.5;
                }
                else
                {
                    double span = wa.Width - ns.Width;
                    _ratio = span > 0 ? ((cx - wa.Left) - ns.Width / 2.0) / span : 0.5;
                }
                if (_ratio < 0) _ratio = 0;
                if (_ratio > 1) _ratio = 1;

                // 居中吸附：拖到这条边的中点附近（±64px）就吸正到正中，不用手动对中
                int mid = IsVertical(ne) ? wa.Top + wa.Height / 2 : wa.Left + wa.Width / 2;
                int cur = IsVertical(ne) ? cy : cx;
                if (Math.Abs(cur - mid) <= CENTER_SNAP_DIST) { _ratio = 0.5; Log.Write("center snap edge=" + ne); }

                _edge = ne;
                _floating = false;
                ApplyLayout(null);
                _t = 1f; _tb = 1f;
            }
            else
            {
                _floating = true;
                _floatX = Location.X;
                _floatY = Location.Y;
                ApplyLayout(null);
                _t = 1f; _tb = 1f;
            }
            Render();
            SaveSettings();
            AssertTopMost();
            Log.Write("drop -> free=" + _floating + " edge=" + _edge + " loc=" + Location + " dist=" + dist);
        }

        private void ItemDragMove()
        {
            if (_pressItem == null) return;
            Point d = new Point(Cursor.Position.X - _pressPoint.X, Cursor.Position.Y - _pressPoint.Y);
            if (!_itemDrag && Math.Abs(d.X) < 5 && Math.Abs(d.Y) < 5) return;
            _itemDrag = true;

            Point pc = PointToClient(Cursor.Position);

            bool vert = IsVertical(_edge);
            int along = vert ? pc.Y : pc.X;
            int newIdx = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                DockItem other = _items[i];
                if (other == _pressItem) continue;
                Rectangle ob = other.R;
                int c = vert ? ob.Top + ob.Height / 2 : ob.Left + ob.Width / 2;
                if (c < along) newIdx++;
            }
            int cur = _items.IndexOf(_pressItem);
            if (newIdx != cur)
            {
                _items.RemoveAt(cur);
                if (newIdx > _items.Count) newIdx = _items.Count;
                _items.Insert(newIdx, _pressItem);
                LayoutRects(Width, Height, _pressItem);
            }

            // 拖动阶段这颗图标只在浮影窗里出现（主窗口不画它），
            // 这样图标被拖出面板边界时不会出现“一半留在白条、一半跟着鼠标”的割裂感
            if (_ghost == null) _ghost = new DragGhost();
            _ghost.ShowAt(_pressItem.Disc, _iconSize, Cursor.Position);

            Render();
        }


        // ------------------------------------------------------------ 拖入文件
        private void OnDockDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
                _dragOver = true;
            }
        }

        private void OnDockDragLeave(object sender, EventArgs e)
        {
            _dragOver = false;
        }

        private void OnDockDragDrop(object sender, DragEventArgs e)
        {
            _dragOver = false;
            if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files == null) return;
            foreach (string f in files)
            {
                try
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    string name = Path.GetFileNameWithoutExtension(f);
                    if (ext == ".lnk" || ext == ".url")
                    {
                        File.Copy(f, Path.Combine(_itemsDir, Path.GetFileName(f)), true);
                    }
                    else
                    {
                        string lnk = Path.Combine(_itemsDir, name + ".lnk");
                        Shortcut.Create(f, lnk);
                    }
                }
                catch { }
            }
            Reload();
            _t = 1f;
            _wantShow = true;
            _leaveMs = 0; _barLeaveMs = 0; _wantBar = true;
            ApplyPosition();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { SystemEvents.DisplaySettingsChanged -= OnDisplayChanged; } catch { }
                _animRun = false;
                if (_tick != null) _tick.Dispose();
                if (_pvTimer != null) _pvTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // ---------------------------------------------------------------- 建立快捷方式
    internal static class Shortcut
    {
        public static void Create(string target, string lnkPath)
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;
                object shell = Activator.CreateInstance(shellType);
                object sc = shellType.InvokeMember("CreateShortcut",
                    System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                Type scType = sc.GetType();
                if (Directory.Exists(target))
                {
                    // 文件夹直接当目标：图标就是真实的黄文件夹，属性里也显示原目录
                    scType.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { target });
                }
                else if (File.Exists(target))
                {
                    scType.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { target });
                    scType.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
                }
                else
                {
                    scType.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { "explorer.exe" });
                    scType.InvokeMember("Arguments", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { "\"" + target + "\"" });
                }
                scType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, sc, null);
            }
            catch { }
        }
    }

    // ---------------------------------------------------------------- 设置窗口：勾选桌面上的图标
    // ---------------------------------------------------------------- 图标提取（取 256 大图标，避免模糊）
    internal static class IconLoader
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("shell32.dll")]
        private static extern int SHGetImageList(int iImageList, ref Guid riid, out IImageList ppv);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IImageList
        {
            [PreserveSig] int Add(IntPtr hbmImage, IntPtr hbmMask, ref int pi);
            [PreserveSig] int ReplaceIcon(int i, IntPtr hicon, ref int pi);
            [PreserveSig] int SetOverlayImage(int iImage, int iOverlay);
            [PreserveSig] int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
            [PreserveSig] int AddMasked(IntPtr hbmImage, int crMask, ref int pi);
            [PreserveSig] int Draw(IntPtr pimldp);
            [PreserveSig] int Remove(int i);
            [PreserveSig] int GetIcon(int i, int flags, ref IntPtr picon);
        }

        private const uint SHGFI_SYSICONINDEX = 0x000004000;
        private const int SHIL_JUMBO = 4;
        private const int ILD_TRANSPARENT = 0x00000001;

        public static Image Load(string path, int size)
        {
            IntPtr hIcon = IntPtr.Zero;
            try
            {
                SHFILEINFO sfi = new SHFILEINFO();
                IntPtr res = SHGetFileInfo(path, 0, ref sfi, (uint)Marshal.SizeOf(typeof(SHFILEINFO)), SHGFI_SYSICONINDEX);
                if (res != IntPtr.Zero && sfi.iIcon >= 0)
                {
                    Guid iid = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");
                    IImageList il;
                    if (SHGetImageList(SHIL_JUMBO, ref iid, out il) == 0 && il != null)
                    {
                        if (il.GetIcon(sfi.iIcon, ILD_TRANSPARENT, ref hIcon) == 0 && hIcon != IntPtr.Zero)
                        {
                            using (Icon ic = Icon.FromHandle(hIcon))
                            {
                                return Normalize(ic.ToBitmap(), size);
                            }
                        }
                    }
                }
            }
            catch { }
            finally
            {
                if (hIcon != IntPtr.Zero) { try { DestroyIcon(hIcon); } catch { } }
            }

            try
            {
                using (Icon ic = Icon.ExtractAssociatedIcon(path))
                {
                    if (ic != null) return Normalize(ic.ToBitmap(), size);
                }
            }
            catch { }
            return null;
        }

        private static Image Normalize(Bitmap src, int size)
        {
            int box = size;
            Bitmap bmp = new Bitmap(box, box, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            Graphics g = Graphics.FromImage(bmp);
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;

            int side = Math.Min(src.Width, src.Height);
            Rectangle srcRect = new Rectangle((src.Width - side) / 2, (src.Height - side) / 2, side, side);
            g.DrawImage(src, new Rectangle(0, 0, box, box), srcRect, GraphicsUnit.Pixel);
            g.Dispose();
            src.Dispose();
            return bmp;
        }
    }
}




