// ============================================================================
//  SimulationCanvas.cs — модуль визуализации.
//
//  Отдельный элемент управления, а не сама форма, чтобы включить двойную
//  буферизацию и не вмешиваться в отрисовку остальных панелей.
//  Координаты мира (метры) переводятся в пиксели коэффициентом масштаба.
// ============================================================================

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using tainicom.Aether.Physics2D.Dynamics;

namespace PhysicsSandbox
{
    /// <summary>
    /// Холст, на котором рисуется сцена. Логика отрисовки передаётся извне
    /// через делегат Render — так модуль графики не знает о физике.
    /// </summary>
    public class SimulationCanvas : Control
    {
        /// <summary>Делегат отрисовки: вызывается при каждой перерисовке.</summary>
        public Action<Graphics> Render;

        /// <summary>Подсказка, отображаемая в левом верхнем углу холста.</summary>
        public string Hint = "";

        public SimulationCanvas()
        {
            // Двойная буферизация устраняет мерцание при частой перерисовке.
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Color.FromArgb(19, 25, 38);
            TabStop = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Render != null) Render(e.Graphics);

            // Подсказка по текущему инструменту рисуется поверх сцены.
            if (!string.IsNullOrEmpty(Hint))
            {
                using (var brush = new SolidBrush(Color.FromArgb(195, 208, 229)))
                    e.Graphics.DrawString(Hint, Font, brush, 12, 12);
            }
        }
    }
}
