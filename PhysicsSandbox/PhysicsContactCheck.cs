// ============================================================================
//  PhysicsContactCheck.cs — проверка трения, которое меняется во время контакта.
//  Сценарий: куб стоит на наклонной платформе с трением 0,35 (держится), затем
//  трение становится 0 — куб должен соскользнуть, в том числе после повторной
//  постановки на платформу.
// ============================================================================
using System;
using tainicom.Aether.Physics2D.Common;

namespace PhysicsSandbox
{
    static class PhysicsContactCheck
    {
        static int failed;

        static void Check(string name, bool ok, string details)
        {
            Console.WriteLine((ok ? "PASS: " : "FAIL: ") + name + "  [" + details + "]");
            if (!ok) failed++;
        }

        static SceneData Scene(float cubeY)
        {
            var s = new SceneData();
            // Наклонная платформа 6 x 0.4 м под углом 0.3 рад (тангенс ~0.31 < 0.35 — удерживает).
            s.Bodies.Add(new BodyData { Shape = "Box", Type = 0, X = 8, Y = 6, Width = 6, Height = .4f, Angle = .3f, Friction = .35f, Restitution = .2f });
            s.Bodies.Add(new BodyData { Shape = "Box", Type = 2, X = 8, Y = cubeY, Width = 1, Height = 1, Friction = .35f, Restitution = .2f });
            return s;
        }

        static int Main()
        {
            // 1. Контроль: при трении 0,35 куб садится и остаётся на месте.
            var a = new PhysicsWorld();
            a.Load(Scene(6.7f));
            var cubeA = a.Bodies[1];
            for (int i = 0; i < 300; i++) a.Step();
            Check("трение 0,35: куб держится на платформе", Math.Abs(cubeA.Body.Position.X - 7.7f) < .2f,
                  "x=" + cubeA.Body.Position.X.ToString("0.00"));

            // 2. Трение меняем на 0 во время контакта — куб должен соскользнуть.
            var b = new PhysicsWorld();
            b.Load(Scene(6.7f));
            var cubeB = b.Bodies[1];
            for (int i = 0; i < 300; i++) b.Step();
            float xStuck = cubeB.Body.Position.X;
            b.Apply(cubeB, new BodyData { Shape = "Box", Width = 1, Height = 1, Density = cubeB.Data.Density, Friction = 0f, Restitution = .2f });
            for (int i = 0; i < 120; i++) b.Step();
            Check("трение 0 во время контакта: куб соскальзывает", cubeB.Body.Position.X < xStuck - .5f,
                  "x " + xStuck.ToString("0.00") + " -> " + cubeB.Body.Position.X.ToString("0.00"));

            // 3. Поднять и поставить обратно на платформу при трении 0 — тоже соскальзывает.
            cubeB.Body.Position = new Vector2(8, 9);
            cubeB.Body.LinearVelocity = Vector2.Zero;
            cubeB.Body.AngularVelocity = 0;
            cubeB.Body.Awake = true;
            for (int i = 0; i < 300; i++) b.Step();
            Check("поставлен обратно при трении 0: соскальзывает", cubeB.Body.Position.X < 7.5f,
                  "x=" + cubeB.Body.Position.X.ToString("0.00"));

            Console.WriteLine(failed == 0 ? "\nТрение учитывается в контакте." : "\nЕСТЬ ОШИБКИ: " + failed);
            return failed;
        }
    }
}
