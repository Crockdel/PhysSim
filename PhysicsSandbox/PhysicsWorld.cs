// ============================================================================
//  PhysicsWorld.cs — физическая модель симулятора.
//
//  Класс PhysicsWorld инкапсулирует работу с библиотекой Aether.Physics2D:
//  создание тел, изменение их свойств «на лету», сохранение и загрузка сцен.
//  Остальные модули приложения (форма, холст) не зависят от типов библиотеки
//  напрямую — они работают только через этот класс.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using tainicom.Aether.Physics2D.Common;
using tainicom.Aether.Physics2D.Dynamics;
using tainicom.Aether.Physics2D.Dynamics.Contacts;
using tainicom.Aether.Physics2D.Dynamics.Joints;

namespace PhysicsSandbox
{
    /// <summary>
    /// Сериализуемое описание одного тела.
    /// Именно этот класс, а не объект Body, попадает в файл сцены: объекты
    /// библиотеки содержат ссылки друг на друга, и их прямая сериализация
    /// привела бы к циклическим ссылкам и огромным файлам.
    /// </summary>
    [Serializable]
    public class BodyData
    {
        /// <summary>Форма тела: "Circle" (круг) или "Box" (прямоугольник).</summary>
        public string Shape = "Circle";

        /// <summary>Тип тела: 0 — статическое, 1 — кинематическое, 2 — динамическое.</summary>
        public int Type = 2;

        /// <summary>Положение центра, угол поворота и скорости.</summary>
        public float X, Y, Angle, Vx, Vy, AngularVelocity;

        /// <summary>Геометрические размеры (для круга используется Width как диаметр).</summary>
        public float Width = 1, Height = 1;

        /// <summary>Плотность материала, кг/м². Из неё библиотека вычисляет массу.</summary>
        public float Density = 1;

        /// <summary>Коэффициенты трения и упругости (от 0 до 1).</summary>
        public float Friction = .35f, Restitution = .2f;

        /// <summary>Цвет заливки в формате ARGB.</summary>
        public int Color = unchecked((int)0xff4dc9f6);

        /// <summary>Поверхностная копия — нужна, чтобы не отдавать наружу ссылку на исходные данные.</summary>
        public BodyData Copy() { return (BodyData)MemberwiseClone(); }

        /// <summary>Площадь фигуры в м². По ней масса пересчитывается в плотность и обратно.</summary>
        public float Area
        {
            get
            {
                if (Shape == "Circle")
                {
                    float r = Width / 2f;          // Width хранит диаметр круга
                    return (float)(Math.PI * r * r);
                }
                return Width * Height;
            }
        }
    }

    /// <summary>
    /// Сериализуемое описание всей сцены: параметры мира и список тел.
    /// </summary>
    [Serializable]
    public class SceneData
    {
        /// <summary>Версия формата — защищает от загрузки несовместимых файлов.</summary>
        public int Version = 1;

        /// <summary>Вектор гравитации, шаг интегрирования и коэффициенты по умолчанию.</summary>
        // GravityY < 0 означает падение вниз: ось Y в Aether.Physics2D направлена вверх.
        public float GravityX, GravityY = -9.81f, Step = 1f / 60;

        /// <summary>
        /// Подшаги: один кадр делится на несколько шагов по Step/SubSteps.
        /// Ошибка интегрирования падает примерно пропорционально числу подшагов.
        /// </summary>
        public int SubSteps = 2;
        public float DefaultFriction = .35f, DefaultRestitution = .2f;

        /// <summary>Включена ли гравитация.</summary>
        public bool GravityEnabled = true;

        /// <summary>Время симуляции в секундах.</summary>
        public double Time;

        /// <summary>Список тел сцены.</summary>
        public List<BodyData> Bodies = new List<BodyData>();
    }

    /// <summary>
    /// Пара «тело библиотеки + его описание». Хранится в списке сцены,
    /// а также записывается в свойство Body.Tag, чтобы по фикстуре,
    /// найденной под курсором, можно было мгновенно получить описание.
    /// </summary>
    public class SceneBody
    {
        public Body Body;
        public BodyData Data;
    }

    /// <summary>
    /// Физический мир приложения: обёртка над World из Aether.Physics2D.
    /// </summary>
    public class PhysicsWorld
    {
        /// <summary>Размер области симуляции в метрах. Стены строятся по этому размеру.</summary>
        public const float Width = 24, Height = 16;

