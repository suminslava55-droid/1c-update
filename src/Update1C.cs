using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.DirectoryServices;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("1C Update")]
[assembly: System.Reflection.AssemblyDescription("Обновление платформы 1С на компьютерах через psexec")]
[assembly: System.Reflection.AssemblyProduct("1C Update")]
[assembly: System.Reflection.AssemblyVersion("1.1.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.1.0.0")]

namespace OneCUpdate
{
    // ------------------------------------------------------------------
    //  Оформление (современная тёмная палитра как в WPF-проекте)
    // ------------------------------------------------------------------
    internal static class Theme
    {
        // Основная палитра
        public static readonly Color Bg = Color.FromArgb(28, 28, 32);           // #1C1C20
        public static readonly Color Surface = Color.FromArgb(36, 36, 41);      // #242429
        public static readonly Color Surface2 = Color.FromArgb(46, 46, 52);     // #2E2E34
        public static readonly Color Surface3 = Color.FromArgb(52, 52, 58);     // #34343A
        public static readonly Color Border = Color.FromArgb(62, 62, 70);       // #3E3E46
        public static readonly Color BorderLight = Color.FromArgb(72, 72, 80);  // #484850
        
        // Текст
        public static readonly Color Text = Color.FromArgb(228, 228, 232);      // #E4E4E8
        public static readonly Color TextSecondary = Color.FromArgb(180, 180, 190); // #B4B4BE
        public static readonly Color Muted = Color.FromArgb(150, 150, 160);     // #9696A0
        public static readonly Color MutedLight = Color.FromArgb(120, 120, 130); // #78787E
        
        // Акценты
        public static readonly Color Accent = Color.FromArgb(0, 122, 204);      // #007ACC VS Code Blue
        public static readonly Color AccentHot = Color.FromArgb(24, 148, 236);  // #1894EC
        public static readonly Color AccentDark = Color.FromArgb(0, 88, 156);   // #00589C
        
        // Семантические цвета (обновлены для лучшей читаемости)
        public static readonly Color Ok = Color.FromArgb(115, 201, 144);        // #73C990 Success Green
        public static readonly Color OkBg = Color.FromArgb(23, 60, 35);         // #173C23 Success Background
        public static readonly Color Warn = Color.FromArgb(236, 196, 92);       // #ECC45C Warning Yellow
        public static readonly Color WarnBg = Color.FromArgb(60, 50, 20);       // #3C3214 Warning Background
        public static readonly Color Err = Color.FromArgb(244, 135, 113);       // #F48771 Error Red
        public static readonly Color ErrBg = Color.FromArgb(60, 30, 25);        // #3C1E19 Error Background
        public static readonly Color Info = Color.FromArgb(120, 180, 240);      // #78B4F0 Info Blue
        public static readonly Color InfoBg = Color.FromArgb(20, 40, 60);       // #14283C Info Background
        public static readonly Color Pending = Color.FromArgb(160, 160, 170);   // #A0A0AA Pending Gray
        
        // Консоль
        public static readonly Color ConsoleBg = Color.FromArgb(20, 20, 23);    // #141417
        
        // Карточки KPI
        public static readonly Color CardBg = Color.FromArgb(40, 40, 46);       // #28282E
        public static readonly Color CardBorder = Color.FromArgb(70, 70, 78);   // #46464E
        public static readonly Color CardHighlight = Color.FromArgb(50, 50, 58); // #32323A
        
        // Шрифты
        public static readonly Font Ui = new Font("Segoe UI", 9f);
        public static readonly Font UiBold = new Font("Segoe UI Semibold", 9f);
        public static readonly Font Mono = new Font("Consolas", 9f);
        public static readonly Font Title = new Font("Segoe UI Semibold", 13f);
        public static readonly Font TitleLarge = new Font("Segoe UI Semibold", 15f);
        public static readonly Font Small = new Font("Segoe UI", 8.25f);
        public static readonly Font KpiValue = new Font("Segoe UI", 20f, FontStyle.Bold);
        public static readonly Font KpiLabel = new Font("Segoe UI", 8.5f);
    }

    // ------------------------------------------------------------------
    //  Сглаженные закругленные углы (GDI+, без обрезки Region)
    // ------------------------------------------------------------------
    internal static class RoundedHelper
    {
        public static System.Drawing.Drawing2D.GraphicsPath CreatePath(Rectangle bounds, int radius)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            if (radius <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }

