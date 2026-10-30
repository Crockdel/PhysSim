// ============================================================================
//  UiChecks.cs — проверка интерфейса без участия человека.
//
//  Создаёт главную форму в скрытом режиме, обходит дерево элементов
//  управления и убеждается, что у каждого интерактивного элемента есть
//  всплывающая подсказка. Дополнительно печатает список полей и снимает
//  снимок окна для визуального контроля.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using tainicom.Aether.Physics2D.Dynamics;

namespace PhysicsSandbox
{
    internal static class UiChecks
    {
        /// <summary>Собирает все элементы формы рекурсивно.</summary>
        static IEnumerable<Control> Walk(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (var nested in Walk(child)) yield return nested;
            }
        }

        [STAThread]
        static int Main()
        {
            Application.EnableVisualStyles();
            // Кириллица в консоли Windows корректно выводится только в UTF-8.
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }

            var form = new MainForm();
            form.Show();                                   // форма должна быть видимой для создания дескрипторов
            Application.DoEvents();

            // Достаём менеджер подсказок через отражение: поле закрытое.
            var field = typeof(MainForm).GetField("tip", BindingFlags.NonPublic | BindingFlags.Instance);
            var tip = (ToolTip)field.GetValue(form);

            int interactive = 0, missing = 0;
            foreach (var c in Walk(form))
            {
                bool needsHint = c is Button || c is ComboBox || c is NumericUpDown ||
                                 c is CheckBox || c is SimulationCanvas;
                if (!needsHint) continue;

                interactive++;
                string text = tip.GetToolTip(c);
                if (string.IsNullOrWhiteSpace(text))
                {
                    missing++;
                    Console.WriteLine("НЕТ ПОДСКАЗКИ: " + c.GetType().Name + " \"" + c.Text + "\"");
                }
            }

            Console.WriteLine("Интерактивных элементов: " + interactive + ", без подсказки: " + missing);

            // --- проверка мгновенного применения свойств через поля интерфейса ---
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var physics = (PhysicsWorld)typeof(MainForm).GetField("physics", flags).GetValue(form);
            var selectBody = typeof(MainForm).GetMethod("UpdateSelectedPanel", flags);
            var selWidth = (NumericUpDown)typeof(MainForm).GetField("selWidth", flags).GetValue(form);
            var selMass = (NumericUpDown)typeof(MainForm).GetField("selMass", flags).GetValue(form);

            var target = physics.Bodies.First(x => x.Body.BodyType == BodyType.Dynamic && x.Data.Shape == "Box");
            typeof(MainForm).GetField("selected", flags).SetValue(form, target);
            selectBody.Invoke(form, null);
            Application.DoEvents();

            decimal massFieldBefore = selMass.Value;
            float massBefore = target.Body.Mass;
            var positionBefore = target.Body.Position;

            // Меняем ширину в поле — значение должно примениться без каких-либо кнопок.
            selWidth.Value = selWidth.Value + 0.5m;
            Application.DoEvents();

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "До:    ширина {0:0.00} м, масса {1:0.00} кг, поле массы {2:0.00}",
                target.Data.Width - 0.5f, massBefore, massFieldBefore));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "После: ширина {0:0.00} м, масса {1:0.00} кг, поле массы {2:0.00}",
                target.Data.Width, target.Body.Mass, selMass.Value));

            bool massChanged = Math.Abs(target.Body.Mass - massBefore) > 0.01f;
            bool massFieldSynced = Math.Abs((float)selMass.Value - target.Body.Mass) < 0.05f;
            bool positionKept = (target.Body.Position - positionBefore).LengthSquared() < 1e-4f;

            Console.WriteLine(massChanged && massFieldSynced && positionKept
                ? "PASS: изменение ширины в интерфейсе применено сразу, масса и положение корректны"
                : "FAIL: изменение ширины не применилось как ожидалось");
            if (!(massChanged && massFieldSynced && positionKept)) missing++;

            // Кнопки верхней панели и поля боковой панели — для отчёта.
            var buttons = Walk(form).OfType<Button>().Select(b => b.Text).ToArray();
            var numbers = Walk(form).OfType<NumericUpDown>().Count();
            var combos = Walk(form).OfType<ComboBox>().Count();
            Console.WriteLine("Кнопок: " + buttons.Length + " (" + string.Join(", ", buttons) + ")");
            Console.WriteLine("Числовых полей: " + numbers + ", списков: " + combos);

            // Снимок окна.
            try
            {
                var bounds = form.Bounds;
                using (var bmp = new Bitmap(bounds.Width, bounds.Height))
                {
                    form.DrawToBitmap(bmp, new Rectangle(0, 0, bounds.Width, bounds.Height));
                    bmp.Save("/tmp/build/ui.png", ImageFormat.Png);
                    Console.WriteLine("Снимок окна сохранён: /tmp/build/ui.png");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Снимок не сделан: " + ex.Message);
            }

            form.Close();
            return missing == 0 ? 0 : 1;
        }
    }
}
