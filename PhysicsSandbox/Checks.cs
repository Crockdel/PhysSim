// ============================================================================
//  Checks.cs — автоматические проверки физической модели.
//
//  Запускаются без окна и интерфейса, поэтому подходят для быстрой проверки
//  после каждой правки: симуляция, сохранение, загрузка и — отдельно —
//  мгновенное применение свойств к выбранному телу.
// ============================================================================

using System;
using System.IO;
using System.Linq;
using PhysicsSandbox;
using tainicom.Aether.Physics2D.Collision.Shapes;
using tainicom.Aether.Physics2D.Common;
using tainicom.Aether.Physics2D.Dynamics;

class Checks
{
    /// <summary>Проверяет условие и выводит результат; при ошибке — исключение.</summary>
    static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }

    static void Main()
    {
        // Кириллица в консоли Windows корректно выводится только в UTF-8.
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }

        // --- соответствие числовых значений перечисления типам тел ---
        Require((int)BodyType.Static == 0 && (int)BodyType.Kinematic == 1 && (int)BodyType.Dynamic == 2,
            "body type mapping");

        // --- демонстрационная сцена и устойчивость расчёта ---
        var p = new PhysicsWorld();
        p.Load(PhysicsWorld.Demo());
        Require(p.Bodies.Count == 14, "demo has 14 objects");

        for (int i = 0; i < 1800; i++) p.Step();
        Require(p.Bodies.All(x => !float.IsNaN(x.Body.Position.Y) && x.Body.Position.Y < 16.1f && x.Body.Position.Y > -.1f),
            "30 seconds of collisions remain finite and inside walls");

        // --- сохранение и загрузка ---
        var saved = p.Capture();
        var path = System.IO.Path.GetTempFileName();
        PhysicsWorld.SaveFile(path, saved);
        var restored = new PhysicsWorld();
        restored.Load(PhysicsWorld.ReadFile(path));
        File.Delete(path);

        Require(restored.Bodies.Count == p.Bodies.Count && Math.Abs(restored.Time - p.Time) < 1e-8,
            "XML restores count and simulation time");
        Require(restored.Bodies.Zip(p.Bodies, (a, b) =>
                (a.Body.Position - b.Body.Position).LengthSquared() < 1e-8f &&
                Math.Abs(a.Body.Rotation - b.Body.Rotation) < 1e-5f &&
                (a.Body.LinearVelocity - b.Body.LinearVelocity).LengthSquared() < 1e-8f).All(x => x),
            "XML restores transforms and velocities");

        // --- гравитация ---
        p.Load(new SceneData());
        var ball = p.Add(new BodyData { X = 4, Y = 2 });
        p.Step();
        // Падение вниз: скорость по Y должна быть отрицательной (ось Y направлена вверх).
        Require(ball.Body.LinearVelocity.Y < 0, "gravity accelerates dynamic body downward");

        p.Settings.GravityEnabled = false;
        p.UpdateGravity();
        ball.Body.LinearVelocity = Vector2.Zero;
        p.Step();
        Require(ball.Body.LinearVelocity.Y == 0, "gravity can be disabled");

        // --- кинематическое и статическое тела ---
        p.Load(new SceneData());
        var k = p.Add(new BodyData { Type = 1, X = 4, Y = 2, Vx = 1 });
        var st = p.Add(new BodyData { Type = 0, X = 8, Y = 2 });
        for (int i = 0; i < 60; i++) p.Step();
        Require(Math.Abs(k.Body.Position.X - 5) < .01f && st.Body.Position.Y == 2,
            "kinematic velocity and static immobility");

        // --- проверка корректности файла ---
        bool rejected = false;
        try { PhysicsWorld.Validate(new SceneData { Step = float.NaN }); }
        catch (InvalidDataException) { rejected = true; }
        Require(rejected, "invalid scene rejected");

        // ====================================================================
        //  Мгновенное применение свойств (требование к интерфейсу)
        // ====================================================================

        // Плотность меняется — масса пересчитывается сразу, без перезапуска.
        p.Load(new SceneData());
        var box = p.Add(new BodyData { Shape = "Box", X = 6, Y = 4, Width = 2, Height = 2, Density = 1, Type = 2 });
        float massBefore = box.Body.Mass;
        p.Apply(box, new BodyData { Shape = "Box", Width = 2, Height = 2, Density = 4, Friction = .4f, Restitution = .5f });
        Require(box.Body.Mass > massBefore * 3.9f && box.Body.Mass < massBefore * 4.1f,
            "density change updates mass immediately (4x density -> 4x mass)");

        // Размер меняется — фикстура пересоздаётся, положение и скорости сохраняются.
        p.Load(new SceneData());
        p.Settings.GravityEnabled = false;
        p.UpdateGravity();
        var moving = p.Add(new BodyData
        {
            Shape = "Box", X = 10, Y = 6, Width = 1, Height = 1, Density = 2,
            Vx = 3, Vy = -2, Angle = .5f, Type = 2
        });
        p.Step();
        var positionBefore = moving.Body.Position;
        var rotationBefore = moving.Body.Rotation;
        var velocityBefore = moving.Body.LinearVelocity;
        float massSmall = moving.Body.Mass;

        bool rebuilt = p.Apply(moving, new BodyData
        {
            Shape = "Box", Width = 3, Height = 3, Density = 2, Friction = .4f, Restitution = .5f,
            Vx = velocityBefore.X, Vy = velocityBefore.Y
        });

        Require(rebuilt, "size change reports rebuilt fixture");
        Require(Math.Abs(moving.Body.Mass - massSmall * 9f) < .05f,
            "size change updates mass by area (3x3 vs 1x1 -> 9x mass)");
        Require((moving.Body.Position - positionBefore).LengthSquared() < 1e-6f,
            "size change keeps position");
        Require(Math.Abs(moving.Body.Rotation - rotationBefore) < 1e-5f,
            "size change keeps rotation");
        Require((moving.Body.LinearVelocity - velocityBefore).LengthSquared() < 1e-6f,
            "size change keeps velocity");
        Require(moving.Body.FixtureList.Count == 1 && moving.Body.FixtureList[0].Shape is PolygonShape,
            "size change leaves exactly one polygon fixture");

        // Смена формы с прямоугольника на круг.
        p.Apply(moving, new BodyData
        {
            Shape = "Circle", Width = 2, Height = 2, Density = 2, Friction = .4f, Restitution = .5f
        });
        Require(moving.Body.FixtureList.Count == 1 &&
                moving.Body.FixtureList[0].Shape is CircleShape,
            "shape change rebuilds fixture as circle");

        // Задание массы через плотность: плотность = масса ÷ площадь.
        p.Load(new SceneData());
        var viaMass = p.Add(new BodyData { Shape = "Box", X = 5, Y = 5, Width = 2, Height = 1, Density = 1, Type = 2 });
        float area = viaMass.Data.Area; // 2 × 1 = 2 м²
        float wantedMass = 10f;
        p.Apply(viaMass, new BodyData
        {
            Shape = "Box", Width = 2, Height = 1, Density = wantedMass / area, Friction = .4f, Restitution = .5f
        });
        Require(Math.Abs(viaMass.Body.Mass - wantedMass) < .01f,
            "mass can be set directly (density = mass / area)");

        // Трение и упругость применяются к существующей фикстуре без пересоздания.
        p.Load(new SceneData());
        var rub = p.Add(new BodyData { Shape = "Box", X = 5, Y = 5, Width = 1, Height = 1, Friction = 0, Restitution = 0, Type = 2 });
        p.Apply(rub, new BodyData
        {
            Shape = "Box", Width = 1, Height = 1, Density = 1, Friction = .9f, Restitution = .8f
        });
        Require(Math.Abs(rub.Body.FixtureList[0].Friction - .9f) < 1e-5f &&
                Math.Abs(rub.Body.FixtureList[0].Restitution - .8f) < 1e-5f,
            "friction and restitution apply immediately");

        // Скорость, заданная полем ввода, применяется к телу сразу.
        p.Load(new SceneData());
        p.Settings.GravityEnabled = false;
        p.UpdateGravity();
        var pushed = p.Add(new BodyData { X = 5, Y = 5, Width = 1, Type = 2 });
        p.Apply(pushed, new BodyData { Shape = "Circle", Width = 1, Height = 1, Density = 1, Vx = 4, Vy = 0 });
        Require(Math.Abs(pushed.Body.LinearVelocity.X - 4f) < 1e-4f,
            "velocity applies immediately");

        Console.WriteLine();
        Console.WriteLine("Все проверки пройдены.");
    }
}
