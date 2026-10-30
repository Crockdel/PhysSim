// ============================================================================
//  Theme.cs — единое оформление окна: цвета, шрифты, кнопки и поля ввода.
//  Только внешний вид. Логика, порядок элементов и обработчики не меняются.
// ============================================================================
using System;
using System.Drawing;
using System.Windows.Forms;

namespace PhysicsSandbox
{
    /// <summary>Палитра и стили, которые применяются к окну целиком.</summary>
    internal static class Theme
    {
        public static readonly Color Surface     = Color.FromArgb(24, 31, 46);    // фон панелей
        public static readonly Color SurfaceAlt  = Color.FromArgb(32, 41, 60);    // фон групп и полей
        public static readonly Color Canvas      = Color.FromArgb(14, 19, 30);    // фон сцены
        public static readonly Color Border      = Color.FromArgb(52, 64, 88);
        public static readonly Color Text        = Color.FromArgb(226, 232, 244);
        public static readonly Color TextMuted   = Color.FromArgb(140, 152, 176);
        public static readonly Color Accent      = Color.FromArgb(80, 201, 176);  // бирюза
        public static readonly Color AccentHover = Color.FromArgb(104, 214, 192);
        public static readonly Color Danger      = Color.FromArgb(232, 96, 96);
        public static readonly Color DangerHover = Color.FromArgb(244, 120, 120);
        public static readonly Color ButtonFace  = Color.FromArgb(40, 52, 76);
        public static readonly Color ButtonHover = Color.FromArgb(52, 67, 98);

        public static readonly Font UiFont    = new Font("Segoe UI", 9f);
        public static readonly Font TitleFont = new Font("Segoe UI Semibold", 9.5f);
        public static readonly Font SmallFont = new Font("Segoe UI", 8.5f);

        /// <summary>Применяет оформление ко всей форме и её дочерним элементам.</summary>
        public static void Apply(Control root)
        {
            root.BackColor = Surface;
            root.ForeColor = Text;
            root.Font = UiFont;
            Style(root);
        }

        static void Style(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is Button) StyleButton((Button)c);
                else if (c is NumericUpDown)
                {
                    var n = (NumericUpDown)c;
                    n.BackColor = SurfaceAlt; n.ForeColor = Text; n.BorderStyle = BorderStyle.FixedSingle;
                    StyleUpDownEdit(n);
                    // Внутреннее поле NumericUpDown создаётся вместе с дескриптором, поэтому красим и тогда.
                    n.HandleCreated += (s, e) => StyleUpDownEdit(n);
                }
                else if (c is ComboBox)
                {
                    var cb = (ComboBox)c;
                    cb.BackColor = SurfaceAlt; cb.ForeColor = Text; cb.FlatStyle = FlatStyle.Flat;
                }
                else if (c is CheckBox)
                {
                    c.ForeColor = Text; c.BackColor = Surface;
                }
                else if (c is GroupBox)
                {
                    c.ForeColor = TextMuted; c.BackColor = Surface; c.Font = SmallFont;
                }
                else if (c is Label)
                {
                    var l = (Label)c;
                    if (l.Font.Bold) { l.Font = TitleFont; l.ForeColor = Accent; }
                    else if (l.ForeColor == Color.FromArgb(60, 80, 120) || l.ForeColor == Color.FromArgb(90, 100, 120))
                        l.ForeColor = TextMuted;
                    else l.ForeColor = Text;
                    l.BackColor = Surface;
                }
                else if (c is Panel || c is FlowLayoutPanel) c.BackColor = Surface;
                if (c.HasChildren) Style(c);
            }
        }

        /// <summary>
        /// У NumericUpDown поле ввода — отдельный внутренний TextBox. Тема красит контрол,
        /// но не это поле, поэтому оно остаётся белым. Красим его явно.
        /// </summary>
        static void StyleUpDownEdit(NumericUpDown n)
        {
            foreach (Control child in n.Controls)
            {
                if (child is TextBoxBase)
                {
                    child.BackColor = SurfaceAlt;
                    child.ForeColor = Text;
                }
            }
        }

        /// <summary>Плоская кнопка с подсветкой при наведении.</summary>
        public static void StyleButton(Button b)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Border;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.MouseOverBackColor = ButtonHover;
            b.FlatAppearance.MouseDownBackColor = Accent;
            b.BackColor = ButtonFace;
            b.ForeColor = Text;
            b.Cursor = Cursors.Hand;
            b.Padding = new Padding(10, 2, 10, 2);
            b.UseVisualStyleBackColor = false;
            // Кнопку «Удалить» подсвечиваем красным, чтобы её нельзя было спутать с остальными.
            if (b.Text.StartsWith("Удалить"))
            {
                b.ForeColor = Danger;
                b.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 40, 48);
            }
        }
    }
}