            int d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRounded(Graphics g, Rectangle bounds, int radius, Color fill, Color border, int borderWidth)
        {
            var mode = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var path = CreatePath(bounds, radius))
            {
                if (fill.A > 0)
                {
                    using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
                }
                if (borderWidth > 0 && border.A > 0)
                {
                    using (var pen = new Pen(border, borderWidth)) g.DrawPath(pen, path);
                }
            }
            g.SmoothingMode = mode;
        }
    }

    /// <summary>Панель с закругленными углами и рамкой (сглаженная отрисовка).</summary>
    internal class RoundedPanel : Panel
    {
        private int _radius = 8;

        public int CornerRadius
        {
            get { return _radius; }
            set { _radius = value; Invalidate(); }
        }

        public Color BorderColor { get; set; }
        public int BorderWidth { get; set; }

        public RoundedPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.Surface;
            BorderColor = Theme.Border;
            BorderWidth = 1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            RoundedHelper.FillRounded(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1),
                _radius, BackColor, BorderColor, BorderWidth);
        }
    }

    /// <summary>Кнопка с закругленными углами (сглаженная отрисовка, hover/press).</summary>
    internal sealed class RoundedButton : Button
    {
        private bool _hover;
        private bool _down;

        public int CornerRadius { get; set; }
        public Color NormalBack { get; set; }
        public Color HoverBack { get; set; }
        public Color DownBack { get; set; }
        public Color NormalBorder { get; set; }
        public Color HoverBorder { get; set; }
        public Color TextColor { get; set; }

        public RoundedButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            TabStop = false;
            Cursor = Cursors.Hand;
            CornerRadius = 6;
            NormalBack = Theme.Surface2;
            HoverBack = Theme.Surface3;
            DownBack = Theme.AccentDark;
            NormalBorder = Theme.Border;
            HoverBorder = Theme.BorderLight;
            TextColor = Theme.Text;
            Font = Theme.Ui;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color back, border, fore;
            if (!Enabled)
            {
                back = Theme.Surface;
                border = Theme.Border;
                fore = Theme.MutedLight;
            }
            else if (_down)
            {
                back = DownBack;
                border = DownBack;
                fore = TextColor;
            }
            else if (_hover)
            {
                back = HoverBack;
                border = HoverBorder;
                fore = TextColor;
            }
            else
            {
                back = NormalBack;
                border = NormalBorder;
                fore = TextColor;
            }

            e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            RoundedHelper.FillRounded(e.Graphics, rect, CornerRadius, back, border, 1);

            TextRenderer.DrawText(e.Graphics, Text, Font, rect, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Выпадающий список в стиле тёмной темы (рисует себя сам).</summary>
    internal sealed class RoundedCombo : Control
    {
        private int _selectedIndex = -1;
        private string _text = "";
        private bool _hover;
        private readonly ContextMenuStrip _menu;

        public readonly List<string> Items = new List<string>();

        public int CornerRadius { get; set; }
        public Color BorderColor { get; set; }
        public event EventHandler SelectedIndexChanged;

        public RoundedCombo()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            CornerRadius = 6;
            BackColor = Theme.Surface2;
            ForeColor = Theme.Text;
            BorderColor = Theme.Border;
            Font = Theme.Ui;
            Size = new Size(150, 27);
            Cursor = Cursors.Hand;

            _menu = new ContextMenuStrip();
            _menu.ShowImageMargin = false;
            _menu.BackColor = Theme.Surface2;
            _menu.ForeColor = Theme.Text;
            _menu.Renderer = new DarkMenuRenderer();
        }

        public int SelectedIndex
        {
            get { return _selectedIndex; }
            set
            {
                int v = value;
                if (v < -1) v = -1;
                if (v >= Items.Count) v = Items.Count - 1;
                if (v == _selectedIndex) return;
                _selectedIndex = v;
                if (v >= 0) _text = Items[v];
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        public override string Text
        {
            get { return _selectedIndex >= 0 && _selectedIndex < Items.Count ? Items[_selectedIndex] : _text; }
            set
            {
                string text = value ?? "";
                _text = text;
                int idx = Items.IndexOf(text);
                bool changed = false;
                if (idx >= 0)
                {
                    if (_selectedIndex != idx) { _selectedIndex = idx; changed = true; }
                }
                else if (_selectedIndex != -1)
                {
                    _selectedIndex = -1;
                    changed = true;
                }
                Invalidate();
                if (changed && SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) ShowMenu();
        }

        private void ShowMenu()
        {
            if (!Enabled || Items.Count == 0) return;
            _menu.Items.Clear();
            for (int i = 0; i < Items.Count; i++)
            {
                int index = i;
                var item = new ToolStripMenuItem(Items[i]);
                item.ForeColor = i == _selectedIndex ? Color.White : Theme.Text;
                item.BackColor = i == _selectedIndex ? Theme.AccentDark : Theme.Surface2;
                item.Click += delegate { SelectedIndex = index; };
                _menu.Items.Add(item);
            }
            _menu.MinimumSize = new Size(Width, 0);
            _menu.Show(this, new Point(0, Height + 2));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            RoundedHelper.FillRounded(e.Graphics, rect, CornerRadius, BackColor,
                _hover && Enabled ? Theme.BorderLight : BorderColor, 1);

            Color fore = Enabled ? ForeColor : Theme.MutedLight;
            var textRect = new Rectangle(10, 0, Math.Max(0, Width - 32), Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, textRect, fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            int cx = Width - 16;
            int cy = Height / 2;
            using (var pen = new Pen(fore, 1.6f))
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                e.Graphics.DrawLines(pen, new Point[]
                {
                    new Point(cx - 3, cy - 2),
                    new Point(cx, cy + 1),
                    new Point(cx + 3, cy - 2)
                });
            }
        }
    }

    /// <summary>Тёмный рендерер контекстного меню для RoundedCombo.</summary>
    internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Selected ? Color.White : Theme.Text;
            base.OnRenderItemText(e);
        }
    }

    internal sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.Surface2; } }
        public override Color MenuItemSelected { get { return Theme.AccentDark; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.AccentDark; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.AccentDark; } }
        public override Color MenuItemBorder { get { return Theme.AccentDark; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Surface2; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Surface2; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Surface2; } }
    }

    /// <summary>Чекбокс в стиле тёмной темы.</summary>
    internal sealed class RoundedCheckBox : CheckBox
    {
        private bool _hover;

        public RoundedCheckBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Font = Theme.Ui;
            ForeColor = Theme.Text;
            Cursor = Cursors.Hand;
            BackColor = Theme.Bg;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size t = TextRenderer.MeasureText(Text ?? "", Font);
            return new Size(t.Width + 26, Math.Max(20, t.Height + 4));
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            const int box = 16;
            int top = (Height - box) / 2;
            var rect = new Rectangle(0, top, box, box);

            Color border = !Enabled ? Theme.Border
                : Checked ? Theme.Accent
                : _hover ? Theme.AccentHot
                : Color.FromArgb(92, 92, 102);

            RoundedHelper.FillRounded(e.Graphics, rect, 3,
                Checked ? Theme.Accent : Theme.ConsoleBg, border, 1);

            if (Checked)
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var pen = new Pen(Color.White, 2f))
                {
                    pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    e.Graphics.DrawLines(pen, new Point[]
                    {
                        new Point(rect.Left + 4, rect.Top + 8),
                        new Point(rect.Left + 7, rect.Top + 11),
                        new Point(rect.Left + 12, rect.Top + 5)
                    });
                }
            }

            var textRect = new Rectangle(box + 8, 0, Math.Max(0, Width - box - 8), Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, textRect, Enabled ? ForeColor : Theme.MutedLight,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Радиокнопка в стиле тёмной темы.</summary>
    internal sealed class RoundedRadioButton : RadioButton
    {
        private bool _hover;

        public RoundedRadioButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Font = Theme.Ui;
            ForeColor = Theme.Text;
            Cursor = Cursors.Hand;
            BackColor = Theme.Bg;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size t = TextRenderer.MeasureText(Text ?? "", Font);
            return new Size(t.Width + 26, Math.Max(20, t.Height + 4));
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            const int d = 16;
            int top = (Height - d) / 2;
            var rect = new Rectangle(0, top, d, d);

            Color border = !Enabled ? Theme.Border
                : Checked ? Theme.Accent
                : _hover ? Theme.AccentHot
                : Color.FromArgb(92, 92, 102);

            RoundedHelper.FillRounded(e.Graphics, rect, d / 2,
                Checked ? Theme.Accent : Theme.ConsoleBg, border, 1);

            if (Checked)
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var brush = new SolidBrush(Color.White))
                    e.Graphics.FillEllipse(brush, rect.Left + 5, rect.Top + 5, 6, 6);
            }

            var textRect = new Rectangle(d + 8, 0, Math.Max(0, Width - d - 8), Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, textRect, Enabled ? ForeColor : Theme.MutedLight,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Индикатор прогресса с закруглениями и акцентной заливкой.</summary>
    internal sealed class RoundedProgressBar : Control
    {
        private int _value;
        private int _maximum = 100;

        public int Value
        {
            get { return _value; }
            set
            {
                int v = Math.Max(0, Math.Min(_maximum, value));
                if (v != _value) { _value = v; Invalidate(); }
            }
        }

        public int Maximum
        {
            get { return _maximum; }
            set
            {
                _maximum = Math.Max(1, value);
                if (_value > _maximum) _value = _maximum;
                Invalidate();
            }
        }

        public RoundedProgressBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Size = new Size(200, 10);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            return new Size(proposedSize.Width > 0 ? proposedSize.Width : 200, 10);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            int h = 8;
            var track = new Rectangle(0, Math.Max(0, (Height - h) / 2), Width - 1, h);
            if (track.Width <= 2) return;

            RoundedHelper.FillRounded(e.Graphics, track, h / 2, Theme.Surface2, Theme.Border, 1);

            int w = (int)Math.Round((double)track.Width * _value / _maximum);
            if (w >= h)
            {
                var fill = new Rectangle(track.X, track.Y, Math.Min(w, track.Width), track.Height);
                RoundedHelper.FillRounded(e.Graphics, fill, h / 2, Theme.Accent, Color.Empty, 0);
            }
            else if (w > 0)
            {
                var fill = new Rectangle(track.X, track.Y, Math.Max(w, h / 2), track.Height);
                RoundedHelper.FillRounded(e.Graphics, fill, h / 2, Theme.Accent, Color.Empty, 0);
            }
        }
    }

    /// <summary>DataGridView со сглаженными закругленными углами и рамкой.</summary>
    internal sealed class RoundedDataGrid : DataGridView
    {
        public int CornerRadius { get; set; }

        public RoundedDataGrid()
        {
            CornerRadius = 8;
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var g = e.Graphics;
            var mode = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            try
            {
                var full = new Rectangle(0, 0, Width, Height);
                var inner = new Rectangle(0, 0, Width - 1, Height - 1);
                using (var rounded = RoundedHelper.CreatePath(inner, CornerRadius))
                {
                    using (var mask = new System.Drawing.Drawing2D.GraphicsPath())
                    {
                        mask.FillMode = System.Drawing.Drawing2D.FillMode.Alternate;
                        mask.AddRectangle(full);
                        mask.AddPath(rounded, false);
                        using (var brush = new SolidBrush(Parent != null ? Parent.BackColor : Theme.Bg))
                            g.FillPath(brush, mask);
                    }
                    using (var pen = new Pen(Theme.Border, 1))
                        g.DrawPath(pen, rounded);
                }
            }
            finally { g.SmoothingMode = mode; }
        }
    }

    // ------------------------------------------------------------------
    //  KPI-карточка для отображения статистики
    // ------------------------------------------------------------------
    internal sealed class KpiCard : Control
    {
        private string _value = "0";
        private readonly string _title;
        private readonly Color _accent;
        private bool _hover;

        public KpiCard(string title, Color accent)
        {
            _title = title;
            _accent = accent;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Size = new Size(168, 72);
            Margin = new Padding(0, 0, 8, 0);
            BackColor = Theme.Surface;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        public void SetValue(string value)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(SetValue), value);
                return;
            }
            _value = value ?? "0";
            Invalidate();
        }

        public void SetValue(int value)
        {
            SetValue(value.ToString());
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.Bg);

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            RoundedHelper.FillRounded(e.Graphics, rect, 6,
                _hover ? Theme.Surface2 : Theme.Surface,
                _hover ? Theme.BorderLight : Theme.Border, 1);

            // акцентная полоса слева (как в WpfUiKit)
            var bar = new Rectangle(6, 8, 3, Math.Max(4, Height - 17));
            RoundedHelper.FillRounded(e.Graphics, bar, 2, _accent, Color.Empty, 0);

            var valueRect = new Rectangle(16, 4, Width - 22, Height - 28);
            TextRenderer.DrawText(e.Graphics, _value, Theme.KpiValue, valueRect, Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            var titleRect = new Rectangle(16, Height - 26, Width - 22, 18);
            TextRenderer.DrawText(e.Graphics, _title, Theme.KpiLabel, titleRect, Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }

    // ------------------------------------------------------------------
    //  Модель
    // ------------------------------------------------------------------
    internal enum JobState { Pending, Running, Ok, Warning, Failed, Canceled, Skipped }

    internal sealed class Job
    {
        public string Name;
        public JobState State = JobState.Pending;
        public DateTime Started;
        public DateTime Finished;
        public int Code = -9999;
        public string Note = "";
        public readonly StringBuilder Text = new StringBuilder();
        public DateTime LastOutput = DateTime.Now;
        public bool MarkerSeen;
        public int Attempt;

        public double Seconds
        {
            get
            {
                DateTime end = State == JobState.Running || State == JobState.Pending ? DateTime.Now : Finished;
                return Math.Max(0, (end - Started).TotalSeconds);
            }
        }
    }

    internal sealed class Options
    {
        public string Psexec = "";
        public string Script = "";
        public string DistribDir = "";
        public string LogDir = "";
        public string User = "";
        public string Password = "";
        public string InstallMode = "no";
        public string DeleteMode = "no";
        public bool DesignerAllClients;
        public bool ThinClient = true;
        public bool ThinClientFile;
        public int Threads = 4;
        public int TimeoutMinutes = 30;
        public bool DryRun;
        public string Delivery = "exe";     // exe | share | path
        public string LocalExe = "";        // путь к 1CUpdate.exe для psexec -c

        public string Iod
        {
            get
            {
                return string.Format("DESIGNERALLCLIENTS={0} THINCLIENT={1} THINCLIENTFILE={2}",
                    DesignerAllClients ? 1 : 0, ThinClient ? 1 : 0, ThinClientFile ? 1 : 0);
            }
        }

        public string Prefix(string pc)
        {
            var sb = new StringBuilder();
            sb.Append("\\\\").Append(pc).Append(" -s -accepteula");
            if (!string.IsNullOrEmpty(User))
            {
                sb.Append(" -u \"").Append(User).Append("\"");
                if (!string.IsNullOrEmpty(Password)) sb.Append(" -p \"").Append(Password).Append("\"");
            }
            return sb.ToString();
        }

        // снять блокировку с копии exe, оставшейся от предыдущего запуска
        public string BuildArgsKill(string pc)
        {
            return Prefix(pc) + " taskkill /f /t /im 1CUpdate.exe";
        }

        public string BuildArgs(string pc, string progressPath)
        {
            var sb = new StringBuilder();
            sb.Append(Prefix(pc));

            if (Delivery == "exe")
            {
                // psexec копирует наш exe на ПК и запускает его в режиме --remote-worker
                sb.Append(" -c -f \"").Append(LocalExe).Append("\"");
                sb.Append(" --remote-worker");
                if (!string.IsNullOrEmpty(progressPath))
                    sb.Append(" --progress \"").Append(progressPath).Append("\"");
            }
            else
            {
                sb.Append(" powershell -NoProfile -ExecutionPolicy Bypass -File \"").Append(Script).Append("\"");
            }

            sb.Append(" -dd \"").Append(DistribDir).Append("\"");
            sb.Append(" -dl \"").Append(LogDir).Append("\"");
            sb.Append(" -ip \"").Append(InstallMode).Append("\"");
            sb.Append(" -dp \"").Append(DeleteMode).Append("\"");
            sb.Append(" -iod \"").Append(Iod).Append("\"");
            return sb.ToString();
        }
    }

    // ------------------------------------------------------------------
    //  Чтение журнала с сетевой шары (журнал пишет PowerShell 5.1 — UTF-16)
    // ------------------------------------------------------------------
    internal sealed class LogTail
    {
        private readonly string _path;
        private long _pos;
        private Encoding _enc;
        private Decoder _dec;
        private readonly StringBuilder _pending = new StringBuilder();

        public LogTail(string path) { _path = path; }

        public void Prepare()
        {
            try
            {
                if (File.Exists(_path)) _pos = new FileInfo(_path).Length;
            }
            catch { }
        }

        public List<string> ReadLines()
        {
            var result = new List<string>();
            try
            {
                if (!File.Exists(_path)) return result;
                using (var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    // файл перезаписали (новый запуск) — начинаем заново
                    if (_pos > fs.Length)
                    {
                        _pos = 0;
                        _dec = _enc == null ? null : _enc.GetDecoder();
                        _pending.Length = 0;
                    }

                    if (_enc == null)
                    {
                        if (fs.Length < 2) return result;   // файл ещё пуст — кодировку не определить
                        var bom = new byte[4];
                        int n = fs.Read(bom, 0, 4);
                        int bomLen = 0;
                        if (n >= 2 && bom[0] == 0xFF && bom[1] == 0xFE) { _enc = new UnicodeEncoding(false, true); bomLen = 2; }
                        else if (n >= 2 && bom[0] == 0xFE && bom[1] == 0xFF) { _enc = new UnicodeEncoding(true, true); bomLen = 2; }
                        else if (n >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF) { _enc = new UTF8Encoding(false); bomLen = 3; }
                        else { _enc = new UTF8Encoding(false); }
                        _dec = _enc.GetDecoder();
                        if (_pos < bomLen) _pos = bomLen;
                    }
                    if (_dec == null) _dec = _enc.GetDecoder();
                    if (fs.Length <= _pos) return result;
                    fs.Seek(_pos, SeekOrigin.Begin);
                    long avail = fs.Length - _pos;
                    if (avail > 512 * 1024)
                    {
                        // слишком много — берём только хвост
                        _pos = fs.Length - 512 * 1024;
                        if (_enc is UnicodeEncoding && (_pos % 2) != 0) _pos++;
                        fs.Seek(_pos, SeekOrigin.Begin);
                        avail = fs.Length - _pos;
                    }

                    int size = (int)Math.Min(avail, 512 * 1024);
                    if (_enc is UnicodeEncoding && (size % 2) != 0) size--;
                    if (size <= 0) return result;

                    var buf = new byte[size];
                    int read = fs.Read(buf, 0, size);
                    if (read <= 0) return result;
                    _pos += read;

                    int maxChars = _enc.GetMaxCharCount(read);
                    var chars = new char[maxChars];
                    int count = _dec.GetChars(buf, 0, read, chars, 0);
                    _pending.Append(chars, 0, count);
                }

                string all = _pending.ToString();
                int start = 0;
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] == '\n')
                    {
                        string line = all.Substring(start, i - start).TrimEnd('\r');
                        start = i + 1;
                        if (line.Length > 0) result.Add(line);
                    }
                }
                _pending.Remove(0, start);
                if (_pending.Length > 200000) _pending.Remove(0, _pending.Length - 100000);
            }
            catch { }
            return result;
        }
    }

    // ------------------------------------------------------------------
    //  Раннер: очередь ПК, N потоков, psexec + хвост журнала
    // ------------------------------------------------------------------
    internal sealed class Runner
    {
        private readonly Options _opt;
        private readonly List<Job> _jobs;
        private readonly ConcurrentQueue<Job> _queue = new ConcurrentQueue<Job>();
        private readonly List<Process> _procs = new List<Process>();
        private volatile bool _stop;
        private long _done;
        private readonly object _dupLock = new object();
        private readonly Dictionary<string, DateTime> _recent = new Dictionary<string, DateTime>();

        public event Action<Job> JobStarted;
        public event Action<Job> JobChanged;
        public event Action<Job, string, bool> LogLine;   // job, text, isSystem
        public event Action Finished;

        public Runner(Options opt, List<Job> jobs)
        {
            _opt = opt;
            _jobs = jobs;
        }

        public void Start()
        {

            foreach (Job j in _jobs) _queue.Enqueue(j);
            int threads = Math.Max(1, Math.Min(_opt.Threads, Math.Max(1, _jobs.Count)));
            for (int i = 0; i < threads; i++)
            {
                var t = new Thread(Worker);
                t.IsBackground = true;
                t.Start();
            }

            var mon = new Thread(Monitor);
            mon.IsBackground = true;
            mon.Start();
        }

        public void Stop()
        {
            _stop = true;
            lock (_procs)
            {
                foreach (Process p in _procs)
                {
                    try { if (!p.HasExited) p.Kill(); } catch { }
                }
            }
        }

        private void Worker()
        {
            Job job;
            while (!_stop && _queue.TryDequeue(out job))
            {
                job.Started = DateTime.Now;
                job.State = JobState.Running;
                Raise(JobStarted, job);

                try { Execute(job); }
                catch (Exception ex)
                {
                    job.State = JobState.Failed;
                    job.Note = ex.Message;
                }

                if (job.State == JobState.Running)
                {
                    job.State = JobState.Failed;
                    if (job.Note.Length == 0) job.Note = "неизвестный результат";
                }
                job.Finished = DateTime.Now;
                Raise(JobChanged, job);
                Interlocked.Increment(ref _done);
            }
        }

        private void Monitor()
        {
            while (true)
            {
                Thread.Sleep(200);
                bool anyRunning = false;
                bool anyPending = false;
                foreach (Job j in _jobs)
                {
                    if (j.State == JobState.Running) anyRunning = true;
                    if (j.State == JobState.Pending) anyPending = true;
                }
                if (!anyRunning && (!anyPending || _stop))
                {
                    if (_stop)
                    {
                        foreach (Job j in _jobs)
                        {
                            if (j.State == JobState.Pending)
                            {
                                j.State = JobState.Canceled;
                                j.Note = "не запускалось";
                                Raise(JobChanged, j);
                            }
                        }
                    }
                    var handler = Finished;
                    if (handler != null) handler();
                    return;
                }
            }
        }

        private void Raise(Action<Job> handler, Job job)
        {
            if (handler == null) return;
            try { handler(job); }
            catch { }
        }

        private void Send(Job job, string line, bool system)
        {
            if (line == null) return;
            if (line.Length > 4000) line = line.Substring(0, 4000) + "…";
            if (IsDuplicate(line)) return;   // одна и та же строка приходит из двух каналов
            job.LastOutput = DateTime.Now;
            if (line.StartsWith("== итог:", StringComparison.Ordinal)) job.MarkerSeen = true;
            job.Text.AppendLine(line);
            if (job.Text.Length > 400000) job.Text.Remove(0, 200000);
            var h = LogLine;
            if (h != null) { try { h(job, line, system); } catch { } }
        }

        private void Execute(Job job)
        {
            job.Attempt++;
            LogTail tail = null;
            LogTail progress = null;
            string progressPath = null;
            if (!string.IsNullOrEmpty(_opt.LogDir))
            {
                if (_opt.Delivery == "exe")
                {
                    // свой файл на каждый запуск: не путаемся с остатками прошлых прогонов
                    string runId = Guid.NewGuid().ToString("N").Substring(0, 8);
                    progressPath = Path.Combine(_opt.LogDir, job.Name + "." + runId + ".progress.txt");
                    progress = new LogTail(progressPath);
                    progress.Prepare();
                }
                else
                {
                    // скрипт пишет журнал по имени компьютера, поэтому IP резолвим в имя
                    string logName = job.Name;
                    if (Regex.IsMatch(logName, @"^\d{1,3}(\.\d{1,3}){3}$"))
                    {
                        try { logName = Dns.GetHostEntry(logName).HostName.Split('.')[0]; } catch { }
                    }
                    tail = new LogTail(Path.Combine(_opt.LogDir, logName + ".log"));
                    tail.Prepare();
                }
            }

            string args = _opt.BuildArgs(job.Name, progressPath);
            Send(job, "psexec " + args, true);

            var psi = new ProcessStartInfo();
            psi.FileName = _opt.Psexec;
            psi.Arguments = args;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.RedirectStandardInput = true;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            // в режиме exe вывод отдаёт наш же exe (UTF-8), иначе — консольная кодировка Windows
            Encoding outEnc = _opt.Delivery == "exe"
                ? Encoding.UTF8
                : Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            psi.StandardOutputEncoding = outEnc;
            psi.StandardErrorEncoding = outEnc;

            Process p;
            try
            {
                p = new Process();
                p.StartInfo = psi;
                p.Start();
            }
            catch (Exception ex)
            {
                job.State = JobState.Failed;
                job.Note = ex.Message;
                Send(job, "не удалось запустить psexec: " + ex.Message, true);
                return;
            }

            try { p.StandardInput.Close(); } catch { }
            lock (_procs) _procs.Add(p);

            var tOut = new Thread(delegate () { Pump(p.StandardOutput, job); });
            tOut.IsBackground = true;
            tOut.Start();
            var tErr = new Thread(delegate () { Pump(p.StandardError, job); });
            tErr.IsBackground = true;
            tErr.Start();

            DateTime started = job.Started;
            bool killedByStop = false;
            bool timedOut = false;
            bool forcedByMarker = false;

            while (true)
            {
                Flush(tail, job);
                Flush(progress, job);
                if (p.WaitForExit(600)) break;

                // итог уже есть, а psexec не завершается — не держим поток
                if (job.MarkerSeen && (DateTime.Now - job.LastOutput).TotalSeconds > 25)
                {
                    Send(job, "1C Update: psexec не завершился, но итог получен — завершаю принудительно", true);
                    forcedByMarker = true;
                    try { p.Kill(); } catch { }
                    break;
                }

                if (_stop)
                {
                    try { p.Kill(); } catch { }
                    killedByStop = true;
                    break;
                }
                if ((DateTime.Now - started).TotalMinutes > _opt.TimeoutMinutes)
                {
                    try { p.Kill(); } catch { }
                    timedOut = true;
                    break;
                }
            }

            try { p.WaitForExit(8000); } catch { }
            tOut.Join(2000);
            tErr.Join(2000);
            Flush(tail, job);
            Flush(progress, job);
            // страховка: если ничего не прочитали, покажем файл прогресса целиком
            if (progress != null && job.Text.Length == 0 && !string.IsNullOrEmpty(progressPath))
            {
                var again = new LogTail(progressPath);
                foreach (string line in again.ReadLines()) Send(job, line, false);
            }

            int code = -9999;
            try { code = p.ExitCode; } catch { }
            job.Code = code;

            // убираем копию программы с целевого ПК (через C$)
            if (!killedByStop) CleanupRemoteCopy(job);
            lock (_procs) _procs.Remove(p);
            try { p.Dispose(); } catch { }

            if (killedByStop)
            {
                job.State = JobState.Canceled;
                job.Note = "остановлено";
                return;
            }
            if (timedOut)
            {
                job.State = JobState.Failed;
                job.Note = "таймаут " + _opt.TimeoutMinutes + " мин";
                return;
            }

            string text = job.Text.ToString();
            bool completed = text.IndexOf("Окончание работы скрипта", StringComparison.OrdinalIgnoreCase) >= 0;

            // прошлый запуск оставил живой процесс — файл занят, копирование не прошло
            if (job.Attempt == 1 && code != 0 &&
                text.IndexOf("rror copying", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Send(job, "1C Update: копия программы на ПК занята прошлым процессом — снимаю блокировку и повторяю", true);
                KillLeftovers(job.Name);
                Thread.Sleep(2000);
                job.Text.Clear();
                job.MarkerSeen = false;
                Execute(job);
                return;
            }

            // наш exe на целевом ПК печатает итог в виде: == итог: ok 8.3.22.2239 ==
            Match marker = Regex.Match(text, @"==\s*итог:\s*(skip|ok|fail|unknown)\s*([^=]*?)\s*==");
            string kind = marker.Success ? marker.Groups[1].Value : "";
            string detail = marker.Success ? marker.Groups[2].Value.Trim() : "";

            bool codeOk = code == 0 || forcedByMarker;   // при принудительном завершении код psexec не показателен

            if (kind == "skip" && codeOk)
            {
                job.State = JobState.Skipped;
                job.Note = "версия уже установлена" + (detail.Length > 0 ? " " + detail : "");
            }
            else if (kind == "ok" && codeOk)
            {
                job.State = JobState.Ok;
                job.Note = detail.Length > 0 ? "установлено " + detail : "";
            }
            else if (kind == "fail" || !codeOk)
            {
                job.State = JobState.Failed;
                if (detail.Length > 0) job.Note = detail;
                else if (code != 0) job.Note = "код psexec " + code + (code == 5 ? " (нет доступа)" : code == 53 ? " (ПК недоступен)" : code == 1 ? " (ошибка/отказ)" : "");
                else job.Note = "установка не подтверждена";
            }
            else if (!completed)
            {
                job.State = JobState.Warning;
                job.Note = job.Text.Length == 0
                    ? "нет вывода: psexec ничего не вернул и файл прогресса не прочитан"
                    : "итог не подтверждён: в выводе нет строки «== итог: … ==»";
            }
            else if (HasProblems(text))
            {
                job.State = JobState.Warning;
                job.Note = "в журнале есть сообщения об ошибках";
            }
            else
            {
                job.State = JobState.Ok;
                job.Note = "";
            }

            // при успехе файл прогресса больше не нужен (при ошибке оставляем для разбора)
            if (progressPath != null && (job.State == JobState.Ok || job.State == JobState.Skipped))
            {
                try { File.Delete(progressPath); } catch { }
            }
        }

        private void KillLeftovers(string pc)
        {
            try
            {
                var psi = new ProcessStartInfo();
                psi.FileName = _opt.Psexec;
                psi.Arguments = _opt.BuildArgsKill(pc);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                var p = new Process();
                p.StartInfo = psi;
                p.Start();
                p.WaitForExit(30000);
                try { p.Dispose(); } catch { }
            }
            catch { }
        }
        // --- удаление копии exe на целевом ПК через административную шару ---
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NETRESOURCE
        {
            public int dwScope;
            public int dwType;
            public int dwDisplayType;
            public int dwUsage;
            public string lpLocalName;
            public string lpRemoteName;
            public string lpComment;
            public string lpProvider;
        }

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        private static extern int WNetAddConnection2(ref NETRESOURCE netResource, string password, string username, int flags);

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        private static extern int WNetCancelConnection2(string name, int flags, bool force);

        private void CleanupRemoteCopy(Job job)
        {
            if (_opt.Delivery != "exe" || string.IsNullOrEmpty(_opt.LocalExe)) return;
            string pc = job.Name;

            string fileName = Path.GetFileName(_opt.LocalExe);
            if (string.IsNullOrEmpty(fileName)) return;

            string share = @"\\" + pc + @"\C$";
            string target = share + @"\Windows\" + fileName;
            bool connected = false;

            try
            {
                if (!string.IsNullOrEmpty(_opt.User))
                {
                    var nr = new NETRESOURCE();
                    nr.dwType = 1;                  // RESOURCETYPE_DISK
                    nr.lpRemoteName = share;
                    int rc = WNetAddConnection2(ref nr, _opt.Password, _opt.User, 0);
                    // 0 — подключились, 85 — уже подключено, 1219 — конфликт учёток (всё равно попробуем)
                    connected = (rc == 0 || rc == 85 || rc == 1219);
                }

                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        if (!File.Exists(target)) return;   // копии нет — и хорошо
                        File.Delete(target);
                        Send(job, "1C Update: копия программы удалена с ПК", true);
                        return;
                    }
                    catch
                    {
                        Thread.Sleep(1000);             // процесс мог ещё держать файл
                    }
                }
                Send(job, "1C Update: копию программы удалить не удалось (нет доступа к C$ или файл занят)", true);
            }
            catch { }
            finally
            {
                if (connected)
                {
                    try { WNetCancelConnection2(share, 0, true); } catch { }
                }
            }
        }
        private bool IsDuplicate(string line)
        {
            lock (_dupLock)
            {
                DateTime when;
                if (_recent.TryGetValue(line, out when) && (DateTime.Now - when).TotalSeconds < 5) return true;
                if (_recent.Count > 400) _recent.Clear();
                _recent[line] = DateTime.Now;
                return false;
            }
        }
        private static bool HasProblems(string text)
        {
            string[] bad = new string[]
            {
                "не удалось", "прервана", "не найдена", "не найден", "не установлена",
                "ошибка", "недопустимое", "не допустимое", "не будет произведена", "не будут производиться"
            };
            string lower = text.ToLowerInvariant();
            foreach (string b in bad)
                if (lower.IndexOf(b, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private void Flush(LogTail tail, Job job)
        {
            if (tail == null) return;
            List<string> lines = tail.ReadLines();
            foreach (string line in lines) Send(job, line, false);
        }

        private void Pump(StreamReader reader, Job job)
        {
            try
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Trim().Length == 0) continue;
                    Send(job, line, true);
                }
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------
    //  Диагностика: пишем необработанные исключения в файл рядом с %TEMP%
    // ------------------------------------------------------------------
    internal static class CrashLog
    {
        public static void Write(string text)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + Environment.MachineName + "] " + text + Environment.NewLine;
            var dirs = new List<string>();
            try { dirs.Add(Path.GetTempPath()); } catch { }
            try { dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp")); } catch { }
            foreach (string dir in dirs)
            {
                try
                {
                    File.AppendAllText(Path.Combine(dir, "1CUpdate-crash.log"), line, new UTF8Encoding(false));
                    return;
                }
                catch { }
            }
        }
    }
    // ------------------------------------------------------------------
    //  Настройки
    // ------------------------------------------------------------------
    // ------------------------------------------------------------------
    //  Встроенные ресурсы (скрипт и, при сборке с -PsExec, сам PsExec.exe)
    // ------------------------------------------------------------------
    internal static class Embedded
    {
        private static byte[] _script;
        private static bool _scriptRead;
        private static byte[] _psexec;
        private static bool _psexecRead;

        public static bool HasScript { get { return ScriptBytes != null; } }
        public static bool HasPsexec { get { return PsexecBytes != null; } }

        public static byte[] ScriptBytes
        {
            get
            {
                if (!_scriptRead) { _script = Read("script.ps1"); _scriptRead = true; }
                return _script;
            }
        }

        private static string _psexecName;

        public static byte[] PsexecBytes
        {
            get
            {
                if (!_psexecRead)
                {
                    if (Environment.Is64BitOperatingSystem)
                    {
                        _psexec = Read("psexec64.exe");
                        if (_psexec != null) _psexecName = "PsExec64.exe";
                    }
                    if (_psexec == null)
                    {
                        _psexec = Read("psexec.exe");
                        if (_psexec != null) _psexecName = "PsExec.exe";
                    }
                    _psexecRead = true;
                }
                return _psexec;
            }
        }

        public static string PsexecName
        {
            get
            {
                byte[] unused = PsexecBytes;
                return string.IsNullOrEmpty(_psexecName) ? "PsExec.exe" : _psexecName;
            }
        }

        public static string ScriptVersion
        {
            get
            {
                byte[] data = ScriptBytes;
                if (data == null) return "";
                try
                {
                    string text = Encoding.UTF8.GetString(data);
                    Match m = Regex.Match(text, @"\$ScriptVersion\s*=\s*""([\d\.]+)""");
                    return m.Success ? m.Groups[1].Value : "";
                }
                catch { return ""; }
            }
        }

        private static byte[] Read(string name)
        {
            try
            {
                using (Stream s = typeof(Embedded).Assembly.GetManifestResourceStream(name))
                {
                    if (s == null) return null;
                    using (var ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        return ms.ToArray();
                    }
                }
            }
            catch { return null; }
        }

        public static bool SameContent(string path, byte[] data)
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists || fi.Length != data.Length) return false;
                using (var fs = File.OpenRead(path))
                {
                    var buf = new byte[8192];
                    int offset = 0;
                    while (true)
                    {
                        int read = fs.Read(buf, 0, buf.Length);
                        if (read <= 0) break;
                        for (int i = 0; i < read; i++)
                            if (buf[i] != data[offset + i]) return false;
                        offset += read;
                    }
                }
                return true;
            }
            catch { return false; }
        }
    }

    // ------------------------------------------------------------------
    //  Каталог для распакованных файлов (ищет доступный на запись)
    // ------------------------------------------------------------------
    internal static class TempArea
    {
        private static string _dir;

        public static string Dir
        {
            get
            {
                if (_dir != null) return _dir;
                var candidates = new List<string>();
                try { candidates.Add(Application.StartupPath); } catch { }
                try { candidates.Add(Path.GetTempPath()); } catch { }
                candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "1CUpdate"));
                foreach (string dir in candidates)
                {
                    try
                    {
                        if (string.IsNullOrEmpty(dir)) continue;
                        Directory.CreateDirectory(dir);
                        string probe = Path.Combine(dir, ".1cupdate-probe-" + Guid.NewGuid().ToString("N"));
                        File.WriteAllText(probe, "ok");
                        File.Delete(probe);
                        _dir = dir;
                        return dir;
                    }
                    catch { }
                }
                _dir = Path.GetTempPath();
                return _dir;
            }
        }
    }
    internal static class Paths
    {
        public static string SettingsFile
        {
            get
            {
                string custom = Environment.GetEnvironmentVariable("ONECUPDATE_CONFIG");
                if (!string.IsNullOrEmpty(custom)) return custom;
                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "1CUpdate");
                try
                {
                    Directory.CreateDirectory(appData);
                    return Path.Combine(appData, "settings.ini");
                }
                catch
                {
                    try { return Path.Combine(Application.StartupPath, "1CUpdate.settings.ini"); }
                    catch { return null; }
                }
            }
        }
    }

    // ------------------------------------------------------------------
    //  Выбор компьютеров из AD
    // ------------------------------------------------------------------
    internal sealed class AdDialog : Form
    {
        private TextBox _domain;
        private TextBox _mask;
        private TreeView _tree;
        private CheckedListBox _list;
        private Label _status;
        private CheckBox _recursive;
        private Button _addBtn;
        private readonly ToolTip _tip = new ToolTip();
        private string _domainTitle = "";
        public List<string> Selected = new List<string>();

        public AdDialog(string initialDomain)
        {
            Text = "Добавить компьютеры из Active Directory";
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Ui;
            Size = new Size(760, 640);
            MinimumSize = new Size(620, 480);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;

            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 5;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.Padding = new Padding(12);

            // домен + загрузка дерева
            var top = new TableLayoutPanel();
            top.Dock = DockStyle.Fill;
            top.ColumnCount = 3;
            top.ColumnCount = 4;
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
            var l1 = new Label();
            l1.Text = "Домен:";
            l1.Dock = DockStyle.Fill;
            l1.TextAlign = ContentAlignment.MiddleLeft;
            l1.ForeColor = Theme.Muted;
            _domain = new TextBox();
            _domain.Dock = DockStyle.Fill;
            _domain.BackColor = Theme.Surface;
            _domain.ForeColor = Theme.Text;
            _domain.BorderStyle = BorderStyle.None;
            _domain.Text = initialDomain;
            _domain.KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { LoadTree(); e.SuppressKeyPress = true; } };
            var loadBtn = MakeButton("Загрузить дерево", true, 140);
            loadBtn.Click += delegate { LoadTree(); };
            var refresh = MakeButton("↻", false, 34);
            refresh.Click += delegate { LoadTree(); };
            top.Controls.Add(l1, 0, 0);
            top.Controls.Add(WrapRounded(_domain, 6, new Padding(8, 4, 8, 4)), 1, 0);
            top.Controls.Add(loadBtn, 2, 0);
            top.Controls.Add(refresh, 3, 0);

            // фильтр
            var mid = new TableLayoutPanel();
            mid.Dock = DockStyle.Fill;
            mid.ColumnCount = 4;
            mid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            mid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            mid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            mid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            var l2 = new Label();
            l2.Text = "Имя содержит:";
            l2.Dock = DockStyle.Fill;
            l2.TextAlign = ContentAlignment.MiddleLeft;
            l2.ForeColor = Theme.Muted;
            _mask = new TextBox();
            _mask.Dock = DockStyle.Fill;
            _mask.BackColor = Theme.Surface;
            _mask.ForeColor = Theme.Text;
            _mask.BorderStyle = BorderStyle.None;
            _mask.KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { LoadComputers(); e.SuppressKeyPress = true; } };
            _recursive = new RoundedCheckBox();
            _recursive.Text = "включая вложенные OU";
            _recursive.AutoSize = true;
            _recursive.Checked = true;
            _recursive.ForeColor = Theme.Text;
            _recursive.Margin = new Padding(8, 6, 0, 0);
            _recursive.CheckedChanged += delegate { LoadComputers(); };
            var findBtn = MakeButton("Найти", false, 84);
            findBtn.Click += delegate { LoadComputers(); };
            mid.Controls.Add(l2, 0, 0);
            mid.Controls.Add(WrapRounded(_mask, 6, new Padding(8, 4, 8, 4)), 1, 0);
            mid.Controls.Add(_recursive, 2, 0);
            mid.Controls.Add(findBtn, 3, 0);

            // дерево + список
            var body = new TableLayoutPanel();
            body.Dock = DockStyle.Fill;
            body.ColumnCount = 2;
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));

            _tree = new TreeView();
            _tree.Dock = DockStyle.Fill;
            _tree.BackColor = Theme.Surface;
            _tree.ForeColor = Theme.Text;
            _tree.BorderStyle = BorderStyle.None;
            _tree.HideSelection = false;
            _tree.ShowLines = true;
            _tree.ShowRootLines = true;
            _tree.BeforeExpand += delegate (object s, TreeViewCancelEventArgs e) { FillNode(e.Node); };
            _tree.AfterSelect += delegate { LoadComputers(); };

            _list = new CheckedListBox();
            _list.Dock = DockStyle.Fill;
            _list.BackColor = Theme.Surface;
            _list.ForeColor = Theme.Text;
            _list.BorderStyle = BorderStyle.None;
            _list.CheckOnClick = true;

            var treeHost = WrapRounded(_tree, 8, new Padding(6, 6, 6, 6));
            treeHost.Margin = new Padding(0, 0, 6, 0);
            var listHost = WrapRounded(_list, 8, new Padding(6, 6, 6, 6));

            body.Controls.Add(treeHost, 0, 0);
            body.Controls.Add(listHost, 1, 0);

            _status = new Label();
            _status.Text = "";
            _status.Dock = DockStyle.Fill;
            _status.ForeColor = Theme.Muted;
            _status.Font = Theme.Small;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.AutoSize = false;
            _status.AutoEllipsis = true;
            _status.Margin = new Padding(0);

            var bottom = new TableLayoutPanel();
            bottom.Dock = DockStyle.Fill;
            bottom.ColumnCount = 4;
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));

            var checkAll = MakeButton("Отметить все", false, 110);
            checkAll.Click += delegate
            {
                for (int i = 0; i < _list.Items.Count; i++) _list.SetItemChecked(i, true);
            };
            _addBtn = MakeButton("Добавить выбранные", true, 180);
            _addBtn.Click += delegate
            {
                foreach (object item in _list.CheckedItems) Selected.Add(Convert.ToString(item));
                if (Selected.Count == 0)
                {
                    MessageBox.Show(this, "Ничего не отмечено.", "1C Update", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                DialogResult = DialogResult.OK;
                Close();
            };
            var cancel = MakeButton("Отмена", false, 100);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            bottom.Controls.Add(checkAll, 1, 0);
            bottom.Controls.Add(_addBtn, 2, 0);
            bottom.Controls.Add(cancel, 3, 0);

            root.Controls.Add(top, 0, 0);
            root.Controls.Add(mid, 0, 1);
            root.Controls.Add(body, 0, 2);
            root.Controls.Add(_status, 0, 3);
            root.Controls.Add(bottom, 0, 4);
            Controls.Add(root);

            Tip(_domain, "Домен: можно короткое имя (CONTOSO) или DNS (contoso.local).`nПусто — взять текущий домен.");
            Tip(_tree, "Структура подразделений (OU). Выберите OU — справа появятся компьютеры.");
            Tip(_mask, "Фильтр по имени компьютера. Пусто — все.");
            Tip(_recursive, "Искать компьютеры и во вложенных OU выбранного подразделения.");
            Tip(_list, "Отметьте нужные компьютеры галочками.");
        }

        private void Tip(Control control, string text)
        {
            try { if (control != null) _tip.SetToolTip(control, text); } catch { }
        }

        private static RoundedPanel WrapRounded(Control inner, int radius, Padding padding)
        {
            var host = new RoundedPanel();
            host.Dock = DockStyle.Fill;
            host.BackColor = Theme.Surface;
            host.BorderColor = Theme.Border;
            host.CornerRadius = radius;
            host.Padding = padding;
            host.Margin = new Padding(0);
            inner.Dock = DockStyle.Fill;
            host.Controls.Add(inner);
            return host;
        }

        private static Button MakeButton(string text, bool primary, int width)
        {
            var b = new Button();
            b.Text = text;
            b.Width = width;
            b.Height = 30;
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = primary ? Theme.Accent : Theme.Surface2;
            b.ForeColor = primary ? Color.White : Theme.Text;
            b.Font = primary ? Theme.UiBold : Theme.Ui;
            b.UseVisualStyleBackColor = false;
            b.FlatAppearance.BorderColor = primary ? Theme.Accent : Theme.Border;
            b.FlatAppearance.BorderSize = 1;
            b.Margin = new Padding(4, 0, 0, 0);
            return b;
        }

        // ----- дерево -----
        private void LoadTree()
        {
            _tree.Nodes.Clear();
            _list.Items.Clear();
            _status.Text = "Подключение к домену…";
            Cursor previous = Cursor;
            Cursor = Cursors.WaitCursor;
            try
            {
                string error;
                DirectoryEntry root = OpenRoot(_domain.Text.Trim(), out error);
                if (root == null)
                {
                    _status.Text = "Ошибка: " + Flatten(error, 200);
                    return;
                }
                using (root)
                {
                    root.RefreshCache();   // убедимся, что контроллер домена отвечает
                    string path = root.Path;
                    _domainTitle = string.IsNullOrEmpty(_domain.Text.Trim()) ? path : _domain.Text.Trim();
                    var node = _tree.Nodes.Add(_domainTitle);
                    node.Tag = path;
                    node.Nodes.Add("…");
                    node.Expand();
                    _tree.SelectedNode = node;
                }
                _status.Text = "Дерево загружено. Выберите подразделение слева.";
            }
            catch (Exception ex)
            {
                _status.Text = "Ошибка: " + Flatten(ex.Message, 220);
            }
            finally { Cursor = previous; }
        }

        private void FillNode(TreeNode node)
        {
            if (node == null || node.Tag == null) return;
            if (node.Nodes.Count != 1 || Convert.ToString(node.Nodes[0].Text) != "…") return;

            node.Nodes.Clear();
            try
            {
                string path = Convert.ToString(node.Tag);
                using (var entry = new DirectoryEntry(path))
                using (var searcher = new DirectorySearcher(entry))
                {
                    searcher.Filter = "(|(objectClass=organizationalUnit)(objectClass=container)(objectClass=builtinDomain))";
                    searcher.SearchScope = SearchScope.OneLevel;
                    searcher.PageSize = 200;
                    searcher.PropertiesToLoad.Add("ou");
                    searcher.PropertiesToLoad.Add("cn");
                    foreach (SearchResult res in searcher.FindAll())
                    {
                        string name = null;
                        if (res.Properties["ou"].Count > 0) name = Convert.ToString(res.Properties["ou"][0]);
                        else if (res.Properties["cn"].Count > 0) name = Convert.ToString(res.Properties["cn"][0]);
                        if (string.IsNullOrEmpty(name)) continue;
                        var child = node.Nodes.Add(name);
                        child.Tag = res.Path;
                        child.Nodes.Add("…");
                    }
                }
            }
            catch (Exception ex)
            {
                _status.Text = "Не удалось прочитать структуру: " + Flatten(ex.Message, 160);
            }
        }

        // ----- компьютеры выбранного подразделения -----
        private void LoadComputers()
        {
            _list.Items.Clear();
            TreeNode node = _tree.SelectedNode;
            if (node == null || node.Tag == null)
            {
                _status.Text = "Сначала выберите подразделение в дереве";
                return;
            }

            Cursor previous = Cursor;
            Cursor = Cursors.WaitCursor;
            try
            {
                string mask = _mask.Text.Trim();
                string like;
                if (mask.Length == 0)
                {
                    like = "*";
                }
                else
                {
                    var escaped = new StringBuilder();
                    foreach (char c in mask)
                    {
                        if (c == '(' || c == ')' || c == '*' || c == '\\') escaped.Append('\\');
                        escaped.Append(c);
                    }
                    like = "*" + escaped + "*";
                }
                string filter = "(&(objectCategory=computer)(name=" + like + "))";

                int count = 0;
                using (var entry = new DirectoryEntry(Convert.ToString(node.Tag)))
                using (var searcher = new DirectorySearcher(entry))
                {
                    searcher.Filter = filter;
                    searcher.SearchScope = _recursive.Checked ? SearchScope.Subtree : SearchScope.OneLevel;
                    searcher.PropertiesToLoad.Add("name");
                    searcher.PageSize = 500;
                    searcher.SizeLimit = 3000;
                    searcher.Sort = new SortOption("name", SortDirection.Ascending);
                    foreach (SearchResult res in searcher.FindAll())
                    {
                        if (res.Properties["name"].Count == 0) continue;
                        _list.Items.Add(Convert.ToString(res.Properties["name"][0]), false);
                        count++;
                        if (count >= 3000) break;
                    }
                }
                _status.Text = "Компьютеров в «" + node.Text + "»: " + count +
                               (_recursive.Checked ? " (с вложенными)" : " (только этот уровень)");
            }
            catch (Exception ex)
            {
                _status.Text = "Ошибка: " + Flatten(ex.Message, 220);
            }
            finally { Cursor = previous; }
        }

        // ----- подключение к домену -----
        private static DirectoryEntry OpenRoot(string domain, out string error)
        {
            error = null;
            string currentDns = null;
            string currentNetbios = null;
            try
            {
                var d = System.DirectoryServices.ActiveDirectory.Domain.GetCurrentDomain();
                currentDns = d.Name;
                using (var entry = d.GetDirectoryEntry())
                {
                    currentNetbios = Convert.ToString(entry.Properties["dc"].Value);
                }
            }
            catch { }

            bool isCurrent = string.IsNullOrEmpty(domain)
                || SameDomain(domain, currentDns)
                || SameDomain(domain, currentNetbios)
                || SameDomain(domain, Environment.UserDomainName);

            if (isCurrent && currentDns != null)
            {
                try
                {
                    var d = System.DirectoryServices.ActiveDirectory.Domain.GetCurrentDomain();
                    return d.GetDirectoryEntry();
                }
                catch (Exception ex) { error = ex.Message; }
            }

            try { return new DirectoryEntry(domain.Length == 0 ? "LDAP://RootDSE" : "LDAP://" + domain); }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        private static bool SameDomain(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            a = a.Trim(); b = b.Trim();
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
            int dotA = a.IndexOf('.'), dotB = b.IndexOf('.');
            if (dotA > 0) a = a.Substring(0, dotA);
            if (dotB > 0) b = b.Substring(0, dotB);
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static string Flatten(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            while (s.IndexOf("  ", StringComparison.Ordinal) >= 0) s = s.Replace("  ", " ");
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }
    // ------------------------------------------------------------------
    //  Главное окно
    // ------------------------------------------------------------------
    internal sealed class MainForm : Form
    {
        private TextBox _computers;
        private Label _countLabel;
        private TextBox _distrib, _logs, _script, _psexec, _user, _password;
        private RadioButton _ipNo, _ipLast, _ipVer;
        private RadioButton _dpNo, _dpAel, _dpAll, _dpVer;
        private RoundedCombo _verList, _delVerList;
        private CheckBox _cbDesigner, _cbThin, _cbThinFile, _cbDryRun, _useEmbeddedPsexec;
        private RoundedCombo _delivery;
        private NumericUpDown _threads;
        private Button _runBtn, _stopBtn, _checkBtn;
        private RoundedProgressBar _progress;
        private Label _progressLabel;
        private DataGridView _grid;
        private RichTextBox _log;
        private Label _status;
        private System.Windows.Forms.Timer _flushTimer;
        private readonly ToolTip _tip = new ToolTip();
        private bool _componentSync;
        private bool _savedThin = true;
        private bool _savedThinFile;
        
        // KPI-карточки
        private KpiCard _kpiTotal, _kpiSuccess, _kpiFailed, _kpiRunning;

        private readonly ConcurrentQueue<KeyValuePair<Color, string>> _queue = new ConcurrentQueue<KeyValuePair<Color, string>>();
        private readonly Dictionary<string, int> _rows = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private Runner _runner;
        private List<Job> _jobs = new List<Job>();
        private DateTime _startedAll;
        private bool _running;
        private bool _autoScroll = true;

        public MainForm()
        {
            Text = "1C Update — обновление платформы 1С на компьютерах";
            try
            {
                Version ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                Text = "1C Update " + ver.Major + "." + ver.Minor + " — обновление платформы 1С на компьютерах";
            }
            catch { }
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Ui;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1180, 880);
            MinimumSize = new Size(1000, 740);
            DoubleBuffered = true;
            KeyPreview = true;

            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            BuildUi();
            LoadSettings();

            _flushTimer = new System.Windows.Forms.Timer();
            _flushTimer.Interval = 120;
            _flushTimer.Tick += delegate { FlushLog(); UpdateTimes(); };
            _flushTimer.Start();

            FormClosing += OnFormClosing;
            KeyDown += OnKeyDown;
        }

        // ==============================================================
        //  Интерфейс
        // ==============================================================
        private void BuildUi()
        {
            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.BackColor = Theme.Bg;
            root.ColumnCount = 1;
            root.RowCount = 5;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 372));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildMain(), 0, 1);
            root.Controls.Add(BuildRunBar(), 0, 2);
            root.Controls.Add(BuildResults(), 0, 3);
            root.Controls.Add(BuildStatus(), 0, 4);
            Controls.Add(root);
        }

        private Control BuildStatus()
        {
            var p = new Panel();
            p.Dock = DockStyle.Fill;
            p.BackColor = Theme.Surface;

            _status = new Label();
            _status.Text = "Готов";
            _status.Dock = DockStyle.Fill;
            _status.ForeColor = Theme.Muted;
            _status.Font = Theme.Small;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.Padding = new Padding(14, 0, 0, 0);

            p.Controls.Add(_status);
            return p;
        }

        private Control BuildHeader()
        {
            var p = new TableLayoutPanel();
            p.Dock = DockStyle.Fill;
            p.ColumnCount = 2;
            p.RowCount = 1;
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            p.Padding = new Padding(14, 10, 14, 0);

            var left = new FlowLayoutPanel();
            left.Dock = DockStyle.Fill;
            left.FlowDirection = FlowDirection.TopDown;
            left.WrapContents = false;
            left.AutoSize = false;
            left.Margin = new Padding(0);
            left.Padding = new Padding(0);

            var title = new Label();
            title.Text = "1C Update";
            title.AutoSize = true;
            title.Height = 28;
            title.Font = Theme.Title;
            title.ForeColor = Theme.Text;
            title.Margin = new Padding(0, 0, 0, 0);
            title.TextAlign = ContentAlignment.MiddleLeft;

            var sub = new Label();
            sub.Text = "Список компьютеров → параметры 1С8InstallAndUninstall.ps1 → psexec → прогресс по каждому ПК";
            sub.AutoSize = true;
            sub.Height = 20;
            sub.Font = Theme.Ui;
            sub.ForeColor = Theme.TextSecondary;
            sub.Margin = new Padding(0, 0, 0, 0);
            sub.TextAlign = ContentAlignment.MiddleLeft;

            left.Controls.Add(title);
            left.Controls.Add(sub);

            var right = new FlowLayoutPanel();
            right.Dock = DockStyle.Fill;
            right.FlowDirection = FlowDirection.LeftToRight;
            right.WrapContents = false;
            right.AutoSize = true;
            right.Padding = new Padding(0, 10, 0, 0);

            var lbl = new Label();
            lbl.Text = "Потоков:";
            lbl.AutoSize = true;
            lbl.ForeColor = Theme.Muted;
            lbl.Margin = new Padding(0, 8, 6, 0);

            _threads = new NumericUpDown();
            _threads.Minimum = 1;
            _threads.Maximum = 64;
            _threads.Value = 4;
            _threads.Width = 52;
            _threads.ForeColor = Theme.Text;
            _threads.BorderStyle = BorderStyle.None;

            var threadsHost = MakeInputHost(_threads, Theme.Surface, 6);
            threadsHost.Size = new Size(66, 30);
            threadsHost.Margin = new Padding(0, 4, 12, 0);

            _checkBtn = MakeButton("Проверить пути", false, 150);
            _checkBtn.Margin = new Padding(0, 4, 0, 0);
            _checkBtn.Click += delegate { CheckPaths(true); };

            right.Controls.Add(lbl);
            right.Controls.Add(threadsHost);
            right.Controls.Add(_checkBtn);

            p.Controls.Add(left, 0, 0);
            p.Controls.Add(right, 1, 0);
            return p;
        }

        private Control BuildMain()
        {
            var p = new TableLayoutPanel();
            p.Dock = DockStyle.Fill;
            p.ColumnCount = 2;
            p.RowCount = 1;
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            p.Padding = new Padding(14, 4, 14, 4);
            p.Controls.Add(BuildComputers(), 0, 0);
            p.Controls.Add(BuildOptions(), 1, 0);
            return p;
        }

        private Control BuildComputers()
        {
            var t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill;
            t.ColumnCount = 1;
            t.RowCount = 3;
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            t.Margin = new Padding(0, 0, 8, 0);

            var lbl = new Label();
            lbl.Text = "Компьютеры (имя или IP) — по одному в строке";
            lbl.Dock = DockStyle.Fill;
            lbl.ForeColor = Theme.Muted;
            lbl.Font = Theme.UiBold;

            var host = new RoundedPanel();
            host.Dock = DockStyle.Fill;
            host.BackColor = Theme.Surface;
            host.BorderColor = Theme.Border;
            host.CornerRadius = 8;
            host.Padding = new Padding(8, 6, 8, 6);

            _computers = new TextBox();
            _computers.Dock = DockStyle.Fill;
            _computers.Multiline = true;
            _computers.ScrollBars = ScrollBars.Vertical;
            _computers.BorderStyle = BorderStyle.None;
            _computers.BackColor = Theme.Surface;
            _computers.ForeColor = Theme.Text;
            _computers.Font = Theme.Mono;
            _computers.AcceptsReturn = true;
            _computers.TextChanged += delegate { UpdateCount(); };
            host.Controls.Add(_computers);

            var bar = new TableLayoutPanel();
            bar.Dock = DockStyle.Fill;
            bar.ColumnCount = 4;
            bar.RowCount = 1;
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var bFile = MakeButton("Из файла…", false, 100);
            bFile.Margin = new Padding(0, 6, 6, 0);
            bFile.Click += delegate { LoadFromFile(); };

            var bAd = MakeButton("Из AD…", false, 78);
            bAd.Margin = new Padding(0, 6, 6, 0);
            bAd.Click += delegate { LoadFromAd(); };

            var bClear = MakeButton("Очистить", false, 86);
            bClear.Margin = new Padding(0, 6, 6, 0);
            bClear.Click += delegate { _computers.Text = ""; };

            _countLabel = new Label();
            _countLabel.Text = "ПК: 0";
            _countLabel.Dock = DockStyle.Fill;
            _countLabel.ForeColor = Theme.Muted;
            _countLabel.TextAlign = ContentAlignment.MiddleRight;

            bar.Controls.Add(bFile, 0, 0);
            bar.Controls.Add(bAd, 1, 0);
            bar.Controls.Add(bClear, 2, 0);
            bar.Controls.Add(_countLabel, 3, 0);

            t.Controls.Add(lbl, 0, 0);
            t.Controls.Add(host, 0, 1);
            t.Controls.Add(bar, 0, 2);
            return t;
        }

        private Control BuildOptions()
        {
            var t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill;
            t.ColumnCount = 1;
            t.RowCount = 2;
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.Margin = new Padding(8, 0, 0, 0);

            var lbl = new Label();
            lbl.Text = "Параметры 1C8InstallAndUninstall.ps1";
            lbl.Dock = DockStyle.Fill;
            lbl.ForeColor = Theme.Muted;
            lbl.Font = Theme.UiBold;

            var body = new TableLayoutPanel();
            body.Dock = DockStyle.Fill;
            body.ColumnCount = 3;
            body.RowCount = 10;
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
            for (int i = 0; i < 9; i++) body.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            AddPathRow(body, 0, "Дистрибутивы -dd", out _distrib, "");
            AddPathRow(body, 1, "Логи -dl", out _logs, "");
            AddComboRow(body, 2, "Скрипт", out _delivery, new string[] { "копировать exe на ПК (psexec -c)", "выложить .ps1 на шару", "внешний .ps1 по пути" });
            AddPathRow(body, 3, "Путь .ps1", out _script, "");
            AddPathRowEmbedded(body, 4, "PsExec.exe", out _psexec, out _useEmbeddedPsexec, FindPsexec(), Embedded.HasPsexec);

            AddTextRow(body, 5, "Пользователь -u", out _user, "", false);

            var lblPass = MakeLabel("Пароль");
            var passHost = new RoundedPanel();
            passHost.Dock = DockStyle.Fill;
            passHost.BackColor = Theme.Surface;
            passHost.BorderColor = Theme.Border;
            passHost.CornerRadius = 6;
            passHost.Padding = new Padding(8, 4, 8, 4);
            passHost.Margin = new Padding(0, 4, 4, 4);
            _password = new TextBox();
            _password.Dock = DockStyle.Fill;
            _password.UseSystemPasswordChar = true;
            _password.BorderStyle = BorderStyle.None;
            _password.BackColor = Theme.Surface;
            _password.ForeColor = Theme.Text;
            passHost.Controls.Add(_password);
            body.Controls.Add(lblPass, 0, 6);
            body.Controls.Add(passHost, 1, 6);
            body.SetColumnSpan(passHost, 2);

            // установка
            var ipRow = new FlowLayoutPanel();
            ipRow.Dock = DockStyle.Fill;
            ipRow.WrapContents = false;
            ipRow.Margin = new Padding(0, 4, 0, 0);
            _ipNo = MakeRadio("не ставить");
            _ipLast = MakeRadio("последнюю");
            _ipVer = MakeRadio("версия");
            _verList = MakeCombo(150);
            _ipNo.CheckedChanged += delegate { SyncModeControls(); };
            _ipLast.CheckedChanged += delegate { SyncModeControls(); };
            _ipVer.CheckedChanged += delegate { SyncModeControls(); };
            ipRow.Controls.Add(_ipNo);
            ipRow.Controls.Add(_ipLast);
            ipRow.Controls.Add(_ipVer);
            _verList.Size = new Size(150, 27);
            _verList.Margin = new Padding(0, 2, 0, 0);
            ipRow.Controls.Add(_verList);
            body.Controls.Add(MakeLabel("Установка -ip"), 0, 7);
            body.Controls.Add(ipRow, 1, 7);
            body.SetColumnSpan(ipRow, 2);

            // удаление
            var dpRow = new FlowLayoutPanel();
            dpRow.Dock = DockStyle.Fill;
            dpRow.WrapContents = false;
            dpRow.Margin = new Padding(0, 4, 0, 0);
            _dpNo = MakeRadio("не удалять");
            _dpAel = MakeRadio("кроме старшей");
            _dpAll = MakeRadio("всё");
            _dpVer = MakeRadio("версия");
            _delVerList = MakeCombo(140);
            _dpNo.CheckedChanged += delegate { SyncModeControls(); };
            _dpAel.CheckedChanged += delegate { SyncModeControls(); };
            _dpAll.CheckedChanged += delegate { SyncModeControls(); };
            _dpVer.CheckedChanged += delegate { SyncModeControls(); };
            dpRow.Controls.Add(_dpNo);
            dpRow.Controls.Add(_dpAel);
            dpRow.Controls.Add(_dpAll);
            dpRow.Controls.Add(_dpVer);
            _delVerList.Size = new Size(140, 27);
            _delVerList.Margin = new Padding(0, 2, 0, 0);
            dpRow.Controls.Add(_delVerList);
            body.Controls.Add(MakeLabel("Удаление -dp"), 0, 8);
            body.Controls.Add(dpRow, 1, 8);
            body.SetColumnSpan(dpRow, 2);

            // компоненты
            var comp = new FlowLayoutPanel();
            comp.Dock = DockStyle.Fill;
            comp.WrapContents = false;
            comp.Margin = new Padding(0, 6, 0, 0);
            _cbDesigner = MakeCheck("Конфигуратор + все клиенты");
            _cbThin = MakeCheck("Тонкий клиент");
            _cbThinFile = MakeCheck("Тонкий клиент (файловый)");
            _cbThin.Checked = true;
            _cbDesigner.CheckedChanged += delegate { SyncComponents(); };
            Tip(_cbDesigner, "DESIGNERALLCLIENTS=1 — конфигуратор (в англ. версии Designer) и ВСЕ клиенты.\nВключает тонкий клиент и файловый — они станут недоступны для правки.\nНужно только разработчикам и администраторам 1С.");
            Tip(_cbThin, "THINCLIENT=1 — тонкий клиент. Обычно это всё, что нужно на рабочих местах.");
            Tip(_cbThinFile, "THINCLIENTFILE=1 — тонкий клиент для работы с файловыми базами.");
            Tip(_delivery, "Как скрипт попадёт на компьютеры:\n• копировать exe на ПК — psexec сам копирует программу (скрипт внутри неё),\n• выложить .ps1 на шару — программа запишет скрипт в каталог и запустит оттуда,\n• внешний .ps1 — использовать ваш файл.");
            comp.Controls.Add(_cbDesigner);
            comp.Controls.Add(_cbThin);
            comp.Controls.Add(_cbThinFile);
            body.Controls.Add(MakeLabel("Компоненты -iod"), 0, 9);
            body.Controls.Add(comp, 1, 9);
            body.SetColumnSpan(comp, 2);

            Tip(_distrib, "Каталог с дистрибутивами 1С: внутри папки вида 8.3.27.2342-64 (с MSI и setup.ini).`nПример: \\\\server\\share\\distr\\1c");
            Tip(_logs, "Куда каждый ПК пишет свой журнал <ИМЯ_ПК>.log.`nКаталог создаётся автоматически, если его нет.`nПример: \\\\server\\share\\distr\\1c\\log");
            Tip(_script, "Файл 1C8InstallAndUninstall.ps1 на шаре.`n• «выложить .ps1 на шару» — программа запишет его сюда;`n• «внешний .ps1» — отсюда запускается;`n• «копировать exe» — путь не используется.");
            Tip(_psexec, "PsExec.exe из PsTools.`nПо умолчанию встроен в программу — путь не нужен.`nСнимите галочку «встроенный», чтобы взять свой файл.");
            Tip(_user, "Учётная запись для подключения psexec.`nДоменная: DOMAIN\\user.  Локальный админ ПК: .\\Administrator");
            Tip(_password, "Пароль этой учётной записи.`nВводится один раз на запуск и нигде не сохраняется.");
            Tip(_threads, "Сколько компьютеров обрабатывать одновременно (1–64).");
            Tip(_runBtn, "Запустить обновление (F5)");
            Tip(_stopBtn, "Остановить выполнение (F6)");
            Tip(_checkBtn, "Проверить каталоги, скрипт и PsExec; заодно найти версии в каталоге дистрибутивов");
            Tip(_ipNo, "Ничего не устанавливать (передаётся -ip no)");
            Tip(_ipLast, "Установить старшую версию из каталога дистрибутивов (передаётся -ip last)");
            Tip(_ipVer, "Установить конкретную версию — выберите её в списке справа");
            Tip(_dpNo, "Ничего не удалять (передаётся -dp no)");
            Tip(_dpAel, "Удалить все платформы, кроме старшей (передаётся -dp ael)");
            Tip(_dpAll, "Удалить все найденные платформы 1С на ПК (передаётся -dp all) — осторожно");
            Tip(_dpVer, "Удалить конкретную версию — выберите её в списке справа");

            _ipLast.Checked = true;
            _dpNo.Checked = true;
            _delivery.SelectedIndexChanged += delegate { UpdateEmbeddedState(); };
            UpdateEmbeddedState();

            t.Controls.Add(lbl, 0, 0);
            t.Controls.Add(body, 0, 1);
            return t;
        }

        private void AddPathRow(TableLayoutPanel body, int row, string caption, out TextBox box, string value)
        {
            body.Controls.Add(MakeLabel(caption), 0, row);
            box = MakePathCell(body, row, null, value);
        }

        private void AddPathRowEmbedded(TableLayoutPanel body, int row, string caption, out TextBox box, out CheckBox check, string value, bool hasResource)
        {
            body.Controls.Add(MakeLabel(caption), 0, row);
            var chk = new RoundedCheckBox();
            chk.Text = "встроенный";
            chk.AutoSize = true;
            chk.ForeColor = hasResource ? Theme.Text : Theme.Muted;
            chk.Enabled = hasResource;
            chk.Checked = hasResource;
            chk.Margin = new Padding(0, 6, 8, 0);
            chk.CheckedChanged += delegate { UpdateEmbeddedState(); };
            box = MakePathCell(body, row, chk, value);
            check = chk;
        }

        private TextBox MakePathCell(TableLayoutPanel body, int row, CheckBox chk, string value)
        {
            var cell = new TableLayoutPanel();
            cell.Dock = DockStyle.Fill;
            cell.ColumnCount = chk == null ? 2 : 3;
            cell.RowCount = 1;
            if (chk != null) cell.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            cell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            cell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
            cell.Margin = new Padding(0);

            var host = new RoundedPanel();
            host.Dock = DockStyle.Fill;
            host.BackColor = Theme.Surface;
            host.BorderColor = Theme.Border;
            host.CornerRadius = 6;
            host.Padding = new Padding(8, 4, 8, 4);
            host.Margin = new Padding(0, 4, 4, 4);

            var tb = new TextBox();
            tb.Dock = DockStyle.Fill;
            tb.BorderStyle = BorderStyle.None;
            tb.BackColor = Theme.Surface;
            tb.ForeColor = Theme.Text;
            tb.Text = value;
            host.Controls.Add(tb);

            var btn = MakeButton("…", false, 34);
            btn.Margin = new Padding(0, 3, 0, 3);
            btn.Click += delegate
            {
                string picked = PickPath(tb == _script || tb == _psexec);
                if (!string.IsNullOrEmpty(picked)) tb.Text = picked;
            };

            int col = 0;
            if (chk != null) { cell.Controls.Add(chk, col, 0); col++; }
            cell.Controls.Add(host, col, 0);
            cell.Controls.Add(btn, col + 1, 0);
            body.Controls.Add(cell, 1, row);
            body.SetColumnSpan(cell, 2);
            return tb;
        }

        private void AddComboRow(TableLayoutPanel body, int row, string caption, out RoundedCombo combo, string[] items)
        {
            body.Controls.Add(MakeLabel(caption), 0, row);
            var cb = new RoundedCombo();
            cb.Dock = DockStyle.Fill;
            cb.Margin = new Padding(0, 4, 0, 4);
            cb.BackColor = Theme.Surface2;
            foreach (string item in items) cb.Items.Add(item);
            if (cb.Items.Count > 0) cb.SelectedIndex = 0;

            body.Controls.Add(cb, 1, row);
            body.SetColumnSpan(cb, 2);
            combo = cb;
        }

        private string DeliveryMode
        {
            get
            {
                if (_delivery == null) return "exe";
                if (_delivery.SelectedIndex == 1) return "share";
                if (_delivery.SelectedIndex == 2) return "path";
                return "exe";
            }
        }
        // «Конфигуратор + все клиенты» — это полный набор, остальные галочки фиксируются включёнными
        private void SyncComponents()
        {
            if (_componentSync) return;
            _componentSync = true;
            try
            {
                if (_cbDesigner.Checked)
                {
                    _savedThin = _cbThin.Checked;
                    _savedThinFile = _cbThinFile.Checked;
                    _cbThin.Checked = true;
                    _cbThinFile.Checked = true;
                    _cbThin.Enabled = false;
                    _cbThinFile.Enabled = false;
                }
                else
                {
                    _cbThin.Enabled = true;
                    _cbThinFile.Enabled = true;
                    _cbThin.Checked = _savedThin;
                    _cbThinFile.Checked = _savedThinFile;
                }
            }
            finally { _componentSync = false; }
        }
        private void Tip(Control control, string text)
        {
            try { if (control != null) _tip.SetToolTip(control, text); } catch { }
        }
        private void UpdateEmbeddedState()
        {
            if (_psexec != null) _psexec.Enabled = !(_useEmbeddedPsexec != null && _useEmbeddedPsexec.Checked);
            if (_script != null)
            {
                bool needPath = DeliveryMode != "exe";
                _script.Enabled = needPath;
                _script.BackColor = needPath ? Theme.Surface : Theme.Surface2;
            }
        }

        private void AddTextRow(TableLayoutPanel body, int row, string caption, out TextBox box, string value, bool password)
        {
            body.Controls.Add(MakeLabel(caption), 0, row);
            var host = new RoundedPanel();
            host.Dock = DockStyle.Fill;
            host.BackColor = Theme.Surface;
            host.BorderColor = Theme.Border;
            host.CornerRadius = 6;
            host.Padding = new Padding(8, 4, 8, 4);
            host.Margin = new Padding(0, 4, 4, 4);
            var tb = new TextBox();
            tb.Dock = DockStyle.Fill;
            tb.BorderStyle = BorderStyle.None;
            tb.BackColor = Theme.Surface;
            tb.ForeColor = Theme.Text;
            tb.Text = value;
            tb.UseSystemPasswordChar = password;
            host.Controls.Add(tb);
            box = tb;
            body.Controls.Add(host, 1, row);
            body.SetColumnSpan(host, 2);
        }

        private Control BuildRunBar()
        {
            var p = new TableLayoutPanel();
            p.Dock = DockStyle.Fill;
            p.ColumnCount = 2;
            p.RowCount = 1;
            p.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
            p.Padding = new Padding(14, 2, 14, 6);

            var flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.WrapContents = false;
            flow.FlowDirection = FlowDirection.LeftToRight;

            _runBtn = MakeButton("▶  Выполнить", true, 160);
            _runBtn.Margin = new Padding(0, 0, 8, 0);
            _runBtn.Click += delegate { StartRun(); };

            _stopBtn = MakeButton("■  Стоп", false, 110);
            _stopBtn.Margin = new Padding(0, 0, 8, 0);
            _stopBtn.Enabled = false;
            _stopBtn.Click += delegate { StopRun(); };
            Tip(_runBtn, "Запустить обновление (F5)");
            Tip(_stopBtn, "Остановить выполнение (F6)");

            var clearLog = MakeButton("Очистить вывод", false, 140);
            clearLog.Margin = new Padding(0, 0, 8, 0);
            clearLog.Click += delegate { _log.Clear(); };

            var report = MakeButton("Сохранить отчёт…", false, 160);
            report.Margin = new Padding(0, 0, 8, 0);
            report.Click += delegate { SaveReport(); };

            _cbDryRun = new RoundedCheckBox();
            _cbDryRun.Text = "только показать команды";
            _cbDryRun.AutoSize = true;
            _cbDryRun.ForeColor = Theme.Muted;
            _cbDryRun.Margin = new Padding(0, 6, 0, 0);

            flow.Controls.Add(_runBtn);
            flow.Controls.Add(_stopBtn);
            flow.Controls.Add(clearLog);
            flow.Controls.Add(report);
            flow.Controls.Add(_cbDryRun);

            var pr = new TableLayoutPanel();
            pr.Dock = DockStyle.Fill;
            pr.ColumnCount = 2;
            pr.RowCount = 1;
            pr.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            pr.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pr.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            pr.Padding = new Padding(0, 10, 0, 0);

            _progress = new RoundedProgressBar();
            _progress.Dock = DockStyle.Fill;
            _progress.Height = 10;
            _progress.Margin = new Padding(0, 6, 8, 0);

            _progressLabel = new Label();
            _progressLabel.Dock = DockStyle.Fill;
            _progressLabel.Text = "";
            _progressLabel.ForeColor = Theme.Muted;
            _progressLabel.TextAlign = ContentAlignment.MiddleRight;

            pr.Controls.Add(_progress, 0, 0);
            pr.Controls.Add(_progressLabel, 1, 0);

            p.Controls.Add(flow, 0, 0);
            p.Controls.Add(pr, 1, 0);
            return p;
        }

        private Control BuildResults()
        {
            var container = new TableLayoutPanel();
            container.Dock = DockStyle.Fill;
            container.ColumnCount = 1;
            container.RowCount = 2;
            container.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
            container.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            container.Padding = new Padding(14, 8, 14, 0);
            
            // KPI панель
            var kpiPanel = new Panel();
            kpiPanel.Dock = DockStyle.Fill;
            kpiPanel.BackColor = Theme.Bg;
            kpiPanel.Padding = new Padding(0, 0, 0, 10);
            
            var kpiFlow = new FlowLayoutPanel();
            kpiFlow.Dock = DockStyle.Fill;
            kpiFlow.FlowDirection = FlowDirection.LeftToRight;
            kpiFlow.WrapContents = false;
            
            _kpiTotal = new KpiCard("Всего ПК", Theme.Info);
            _kpiSuccess = new KpiCard("Успешно", Theme.Ok);
            _kpiFailed = new KpiCard("Ошибки", Theme.Err);
            _kpiRunning = new KpiCard("В процессе", Theme.Accent);
            
            kpiFlow.Controls.Add(_kpiTotal);
            kpiFlow.Controls.Add(_kpiSuccess);
            kpiFlow.Controls.Add(_kpiFailed);
            kpiFlow.Controls.Add(_kpiRunning);
            
            kpiPanel.Controls.Add(kpiFlow);
            container.Controls.Add(kpiPanel, 0, 0);
            
            var split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Horizontal;
            split.SplitterWidth = 6;
            split.BackColor = Theme.Bg;
            split.Panel1.BackColor = Theme.Bg;
            split.Panel2.BackColor = Theme.Bg;
            split.Panel1.Padding = new Padding(0, 0, 0, 0);
            split.Panel2.Padding = new Padding(0, 0, 0, 8);

            _grid = new RoundedDataGrid();
            _grid.Dock = DockStyle.Fill;
            _grid.BackgroundColor = Theme.Surface;
            _grid.BorderStyle = BorderStyle.None;
            _grid.GridColor = Color.FromArgb(52, 52, 58);
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.RowHeadersVisible = false;
            _grid.ReadOnly = true;
            _grid.MultiSelect = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.EnableHeadersVisualStyles = false;
            _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            _grid.ColumnHeadersHeight = 30;
            _grid.RowTemplate.Height = 26;
            _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;

            var hdr = new DataGridViewCellStyle();
            hdr.BackColor = Theme.Surface2;
            hdr.ForeColor = Theme.Muted;
            hdr.SelectionBackColor = Theme.Surface2;
            hdr.SelectionForeColor = Theme.Muted;
            hdr.Font = Theme.UiBold;
            hdr.Padding = new Padding(6, 0, 0, 0);
            _grid.ColumnHeadersDefaultCellStyle = hdr;

            var cell = new DataGridViewCellStyle();
            cell.BackColor = Theme.Surface;
            cell.ForeColor = Theme.Text;
            cell.SelectionBackColor = Color.FromArgb(0, 88, 156);
            cell.SelectionForeColor = Color.White;
            cell.Padding = new Padding(6, 0, 0, 0);
            _grid.DefaultCellStyle = cell;

            AddColumn("ПК", 150, false);
            AddColumn("Статус", 150, false);
            AddColumn("Длительность", 100, true);
            AddColumn("Код", 60, true);
            AddColumn("Примечание", 380, false);

            _grid.CellDoubleClick += delegate (object s, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0) return;
                string pc = Convert.ToString(_grid.Rows[e.RowIndex].Cells[0].Value);
                if (string.IsNullOrEmpty(pc)) return;
                string path = Path.Combine(_logs.Text.Trim(), pc + ".log");
                try
                {
                    if (File.Exists(path)) Process.Start("notepad.exe", "\"" + path + "\"");
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "1C Update"); }
            };

            var logHost = new RoundedPanel();
            logHost.Dock = DockStyle.Fill;
            logHost.BackColor = Theme.ConsoleBg;
            logHost.BorderColor = Theme.BorderLight;
            logHost.CornerRadius = 8;
            logHost.Padding = new Padding(8, 8, 8, 8);

            _log = new RichTextBox();
            _log.Dock = DockStyle.Fill;
            _log.BorderStyle = BorderStyle.None;
            _log.BackColor = Theme.ConsoleBg;
            _log.ForeColor = Theme.Text;
            _log.Font = Theme.Mono;
            _log.ReadOnly = true;
            _log.WordWrap = false;
            _log.ScrollBars = RichTextBoxScrollBars.Both;
            _log.DetectUrls = false;
            _log.HideSelection = false;
            _log.MouseWheel += delegate { _autoScroll = false; };

            logHost.Controls.Add(_log);

            split.Panel1.Controls.Add(_grid);
            split.Panel2.Controls.Add(logHost);
            
            container.Controls.Add(split, 0, 1);
            return container;
        }
        
        // Обновление KPI-карточек
        private void UpdateKpiCards()
        {
            if (_jobs == null || _jobs.Count == 0) return;
            
            int total = _jobs.Count;
            int success = _jobs.Count(j => j.State == JobState.Ok || j.State == JobState.Skipped);
            int failed = _jobs.Count(j => j.State == JobState.Failed);
            int running = _jobs.Count(j => j.State == JobState.Running);
            
            _kpiTotal.SetValue(total);
            _kpiSuccess.SetValue(success);
            _kpiFailed.SetValue(failed);
            _kpiRunning.SetValue(running);
        }

        private void AddColumn(string header, int width, bool fixedWidth)
        {
            var col = new DataGridViewTextBoxColumn();
            col.HeaderText = header;
            col.SortMode = DataGridViewColumnSortMode.NotSortable;
            if (fixedWidth) col.Width = width;
            else col.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            _grid.Columns.Add(col);
        }

        // ----- мелкие фабрики -----
        private static Label MakeLabel(string text)
        {
            var l = new Label();
            l.Text = text;
            l.Dock = DockStyle.Fill;
            l.ForeColor = Theme.Muted;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.Margin = new Padding(0);
            return l;
        }

        private static RadioButton MakeRadio(string text)
        {
            var r = new RoundedRadioButton();
            r.Text = text;
            r.AutoSize = true;
            r.ForeColor = Theme.Text;
            r.Margin = new Padding(0, 2, 10, 0);
            return r;
        }

        private static CheckBox MakeCheck(string text)
        {
            var c = new RoundedCheckBox();
            c.Text = text;
            c.AutoSize = true;
            c.ForeColor = Theme.Text;
            c.Margin = new Padding(0, 2, 14, 0);
            return c;
        }

        private static RoundedCombo MakeCombo(int width)
        {
            var c = new RoundedCombo();
            c.BackColor = Theme.Surface2;
            c.ForeColor = Theme.Text;
            c.Width = width;
            c.Height = 27;
            c.Margin = new Padding(0, 2, 0, 0);
            return c;
        }

        /// <summary>Помещает ввод в закругленную рамку, выравнивая его по центру.</summary>
        private static RoundedPanel MakeInputHost(Control inner, Color back, int radius)
        {
            var host = new RoundedPanel();
            host.BackColor = back;
            host.BorderColor = Theme.Border;
            host.CornerRadius = radius;
            inner.Dock = DockStyle.None;
            inner.BackColor = back;
            host.Controls.Add(inner);
            host.Layout += delegate
            {
                inner.Left = 6;
                inner.Width = Math.Max(20, host.Width - 12);
                inner.Top = Math.Max(0, (host.Height - inner.Height) / 2);
            };
            return host;
        }

        private static Button MakeButton(string text, bool primary, int width)
        {
            var b = new RoundedButton();
            b.Text = text;
            b.AutoSize = false;
            b.Width = width;
            b.Height = 30;
            b.Font = primary ? Theme.UiBold : Theme.Ui;
            b.TextColor = primary ? Color.White : Theme.Text;
            b.NormalBack = primary ? Theme.Accent : Theme.Surface2;
            b.HoverBack = primary ? Theme.AccentHot : Theme.Surface3;
            b.DownBack = primary ? Theme.AccentDark : Theme.AccentDark;
            b.NormalBorder = primary ? Theme.Accent : Theme.Border;
            b.HoverBorder = primary ? Theme.AccentHot : Theme.BorderLight;
            b.CornerRadius = 6;
            return b;
        }

        private static string FindPsexec()
        {
            string[] common = new string[]
            {
                Path.Combine(Application.StartupPath, "PsExec64.exe"),
                Path.Combine(Application.StartupPath, "PsExec.exe"),
                Path.Combine(Application.StartupPath, @"my\PsExec64.exe"),
                Path.Combine(Application.StartupPath, @"my\PsExec.exe"),
                @"C:\inst\PsExec64.exe", @"C:\inst\PsExec.exe", @"C:\PsTools\PsExec.exe", @"C:\Windows\System32\PsExec.exe"
            };
            foreach (string p in common)
            {
                try { if (File.Exists(p)) return p; } catch { }
            }
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in path.Split(';'))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                try
                {
                    string p = Path.Combine(dir.Trim(), "PsExec.exe");
                    if (File.Exists(p)) return p;
                }
                catch { }
            }
            return "";
        }

        // ==============================================================
        //  Список ПК
        // ==============================================================
        private void UpdateCount()
        {
            _countLabel.Text = "ПК: " + ParseComputers().Count;
        }

        private List<string> ParseComputers()
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string text = _computers.Text.Replace("\r", "\n");
            string[] parts = text.Split(new char[] { '\n', ';', ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string raw in parts)
            {
                string name = raw.Trim().Trim('"').Trim('\'').Trim();
                if (name.Length == 0) continue;
                if (name.StartsWith("\\\\")) name = name.Substring(2);
                if (name.Length == 0) continue;
                if (seen.Add(name)) result.Add(name);
            }
            return result;
        }

        private void LoadFromFile()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Список компьютеров";
                dlg.Filter = "Текст (*.txt)|*.txt|Все файлы (*.*)|*.*";
                try
                {
                    if (File.Exists(@"C:\inst\comp.txt")) dlg.FileName = @"C:\inst\comp.txt";
                }
                catch { }
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    string[] lines = File.ReadAllLines(dlg.FileName, DetectEncoding(dlg.FileName));
                    var sb = new StringBuilder();
                    foreach (string line in lines)
                    {
                        string t = line.Trim();
                        if (t.Length == 0) continue;
                        if (t.StartsWith(";") || t.StartsWith("#")) continue;
                        sb.AppendLine(t);
                    }
                    _computers.Text = sb.ToString().TrimEnd();
                    UpdateCount();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Не удалось прочитать файл:\n" + ex.Message, "1C Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private static Encoding DetectEncoding(string path)
        {
            try
            {
                byte[] b = new byte[3];
                using (var fs = File.OpenRead(path)) { fs.Read(b, 0, 3); }
                if (b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return Encoding.UTF8;
                if (b[0] == 0xFF && b[1] == 0xFE) return Encoding.Unicode;
                return Encoding.GetEncoding(1251);
            }
            catch { return Encoding.Default; }
        }

        private void LoadFromAd()
        {
            string domain = "";
            try { domain = Environment.UserDomainName; } catch { }
            if (string.IsNullOrEmpty(domain)) domain = "";

            using (var dlg = new AdDialog(domain))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var existing = ParseComputers();
                var sb = new StringBuilder();
                foreach (string pc in existing) sb.AppendLine(pc);
                foreach (string pc in dlg.Selected)
                {
                    if (!existing.Contains(pc, StringComparer.OrdinalIgnoreCase)) sb.AppendLine(pc);
                }
                _computers.Text = sb.ToString().TrimEnd();
                UpdateCount();
            }
        }

        // ==============================================================
        //  Параметры
        // ==============================================================
        private void SyncModeControls()
        {
            // Не отключаем ComboBox (Windows рисует у отключённого светлый фон),
            // а приглушаем текст — тёмная тема остаётся целой.
            _verList.ForeColor = _ipVer.Checked ? Theme.Text : Theme.Muted;
            _delVerList.ForeColor = _dpVer.Checked ? Theme.Text : Theme.Muted;
        }

        private string PickPath(bool isFile)
        {
            if (isFile)
            {
                using (var dlg = new OpenFileDialog())
                {
                    dlg.Title = "Выберите файл";
                    dlg.Filter = "Все файлы (*.*)|*.*";
                    return dlg.ShowDialog(this) == DialogResult.OK ? dlg.FileName : null;
                }
            }
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Выберите папку";
                dlg.ShowNewFolderButton = false;
                return dlg.ShowDialog(this) == DialogResult.OK ? dlg.SelectedPath : null;
            }
        }

        private Options CollectOptions()
        {
            var o = new Options();
            o.Psexec = _psexec.Text.Trim();
            o.Script = _script.Text.Trim();
            o.DistribDir = _distrib.Text.Trim();
            o.LogDir = _logs.Text.Trim();
            o.User = _user.Text.Trim();
            o.Password = _password.Text;
            o.Threads = (int)_threads.Value;
            o.DesignerAllClients = _cbDesigner.Checked;
            o.ThinClient = _cbThin.Checked;
            o.ThinClientFile = _cbThinFile.Checked;
            o.DryRun = _cbDryRun.Checked;
            o.Delivery = DeliveryMode;

            if (_ipNo.Checked) o.InstallMode = "no";
            else if (_ipLast.Checked) o.InstallMode = "last";
            else o.InstallMode = _verList.Text.Trim();

            if (_dpNo.Checked) o.DeleteMode = "no";
            else if (_dpAel.Checked) o.DeleteMode = "ael";
            else if (_dpAll.Checked) o.DeleteMode = "all";
            else o.DeleteMode = _delVerList.Text.Trim();

            return o;
        }

        private bool CheckPaths(bool showResult)
        {
            var problems = new List<string>();
            var ok = new List<string>();
            Options o = CollectOptions();

            try
            {
                if (string.IsNullOrEmpty(o.DistribDir) || !Directory.Exists(o.DistribDir))
                    problems.Add("Каталог дистрибутивов недоступен: " + o.DistribDir);
                else
                {
                    var versions = ScanVersions(o.DistribDir);
                    ok.Add("Дистрибутивы: " + versions.Count + " версий (" + string.Join(", ", versions.Take(6).ToArray()) + (versions.Count > 6 ? "…" : "") + ")");
                    FillVersions(versions);
                }
            }
            catch (Exception ex) { problems.Add("Дистрибутивы: " + ex.Message); }

            try
            {
                string mode = DeliveryMode;
                if (mode == "exe")
                {
                    ok.Add("Скрипт " + Embedded.ScriptVersion + " встроен в exe — psexec скопирует его на каждый ПК");
                }
                else if (mode == "share")
                {
                    string scriptDir = Path.GetDirectoryName(o.Script);
                    if (string.IsNullOrEmpty(scriptDir)) problems.Add("Не задан путь, куда выложить встроенный скрипт");
                    else if (!CanWriteTo(scriptDir)) problems.Add("Нет доступа на запись в каталог скрипта: " + scriptDir);
                    else ok.Add("Встроенный скрипт " + Embedded.ScriptVersion + " будет выложен: " + o.Script);
                }
                else if (string.IsNullOrEmpty(o.Script) || !File.Exists(o.Script))
                    problems.Add("Скрипт не найден: " + o.Script);
                else ok.Add("Скрипт найден: " + o.Script);
            }
            catch (Exception ex) { problems.Add("Скрипт: " + ex.Message); }

            try
            {
                if (_useEmbeddedPsexec.Checked && Embedded.HasPsexec)
                    ok.Add("PsExec встроен в программу (распакуется автоматически)");
                else if (string.IsNullOrEmpty(o.Psexec) || !File.Exists(o.Psexec))
                    problems.Add("PsExec.exe не найден: " + o.Psexec);
                else ok.Add("PsExec найден: " + o.Psexec);
            }
            catch (Exception ex) { problems.Add("PsExec: " + ex.Message); }

            _status.Text = problems.Count == 0 ? "Проверка путей: всё на месте" : "Проверка путей: проблем — " + problems.Count;

            if (showResult)
            {
                var sb = new StringBuilder();
                foreach (string s in ok) sb.AppendLine("✓ " + s);
                foreach (string s in problems) sb.AppendLine("✗ " + s);
                MessageBox.Show(this, sb.ToString(), "Проверка путей",
                    MessageBoxButtons.OK, problems.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            return problems.Count == 0;
        }

        private static bool CanWriteTo(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir)) return false;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, ".1cupdate-write-test-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        // PsExec: из встроенного ресурса (распаковывается рядом с .exe) или по указанному пути
        private string ResolvePsexec(out string error)
        {
            error = null;
            if (_useEmbeddedPsexec != null && _useEmbeddedPsexec.Checked && Embedded.HasPsexec)
            {
                byte[] data = Embedded.PsexecBytes;
                try
                {
                    string target = Path.Combine(TempArea.Dir, Embedded.PsexecName);
                    if (!Embedded.SameContent(target, data)) File.WriteAllBytes(target, data);
                    return target;
                }
                catch (Exception ex)
                {
                    error = "Не удалось распаковать встроенный PsExec.exe: " + ex.Message;
                    return null;
                }
            }
            string path = _psexec.Text.Trim();
            if (path.Length == 0)
            {
                error = "Не найден PsExec.exe: укажите путь в поле «PsExec.exe».";
                return null;
            }
            return path;
        }

        // Скрипт: встроенный выкладывается на шару, внешний берётся по пути, в режиме exe не нужен
        private string DeployScript(Options o, out string error)
        {
            error = null;
            string mode = DeliveryMode;
            if (mode != "share") return o.Script;
            if (!Embedded.HasScript)
            {
                error = "В этой сборке нет встроенного скрипта — укажите .ps1 вручную.";
                return null;
            }

            byte[] data = Embedded.ScriptBytes;
            string target = o.Script;
            if (string.IsNullOrEmpty(target))
            {
                error = "Не задан путь, куда выложить встроенный скрипт.";
                return null;
            }
            try
            {
                string dir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                if (Embedded.SameContent(target, data))
                {
                    AppendLog(Theme.Muted, "Встроенный скрипт " + Embedded.ScriptVersion + " уже лежит на шаре: " + target);
                }
                else
                {
                    File.WriteAllBytes(target, data);
                    AppendLog(Theme.Ok, "Встроенный скрипт " + Embedded.ScriptVersion + " выложен на шару: " + target);
                }
                return target;
            }
            catch (Exception ex)
            {
                error = "Не удалось выложить встроенный скрипт в " + target + ": " + ex.Message +
                        "\n\nУкажите каталог, куда есть доступ на запись, либо выберите другой способ доставки скрипта.";
                return null;
            }
        }
        private static List<string> ScanVersions(string dir)
        {
            var result = new List<string>();
            try
            {
                foreach (string path in Directory.GetDirectories(dir))
                {
                    string name = Path.GetFileName(path);
                    Match m = Regex.Match(name, @"^(\d+\.\d+\.\d+\.\d+)(-(32|64))?$");
                    if (!m.Success) continue;
                    string ver = m.Groups[1].Value;
                    if (!result.Contains(ver)) result.Add(ver);
                }
            }
            catch { }
            result.Sort(delegate (string a, string b)
            {
                return CompareVersions(b, a);
            });
            return result;
        }

        private static int CompareVersions(string a, string b)
        {
            try
            {
                string[] pa = a.Split('.');
                string[] pb = b.Split('.');
                for (int i = 0; i < Math.Min(pa.Length, pb.Length); i++)
                {
                    int x = Convert.ToInt32(pa[i]);
                    int y = Convert.ToInt32(pb[i]);
                    if (x != y) return x.CompareTo(y);
                }
                return pa.Length.CompareTo(pb.Length);
            }
            catch { return string.Compare(a, b, StringComparison.Ordinal); }
        }

        private void FillVersions(List<string> versions)
        {
            string current = _verList.Text;
            string currentDel = _delVerList.Text;
            _verList.Items.Clear();
            _delVerList.Items.Clear();
            foreach (string v in versions)
            {
                _verList.Items.Add(v);
                _delVerList.Items.Add(v);
            }
            if (current.Length > 0) _verList.Text = current;
            else if (versions.Count > 0) _verList.Text = versions[0];
            if (currentDel.Length > 0) _delVerList.Text = currentDel;
        }

        // ==============================================================
        //  Запуск
        // ==============================================================
        private void StartRun()
        {
            if (_running) return;

            List<string> pcs = ParseComputers();
            if (pcs.Count == 0)
            {
                MessageBox.Show(this, "Добавьте компьютеры: по одному имени в строке, или кнопкой «Из файла…».",
                    "1C Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Options o = CollectOptions();
            o.LocalExe = Application.ExecutablePath;
            o.Delivery = DeliveryMode;

            var problems = new List<string>();

            if (!_cbDryRun.Checked)
            {
                string resolveError;
                string psexec = ResolvePsexec(out resolveError);
                if (psexec == null) { ShowError(resolveError); return; }
                o.Psexec = psexec;

                string deployError;
                string scriptPath = DeployScript(o, out deployError);
                if (scriptPath == null) { ShowError(deployError); return; }
                o.Script = scriptPath;

                if (o.Delivery == "path" && !File.Exists(o.Script)) problems.Add("не найден скрипт .ps1: " + o.Script);
                if (string.IsNullOrEmpty(o.Psexec) || !File.Exists(o.Psexec)) problems.Add("не найден PsExec.exe");
            }
            if (string.IsNullOrEmpty(o.DistribDir)) problems.Add("не задан каталог дистрибутивов -dd");
            if (string.IsNullOrEmpty(o.LogDir)) problems.Add("не задан каталог журналов -dl");
            if (string.IsNullOrEmpty(o.InstallMode)) problems.Add("не выбран режим установки -ip");
            if (string.IsNullOrEmpty(o.DeleteMode)) problems.Add("не выбран режим удаления -dp");
            if (!string.IsNullOrEmpty(o.User) && o.Password.Length == 0)
                problems.Add("не введён пароль (psexec запросит его и зависнет)");
            if (!o.DesignerAllClients && !o.ThinClient && !o.ThinClientFile && !_cbDryRun.Checked)
                problems.Add("не выбран ни один компонент -iod");

            if (problems.Count > 0)
            {
                MessageBox.Show(this, "Проверьте параметры:\n\n— " + string.Join("\n— ", problems.ToArray()),
                    "1C Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _jobs = new List<Job>();
            _rows.Clear();
            _grid.Rows.Clear();
            foreach (string pc in pcs)
            {
                var job = new Job();
                job.Name = pc;
                _jobs.Add(job);
                int idx = _grid.Rows.Add(pc, "Ожидание", "", "", "");
                _grid.Rows[idx].DefaultCellStyle.ForeColor = Theme.Muted;
                _rows[pc] = idx;
            }

            if (o.DryRun)
            {
                AppendLog(Theme.Info, "Режим «только показать команды» — ничего не выполняется.");
                foreach (Job job in _jobs)
                {
                    AppendLog(Theme.Text, "[" + job.Name + "] " + o.Psexec + " " + o.BuildArgs(job.Name, null));
                    job.State = JobState.Ok;
                    job.Note = "dry-run";
                    SetRow(job, "команда", -1);
                }
                _status.Text = "Команды выведены в журнал";
                FlushLog();
                return;
            }

            _running = true;
            SetRunningUi(true);
            _startedAll = DateTime.Now;
            _progress.Maximum = _jobs.Count;
            _progress.Value = 0;
            _progressLabel.Text = "0 / " + _jobs.Count;

            AppendLog(Theme.Muted, new string('─', 100));
            AppendLog(Theme.Info, "Запуск: " + _jobs.Count + " ПК, потоков " + o.Threads + ", " + DateTime.Now.ToString("HH:mm:ss"));
            AppendLog(Theme.Muted, "Установка -ip " + o.InstallMode + " · удаление -dp " + o.DeleteMode + " · компоненты " + o.Iod);

            _runner = new Runner(o, _jobs);
            _runner.JobStarted += delegate (Job job) { Post(delegate { SetRow(job, null, 0); }); };
            _runner.JobChanged += delegate (Job job) { Post(delegate { OnJobChanged(job); }); };
            _runner.LogLine += delegate (Job job, string line, bool system) { Post(delegate { OnLogLine(job, line, system); }); };
            _runner.Finished += delegate { Post(OnAllFinished); };
            _runner.Start();
        }

        private void StopRun()
        {
            if (!_running) return;
            AppendLog(Theme.Warn, "Остановка по требованию пользователя…");
            if (_runner != null) _runner.Stop();
        }

        private void OnJobChanged(Job job)
        {
            SetRow(job, null, 0);
            int done = 0;
            foreach (Job j in _jobs)
                if (j.State == JobState.Ok || j.State == JobState.Warning || j.State == JobState.Failed || j.State == JobState.Canceled || j.State == JobState.Skipped) done++;
            _progress.Value = Math.Min(done, _progress.Maximum);
            _progressLabel.Text = done + " / " + _jobs.Count;
        }

        private void OnAllFinished()
        {
            _running = false;
            SetRunningUi(false);

            int ok = 0, warn = 0, fail = 0, cancel = 0, skip = 0;
            foreach (Job j in _jobs)
            {
                if (j.State == JobState.Ok) ok++;
                else if (j.State == JobState.Warning) warn++;
                else if (j.State == JobState.Failed) fail++;
                else if (j.State == JobState.Canceled) cancel++;
                else if (j.State == JobState.Skipped) skip++;
            }

            double sec = (DateTime.Now - _startedAll).TotalSeconds;
            string summary = string.Format("Итого: {0} ПК · успешно {1} · уже стояло {2} · с замечаниями {3} · ошибок {4}{5} · {6:0.0} с",
                _jobs.Count, ok, skip, warn, fail, cancel > 0 ? " · отменено " + cancel : "", sec);
            AppendLog(fail == 0 && warn == 0 ? Theme.Ok : Theme.Warn, summary);
            _status.Text = summary;
            FlushLog();
        }

        private void SetRunningUi(bool running)
        {
            _runBtn.Enabled = !running;
            _stopBtn.Enabled = running;
            _checkBtn.Enabled = !running;
            _threads.Enabled = !running;
            _computers.Enabled = !running;
            _cbDryRun.Enabled = !running;
            if (running) _autoScroll = true;
        }

        private void SetRow(Job job, string statusOverride, int unused)
        {
            int idx;
            if (!_rows.TryGetValue(job.Name, out idx)) return;
            if (idx >= _grid.Rows.Count) return;
            DataGridViewRow row = _grid.Rows[idx];

            string status = statusOverride ?? StateText(job.State);
            row.Cells[1].Value = status;
            row.Cells[2].Value = job.State == JobState.Pending ? "" : FormatDuration(job.Seconds);
            row.Cells[3].Value = job.Code == -9999 ? "" : job.Code.ToString();
            row.Cells[4].Value = job.Note;

            // Цветовая схема статусов с фоном для лучшей визуализации
            Color textColor = Theme.Text;
            Color bgColor = (idx % 2 == 0) ? Theme.Surface : Theme.Surface2;
            
            if (job.State == JobState.Ok)
            {
                textColor = Theme.Ok;
                // Тонкая подсветка фона для успешных операций
                bgColor = (idx % 2 == 0) ? Color.FromArgb(28, 50, 35) : Color.FromArgb(33, 55, 40);
            }
            else if (job.State == JobState.Warning)
            {
                textColor = Theme.Warn;
                bgColor = (idx % 2 == 0) ? Color.FromArgb(50, 45, 30) : Color.FromArgb(55, 50, 35);
            }
            else if (job.State == JobState.Failed)
            {
                textColor = Theme.Err;
                bgColor = (idx % 2 == 0) ? Color.FromArgb(50, 30, 28) : Color.FromArgb(55, 35, 33);
            }
            else if (job.State == JobState.Running)
            {
                textColor = Theme.Info;
                bgColor = (idx % 2 == 0) ? Color.FromArgb(30, 40, 55) : Color.FromArgb(35, 45, 60);
            }
            else if (job.State == JobState.Canceled)
            {
                textColor = Theme.Muted;
            }
            else if (job.State == JobState.Skipped)
            {
                textColor = Theme.Info;
            }
            
            row.DefaultCellStyle.ForeColor = textColor;
            row.DefaultCellStyle.BackColor = bgColor;
            row.DefaultCellStyle.SelectionBackColor = Theme.AccentDark;
            row.DefaultCellStyle.SelectionForeColor = Color.White;
            
            UpdateKpiCards();
        }

        private void UpdateTimes()
        {
            if (!_running) return;
            foreach (Job j in _jobs)
            {
                if (j.State == JobState.Running) SetRow(j, null, 0);
            }
            UpdateKpiCards();
        }

        private static string StateText(JobState state)
        {
            switch (state)
            {
                case JobState.Pending: return "Ожидание";
                case JobState.Running: return "Выполняется…";
                case JobState.Ok: return "Готово";
                case JobState.Warning: return "С замечаниями";
                case JobState.Failed: return "Ошибка";
                case JobState.Canceled: return "Отменено";
                case JobState.Skipped: return "Уже установлена";
            }
            return "";
        }

        private static string FormatDuration(double seconds)
        {
            if (seconds < 1) return "";
            int s = (int)Math.Round(seconds);
            return (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
        }

        // ==============================================================
        //  Журнал
        // ==============================================================
        private void OnLogLine(Job job, string line, bool system)
        {
            Color color = system ? Theme.Muted : Classify(line);
            AppendLog(color, "[" + job.Name + "] " + line);
        }

        private static readonly Regex RxErr = new Regex(@"(?i)(\berror\b|ошибк|не удалось|не найден|не найдена|прервана|отказано|недопустим)", RegexOptions.Compiled);
        private static readonly Regex RxWarn = new Regex(@"(?i)(предупрежд|\bwarn|не будет|пропущен|уже установлен)", RegexOptions.Compiled);
        private static readonly Regex RxOk = new Regex(@"(?i)(окончание работы|успешно|установка 1с)", RegexOptions.Compiled);

        private static Color Classify(string line)
        {
            if (string.IsNullOrEmpty(line)) return Theme.Text;
            if (RxOk.IsMatch(line)) return Theme.Ok;
            if (RxErr.IsMatch(line)) return Theme.Err;
            if (RxWarn.IsMatch(line)) return Theme.Warn;
            return Theme.Text;
        }

        private void AppendLog(Color color, string text)
        {
            _queue.Enqueue(new KeyValuePair<Color, string>(color, text ?? ""));
        }

        private void FlushLog()
        {
            if (_queue.IsEmpty) return;
            bool atBottom = _autoScroll && IsAtBottom();
            _log.SuspendLayout();
            try
            {
                Color current = Color.Empty;
                var sb = new StringBuilder();
                KeyValuePair<Color, string> item;
                while (_queue.TryDequeue(out item))
                {
                    if (item.Key != current)
                    {
                        if (sb.Length > 0) AppendText(sb.ToString(), current);
                        sb.Length = 0;
                        current = item.Key;
                    }
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(item.Value);
                }
                if (sb.Length > 0) AppendText(sb.ToString(), current);

                if (_log.TextLength > 400000)
                {
                    _log.Select(0, 100000);
                    _log.SelectedText = "";
                }
            }
            finally { _log.ResumeLayout(); }

            if (atBottom)
            {
                _log.SelectionStart = _log.TextLength;
                _log.SelectionLength = 0;
                _log.ScrollToCaret();
            }
        }

        private void AppendText(string text, Color color)
        {
            _log.SelectionStart = _log.TextLength;
            _log.SelectionLength = 0;
            _log.SelectionColor = color;
            _log.AppendText(text + "\n");
        }

        private bool IsAtBottom()
        {
            try
            {
                var si = new SCROLLINFO();
                si.cbSize = (uint)Marshal.SizeOf(typeof(SCROLLINFO));
                si.fMask = SIF_RANGE | SIF_PAGE | SIF_POS;
                if (!GetScrollInfo(_log.Handle, SB_VERT, ref si)) return true;
                int maxPos = si.nMax - (int)si.nPage;
                return si.nPos >= maxPos - 4;
            }
            catch { return true; }
        }

        private void Post(Action action)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(action);
            }
            catch { }
        }

        // ==============================================================
        //  Отчёт
        // ==============================================================
        private void SaveReport()
        {
            if (_jobs.Count == 0) { MessageBox.Show(this, "Отчёт пуст.", "1C Update"); return; }
            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = "Сохранить отчёт";
                dlg.Filter = "Текст (*.txt)|*.txt|CSV (*.csv)|*.csv|Все файлы (*.*)|*.*";
                dlg.FileName = "1c-update-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".csv";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var sb = new StringBuilder();
                    if (dlg.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    {
                        sb.AppendLine("Компьютер;Статус;Длительность,с;Код;Примечание");
                        foreach (Job j in _jobs)
                            sb.AppendLine(j.Name + ";" + StateText(j.State) + ";" + j.Seconds.ToString("0.0") + ";" +
                                          (j.Code == -9999 ? "" : j.Code.ToString()) + ";" + j.Note.Replace(";", ","));
                    }
                    else
                    {
                        sb.AppendLine("Отчёт 1C Update " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                        foreach (Job j in _jobs)
                            sb.AppendLine(string.Format("{0,-20} {1,-18} {2,8:0.0} с  код {3}  {4}",
                                j.Name, StateText(j.State), j.Seconds, j.Code, j.Note));
                    }
                    File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "1C Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        // ==============================================================
        //  Настройки
        // ==============================================================
        private void LoadSettings()
        {
            SyncModeControls();
            try
            {
                string path = Paths.SettingsFile;
                if (path == null || !File.Exists(path)) return;
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    int eq = raw.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = raw.Substring(0, eq);
                    string value = raw.Substring(eq + 1);
                    switch (key)
                    {
                        case "psexec": if (value.Length > 0) _psexec.Text = value; break;
                        case "script": if (value.Length > 0) _script.Text = value; break;
                        case "dd": if (value.Length > 0) _distrib.Text = value; break;
                        case "dl": if (value.Length > 0) _logs.Text = value; break;
                        case "user": _user.Text = value; break;
                        case "threads": { int n; if (int.TryParse(value, out n) && n >= 1 && n <= 64) _threads.Value = n; break; }
                        case "ip": if (value == "no") _ipNo.Checked = true; else if (value == "last") _ipLast.Checked = true; else { _ipVer.Checked = true; _verList.Text = value; } break;
                        case "dp": if (value == "no") _dpNo.Checked = true; else if (value == "ael") _dpAel.Checked = true; else if (value == "all") _dpAll.Checked = true; else { _dpVer.Checked = true; _delVerList.Text = value; } break;
                        case "designer": _cbDesigner.Checked = value == "1"; break;
                        case "thin": _cbThin.Checked = value == "1"; break;
                        case "thinfiles": _cbThinFile.Checked = value == "1"; break;
                        case "psexecembedded":
                            if (Embedded.HasPsexec) _useEmbeddedPsexec.Checked = value == "1";
                            break;
                        case "delivery":
                            if (value == "share") _delivery.SelectedIndex = 1;
                            else if (value == "path") _delivery.SelectedIndex = 2;
                            else _delivery.SelectedIndex = 0;
                            break;
                        case "computers": _computers.Text = value.Replace("\\n", "\r\n"); break;
                        case "bounds":
                            {
                                string[] b = value.Split(',');
                                int x, y, w, h;
                                if (b.Length == 4 && int.TryParse(b[0], out x) && int.TryParse(b[1], out y) &&
                                    int.TryParse(b[2], out w) && int.TryParse(b[3], out h) && w > 700 && h > 500)
                                {
                                    var rect = new Rectangle(x, y, w, h);
                                    if (Screen.PrimaryScreen.WorkingArea.IntersectsWith(rect)) Bounds = rect;
                                }
                                break;
                            }
                    }
                }
            }
            catch { }
            SyncModeControls();
            SyncComponents();
        }

        private void SaveSettings()
        {
            try
            {
                string path = Paths.SettingsFile;
                if (path == null) return;
                var sb = new StringBuilder();
                sb.AppendLine("psexec=" + _psexec.Text.Trim());
                sb.AppendLine("script=" + _script.Text.Trim());
                sb.AppendLine("dd=" + _distrib.Text.Trim());
                sb.AppendLine("dl=" + _logs.Text.Trim());
                sb.AppendLine("user=" + _user.Text.Trim());
                sb.AppendLine("threads=" + ((int)_threads.Value));
                Options o = CollectOptions();
                sb.AppendLine("ip=" + o.InstallMode);
                sb.AppendLine("dp=" + o.DeleteMode);
                sb.AppendLine("designer=" + (_cbDesigner.Checked ? "1" : "0"));
                sb.AppendLine("thin=" + (_cbThin.Checked ? "1" : "0"));
                sb.AppendLine("thinfiles=" + (_cbThinFile.Checked ? "1" : "0"));
                sb.AppendLine("delivery=" + DeliveryMode);
                sb.AppendLine("psexecembedded=" + (_useEmbeddedPsexec.Checked ? "1" : "0"));
                sb.AppendLine("computers=" + _computers.Text.Replace("\r", "").Replace("\n", "\\n"));
                sb.AppendLine("bounds=" + Bounds.X + "," + Bounds.Y + "," + Bounds.Width + "," + Bounds.Height);
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        private void ShowError(string message)
        {
            MessageBox.Show(this, message, "1C Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_running)
            {
                var r = MessageBox.Show(this, "Обновление ещё выполняется. Остановить и выйти?", "1C Update",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) { e.Cancel = true; return; }
                StopRun();
                Thread.Sleep(300);
            }
            SaveSettings();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5) { StartRun(); e.Handled = true; }
            else if (e.KeyCode == Keys.F6) { StopRun(); e.Handled = true; }
        }

        // ----- автопрокрутка журнала -----
        [StructLayout(LayoutKind.Sequential)]
        private struct SCROLLINFO
        {
            public uint cbSize;
            public uint fMask;
            public int nMin;
            public int nMax;
            public uint nPage;
            public int nPos;
            public int nTrackPos;
        }

        private const int SB_VERT = 1;
        private const uint SIF_RANGE = 0x1;
        private const uint SIF_PAGE = 0x2;
        private const uint SIF_POS = 0x4;

        [DllImport("user32.dll")]
        private static extern bool GetScrollInfo(IntPtr hwnd, int fnBar, ref SCROLLINFO lpsi);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(Handle, 19, ref on, sizeof(int));
            }
            catch { }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            try
            {
                if (_grid.IsHandleCreated) SetWindowTheme(_grid.Handle, "DarkMode_Explorer", null);
                if (_log.IsHandleCreated) SetWindowTheme(_log.Handle, "DarkMode_Explorer", null);
            }
            catch { }

            SetPlaceholder(_distrib, @"\\server\share\distr\1c");
            SetPlaceholder(_logs, @"\\server\share\distr\1c\log");
            SetPlaceholder(_script, @"\\server\share\distr\1c\log\1C8InstallAndUninstall-1.09.ps1");
            SetPlaceholder(_psexec, @"C:\inst\PsExec.exe");
            SetPlaceholder(_user, @"DOMAIN\admin");
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private const int EM_SETCUEBANNER = 0x1501;

        private static void SetPlaceholder(Control control, string text)
        {
            try
            {
                if (control != null && control.IsHandleCreated)
                    SendMessage(control.Handle, EM_SETCUEBANNER, (IntPtr)1, text);
            }
            catch { }
        }

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string subAppName, string subIdList);
    }

    // ------------------------------------------------------------------
    //  Точка входа
    // ------------------------------------------------------------------
    // ------------------------------------------------------------------
    //  Режим --remote-worker: выполняется НА целевом ПК (psexec -c)
    // ------------------------------------------------------------------
    internal static class RemoteWorker
    {
        public static int Run(string[] args)
        {
            try
            {
                return RunCore(args);
            }
            catch (Exception ex)
            {
                try
                {
                    StreamWriter w = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
                    w.AutoFlush = true;
                    w.WriteLine("1C Update: КРИТИЧЕСКАЯ ОШИБКА: " + ex.Message);
                    w.WriteLine(ex.ToString());
                    w.Flush();
                }
                catch { }
                CrashLog.Write("worker: " + ex);
                return 4;
            }
        }

        private static int RunCore(string[] args)
        {
            CrashLog.Write("старт worker: " + string.Join(" ", args));
            string dd = "", dl = "", ip = "no", dp = "no", iod = "", progressFile = "";
            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "-dd" && i + 1 < args.Length) { dd = args[++i]; continue; }
                if (a == "-dl" && i + 1 < args.Length) { dl = args[++i]; continue; }
                if (a == "-ip" && i + 1 < args.Length) { ip = args[++i]; continue; }
                if (a == "-dp" && i + 1 < args.Length) { dp = args[++i]; continue; }
                if (a == "-iod" && i + 1 < args.Length) { iod = args[++i]; continue; }
                if (a == "--progress" && i + 1 < args.Length) { progressFile = args[++i]; continue; }
            }

            StreamWriter outW = null;
            try
            {
                outW = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
                outW.AutoFlush = true;
            }
            catch { }
            Action<string> W = delegate (string s)
            {
                try { if (_progress != null) _progress.WriteLine(s); } catch { }
                try { if (outW != null) { outW.WriteLine(s); outW.Flush(); } } catch { }
                try { CrashLog.Write(s); } catch { }   // след на случай падения процесса
            };

            OpenProgress(dl, progressFile, W);

            byte[] data = Embedded.ScriptBytes;
            if (data == null) { W("1C Update: встроенный скрипт не найден"); return 3; }

            // скрипт кладём во временный каталог этого ПК (не в C:\Windows)
            string temp = null;
            Exception lastError = null;
            foreach (string dir in new string[] { Path.GetTempPath(), TempArea.Dir })
            {
                try
                {
                    string candidate = Path.Combine(dir, "1c8-" + Guid.NewGuid().ToString("N") + ".ps1");
                    File.WriteAllBytes(candidate, data);
                    temp = candidate;
                    break;
                }
                catch (Exception ex) { lastError = ex; }
            }
            if (temp == null)
            {
                W("1C Update: не удалось распаковать скрипт: " + (lastError != null ? lastError.Message : "нет доступа к каталогу"));
                return 3;
            }

            W("1C Update " + Embedded.ScriptVersion + " · " + Environment.MachineName + " · скрипт распакован");
            W("1C Update: -dd " + dd);
            W("1C Update: -dl " + dl);

            // каталог для журналов может не существовать на шаре — создаём
            bool logOk = EnsureLogDir(dl, W);
            W(logOk ? "1C Update: каталог логов доступен" : "1C Update: каталог логов недоступен");

            // что уже стоит и что собираемся ставить
            string target = null;
            try { target = ResolveTargetVersion(ip, dd, W); }
            catch (Exception ex) { W("1C Update: ошибка определения версии: " + ex.Message); }
            W("1C Update: целевая версия: " + (target == null ? "не определена" : target));

            if (target != null)
            {
                List<string> before = null;
                try { before = InstalledVersions(); }
                catch (Exception ex) { W("1C Update: ошибка чтения реестра: " + ex.Message); }

                if (before == null)
                {
                    W("1C Update: проверка установленной версии недоступна — продолжаю установку");
                }
                else
                {
                    W("1C Update: установлено сейчас: " + (before.Count == 0 ? "платформ 1С не найдено" : string.Join(", ", before.ToArray())));
                    if (before.Contains(target))
                    {
                        W("1C Update: версия " + target + " уже установлена — установка не требуется");
                        W("== итог: skip " + target + " ==");
                        try { File.Delete(temp); } catch { }
                        CloseProgress();
                        return 0;
                    }
                    W("1C Update: установка требуется");
                }
            }

            LogTail tail = null;
            if (!string.IsNullOrEmpty(dl))
            {
                tail = new LogTail(Path.Combine(dl, Environment.MachineName + ".log"));
                tail.Prepare();
            }

            string ps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                @"WindowsPowerShell\v1.0\powershell.exe");
            if (!File.Exists(ps)) ps = "powershell.exe";

            string psArgs = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + temp + "\"" +
                            " -dd \"" + dd + "\" -dl \"" + dl + "\" -ip \"" + ip + "\"" +
                            " -dp \"" + dp + "\" -iod \"" + iod + "\"";

            var psi = new ProcessStartInfo();
            psi.FileName = ps;
            psi.Arguments = psArgs;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.RedirectStandardInput = true;

            Process p;
            try
            {
                p = new Process();
                p.StartInfo = psi;
                p.Start();
            }
            catch (Exception ex)
            {
                W("1C Update: не удалось запустить PowerShell: " + ex.Message);
                return 3;
            }
            try { p.StandardInput.Close(); } catch { }

            var tOut = new Thread(delegate ()
            {
                try { string line; while ((line = p.StandardOutput.ReadLine()) != null) W(line); } catch { }
            });
            tOut.IsBackground = true;
            tOut.Start();
            var tErr = new Thread(delegate ()
            {
                try { string line; while ((line = p.StandardError.ReadLine()) != null) W(line); } catch { }
            });
            tErr.IsBackground = true;
            tErr.Start();

            while (!p.HasExited)
            {
                FlushLog(tail, W);
                Thread.Sleep(400);
            }
            try { p.WaitForExit(30000); } catch { }
            tOut.Join(2000);
            tErr.Join(2000);
            FlushLog(tail, W);

            int code = 0;
            try { code = p.ExitCode; } catch { }

            // проверка результата по факту
            if (target != null)
            {
                List<string> after = InstalledVersions();
                if (after.Contains(target))
                {
                    W("1C Update: проверка после установки — версия " + target + " найдена");
                    W("== итог: ok " + target + " ==");
                }
                else
                {
                    W("1C Update: проверка после установки — версия " + target + " НЕ найдена");
                    W("== итог: fail версия " + target + " не найдена после установки ==");
                }
            }
            else
            {
                W("== итог: " + (code == 0 ? "ok" : "fail") + " ==");
            }

            try { File.Delete(temp); } catch { }
            CloseProgress();
            return code;
        }

        private static void FlushLog(LogTail tail, Action<string> write)
        {
            if (tail == null) return;
            List<string> lines = tail.ReadLines();
            foreach (string line in lines) write(line);
        }

        private static StreamWriter _progress;

        // отдельный канал прогресса: файл на шаре (stdout через psexec иногда теряется)
        private static void OpenProgress(string dl, string explicitPath, Action<string> write)
        {
            string path = !string.IsNullOrEmpty(explicitPath)
                ? explicitPath
                : (string.IsNullOrEmpty(dl) ? null : Path.Combine(dl, Environment.MachineName + ".progress.txt"));
            if (string.IsNullOrEmpty(path)) return;

            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    string dir = Path.GetDirectoryName(path);
                    if (attempt > 0 && !string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                    _progress = new StreamWriter(fs, new UTF8Encoding(false));
                    _progress.AutoFlush = true;
                    return;
                }
                catch (Exception ex)
                {
                    if (attempt == 0) continue;
                    write("1C Update: прогресс-файл недоступен (" + ex.Message + ") — остаётся только вывод psexec");
                }
            }
        }
        private static void CloseProgress()
        {
            try { if (_progress != null) { _progress.Flush(); _progress.Dispose(); } } catch { }
            _progress = null;
        }
        private static bool EnsureLogDir(string dl, Action<string> write)
        {
            if (string.IsNullOrEmpty(dl)) return false;
            try
            {
                if (!Directory.Exists(dl)) Directory.CreateDirectory(dl);
                return Directory.Exists(dl);
            }
            catch (Exception ex)
            {
                write("1C Update: каталог логов " + dl + " недоступен (" + ex.Message + ")");
                return false;
            }
        }

        // установленные платформы 1С (так же, как в скрипте: реестр, 32- и 64-битные ветки)
        private static List<string> InstalledVersions()
        {
            var list = new List<string>();
            string[] roots = new string[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };
            foreach (string root in roots)
            {
                try
                {
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(root))
                    {
                        if (key == null) continue;
                        foreach (string sub in key.GetSubKeyNames())
                        {
                            try
                            {
                                using (RegistryKey k = key.OpenSubKey(sub))
                                {
                                    if (k == null) continue;
                                    object name = k.GetValue("DisplayName");
                                    if (name == null) continue;
                                    if (!Regex.IsMatch(Convert.ToString(name), "^(1С|1C)")) continue;
                                    object ver = k.GetValue("DisplayVersion");
                                    if (ver == null) continue;
                                    string v = Convert.ToString(ver).Trim();
                                    if (v.Length > 0 && !list.Contains(v)) list.Add(v);
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
            return list;
        }

        // какая версия будет ставиться: конкретная из -ip или старшая из каталога для "last"
        private static string ResolveTargetVersion(string ip, string dd, Action<string> write)
        {
            if (string.IsNullOrEmpty(ip) || ip == "no") return null;
            if (Regex.IsMatch(ip, @"^\d+\.\d+\.\d+\.\d+$")) return ip;
            if (ip != "last") return null;

            try
            {
                string best = null;
                foreach (string dir in Directory.GetDirectories(dd))
                {
                    Match m = Regex.Match(Path.GetFileName(dir), @"^(\d+\.\d+\.\d+\.\d+)(-(32|64))?$");
                    if (!m.Success) continue;
                    string v = m.Groups[1].Value;
                    if (best == null || CompareVersions(v, best) > 0) best = v;
                }
                if (best == null) write("1C Update: в " + dd + " нет папок с версиями — какую ставить, определит скрипт");
                return best;
            }
            catch (Exception ex)
            {
                write("1C Update: не удалось прочитать каталог дистрибутивов " + dd + " (" + ex.Message + ")");
                return null;
            }
        }

        private static int CompareVersions(string a, string b)
        {
            try
            {
                string[] pa = a.Split('.');
                string[] pb = b.Split('.');
                for (int i = 0; i < Math.Min(pa.Length, pb.Length); i++)
                {
                    int x = Convert.ToInt32(pa[i]);
                    int y = Convert.ToInt32(pb[i]);
                    if (x != y) return x.CompareTo(y);
                }
                return pa.Length.CompareTo(pb.Length);
            }
            catch { return string.Compare(a, b, StringComparison.Ordinal); }
        }


    }
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
                {
                    try { CrashLog.Write("Unhandled: " + Convert.ToString(e.ExceptionObject)); } catch { }
                };
                if (args.Length >= 2 && args[0] == "--remote-worker") return RemoteWorker.Run(args);
                if (args.Length >= 2 && args[0] == "--commands")
                {
                    // Пакетный режим: напечатать команды для списка ПК
                    var opt = new Options();
                    opt.Psexec = @"C:\inst\PsExec.exe";
                    opt.Script = @"\\server\share\1C8InstallAndUninstall.ps1";
                    opt.DistribDir = @"\\server\share\distr";
                    opt.LogDir = @"\\server\share\log";
                    foreach (string pc in File.ReadAllLines(args[1]))
                    {
                        if (pc.Trim().Length == 0) continue;
                        Console.WriteLine(opt.Psexec + " " + opt.BuildArgs(pc.Trim(), null));
                    }
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
                return 0;
            }
            catch (Exception ex)
            {
                try { MessageBox.Show(ex.ToString(), "1C Update", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
                return 1;
            }
        }
    }
}
