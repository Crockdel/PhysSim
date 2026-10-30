// ============================================================================
//  ZoomCheck.cs — проверка: при зуме колесом точка сцены под курсором не сдвигается,
//  и порядок полей «Ширина → Высота» в блоке нового тела сохраняется.
// ============================================================================
using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using tainicom.Aether.Physics2D.Common;

namespace PhysicsSandbox
{
    static class ZoomCheck
    {
        static int failed;
        static void Check(string name, bool ok, string details)
        {
            Console.WriteLine((ok ? "PASS: " : "FAIL: ") + name + "  [" + details + "]");
            if (!ok) failed++;
        }
        static T F<T>(object o, string n) { return (T)o.GetType().GetField(n, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(o); }

        [STAThread]
        static int Main()
        {
            var form = new MainForm();
            form.Show();
            Application.DoEvents();
            var canvas = F<Control>(form, "canvas");
            var side = F<Control>(form, "side");
            var mode = F<ComboBox>(form, "mode");
            var shape = F<ComboBox>(form, "shape");

            // --- зум относительно курсора ---
            var wheel = typeof(MainForm).GetMethod("WheelScene", BindingFlags.Instance | BindingFlags.NonPublic);
            var worldPoint = typeof(MainForm).GetMethod("WorldPoint", BindingFlags.Instance | BindingFlags.NonPublic);
            Point cursor = new Point(canvas.Width / 4, canvas.Height / 3);   // точка не в центре
            Vector2 before = (Vector2)worldPoint.Invoke(form, new object[] { cursor });

            wheel.Invoke(form, new object[] { canvas, new MouseEventArgs(MouseButtons.None, 0, cursor.X, cursor.Y, 120) });
            Application.DoEvents();
            Vector2 after = (Vector2)worldPoint.Invoke(form, new object[] { cursor });
            float drift = Math.Abs(before.X - after.X) + Math.Abs(before.Y - after.Y);
            Check("зум колесом: точка под курсором не сдвигается", drift < .01f, "сдвиг " + drift.ToString("0.0000") + " м");

            for (int i = 0; i < 4; i++)
                wheel.Invoke(form, new object[] { canvas, new MouseEventArgs(MouseButtons.None, 0, cursor.X, cursor.Y, -120) });
            Application.DoEvents();
            after = (Vector2)worldPoint.Invoke(form, new object[] { cursor });
            drift = Math.Abs(before.X - after.X) + Math.Abs(before.Y - after.Y);
            Check("отдаление колесом: точка под курсором не сдвигается", drift < .01f, "сдвиг " + drift.ToString("0.0000") + " м");

            // --- порядок полей нового тела: Ширина, Высота, Плотность ---
            mode.SelectedIndex = 1; shape.SelectedIndex = 1; Application.DoEvents();
            var rows = new System.Collections.Generic.List<string>();
            var newBox = F<Control>(form, "newBodyBox");   // блок нового тела
            foreach (Control c in newBox.Controls)
                if (c.Visible && c is Panel && c.Controls.Count > 0 && c.Controls[0] is Label) rows.Add(((Label)c.Controls[0]).Text);
            int iw = rows.FindIndex(t => t.StartsWith("Ширина")), ih = rows.FindIndex(t => t.StartsWith("Высота"));
            Check("прямоугольник: «Высота» сразу после «Ширины»", iw >= 0 && ih == iw + 1,
                  "порядок: " + string.Join(" | ", rows));

            form.Close();
            Console.WriteLine(failed == 0 ? "\nЗум и порядок полей в порядке." : "\nЕСТЬ ОШИБКИ: " + failed);
            return failed;
        }
    }
}
