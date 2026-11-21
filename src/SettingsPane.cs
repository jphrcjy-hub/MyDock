using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace MyDock
{
    // ---------------------------------------------------------------- 系统对话框深色标题栏
    internal static class SysDark
    {
        [System.Runtime.InteropServices.DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
        private static extern int SetPreferredAppMode(int mode);
        [System.Runtime.InteropServices.DllImport("uxtheme.dll", EntryPoint = "#136")]
        private static extern void FlushMenuThemes();
        [System.Runtime.InteropServices.DllImport("uxtheme.dll", EntryPoint = "#133", SetLastError = true)]
        private static extern bool AllowDarkModeForWindow(IntPtr h, bool allow);
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumProc cb, IntPtr p);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr h);
        private delegate bool EnumProc(IntPtr h, IntPtr p);
        private static bool _on;

        // 2 = AllowDark：标题栏/系统对话框跟随系统主题（Win10 1903+ / Win11）
        public static void Enable()
        {
            if (_on) return;
            _on = true;
            try { SetPreferredAppMode(2); FlushMenuThemes(); }
            catch { }
        }

        private static void Paint(IntPtr h, bool dark)
        {
            try { AllowDarkModeForWindow(h, dark); } catch { }
            int v = dark ? 1 : 0;
            try { DwmSetWindowAttribute(h, 20, ref v, 4); } catch { }
            try { DwmSetWindowAttribute(h, 19, ref v, 4); } catch { }
        }

        // 把本进程里已经弹出的系统对话框挨个套上跟随系统的标题栏
        public static int Follow()
        {
            int n = 0;
            uint me = (uint)Process.GetCurrentProcess().Id;
            bool dark = !Theme.IsLight();
            try
            {
                EnumWindows(delegate(IntPtr h, IntPtr p)
                {
                    uint pid;
                    GetWindowThreadProcessId(h, out pid);
                    if (pid != me || !IsWindowVisible(h)) return true;
                    System.Text.StringBuilder sb = new System.Text.StringBuilder(64);
                    GetClassName(h, sb, 64);
                    if (sb.ToString() == "#32770") { Paint(h, dark); n++; }
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
            return n;
        }
    }

    // ---------------------------------------------------------------- 系统主题
    internal static class Theme
    {        // Windows 10/11：应用主题 0=深色 1=浅色
        public static bool IsLight()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (k != null)
                    {
                        object v = k.GetValue("AppsUseLightTheme");
                        if (v != null) return Convert.ToInt32(v) != 0;
                    }
                }
            }
            catch { }
            return false;
        }
    }

    // ---------------------------------------------------------------- 配色
    internal static class Pal2
    {
        public static Color Bg, Card, CardBorder, Ink, Label, Muted, Input, InputBorder, Accent, AccentHover, Track, Btn, BtnHover, BtnBorder;

        public static void Load(bool light)
        {
            if (light)
            {
                Bg = Color.FromArgb(244, 245, 248);
                Card = Color.FromArgb(255, 255, 255);
                CardBorder = Color.FromArgb(226, 229, 235);
                Ink = Color.FromArgb(24, 28, 34);
                Label = Color.FromArgb(70, 78, 90);
                Muted = Color.FromArgb(138, 146, 158);
                Input = Color.FromArgb(252, 253, 254);
                InputBorder = Color.FromArgb(212, 217, 224);
                Accent = Color.FromArgb(45, 106, 235);
                AccentHover = Color.FromArgb(30, 92, 222);
                Track = Color.FromArgb(226, 230, 236);
                Btn = Color.FromArgb(246, 247, 249);
                BtnHover = Color.FromArgb(237, 239, 243);
                BtnBorder = Color.FromArgb(214, 219, 226);
            }
            else
            {
                Bg = Color.FromArgb(24, 26, 30);
                Card = Color.FromArgb(35, 37, 42);
                CardBorder = Color.FromArgb(51, 55, 62);
                Ink = Color.FromArgb(235, 238, 243);
                Label = Color.FromArgb(178, 185, 195);
                Muted = Color.FromArgb(120, 128, 140);
                Input = Color.FromArgb(28, 30, 35);
                InputBorder = Color.FromArgb(58, 62, 70);
                Accent = Color.FromArgb(74, 132, 246);
                AccentHover = Color.FromArgb(96, 148, 250);
                Track = Color.FromArgb(52, 56, 63);
                Btn = Color.FromArgb(44, 47, 53);
                BtnHover = Color.FromArgb(52, 56, 63);
                BtnBorder = Color.FromArgb(62, 67, 75);
            }
        }
    }

    // ---------------------------------------------------------------- 绘制小工具
    internal static class Ui
    {
        // 自绘控件用：取宿主底色，避免控件自己留出“异色块”
        public static Color Host(Control c)
        {
            Control p = c == null ? null : c.Parent;
            if (p != null && p.BackColor != Color.Transparent && p.BackColor.A >= 255) return p.BackColor;
            return Pal2.Card;
        }

        public static void Fill(Graphics g, Control c)
        {
            using (SolidBrush b = new SolidBrush(Host(c))) g.FillRectangle(b, 0, 0, c.Width, c.Height);
        }
    }

    // ---------------------------------------------------------------- 圆角卡片
    internal class Card : Panel
    {
        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Gfx.Round(r, 14))
            {
                using (SolidBrush b = new SolidBrush(Pal2.Card)) g.FillPath(b, p);
                using (Pen pen = new Pen(Pal2.CardBorder)) g.DrawPath(pen, p);
            }
        }
    }

    // ---------------------------------------------------------------- 自绘滑条
    internal class Slider : Control
    {
        public event EventHandler ValueChanged;
        private int _min, _max = 100, _val = 50;
        public int Step = 1;   // 值的最小步进（图标尺寸用 4px 一档）
        private bool _hot, _drag;
        private float _grow;   // 悬停/拖动时圆点的放大进度（0~1，做平滑过渡）
        private Timer _ani;

        private void EnsureAnim()
        {
            if (_ani != null) return;
            _ani = new Timer();
            _ani.Interval = 16;
            _ani.Tick += delegate
            {
                float target = (_hot || _drag) ? 1f : 0f;
                float d = target - _grow;
                if (Math.Abs(d) < 0.03f) { _grow = target; _ani.Stop(); }
                else _grow += d * 0.25f;
                Invalidate();
            };
            Disposed += delegate { if (_ani != null) { _ani.Stop(); _ani.Dispose(); } };
        }

        private void Animate()
        {
            EnsureAnim();
            if (!_ani.Enabled) _ani.Start();
        }

        public Slider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 22;
            Cursor = Cursors.Hand;
        }

        public int Minimum { get { return _min; } set { _min = value; if (_val < _min) _val = _min; Invalidate(); } }
        public int Maximum { get { return _max; } set { _max = value; if (_val > _max) _val = _max; Invalidate(); } }

        public int Value
        {
            get { return _val; }
            set
            {
                int v = value;
                if (v < _min) v = _min;
                if (v > _max) v = _max;
                if (v == _val) return;
                _val = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.Fill(g, this);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int d = 13 + (int)Math.Round(3 * _grow);   // 圆点 13→16
            int pad = d / 2 + 1;                        // 两端留出半径，圆点不再被切掉
            int th = 4;
            int ty = (Height - th) / 2;
            int tw = Width - 1 - pad * 2;
            if (tw < 2) tw = 2;

            bool off = !Enabled;
            Color trackC = off ? Blend(Pal2.Track, Pal2.Card, 0.55f) : Pal2.Track;
            Color accC = off ? Blend(Pal2.Accent, Pal2.Card, 0.55f) : Pal2.Accent;

            using (GraphicsPath p = Gfx.Round(new Rectangle(pad, ty, tw, th), th))
            using (SolidBrush b = new SolidBrush(trackC))
                g.FillPath(b, p);

            float k = _max > _min ? (_val - _min) / (float)(_max - _min) : 0f;
            int x = pad + (int)Math.Round(tw * k);
            if (x - pad > 1)
            {
                using (GraphicsPath p = Gfx.Round(new Rectangle(pad, ty, x - pad, th), th))
                using (SolidBrush b = new SolidBrush(accC))
                    g.FillPath(b, p);
            }

            int cx = x - d / 2;
            int cy = (Height - d) / 2;
            // 圆点投影，让它在浅色底上有层次
            using (GraphicsPath p = Gfx.Circle(new RectangleF(cx + 0.5f, cy + 1.5f, d, d)))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(28, Pal2.Ink.R, Pal2.Ink.G, Pal2.Ink.B)))
                g.FillPath(b, p);
            using (GraphicsPath p = Gfx.Circle(new RectangleF(cx, cy, d, d)))
            {
                using (SolidBrush b = new SolidBrush(off ? Blend(Color.White, Pal2.Card, 0.35f) : Color.White)) g.FillPath(b, p);
                using (Pen pen = new Pen(accC, _hot || _drag ? 1.8f : 1.5f)) g.DrawPath(pen, p);
            }
        }

        private static Color Blend(Color a, Color b, float k)
        {
            if (k < 0f) k = 0f;
            if (k > 1f) k = 1f;
            return Color.FromArgb(
                (int)Math.Round(a.R + (b.R - a.R) * k),
                (int)Math.Round(a.G + (b.G - a.G) * k),
                (int)Math.Round(a.B + (b.B - a.B) * k));
        }

        private void SetFromX(int x)
        {
            int d = 13 + (int)Math.Round(3 * _grow);
            int pad = d / 2 + 1;
            float k = (x - pad) / (float)Math.Max(1, Width - 1 - pad * 2);
            if (k < 0f) k = 0f;
            if (k > 1f) k = 1f;
            int v = _min + (int)Math.Round(k * (_max - _min));
            if (Step > 1)
            {
                v = _min + (int)Math.Round((v - _min) / (double)Step) * Step;
                if (v > _max) v = _max;
                if (v < _min) v = _min;
            }
            Value = v;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            _drag = true;
            Capture = true;          // 拖出控件范围也要继续跟随，边拖边改
            Animate();
            SetFromX(e.X);
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_drag) SetFromX(e.X);
            if (!_hot) { _hot = true; Animate(); Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _drag = false;
            Capture = false;
            Animate();
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hot) { _hot = false; Animate(); Invalidate(); }
        }
    }

    // ---------------------------------------------------------------- 自绘按钮
    internal class FlatBtn : Control
    {
        public bool Accent;
        private bool _hot, _down;

        public FlatBtn()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 32;
            Cursor = Cursors.Hand;
            Font = new Font("Microsoft YaHei UI", 9f);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.Fill(g, this);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = Accent
                ? (_down ? Pal2.Accent : (_hot ? Pal2.AccentHover : Pal2.Accent))
                : (_down ? Pal2.BtnHover : (_hot ? Pal2.BtnHover : Pal2.Btn));
            Color text = Accent ? Color.White : Pal2.Ink;
            using (GraphicsPath p = Gfx.Round(r, 8))
            {
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                if (!Accent)
                    using (Pen pen = new Pen(Pal2.BtnBorder)) g.DrawPath(pen, p);
            }
            Rectangle tr = new Rectangle(0, 0, Width, Height);
            TextRenderer.DrawText(g, Text, Font, tr, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hot = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hot = false; _down = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _down = true; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _down = false; Invalidate(); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Invalidate(); }
    }

    // ---------------------------------------------------------------- 自绘勾选
    internal class FlatCheck : Control
    {
        private bool _checked;

        public event EventHandler CheckedChanged;

        public FlatCheck()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 26;
            Cursor = Cursors.Hand;
            Font = new Font("Microsoft YaHei UI", 9f);
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.Fill(g, this);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int box = 17;
            int by = (Height - box) / 2;
            Rectangle r = new Rectangle(1, by, box - 1, box - 1);
            using (GraphicsPath p = Gfx.Round(r, 5))
            {
                using (SolidBrush b = new SolidBrush(_checked ? Pal2.Accent : Pal2.Input)) g.FillPath(b, p);
                using (Pen pen = new Pen(_checked ? Pal2.Accent : Pal2.InputBorder)) g.DrawPath(pen, p);
                if (_checked)
                {
                    using (Pen pen = new Pen(Color.White, 1.8f))
                    {
                        g.DrawLine(pen, r.X + 4, r.Y + 8.5f, r.X + 7, r.Y + 12f);
                        g.DrawLine(pen, r.X + 7, r.Y + 12f, r.X + 12.5f, r.Y + 5.5f);
                    }
                }
            }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(box + 6, 0, Width - box - 6, Height), Pal2.Ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            Checked = !Checked;
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
    }

    // ---------------------------------------------------------------- 自绘下拉框（无箭头、带边框）
    internal class Select : Control
    {
        private readonly List<string> _items = new List<string>();
        private int _sel = -1;
        private bool _hot;
        private SelectPopup _popup;

        public event EventHandler SelectedIndexChanged;

        public Select()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 32;
            Cursor = Cursors.Hand;
            Font = new Font("Microsoft YaHei UI", 9.5f);
        }

        public List<string> Items { get { return _items; } }

        public int SelectedIndex
        {
            get { return _sel; }
            set
            {
                int v = value;
                if (v >= _items.Count) v = _items.Count - 1;
                if (v < -1) v = -1;
                if (v == _sel) return;
                _sel = v;
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.Fill(g, this);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Gfx.Round(r, 8))
            {
                using (SolidBrush b = new SolidBrush(Pal2.Input)) g.FillPath(b, p);
                using (Pen pen = new Pen(_hot || (_popup != null && _popup.Visible) ? Pal2.Accent : Pal2.InputBorder))
                    g.DrawPath(pen, p);
            }
            string t = _sel >= 0 && _sel < _items.Count ? _items[_sel] : "";
            TextRenderer.DrawText(g, t, Font, new Rectangle(12, 0, Width - 20, Height), Pal2.Ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hot = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hot = false; Invalidate(); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_popup != null && _popup.Visible) { _popup.Close(); return; }
            _popup = new SelectPopup(this);
            _popup.Open();
        }
    }

    internal class SelectPopup : Form
    {
        private readonly Select _owner;
        private int _hot = -1;
        private const int PAD = 6;
        private const int ROW = 30;

        public SelectPopup(Select owner)
        {
            _owner = owner;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Pal2.Input;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            KeyPreview = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Gfx.Round(r, 10))
            {
                using (SolidBrush b = new SolidBrush(Pal2.Card)) g.FillPath(b, p);
                using (Pen pen = new Pen(Pal2.InputBorder)) g.DrawPath(pen, p);
            }
            for (int i = 0; i < _owner.Items.Count; i++)
            {
                Rectangle row = new Rectangle(PAD, PAD + i * ROW, Width - PAD * 2 - 1, ROW);
                bool sel = i == _owner.SelectedIndex;
                if (i == _hot || sel)
                {
                    Color c = Pal2.Accent;
                    using (GraphicsPath p = Gfx.Round(row, 7))
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(i == _hot ? 40 : 24, c.R, c.G, c.B)))
                        g.FillPath(b, p);
                }
                TextRenderer.DrawText(g, _owner.Items[i], _owner.Font,
                    new Rectangle(row.X + 12, row.Y, row.Width - 16, row.Height), Pal2.Ink,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
        }

        public void Open()
        {
            int w = _owner.Width;
            int h = PAD * 2 + _owner.Items.Count * ROW + 2;
            Point p = _owner.PointToScreen(new Point(0, _owner.Height + 5));
            Rectangle work = Screen.FromPoint(p).WorkingArea;
            if (p.Y + h > work.Bottom) p = _owner.PointToScreen(new Point(0, -h - 5));
            if (p.X + w > work.Right) p.X = work.Right - w;
            Bounds = new Rectangle(p.X, p.Y, w, h);
            // 以设置窗口为 owner：只在设置窗口之上，不跟着设置窗口一起“全程置顶”
            Form f = _owner.FindForm();
            if (f != null) Owner = f;
            Show();
            Activate();
            Invalidate();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            Close();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int idx = IndexAt(e.Location);
            if (idx != _hot) { _hot = idx; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hot = -1;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int idx = IndexAt(e.Location);
            if (idx >= 0) _owner.SelectedIndex = idx;
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) Close();
        }

        private int IndexAt(Point p)
        {
            int i = (p.Y - PAD) / ROW;
            if (i < 0 || i >= _owner.Items.Count) return -1;
            if (p.X < PAD || p.X > Width - PAD) return -1;
            return i;
        }
    }

    // 不给原生滚动条的 ListBox：去掉 WS_VSCROLL，滚轮 / 方向键照旧能滚，右侧不留难看的条
    internal class NoBarList : ListBox
    {
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style &= ~0x00200000;
                return cp;
            }
        }
    }

    // ---------------------------------------------------------------- 设置窗口
    // 只列出“已经在快捷栏里”的图标；增删/改外观都会实时预览到快捷栏，
    // 点“确定”才落盘，点“取消”由 DockForm 还原成打开前的状态。
    internal class SettingsForm : Form
    {
        private readonly string _items;
        private readonly Slider _slSize;
        private readonly Select _cbBg;
        private readonly Slider _slBar;
        private readonly Slider _slGap;
        private readonly Func<int, int, int, int> _maxBar;   // (数量, 图标尺寸, 间距) -> 白条长度上限%
        private readonly Label _lblSizeVal;
        private readonly Label _lblBarVal;
        private readonly Label _lblGapVal;
        private readonly Slider _slAlpha;
        private readonly Label _lblAlphaVal;
        private readonly FlatCheck _ckGlass;
        private readonly Card _cardLook;
        private readonly Card _cardItems;
        private readonly FlatCheck _ckAuto;
        private readonly Panel _pathBox;
        private readonly TextBox _tbDesktop;
        private readonly ListBox _listItems;
        private readonly Panel _listBox;
        private bool _ready;      // 控件全部建好之后才允许重算白条上限
        private int _bgMode = 1;  // 跟随快捷栏的背景选择（黑/白/跟随系统），与透明度无关
        private System.Windows.Forms.Timer _dlgTimer;   // 系统对话框弹出期间，帮它把标题栏刷成跟随系统主题
        private readonly List<string> _paths = new List<string>();
        private string _desktop;
        private bool _init;
        private bool _hotPath;

        private static readonly int[] Sizes = new int[] { 32, 40, 48, 64 };
        private static readonly string[] SizeNames = new string[] { "小", "中", "大", "超大" };
        private static readonly string[] BgNames = new string[] { "黑色", "白色", "跟随系统" };

        public event EventHandler Changed;

        public bool AutoStart { get { return _ckAuto != null && _ckAuto.Checked; } }
        public int Gap { get { return _slGap == null ? 6 : _slGap.Value; } }
        public int BarPct { get { return _slBar == null ? 50 : _slBar.Value; } }
        public int AlphaPct { get { return (_slAlpha == null || !_ckGlass.Checked) ? 0 : _slAlpha.Value; } }

        public int IconSize
        {
            get
            {
                int v = _slSize == null ? 48 : _slSize.Value;
                if (v < 32) v = 32;
                if (v > 100) v = 100;
                return v;
            }
        }

        public int BgStyle
        {
            get
            {
                int i = _cbBg == null ? 0 : _cbBg.SelectedIndex;
                if (i < 0 || i > 2) i = 1;
                return i;
            }
        }

        public string DesktopDir { get { return _desktop; } }
        public List<string> ItemPaths { get { return _paths; } }

        public SettingsForm(string desktopDir, string itemsDir, int iconSize, int bgMode, int barPct, int gap, List<string> itemPaths, bool autoStart, Func<int, int, int, int> maxBarPct, int alphaPct)
        {
            _desktop = desktopDir;
            _items = itemsDir;
            _maxBar = maxBarPct;
            _bgMode = (bgMode < 0 || bgMode > 2) ? 1 : bgMode;
            SysDark.Enable();

            Text = "快捷栏设置";
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(760, 566);
            Font = new Font("Microsoft YaHei UI", 9f);
            // 不全程置顶：只在弹出时激活到最前，点到别的窗口就正常让位
            TopMost = false;
            DoubleBuffered = true;
            BackColor = Pal2.Bg;
            KeyPreview = true;
            Paint += PaintFrame;

            // 无边框窗口：标题栏区域可拖动，右上角自绘关闭键
            MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && e.Y < 62) DragWindow();
            };
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
            };

            FlatBtn shut = new FlatBtn();
            shut.Text = "×";
            shut.Font = new Font("Microsoft YaHei UI", 12f);
            shut.Bounds = new Rectangle(716, 16, 30, 30);
            shut.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(shut);

            Label title = new Label();
            title.AutoSize = false;
            title.Bounds = new Rectangle(26, 16, 620, 26);
            title.Text = "快捷栏设置";
            title.Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold);
            title.Tag = "title";
            Controls.Add(title);

            Label sub = new Label();
            sub.AutoSize = false;
            sub.Bounds = new Rectangle(28, 44, 700, 20);
            sub.Text = "改动会实时预览到快捷栏；确定 = 保存，取消 = 还原成打开前的样子";
            sub.Tag = "muted";
            Controls.Add(sub);

            // ---------------------------------------------------------- 卡片一：外观
            // 两列固定网格：标签列 24~84，控件列 92 起；第二列 372 / 444。
            // 上下两行的标签、控件左边缘、控件宽度完全对齐。
            _cardLook = new Card();
            _cardLook.Bounds = new Rectangle(20, 72, 720, 156);
            Controls.Add(_cardLook);

            Label sizeLabel = MakeLabel("图标大小", 24, 17, 64);
            _cardLook.Controls.Add(sizeLabel);

            _slSize = new Slider();
            _slSize.Minimum = 32;      // 下限还是原来的「小」
            _slSize.Maximum = 100;
            _slSize.Step = 4;
            _slSize.Bounds = new Rectangle(92, 18, 196, 24);
            _slSize.Value = iconSize < 32 ? 32 : (iconSize > 100 ? 100 : iconSize);

            _lblSizeVal = new Label();
            _lblSizeVal.AutoSize = false;
            _lblSizeVal.Bounds = new Rectangle(296, 18, 56, 24);
            _lblSizeVal.Text = _slSize.Value + " px";
            _lblSizeVal.Tag = "muted";

            _slSize.ValueChanged += delegate
            {
                _lblSizeVal.Text = _slSize.Value + " px";
                RefreshBarLimit();
                RaiseChanged();
            };
            _cardLook.Controls.Add(_slSize);
            _cardLook.Controls.Add(_lblSizeVal);

            Label bgLabel = MakeLabel("背景", 372, 17, 64);
            _cardLook.Controls.Add(bgLabel);

            _cbBg = new Select();
            _cbBg.Bounds = new Rectangle(444, 12, 196, 32);
            foreach (string s in BgNames) _cbBg.Items.Add(s);
            _cbBg.SelectedIndex = (bgMode < 0 || bgMode > 2) ? 1 : bgMode;
            _cbBg.SelectedIndexChanged += delegate
            {
                _bgMode = BgStyle;
                ApplyTheme();          // 设置界面配色跟着快捷栏背景走
                RaiseChanged();
            };
            _cardLook.Controls.Add(_cbBg);

            Label barLabel = MakeLabel("白条长度", 24, 63, 64);
            _cardLook.Controls.Add(barLabel);

            _slBar = new Slider();
            _slBar.Minimum = 5;
            _slBar.Maximum = 100;
            _slBar.Bounds = new Rectangle(92, 64, 196, 24);
            _slBar.Value = Math.Max(5, Math.Min(100, barPct));
            _slBar.ValueChanged += delegate
            {
                _lblBarVal.Text = BarText() + "%";
                RaiseChanged();
            };
            _cardLook.Controls.Add(_slBar);

            _lblBarVal = new Label();
            _lblBarVal.AutoSize = false;
            _lblBarVal.Bounds = new Rectangle(296, 64, 56, 24);
            _lblBarVal.Text = BarText() + "%";
            _lblBarVal.Tag = "muted";
            _cardLook.Controls.Add(_lblBarVal);

            Label gapLabel = MakeLabel("图标间距", 372, 63, 64);
            _cardLook.Controls.Add(gapLabel);

            _slGap = new Slider();
            _slGap.Minimum = 0;
            _slGap.Maximum = 24;
            _slGap.Bounds = new Rectangle(444, 64, 196, 24);
            _slGap.Value = Math.Max(0, Math.Min(24, gap));
            _slGap.ValueChanged += delegate
            {
                _lblGapVal.Text = _slGap.Value + " px";
                RefreshBarLimit();
                RaiseChanged();
            };
            _cardLook.Controls.Add(_slGap);

            _lblGapVal = new Label();
            _lblGapVal.AutoSize = false;
            _lblGapVal.Bounds = new Rectangle(648, 64, 60, 24);
            _lblGapVal.Text = _slGap.Value + " px";
            _lblGapVal.Tag = "muted";
            _cardLook.Controls.Add(_lblGapVal);

            // 第三行：整条栏的透明度（横跨两列）
            Label alphaLabel = MakeLabel("透明度", 24, 109, 64);
            _cardLook.Controls.Add(alphaLabel);

            _slAlpha = new Slider();
            _slAlpha.Minimum = 50;
            _slAlpha.Maximum = 100;
            _slAlpha.Step = 1;   // 透明度 1% 一档，拖起来更细腻
            _slAlpha.Bounds = new Rectangle(92, 110, 196, 24);
            _slAlpha.Value = alphaPct < 50 ? 50 : (alphaPct > 100 ? 100 : alphaPct);
            _slAlpha.ValueChanged += delegate
            {
                SyncAlphaUi();
                RaiseChanged();
            };
            _cardLook.Controls.Add(_slAlpha);

            _lblAlphaVal = new Label();
            _lblAlphaVal.AutoSize = false;
            _lblAlphaVal.Bounds = new Rectangle(296, 110, 56, 24);
            _lblAlphaVal.Tag = "muted";
            _cardLook.Controls.Add(_lblAlphaVal);
            Log.Write("settings open alpha=" + alphaPct + " slider=" + _slAlpha.Value + " bg=" + bgMode);

            // 勾选后才应用透明度；取消勾选 = 纯色背景（完全不透明）
            _ckGlass = new FlatCheck();
            _ckGlass.Text = "应用透明度（取消勾选 = 纯色背景）";
            _ckGlass.Bounds = new Rectangle(372, 104, 320, 30);
            _ckGlass.Checked = alphaPct > 0;
            _ckGlass.CheckedChanged += delegate
            {
                SyncAlphaUi();
                RaiseChanged();
            };
            _cardLook.Controls.Add(_ckGlass);
            SyncAlphaUi();

            // ---------------------------------------------------------- 卡片二：图标清单
            _cardItems = new Card();
            _cardItems.Bounds = new Rectangle(20, 240, 720, 266);
            Controls.Add(_cardItems);

            Label pathLabel = MakeLabel("桌面目录", 20, 22, 64);
            _cardItems.Controls.Add(pathLabel);

            _pathBox = new Panel();
            _pathBox.Bounds = new Rectangle(92, 17, 306, 32);
            _pathBox.Paint += PaintPathBox;
            _pathBox.MouseEnter += delegate { _hotPath = true; _pathBox.Invalidate(); };
            _pathBox.MouseLeave += delegate { _hotPath = false; _pathBox.Invalidate(); };
            _cardItems.Controls.Add(_pathBox);

            _tbDesktop = new TextBox();
            _tbDesktop.BorderStyle = BorderStyle.None;
            _tbDesktop.Bounds = new Rectangle(11, 8, 284, 18);
            _tbDesktop.Text = _desktop;
            _tbDesktop.Leave += delegate { ApplyDesktopText(); };
            _pathBox.Controls.Add(_tbDesktop);

            FlatBtn browse = new FlatBtn();
            browse.Text = "选择文件夹";
            browse.Bounds = new Rectangle(412, 17, 96, 32);
            browse.Click += delegate
            {
                using (FolderBrowserDialog d = new FolderBrowserDialog())
                {
                    d.Description = "选择桌面图标所在的文件夹";
                    string cur = _tbDesktop.Text.Trim();
                    d.SelectedPath = Directory.Exists(cur) ? cur : _desktop;
                    StartDialogTheme();
            DialogResult dr;
            try { dr = d.ShowDialog(this); }
            finally { StopDialogTheme(); }
            if (dr == DialogResult.OK)
                    {
                        _tbDesktop.Text = d.SelectedPath;
                        ApplyDesktopText();
                    }
                }
            };
            _cardItems.Controls.Add(browse);

            FlatBtn addDesk = new FlatBtn();
            addDesk.Text = "添加桌面图标";
            addDesk.Bounds = new Rectangle(514, 17, 118, 32);
            addDesk.Click += delegate { AddDesktopIcons(); };
            _cardItems.Controls.Add(addDesk);

            Label listLabel = MakeLabel("快捷栏里的图标（双击名字 = 删除；双击空白 = 打开桌面图标文件夹；也可以把文件拖进来）", 20, 60, 690);
            _cardItems.Controls.Add(listLabel);

            _listBox = new Panel();
            _listBox.Bounds = new Rectangle(18, 86, 684, 124);
            _listBox.Paint += PaintListBox;
            _cardItems.Controls.Add(_listBox);

            _listItems = new NoBarList();
            _listItems.Bounds = new Rectangle(8, 10, 668, 104);
            _listItems.BorderStyle = BorderStyle.None;
            _listItems.DrawMode = DrawMode.OwnerDrawFixed;
            _listItems.ItemHeight = 26;
            _listItems.Font = new Font("Microsoft YaHei UI", 9.5f);
            _listItems.SelectionMode = SelectionMode.MultiExtended;
            _listItems.IntegralHeight = false;
            _listItems.HorizontalScrollbar = false;
            _listItems.AllowDrop = true;
            _listItems.DrawItem += delegate(object s, DrawItemEventArgs e2) { DrawListItem(e2); };
            _listItems.KeyDown += delegate(object s, KeyEventArgs e2)
            {
                if (e2.KeyCode == Keys.Delete) RemoveSelected();
            };
            _listItems.DoubleClick += delegate
            {
                // 像文件夹一样：只有双击“名字”才删；双击空白处打开桌面图标文件夹
                Point pt = _listItems.PointToClient(Cursor.Position);
                int idx = _listItems.IndexFromPoint(pt);
                bool onName = false;
                if (idx >= 0 && idx < _listItems.Items.Count)
                {
                    Rectangle rr = _listItems.GetItemRectangle(idx);
                    string t = _listItems.Items[idx].ToString();
                    int tw = TextRenderer.MeasureText(t, _listItems.Font).Width;
                    onName = pt.X >= rr.X + 16 && pt.X <= rr.X + 16 + tw;
                }
                if (onName) { RemoveSelected(); return; }
                OpenDesktopFolder();
            };
            _listItems.DragEnter += delegate(object s, DragEventArgs e2)
            {
                if (e2.Data != null && e2.Data.GetDataPresent(DataFormats.FileDrop)) e2.Effect = DragDropEffects.Copy;
            };
            _listItems.DragDrop += delegate(object s, DragEventArgs e2)
            {
                if (e2.Data == null || !e2.Data.GetDataPresent(DataFormats.FileDrop)) return;
                string[] fs = (string[])e2.Data.GetData(DataFormats.FileDrop);
                if (fs != null) AddPaths(fs);
            };
            _listBox.Controls.Add(_listItems);

            _listItems.MouseEnter += delegate { if (!_tbDesktop.Focused && !_listItems.Focused) _listItems.Focus(); };

            // 没有原生滚动条了，滚轮自己接管：一格两行，滚到头就停住
            _listItems.MouseWheel += delegate(object s, MouseEventArgs e2)
            {
                HandledMouseEventArgs hm = e2 as HandledMouseEventArgs;
                if (hm != null) hm.Handled = true;
                int cnt = _listItems.Items.Count;
                int vis = Math.Max(1, _listItems.Height / _listItems.ItemHeight);
                int maxTop = Math.Max(0, cnt - vis);
                if (maxTop <= 0) return;
                int step = e2.Delta > 0 ? -2 : 2;
                int t = _listItems.TopIndex + step;
                if (t < 0) t = 0;
                if (t > maxTop) t = maxTop;
                _listItems.TopIndex = t;
            };

            // 列表不再显示任何滚动条：去掉原生 WS_VSCROLL 样式，滚轮 / 方向键照样能滚
            _listItems.HandleCreated += delegate
            {
                int st = GetWindowLong(_listItems.Handle, -16);
                SetWindowLong(_listItems.Handle, -16, st & ~0x00200000);
                SetWindowPos(_listItems.Handle, IntPtr.Zero, 0, 0, 0, 0,
                    0x0002 | 0x0001 | 0x0004 | 0x0020 /*NOMOVE|NOSIZE|NOZORDER|FRAMECHANGED*/);
            };


            FlatBtn all = new FlatBtn();
            all.Text = "全选";
            all.Bounds = new Rectangle(18, 222, 88, 32);
            all.Click += delegate { SelectAll(); };
            _cardItems.Controls.Add(all);

            FlatBtn remove = new FlatBtn();
            remove.Text = "删除";
            remove.Bounds = new Rectangle(114, 222, 88, 32);
            remove.Click += delegate { RemoveSelected(); };
            _cardItems.Controls.Add(remove);

            FlatBtn up = new FlatBtn();
            up.Text = "上移";
            up.Bounds = new Rectangle(210, 222, 76, 32);
            up.Click += delegate { MoveSelected(-1); };
            _cardItems.Controls.Add(up);

            FlatBtn down = new FlatBtn();
            down.Text = "下移";
            down.Bounds = new Rectangle(294, 222, 76, 32);
            down.Click += delegate { MoveSelected(1); };
            _cardItems.Controls.Add(down);

            // ---------------------------------------------------------- 底部
            _ckAuto = new FlatCheck();
            _ckAuto.Text = "开机自动启动";
            _ckAuto.Bounds = new Rectangle(24, 528, 220, 26);
            _ckAuto.Checked = autoStart;
            Controls.Add(_ckAuto);

            FlatBtn cancel = new FlatBtn();
            cancel.Text = "取消";
            cancel.Bounds = new Rectangle(540, 522, 96, 34);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);

            FlatBtn ok = new FlatBtn();
            ok.Text = "确定";
            ok.Accent = true;
            ok.Bounds = new Rectangle(646, 522, 96, 34);
            ok.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            Controls.Add(ok);

            foreach (string p in itemPaths)
            {
                if (File.Exists(p) || Directory.Exists(p)) _paths.Add(p);
            }
            RefreshList();
            _ready = true;
            RefreshBarLimit();
            _init = true;
            ApplyTheme();
            Activated += delegate { bool light = UiIsLight(); if (light != _light) { ApplyTheme(); } };
        }

        private bool _light = true;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr h, int msg, int w, int l);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr h, int idx);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr h, int idx, int val);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        private void DragWindow()
        {
            ReleaseCapture();
            SendMessage(Handle, 0x00A1 /*WM_NCLBUTTONDOWN*/, 0x0002 /*HTCAPTION*/, 0);
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

        // Win11：让系统给窗口做圆角 + 投影；老系统上退化成"直角色块"，所以下面再补一层圆角区域兜底
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int pref = 2;                                  // DWMWCP_ROUND
                DwmSetWindowAttribute(Handle, 33, ref pref, 4);
                int dark = _light ? 0 : 1;
                DwmSetWindowAttribute(Handle, 20, ref dark, 4); // 标题栏/边框跟着界面深浅色
            }
            catch { }
        }

        private void PaintFrame(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Gfx.Round(new Rectangle(0, 0, Width - 1, Height - 1), 18))
            using (Pen pen = new Pen(Pal2.CardBorder))
                g.DrawPath(pen, p);
        }

        // ---------------------------------------------------------- 控件工厂
        private Label MakeLabel(string text, int x, int y, int w)
        {
            Label l = new Label();
            l.AutoSize = false;
            l.Bounds = new Rectangle(x, y, w, 22);
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.Text = text;
            l.Font = new Font("Microsoft YaHei UI", 9f);
            return l;
        }

        // ---------------------------------------------------------- 自绘：输入框 / 列表容器
        private void PaintPathBox(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, _pathBox.Width - 1, _pathBox.Height - 1);
            using (GraphicsPath p = Gfx.Round(r, 7))
            {
                using (SolidBrush b = new SolidBrush(Pal2.Input)) g.FillPath(b, p);
                using (Pen pen = new Pen(_hotPath ? Pal2.Accent : Pal2.InputBorder)) g.DrawPath(pen, p);
            }
        }

        private void PaintListBox(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, _listBox.Width - 1, _listBox.Height - 1);
            using (GraphicsPath p = Gfx.Round(r, 8))
            {
                using (SolidBrush b = new SolidBrush(Pal2.Input)) g.FillPath(b, p);
                using (Pen pen = new Pen(Pal2.InputBorder)) g.DrawPath(pen, p);
            }

        }


        private void DrawListItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool sel = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Rectangle r = e.Bounds;
            using (SolidBrush b = new SolidBrush(Pal2.Input)) g.FillRectangle(b, r);
            if (sel)
            {
                Rectangle rr = new Rectangle(r.X + 6, r.Y + 2, r.Width - 12, r.Height - 4);
                using (GraphicsPath p = Gfx.Round(rr, 7))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(34, Pal2.Accent.R, Pal2.Accent.G, Pal2.Accent.B)))
                    g.FillPath(b, p);
            }
            string t = _listItems.Items[e.Index].ToString();
            TextRenderer.DrawText(g, t, _listItems.Font, new Rectangle(r.X + 16, r.Y, r.Width - 28, r.Height),
                Pal2.Ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }

        // ---------------------------------------------------------- 主题
        private void SyncAlphaUi()
        {
            if (_slAlpha == null || _lblAlphaVal == null) return;
            _slAlpha.Enabled = _ckGlass.Checked;
            _lblAlphaVal.Text = _ckGlass.Checked ? _slAlpha.Value + "%" : "纯色";
            _lblAlphaVal.Invalidate();
            if (_slAlpha != null) _slAlpha.Invalidate();
        }

        // 设置界面用哪套配色：黑色 → 深色，白色 → 浅色，跟随系统 → 看系统
        private bool UiIsLight()
        {
            if (_bgMode == 0) return false;
            if (_bgMode == 1) return true;
            return Theme.IsLight();
        }

        private void ApplyTheme()
        {
            _light = UiIsLight();
            Pal2.Load(_light);

            BackColor = Pal2.Bg;
            ForeColor = Pal2.Ink;

            foreach (Control c in Controls)
            {
                if (c is Label)
                {
                    if (c.Tag is string && (string)c.Tag == "title") c.ForeColor = Pal2.Ink;
                    else c.ForeColor = (c.Tag is string && (string)c.Tag == "muted") ? Pal2.Muted : Pal2.Label;
                }
            }

            // 卡片里的控件
            foreach (Control c in new Control[] { _cardLook, _cardItems })
            {
                foreach (Control cc in c.Controls)
                {
                    if (cc is Label) cc.ForeColor = (cc.Tag is string && (string)cc.Tag == "muted") ? Pal2.Muted : Pal2.Label;
                }
                c.Invalidate();
            }

            if (_tbDesktop != null) { _tbDesktop.BackColor = Pal2.Input; _tbDesktop.ForeColor = Pal2.Ink; }
            if (_pathBox != null) _pathBox.Invalidate();
            if (_listItems != null) { _listItems.BackColor = Pal2.Input; _listItems.ForeColor = Pal2.Ink; _listItems.Invalidate(); }
            if (_listBox != null) _listBox.Invalidate();

            foreach (Control c in Controls)
            {
                if (c is FlatBtn || c is Slider || c is FlatCheck || c is Select) c.Invalidate();
            }
            _cardLook.Invalidate(true);
            _cardItems.Invalidate(true);
            Invalidate(true);
        }

        // ---------------------------------------------------------- 列表
        private void RaiseChanged()
        {
            if (!_init) return;
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        private void SelectAll()
        {
            if (_listItems.Items.Count == 0) return;
            _listItems.SelectedIndex = -1;
            for (int i = 0; i < _listItems.Items.Count; i++) _listItems.SetSelected(i, true);
        }

        private void ApplyDesktopText()
        {
            if (_tbDesktop == null) return;
            string p = _tbDesktop.Text.Trim().Trim('"');
            if (p.Length == 0)
            {
                _tbDesktop.Text = _desktop;
                return;
            }
            if (!Directory.Exists(p))
            {
                MessageBox.Show(this, "目录不存在：\r\n" + p, "桌面目录",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _tbDesktop.Text = _desktop;
                return;
            }
            if (string.Equals(p, _desktop, StringComparison.OrdinalIgnoreCase)) return;
            _desktop = p;
            RaiseChanged();
        }

        // 系统对话框（选择文件夹 / 打开文件）是原生窗口，自己不会认系统的深色主题，
        // 这里在它弹出期间轮询，把标题栏刷成和系统一致
        private void StartDialogTheme()
        {
            if (_dlgTimer == null)
            {
                _dlgTimer = new System.Windows.Forms.Timer();
                _dlgTimer.Interval = 120;
                _dlgTimer.Tick += delegate { SysDark.Follow(); };
                FormClosed += delegate { if (_dlgTimer != null) { _dlgTimer.Stop(); _dlgTimer.Dispose(); } };
            }
            SysDark.Follow();
            _dlgTimer.Start();
        }

        private void StopDialogTheme()
        {
            if (_dlgTimer != null) _dlgTimer.Stop();
        }

        // 从桌面目录里挑图标（可多选）加进快捷栏
        private void AddDesktopIcons()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "从桌面选择要添加的图标（可多选）";
                d.Multiselect = true;
                d.CheckFileExists = true;
                d.Filter = "快捷方式|*.lnk;*.url|程序|*.exe|所有文件|*.*";
                string start = _tbDesktop.Text.Trim();
                if (Directory.Exists(start)) d.InitialDirectory = start;
                else if (Directory.Exists(_desktop)) d.InitialDirectory = _desktop;
                StartDialogTheme();
            DialogResult dr;
            try { dr = d.ShowDialog(this); }
            finally { StopDialogTheme(); }
            if (dr == DialogResult.OK) AddPaths(d.FileNames);
            }
        }

        private void AddPaths(IEnumerable<string> items)
        {
            bool any = false;
            foreach (string raw in items)
            {
                string p = raw;
                if (!File.Exists(p) && !Directory.Exists(p)) continue;
                bool dup = false;
                foreach (string e in _paths)
                {
                    if (string.Equals(e, p, StringComparison.OrdinalIgnoreCase)) { dup = true; break; }
                }
                if (dup) continue;
                _paths.Add(p);
                any = true;
            }
            if (!any) return;
            RefreshList();
            _listItems.SelectedIndex = _listItems.Items.Count - 1;
            RaiseChanged();
        }

        // 双击列表空白处，像打开文件夹一样打开桌面图标目录
        private void OpenDesktopFolder()
        {
            string p = (_tbDesktop != null) ? _tbDesktop.Text.Trim() : "";
            if (p.Length == 0 || !Directory.Exists(p)) p = _desktop;
            if (p.Length == 0 || !Directory.Exists(p)) return;
            try { System.Diagnostics.Process.Start("explorer.exe", "\"" + p + "\""); }
            catch { }
        }

        private void RemoveSelected()
        {
            if (_paths.Count == 0) return;
            List<int> del = new List<int>();
            foreach (int i in _listItems.SelectedIndices) if (i >= 0 && i < _paths.Count) del.Add(i);
            if (del.Count == 0) return;
            del.Sort();
            for (int i = del.Count - 1; i >= 0; i--) _paths.RemoveAt(del[i]);
            RefreshList();
            if (_listItems.Items.Count > 0)
                _listItems.SelectedIndex = Math.Min(del[0], _listItems.Items.Count - 1);
            RaiseChanged();
        }

        private void MoveSelected(int delta)
        {
            int i = _listItems.SelectedIndex;
            int j = i + delta;
            if (i < 0 || j < 0 || j >= _paths.Count) return;
            string tmp = _paths[i];
            _paths[i] = _paths[j];
            _paths[j] = tmp;
            RefreshList();
            _listItems.SelectedIndex = j;
            RaiseChanged();
        }

        private void RefreshList()
        {
            _listItems.BeginUpdate();
            _listItems.Items.Clear();
            foreach (string p in _paths)
            {
                string n = Path.GetFileNameWithoutExtension(p);
                if (n == null || n.Length == 0) n = Path.GetFileName(p);
                _listItems.Items.Add(n);
            }
            _listItems.EndUpdate();
        }

        // 白条长度上限跟着图标数量/尺寸/间距走：最多和图标那一排一样长
        // 白条长度：滑条上限随图标数变化，但显示的是「达到上限的百分比」，
        // 拉到最右永远显示 100%（= 正好到最外那颗图标的边缘）
        private int BarText()
        {
            if (_slBar == null) return 50;
            int mx = _slBar.Maximum <= 0 ? 100 : _slBar.Maximum;
            int v = _slBar.Value;
            int pc = (int)Math.Round(v * 100.0 / mx);
            if (pc < 1) pc = 1;
            if (pc > 100) pc = 100;
            return pc;
        }

        private void RefreshBarLimit()
        {
            if (_slBar == null || _maxBar == null || !_ready) return;
            int mx = _maxBar(_paths.Count, IconSize, Gap);
            if (mx < 5) mx = 5;
            if (mx > 100) mx = 100;
            _slBar.Maximum = mx;
            if (_slBar.Value > mx) _slBar.Value = mx;
            if (_lblBarVal != null) _lblBarVal.Text = BarText() + "%";
        }
    }
}