        /// <summary>Мир библиотеки. Открыт наружу только для операций с соединениями.</summary>
        public World World { get; private set; }

        /// <summary>Все пользовательские тела сцены (без стен).</summary>
        public List<SceneBody> Bodies = new List<SceneBody>();

        /// <summary>Текущие параметры мира.</summary>
        public SceneData Settings = new SceneData();

        /// <summary>Время симуляции в секундах.</summary>
        public double Time;

        public PhysicsWorld() { Load(new SceneData()); }

        /// <summary>
        /// Создаёт тело по описанию и добавляет его в мир.
        /// Все свойства (плотность, трение, упругость, скорости) применяются
        /// сразу при создании, поэтому тело выглядит именно так, как задано.
        /// </summary>
        public SceneBody Add(BodyData data)
        {
            // Копия защищает сцену от случайного изменения исходного объекта вызывающим кодом.
            var d = data.Copy();
            var position = new Vector2(d.X, d.Y);
            var type = (BodyType)d.Type;

            // Круг задаётся радиусом, прямоугольник — шириной и высотой.
            Body body = d.Shape == "Circle"
                ? World.CreateCircle(d.Width / 2f, d.Density, position, type)
                : World.CreateRectangle(d.Width, d.Height, d.Density, position, d.Angle, type);

            // Поворот и скорости выставляются после создания: конструктор мира их не принимает.
            body.Rotation = d.Angle;
            body.LinearVelocity = new Vector2(d.Vx, d.Vy);
            body.AngularVelocity = d.AngularVelocity;

            // Коэффициенты задаются каждой фикстуре: у тела может быть несколько фигур.
            foreach (var fixture in body.FixtureList)
            {
                fixture.Friction = d.Friction;
                fixture.Restitution = d.Restitution;
            }

            // Флаг IsBullet («пуля») намеренно не ставится: непрерывное обнаружение столкновений
            // включено глобально (Settings.ContinuousPhysics). Замер: при 60 и 200 м/с тело
            // останавливается у тонкой стены и без флага. С флагом тела никогда не засыпают,
            // и устоявшаяся сцена считается каждый кадр — лишняя работа и дрожание.

            var item = new SceneBody { Body = body, Data = d };
            body.Tag = item;          // обратная ссылка: фикстура → описание
            Bodies.Add(item);
            return item;
        }

        /// <summary>Применяет к миру текущий вектор гравитации с учётом переключателя.</summary>
        public void UpdateGravity()
        {
            // Библиотека сама применяет гравитацию к каждому динамическому телу на каждом шаге,
            // поэтому при отключении достаточно обнулить вектор, а не складывать силы.
            World.Gravity = Settings.GravityEnabled
                ? new Vector2(Settings.GravityX, Settings.GravityY)
                : Vector2.Zero;

            // Будим тела: иначе «спящее» тело не заметит изменения гравитации.
            foreach (var item in Bodies) item.Body.Awake = true;
        }

        /// <summary>Выполняет один шаг симуляции с фиксированным шагом интегрирования.</summary>
        public void Step()
        {
            // Трение и упругость контакта библиотека фиксирует один раз, в момент касания.
            // Поэтому пересчитываем только контакты, которые начались заново, а не все на каждом шаге.
            // Это даёт тот же результат, что и полный пересчёт, но без лишней работы.
            RefreshNewContacts();
            // Подшаги: тот же интервал делится на несколько меньших шагов.
            int n = Math.Max(1, Math.Min(8, Settings.SubSteps));
            float h = Settings.Step / n;
            for (int i = 0; i < n; i++) World.Step(h);
            Time += Settings.Step;
        }

        /// <summary>Контакты, для которых значения трения и упругости уже выставлены.</summary>
        private readonly HashSet<Contact> refreshed = new HashSet<Contact>();

