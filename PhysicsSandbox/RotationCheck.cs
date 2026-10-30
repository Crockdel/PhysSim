// ============================================================================
//  RotationCheck.cs — проверка, что нарисованный прямоугольник совпадает с
//  физическим телом при любом угле поворота.
//
//  Берём длинную тонкую платформу под углом, находим по физике положение её
//  конца (через body.GetWorldPoint) и смотрим реальный пиксель на холсте:
//    * в точке физического конца должен быть цвет платформы;
//    * в зеркальной точке (если отрисовка повёрнута не в ту сторону) — фон.
// ============================================================================
using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using tainicom.Aether.Physics2D.Common;

namespace PhysicsSandbox
{
    static class RotationCheck
    {
        static int failed;

        static void Check(string name, bool ok, string details)
        {
            Console.WriteLine((ok ? "PASS: " : "FAIL: ") + name + "  [" + details + "]");
            if (!ok) failed++;
        }

        static T Field<T>(object o, string name)
        {
            return (T)o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(o);
        }

        static bool Near(Color a, Color b, int tol)
        {
            return Math.Abs(a.R - b.R) <= tol && Math.Abs(a.G - b.G) <= tol && Math.Abs(a.B - b.B) <= tol;
        }

        [STAThread]
        static int Main()
        {
            var form = new MainForm();
            form.Show();
            Application.DoEvents();

            var set = typeof(MainForm).GetMethod("SetScene", BindingFlags.Instance | BindingFlags.NonPublic);
            var canvas = Field<Control>(form, "canvas");
            var physics = Field<PhysicsWorld>(form, "physics");

            float[] angles = { 0.5f, -0.5f, 1.2f, 2.6f, -2.0f };
            foreach (float angle in angles)
            {
                // Сцена из одной статической платформы 6 x 0.6 м в центре мира.
                var scene = new SceneData();
                int plate = unchecked((int)0xffff0000);          // ярко-красный, не спутать с фоном и сеткой
                scene.Bodies.Add(new BodyData
                {
                    Shape = "Box", Type = 0, X = 12, Y = 8, Width = 6, Height = .6f, Angle = angle, Color = plate
                });
                set.Invoke(form, new object[] { scene });
                Application.DoEvents();

                using (var bmp = new Bitmap(canvas.Width, canvas.Height))
                {
                    canvas.DrawToBitmap(bmp, new Rectangle(0, 0, canvas.Width, canvas.Height));
                    float scale = Field<float>(form, "scale"), ox = Field<float>(form, "ox"), oy = Field<float>(form, "oy");

                    // Мировая точка -> пиксель холста (обратно к WorldPoint в MainForm).
                    Func<Vector2, Point> toPixel = w =>
                        new Point((int)Math.Round(ox + w.X * scale),
                                  (int)Math.Round(oy + (PhysicsWorld.Height - w.Y) * scale));

                    var body = physics.Bodies[0].Body;
                    // Точки на осевой линии платформы: 2,5 м от центра вдоль её длинной стороны.
                    Vector2 endPhysics = body.GetWorldPoint(new Vector2(2.5f, 0));
                    Vector2 endMirror = body.GetWorldPoint(new Vector2(2.5f, 0));
                    // Зеркальный вариант: тот же вылет, но с обратным знаком угла.
                    float c = (float)Math.Cos(-angle), s = (float)Math.Sin(-angle);
                    endMirror = new Vector2(12 + 2.5f * c, 8 + 2.5f * s);

                    Point pp = toPixel(endPhysics), pm = toPixel(endMirror);
                    Color cp = bmp.GetPixel(pp.X, pp.Y), cm = bmp.GetPixel(pm.X, pm.Y);
                    Color red = Color.FromArgb(plate);

                    bool distinct = Math.Abs(pp.X - pm.X) + Math.Abs(pp.Y - pm.Y) > 20;   // для угла ~0 тест не имеет смысла
                    Check(string.Format("угол {0:0.0} рад: конец платформы нарисован там, где он в физике", angle),
                          Near(cp, red, 40),
                          string.Format("пиксель физ.конца {0}={1}", pp, cp));
                    if (distinct)
                        Check(string.Format("угол {0:0.0} рад: в зеркальной точке платформы нет", angle),
                              !Near(cm, red, 40),
                              string.Format("пиксель зеркала {0}={1}", pm, cm));
                }
            }

            form.Close();
            Console.WriteLine(failed == 0 ? "\nОтрисовка совпадает с физикой." : "\nЕСТЬ ОШИБКИ: " + failed);
            return failed;
        }
    }
}
