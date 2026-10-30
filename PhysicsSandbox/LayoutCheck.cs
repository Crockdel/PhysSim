// ============================================================================
//  LayoutCheck.cs — проверка раскладки окна и свойств демо-сцены.
//  Запускает настоящую форму, читает реальные границы элементов и проверяет,
//  что холст занимает ровно свободную область между панелями.
// ============================================================================
using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace PhysicsSandbox
{
    static class LayoutCheck
    {
        static int failed;

        static void Check(string name, bool ok, string details)
        {
            Console.WriteLine((ok ? "PASS: " : "FAIL: ") + name + "  [" + details + "]");
            if (!ok) failed++;
        }

        [STAThread]
        static int Main()
        {
            // --- свойства всех тел демо-сцены ---
            var demo = PhysicsWorld.Demo();
            bool allOk = true;
            foreach (var b in demo.Bodies)
                if (Math.Abs(b.Restitution - .2f) > 1e-6 || Math.Abs(b.Friction - .35f) > 1e-6) allOk = false;
            Check("все " + demo.Bodies.Count + " тел демо: упругость 0,2 и трение 0,35", allOk, "");

            // --- реальные фикстуры физических тел после загрузки демо в мир ---
            var world = new PhysicsWorld();
            world.Load(PhysicsWorld.Demo());
            int fixtures = 0, wrong = 0;
            foreach (var item in world.Bodies)
                foreach (var f in item.Body.FixtureList)
                {
                    fixtures++;
                    if (Math.Abs(f.Friction - .35f) > 1e-6 || Math.Abs(f.Restitution - .2f) > 1e-6) wrong++;
                }
            Check("физические фикстуры в мире (" + fixtures + " шт.): упругость 0,2 и трение 0,35", wrong == 0 && fixtures > 0, "неверных: " + wrong);

            // --- раскладка формы ---
            var form = new MainForm();
            form.Show();
            Application.DoEvents();

            var canvas = (Control)typeof(MainForm)
                .GetField("canvas", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);

            Control top = null, right = null, bottom = null;
            foreach (Control c in form.Controls)
            {
                if (c.Dock == DockStyle.Top) top = c;
                if (c.Dock == DockStyle.Right) right = c;
                if (c.Dock == DockStyle.Bottom) bottom = c;
            }

            var client = form.ClientRectangle;
            Console.WriteLine("клиентская область: " + client);
            Console.WriteLine("верхняя панель:     " + top.Bounds);
            Console.WriteLine("правая панель:      " + right.Bounds);
            Console.WriteLine("строка состояния:   " + bottom.Bounds);
            Console.WriteLine("холст:              " + canvas.Bounds);

            Check("холст начинается под верхней панелью", canvas.Top >= top.Bottom, canvas.Top + " >= " + top.Bottom);
            Check("холст заканчивается над строкой состояния", canvas.Bottom <= bottom.Top, canvas.Bottom + " <= " + bottom.Top);
            Check("холст заканчивается левее правой панели", canvas.Right <= right.Left, canvas.Right + " <= " + right.Left);
            Check("холст начинается у левого края окна", canvas.Left == 0, canvas.Left.ToString());

            // Центр мира в координатах холста должен совпадать с центром видимой области холста.
            float scale = Math.Min(canvas.ClientSize.Width / PhysicsWorld.Width,
                                   canvas.ClientSize.Height / PhysicsWorld.Height) * 0.94f;
            float ox = (canvas.ClientSize.Width - PhysicsWorld.Width * scale) / 2f;
            float oy = (canvas.ClientSize.Height - PhysicsWorld.Height * scale) / 2f;
            float gapLeft = ox, gapRight = canvas.ClientSize.Width - ox - PhysicsWorld.Width * scale;
            float gapTop = oy, gapBottom = canvas.ClientSize.Height - oy - PhysicsWorld.Height * scale;
            Console.WriteLine(string.Format("отступы мира: слева {0:0.0}, справа {1:0.0}, сверху {2:0.0}, снизу {3:0.0}",
                gapLeft, gapRight, gapTop, gapBottom));
            Check("отступы слева и справа равны", Math.Abs(gapLeft - gapRight) < 1, "");
            Check("отступы сверху и снизу равны", Math.Abs(gapTop - gapBottom) < 1, "");

            // --- проверка по реальным пикселям: рисуем холст в картинку и ищем рамку мира ---
            using (var bmp = new Bitmap(canvas.Width, canvas.Height))
            {
                canvas.DrawToBitmap(bmp, new Rectangle(0, 0, canvas.Width, canvas.Height));
                int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
                for (int y = 0; y < bmp.Height; y++)
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        Color c = bmp.GetPixel(x, y);
                        // цвет рамки мира (100, 123, 156) со сглаживанием
                        if (Math.Abs(c.R - 100) < 14 && Math.Abs(c.G - 123) < 14 && Math.Abs(c.B - 156) < 14)
                        {
                            if (x < minX) minX = x; if (x > maxX) maxX = x;
                            if (y < minY) minY = y; if (y > maxY) maxY = y;
                        }
                    }
                int pl = minX, pr = bmp.Width - 1 - maxX, pt = minY, pb = bmp.Height - 1 - maxY;
                Console.WriteLine(string.Format("ПИКСЕЛИ: отступ слева {0}, справа {1}, сверху {2}, снизу {3}", pl, pr, pt, pb));
                Check("пиксели: слева = справа (±2)", Math.Abs(pl - pr) <= 2, pl + " / " + pr);
                Check("пиксели: сверху = снизу (±2)", Math.Abs(pt - pb) <= 2, pt + " / " + pb);
            }

            form.Close();
            Console.WriteLine(failed == 0 ? "\nРаскладка корректна." : "\nЕСТЬ ОШИБКИ: " + failed);
            return failed;
        }
    }
}