        /// <summary>
        /// Пересчитывает трение и упругость только у контактов, которые касаются впервые.
        /// Уже обработанные контакты не трогаем, а контакты, которые разошлись, забываем.
        /// </summary>
        private void RefreshNewContacts()
        {
            var alive = new HashSet<Contact>();
            foreach (var body in World.BodyList)
                for (var edge = body.ContactList; edge != null; edge = edge.Next)
                {
                    var c = edge.Contact;
                    if (c == null || !c.IsTouching) continue;
                    alive.Add(c);
                    if (refreshed.Add(c))
                    {
                        c.ResetFriction();
                        c.ResetRestitution();
                    }
                }
            // Контакты, которые больше не касаются, выпадают из набора: при новом касании они пересчитаются.
            refreshed.IntersectWith(alive);
        }

        /// <summary>Удаляет тело из мира и из списка сцены.</summary>
        public void Remove(SceneBody item)
        {
            World.Remove(item.Body);
            Bodies.Remove(item);
        }

        /// <summary>
        /// Изменяет свойства существующего тела «на лету», без перезапуска симуляции.
        /// Если изменились размеры, фикстура пересоздаётся: изменение геометрии
        /// существующей фигуры библиотека не поддерживает.
        /// Возвращает true, если тело было пересоздано (нужно снять соединение мыши).
        /// </summary>
        public bool Apply(SceneBody item, BodyData properties)
        {
            bool sizeChanged =
                Math.Abs(item.Data.Width - properties.Width) > 0.0001f ||
                Math.Abs(item.Data.Height - properties.Height) > 0.0001f ||
                item.Data.Shape != properties.Shape;

            // Размеры и форма запоминаются всегда — по ним рисуется тело.
            item.Data.Width = properties.Width;
            item.Data.Height = properties.Height;
            item.Data.Shape = properties.Shape;
            item.Data.Density = properties.Density;
            item.Data.Friction = properties.Friction;
            item.Data.Restitution = properties.Restitution;

            if (sizeChanged)
                RebuildFixture(item);
            else
                UpdateFixtureProperties(item);

            // Скорость применяется в любом случае — это позволяет «толкнуть» тело полем ввода.
            item.Body.LinearVelocity = new Vector2(properties.Vx, properties.Vy);
            item.Body.Awake = true;
            return sizeChanged;
        }

        /// <summary>
        /// Меняет плотность, трение и упругость у существующих фикстур.
        /// Масса пересчитывается отдельно методом ResetMassData.
        /// </summary>
        private static void UpdateFixtureProperties(SceneBody item)
        {
            foreach (var fixture in item.Body.FixtureList)
            {
                fixture.Shape.Density = item.Data.Density;  // плотность хранится в фигуре
                fixture.Friction = item.Data.Friction;
                fixture.Restitution = item.Data.Restitution;
            }
            item.Body.ResetMassData();
            RefreshContacts(item.Body);
        }

        /// <summary>
        /// Пересчитывает трение и упругость у всех контактов тела и будит его.
        /// Контакт Box2D/Aether.Physics2D вычисляет смешанное трение и упругость один раз,
        /// при первом касании, и хранит результат. Без этой функции после изменения
        /// трения на 0 тело на наклонной платформе продолжало бы держаться по старому
        /// значению и засыпало, а при повторной посадке соскальзывало бы.
        /// </summary>
        private static void RefreshContacts(Body body)
        {
            for (var edge = body.ContactList; edge != null; edge = edge.Next)
            {
                if (edge.Contact == null) continue;
                // ResetFriction / ResetRestitution заново берут значения из обеих фикстур контакта.
                edge.Contact.ResetFriction();
                edge.Contact.ResetRestitution();
            }
            // Просыпаем тело, чтобы новые силы трения применились уже на следующем шаге.
            body.Awake = true;
        }

        /// <summary>
        /// Пересоздаёт фикстуру тела с новыми размерами, сохраняя положение,
        /// поворот и скорости. Без этого изменение ширины или высоты
        /// не отражалось бы на физике: фигура уже создана и её вершины неизменяемы.
        /// </summary>
        private void RebuildFixture(SceneBody item)
        {
            Body body = item.Body;

            // 1. Запоминаем кинематическое состояние.
            Vector2 position = body.Position;
            float rotation = body.Rotation;
            Vector2 velocity = body.LinearVelocity;
            float angularVelocity = body.AngularVelocity;

            // 2. Удаляем старые фикстуры. Копируем список в массив, потому что
            //    Remove изменяет коллекцию во время перебора.
            var old = new Fixture[body.FixtureList.Count];
            body.FixtureList.CopyTo(old, 0);
            foreach (var fixture in old) body.Remove(fixture);

            // 3. Создаём новую фигуру нужного размера и типа.
            Fixture created = item.Data.Shape == "Circle"
                ? body.CreateCircle(item.Data.Width / 2f, item.Data.Density)
                : body.CreateRectangle(item.Data.Width, item.Data.Height, item.Data.Density, Vector2.Zero);

            created.Friction = item.Data.Friction;
            created.Restitution = item.Data.Restitution;

            // 4. Пересчитываем массу по новой геометрии и плотности.
            body.ResetMassData();

            // 5. Возвращаем тело на место — иначе оно «прыгнет» в начало координат.
            body.SetTransform(position, rotation);
            body.LinearVelocity = velocity;
            body.AngularVelocity = angularVelocity;
            body.Awake = true;
        }

