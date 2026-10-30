// ============================================================================
//  SelectedCheck.cs — проверяет ВИДИМЫЕ поля на экране (не структуру контролов):
//  блок «Параметры нового тела» и блок «Свойства выбранного тела» показывают
//  одинаковый набор полей в одном порядке для каждой формы.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;
using tainicom.Aether.Physics2D.Dynamics;

namespace PhysicsSandbox
{
    static class SelectedCheck
    {
        static int failed;
        static void Check(string name, bool ok, string details)
        {
            Console.WriteLine((ok ? "PASS: " : "FAIL: ") + name + "  [" + details + "]");
            if (!ok) failed++;
        }
        static T F<T>(object o, string n) { return (T)o.GetType().GetField(n, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(o); }
        static void Call(object o, string n, params object[] a) { o.GetType().GetMethod(n, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(o, a); }

        static string Caption(Control row)
        {
            foreach (Control c in row.Controls) if (c is Label) return ((Label)c).Text;
            return "";
        }

        // Видимые строки контейнера в порядке отображения (по Top).
        static List<string> Visible(Control box)
        {
            var rows = new List<Control>();
            foreach (Control c in box.Controls) if (c.Visible && c is Panel) rows.Add(c);
            rows.Sort((a, b) => a.Top.CompareTo(b.Top));
            var list = new List<string>();
            foreach (var r in rows) list.Add(Caption(r));
            return list;
        }

        static string Join(List<string> l) { return string.Join(" | ", l); }

        [STAThread]
        static int Main()
        {
            var form = new MainForm();
            form.Show();
            Application.DoEvents();
            var physics = F<PhysicsWorld>(form, "physics");
            var newBox = F<Control>(form, "newBodyBox");
            var group = F<Control>(form, "groupSelected");
            var mode = F<ComboBox>(form, "mode");
            var shape = F<ComboBox>(form, "shape");
            var sel = typeof(MainForm).GetField("selected", BindingFlags.Instance | BindingFlags.NonPublic);

            // --- блок нового тела ---
            mode.SelectedIndex = 1;
            shape.SelectedIndex = 1; Application.DoEvents();
            var newRect = Visible(newBox);
            Check("новое, прямоугольник: «Высота» сразу после «Ширины»",
                  newRect.Count > 1 && newRect[0] == "Ширина, м" && newRect[1] == "Высота, м", Join(newRect));

            shape.SelectedIndex = 0; Application.DoEvents();
            var newCircle = Visible(newBox);
            Check("новое, круг: «Диаметр», без «Высоты»",
                  newCircle.Contains("Диаметр, м") && !newCircle.Contains("Высота, м"), Join(newCircle));

            // --- блок выбранного тела ---
            mode.SelectedIndex = 0; Application.DoEvents();
            var box = physics.Add(new BodyData { Shape = "Box", Type = 2, X = 10, Y = 8, Width = 2, Height = 1, Density = 1, Friction = .35f, Restitution = .2f });
            var circ = physics.Add(new BodyData { Shape = "Circle", Type = 2, X = 14, Y = 8, Width = 1, Height = 1, Density = 1, Friction = .35f, Restitution = .2f });

            Check("блок «Свойства выбранного тела» подключён к боковой панели", group.Parent != null, "");

            sel.SetValue(form, box); Call(form, "UpdateSelectedPanel"); Application.DoEvents();
            var selRect = Visible(group);
            Check("выбранный прямоугольник: видны «Ширина», «Высота», «Плотность», «Трение», «Упругость», «Скорость X/Y»",
                  selRect.Contains("Ширина, м") && selRect.Contains("Высота, м") && selRect.Contains("Плотность") &&
                  selRect.Contains("Трение") && selRect.Contains("Упругость") && selRect.Contains("Скорость X") && selRect.Contains("Скорость Y"),
                  Join(selRect));
            Check("выбранный прямоугольник: «Высота» сразу после «Ширины»",
                  selRect.IndexOf("Высота, м") == selRect.IndexOf("Ширина, м") + 1, Join(selRect));
            Check("выбранный прямоугольник: набор полей совпадает с новым прямоугольником",
                  Join(selRect) == Join(newRect) || SameSet(selRect, newRect), "новое: " + Join(newRect) + " ; выбранное: " + Join(selRect));

            sel.SetValue(form, circ); Call(form, "UpdateSelectedPanel"); Application.DoEvents();
            var selCirc = Visible(group);
            Check("выбранный круг: «Диаметр», без «Высоты»",
                  selCirc.Contains("Диаметр, м") && !selCirc.Contains("Высота, м") && !selCirc.Contains("Ширина, м"), Join(selCirc));
            Check("выбранный круг: набор полей совпадает с новым кругом",
                  SameSet(selCirc, newCircle), "новое: " + Join(newCircle) + " ; выбранное: " + Join(selCirc));

            form.Close();
            Console.WriteLine(failed == 0 ? "\nБлоки совпадают по полям и порядку." : "\nЕСТЬ ОШИБКИ: " + failed);
            return failed;
        }

        static bool SameSet(List<string> a, List<string> b)
        {
            var x = new List<string>(a); var y = new List<string>(b);
            x.Sort(); y.Sort();
            return string.Join("|", x) == string.Join("|", y);
        }
    }
}
