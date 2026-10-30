// ============================================================================
//  FeaturesCheck.cs — проверка четырёх новых возможностей окна:
//    1. панорамирование камеры средней кнопкой мыши;
//    2. блок «Параметры нового тела» виден только при инструменте «Создать объект»;
//    3. для круга скрыто поле «Высота», а «Ширина» называется «Диаметр»;
//    4. кнопка «Удалить» удаляет выбранное тело.
// ============================================================================
using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace PhysicsSandbox
{
    static class FeaturesCheck
    {
        static int failed;

        static void Check(string name, bool ok, string details)
        {
            Console.WriteLine((ok ? "PASS: " : "FAIL: ") + name + "  [" + details + "]");
            if (!ok) failed++;
        }

        static T F<T>(object o, string name)
        {
            return (T)o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(o);
        }

        static void Call(object o, string name, params object[] args)
        {
            o.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Invoke(o, args);
        }

        static Control FindButton(Control root, string text)
        {
            foreach (Control c in root.Controls)
            {
                if (c is Button && c.Text == text) return c;
                var found = FindButton(c, text);
                if (found != null) return found;
            }
            return null;
        }

        [STAThread]
        static int Main()
        {
            var form = new MainForm();
            form.Show();
            Application.DoEvents();

            var canvas = F<Control>(form, "canvas");
            var mode = F<ComboBox>(form, "mode");
            var shape = F<ComboBox>(form, "shape");
            var rowWidth = F<Panel>(form, "rowWidth");
            var rowHeight = F<Panel>(form, "rowHeight");
            var newBodyTitle = F<Label>(form, "newBodyTitle");
            var physics = F<PhysicsWorld>(form, "physics");

            // ---------- 2. блок нового тела только при «Создать объект» ----------
            mode.SelectedIndex = 0; Application.DoEvents();
            Check("инструмент «Выбор»: блок «Параметры нового тела» скрыт", !newBodyTitle.Visible && !rowWidth.Visible,
                  "заголовок " + newBodyTitle.Visible + ", ширина " + rowWidth.Visible);

            mode.SelectedIndex = 1; Application.DoEvents();
            Check("инструмент «Создать объект»: блок показан", newBodyTitle.Visible && rowWidth.Visible,
                  "заголовок " + newBodyTitle.Visible + ", ширина " + rowWidth.Visible);

            // ---------- 3. круг: нет высоты, «Диаметр» ----------
            shape.SelectedIndex = 0; Application.DoEvents();   // Круг
            var caption = F<Label>(form, "widthCaption");
            Check("круг: поле «Высота» скрыто", !rowHeight.Visible, "высота видима = " + rowHeight.Visible);
            Check("круг: подпись «Диаметр»", caption.Text.StartsWith("Диаметр"), "подпись = «" + caption.Text + "»");

            shape.SelectedIndex = 1; Application.DoEvents();   // Прямоугольник
            Check("прямоугольник: поле «Высота» показано", rowHeight.Visible, "высота видима = " + rowHeight.Visible);
            Check("прямоугольник: подпись «Ширина»", caption.Text.StartsWith("Ширина"), "подпись = «" + caption.Text + "»");

            mode.SelectedIndex = 0; Application.DoEvents();
            shape.SelectedIndex = 0; Application.DoEvents();

            // ---------- 4. кнопка «Удалить» ----------
            // Ставим тело программно и выбираем его, затем жмём кнопку.
            var sceneBefore = physics.Bodies.Count;
            var item = physics.Add(new BodyData { Shape = "Box", Type = 2, X = 12, Y = 10, Width = 1, Height = 1, Density = 1, Friction = .35f, Restitution = .2f });
            F<object>(form, "selected");   // проверка, что поле доступно
            typeof(MainForm).GetField("selected", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, item);
            Call(form, "UpdateSelectedPanel");

            var del = FindButton(form, "Удалить") as Button;
            Check("кнопка «Удалить» есть на панели", del != null, "");
            if (del != null) del.PerformClick();
            Application.DoEvents();
            Check("«Удалить» убирает выбранное тело", physics.Bodies.Count == sceneBefore && !physics.Bodies.Contains(item),
                  "тел было " + sceneBefore + ", стало " + physics.Bodies.Count);
            Check("после удаления выбор сброшен", F<object>(form, "selected") == null, "");

            // ---------- 1. панорамирование средней кнопкой ----------
            float panX0 = F<float>(form, "panX"), panY0 = F<float>(form, "panY");
            // Средняя кнопка: нажатие → движение на (+40, -25) → отпускание.
            var down = new MouseEventArgs(MouseButtons.Middle, 1, 300, 300, 0);
            var move = new MouseEventArgs(MouseButtons.Middle, 0, 340, 275, 0);
            var up = new MouseEventArgs(MouseButtons.Middle, 1, 340, 275, 0);
            canvas.GetType().GetEvent("MouseDown");
            typeof(Control).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(canvas, new object[] { down });
            typeof(Control).GetMethod("OnMouseMove", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(canvas, new object[] { move });
            typeof(Control).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(canvas, new object[] { up });
            Application.DoEvents();
            float dx = F<float>(form, "panX") - panX0, dy = F<float>(form, "panY") - panY0;
            Check("средняя кнопка сдвигает камеру", Math.Abs(dx - 40) < .5f && Math.Abs(dy + 25) < .5f,
                  "сдвиг (" + dx + ", " + dy + ")");
            Check("средняя кнопка не создаёт тело", physics.Bodies.Count == sceneBefore, "тел " + physics.Bodies.Count);

            // «Вписать сцену» сбрасывает камеру
            var fit = FindButton(form, "Вписать сцену") as Button;
            if (fit != null) fit.PerformClick();
            Check("«Вписать сцену» возвращает камеру в центр", F<float>(form, "panX") == 0 && F<float>(form, "panY") == 0, "");

            form.Close();
            Console.WriteLine(failed == 0 ? "\nВсе новые возможности работают." : "\nЕСТЬ ОШИБКИ: " + failed);
            return failed;
        }
    }
}