        /// <summary>
        /// Создаёт снимок сцены для сохранения в файл или для команды «Сброс».
        /// В снимок попадают актуальные положение, угол и скорости каждого тела.
        /// </summary>
        public SceneData Capture()
        {
            var scene = new SceneData
            {
                GravityX = Settings.GravityX,
                GravityY = Settings.GravityY,
                GravityEnabled = Settings.GravityEnabled,
                Step = Settings.Step,
                SubSteps = Settings.SubSteps,
                DefaultFriction = Settings.DefaultFriction,
                DefaultRestitution = Settings.DefaultRestitution,
                Time = Time
            };

            foreach (var item in Bodies)
            {
                var d = item.Data.Copy();
                var b = item.Body;
                d.X = b.Position.X;
                d.Y = b.Position.Y;
                d.Angle = b.Rotation;
                d.Vx = b.LinearVelocity.X;
                d.Vy = b.LinearVelocity.Y;
                d.AngularVelocity = b.AngularVelocity;
                scene.Bodies.Add(d);
            }
            return scene;
        }

        /// <summary>Полностью заменяет сцену: пересоздаёт мир, стены и все тела.</summary>
        public void Load(SceneData scene)
        {
            Validate(scene);

            World = new World(Vector2.Zero);
            Bodies.Clear();
            Time = scene.Time;

            Settings = new SceneData
            {
                GravityX = scene.GravityX,
                GravityY = scene.GravityY,
                GravityEnabled = scene.GravityEnabled,
                Step = scene.Step,
                SubSteps = scene.SubSteps,
                DefaultFriction = scene.DefaultFriction,
                DefaultRestitution = scene.DefaultRestitution
            };

            // Границы области симуляции. Они статичны, поэтому в файл не сохраняются:
            // при загрузке создаются заново по постоянным размерам мира.
            World.CreateRectangle(Width + 2, 1, 1, new Vector2(Width / 2, Height + .5f), 0, BodyType.Static);
            World.CreateRectangle(Width + 2, 1, 1, new Vector2(Width / 2, -.5f), 0, BodyType.Static);
            World.CreateRectangle(1, Height, 1, new Vector2(-.5f, Height / 2), 0, BodyType.Static);
            World.CreateRectangle(1, Height, 1, new Vector2(Width + .5f, Height / 2), 0, BodyType.Static);

            foreach (var d in scene.Bodies) Add(d);
            UpdateGravity();
        }

        /// <summary>Проверяет, что число конечно (не NaN и не бесконечность).</summary>
        private static bool Finite(float x) { return !float.IsNaN(x) && !float.IsInfinity(x); }

        /// <summary>
        /// Проверяет корректность загруженной сцены.
        /// Некорректный файл должен приводить к понятному исключению,
        /// а не к аварийному завершению программы.
        /// </summary>
        public static void Validate(SceneData s)
        {
            if (s == null || s.Version != 1 || s.Bodies == null || s.Bodies.Count > 1000)
                throw new InvalidDataException("Неподдерживаемый формат сцены или больше 1000 тел.");

            if (s.SubSteps < 1 || s.SubSteps > 8 ||
                !Finite(s.Step) || s.Step < .001f || s.Step > .05f ||
                !Finite(s.GravityX) || !Finite(s.GravityY) || Math.Abs(s.GravityX) > 50 || Math.Abs(s.GravityY) > 50 ||
                !Finite(s.DefaultFriction) || s.DefaultFriction < 0 || s.DefaultFriction > 1 ||
                !Finite(s.DefaultRestitution) || s.DefaultRestitution < 0 || s.DefaultRestitution > 1 ||
                double.IsNaN(s.Time) || double.IsInfinity(s.Time) || s.Time < 0)
                throw new InvalidDataException("Недопустимые параметры мира.");

            foreach (var d in s.Bodies)
            {
                if (d == null || (d.Shape != "Circle" && d.Shape != "Box") || d.Type < 0 || d.Type > 2 ||
                    !Finite(d.X) || !Finite(d.Y) || Math.Abs(d.X) > 10000 || Math.Abs(d.Y) > 10000 ||
                    !Finite(d.Angle) || !Finite(d.AngularVelocity) || Math.Abs(d.AngularVelocity) > 10000 ||
                    !Finite(d.Vx) || !Finite(d.Vy) || Math.Abs(d.Vx) > 100 || Math.Abs(d.Vy) > 100 ||
                    !Finite(d.Width) || !Finite(d.Height) || d.Width < .2f || d.Width > 6 || d.Height < .2f || d.Height > 6 ||
                    !Finite(d.Density) || d.Density < .1f || d.Density > 100 ||
                    !Finite(d.Friction) || d.Friction < 0 || d.Friction > 1 ||
                    !Finite(d.Restitution) || d.Restitution < 0 || d.Restitution > 1)
                    throw new InvalidDataException("Файл содержит недопустимые свойства тела.");
            }
        }

        /// <summary>Сохраняет сцену в XML-файл.</summary>
        public static void SaveFile(string path, SceneData scene)
        {
            Validate(scene);
            var serializer = new XmlSerializer(typeof(SceneData));
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
                serializer.Serialize(stream, scene);
        }

        /// <summary>
        /// Читает сцену из XML-файла с защитой от внешних сущностей (XXE)
        /// и ограничением размера файла.
        /// </summary>
        public static SceneData ReadFile(string path)
        {
            if (new FileInfo(path).Length > 8 * 1024 * 1024)
                throw new InvalidDataException("Слишком большой файл сцены (больше 8 МБ).");

            // DtdProcessing.Prohibit запрещает подстановку внешних сущностей —
            // иначе файл сцены мог бы прочитать произвольный файл с диска.
            var options = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using (var reader = XmlReader.Create(path, options))
            {
                var scene = (SceneData)new XmlSerializer(typeof(SceneData)).Deserialize(reader);
                Validate(scene);
                return scene;
            }
        }

        /// <summary>
        /// Демонстрационная сцена: наклонные плоскости, горка тел и башня.
        /// Координаты подобраны так, чтобы тела падали сверху вниз: платформы ниже, тела выше них.
        /// </summary>
        public static SceneData Demo()
        {
            var s = new SceneData();
            // Углы платформ заданы в физической системе (Y вверх, плюс — против часовой стрелки):
            // левая платформа опущена вправо, правая — влево, вместе они образуют воронку к центру.
            s.Bodies.Add(new BodyData { Shape = "Box", Type = 0, X = 6, Y = 9, Width = 6, Height = .4f, Angle = -.18f, Color = unchecked((int)0xff72819b) });
            s.Bodies.Add(new BodyData { Shape = "Box", Type = 0, X = 18, Y = 6, Width = 6, Height = .4f, Angle = .18f, Color = unchecked((int)0xff72819b) });

            for (int i = 0; i < 8; i++)
                s.Bodies.Add(new BodyData
                {
                    Shape = i % 2 == 0 ? "Circle" : "Box",
                    X = 4 + i * 1.8f,
                    Y = 14.5f - (i % 3) * 1.3f,
                    Width = .9f,
                    Height = .9f,
                    Color = i % 2 == 0 ? unchecked((int)0xff50d4c0) : unchecked((int)0xffffb454)
                });

            for (int i = 0; i < 4; i++)
                s.Bodies.Add(new BodyData { Shape = "Box", X = 12, Y = .6f + i * 1.05f, Width = 1, Height = 1, Color = unchecked((int)0xff8da7ff) });

            // Единые физические свойства для всех тел демо-сцены (включая платформы):
            // упругость 0,2 и трение 0,35, как базовые значения приложения.
            foreach (var body in s.Bodies)
            {
                body.Restitution = .2f;
                body.Friction = .35f;
            }

            return s;
        }
    }
}
