// ============================================================================
//  MainForm.cs — главное окно приложения.
//
//  Интерфейс строится программно (файла MainForm.Designer.cs нет), чтобы весь
//  код окна читался подряд. Каждый элемент управления получает всплывающую
//  подсказку, а изменение любого физического параметра выбранного тела
//  применяется к нему сразу — без кнопки «применить» и без перезапуска.
// ============================================================================

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;
using tainicom.Aether.Physics2D.Common;
using tainicom.Aether.Physics2D.Dynamics;
using tainicom.Aether.Physics2D.Dynamics.Joints;

namespace PhysicsSandbox
{
    public class MainForm : Form
    {
        // ---------------------------------------------------------------- поля

        /// <summary>Физическая модель сцены.</summary>
        readonly PhysicsWorld physics = new PhysicsWorld();

        /// <summary>Холст для отрисовки.</summary>
        readonly SimulationCanvas canvas = new SimulationCanvas();

        /// <summary>Таймер перерисовки: примерно 60 кадров в секунду.</summary>
        // Кадры задаёт отдельный поток-метроном (FrameThread), а не таймер формы.
        // Таймер Windows ограничен тактом ~15,6 мс (потолок ~64 кадра/с), а его сообщения
        // имеют самый низкий приоритет: пока двигается мышь, они не доходят до формы,
        // и физика «встаёт на паузу». Поток ставит в очередь обычное сообщение (BeginInvoke),
        // которое обрабатывается раньше ввода мыши и перерисовки.
        const double MaxFrameSeconds = 1.0 / 144.0;   // потолок частоты кадров (под мониторы 144 Гц)
        Thread frameThread;
        volatile bool frameLoopStop;
        // Зажатые клавиши камеры W A S D: нужны там, где нет средней кнопки мыши.
        readonly HashSet<Keys> heldKeys = new HashSet<Keys>();
        const float KeyPanSpeed = 420f;   // пикселей в секунду
        int framePending;                              // 1, пока кадр стоит в очереди и не выполнен

        /// <summary>Секундомер для расчёта реального времени кадра.</summary>
        readonly Stopwatch clock = Stopwatch.StartNew();

        /// <summary>Строка состояния внизу окна.</summary>
        readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 30, TextAlign = ContentAlignment.MiddleLeft };
        // Координаты курсора выводятся в отдельную метку справа, а не в строку состояния:
        // так текст подсказок и координат не перетирают друг друга и не меняют цвет строки.
        // Координаты курсора: дочерний элемент строки состояния, прижат к её правому краю.
        // Положение задаёт докинг WinForms, поэтому оно не зависит от размера окна.
        readonly Label cursorInfo = new Label { Dock = DockStyle.Right, Width = 260, TextAlign = ContentAlignment.MiddleRight };

        // Служебная информация (FPS, тела, контакты, время) выводится в отдельной метке строки
        // состояния, левее координат курсора. Раньше она писалась прямо в строку состояния и
        // через полсекунды затирала сообщения («Сцена сохранена: ...», «Пауза...» и другие).
        readonly Label infoLabel = new Label { Dock = DockStyle.Right, Width = 430, TextAlign = ContentAlignment.MiddleRight };

        // Правый отступ текста строки состояния: ширина двух меток справа, чтобы текст не уходил под них.
        const int StatusRightPadding = 430 + 260;

        /// <summary>Правая панель управления с прокруткой.</summary>
        readonly FlowLayoutPanel side = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 300,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(10)
        };

        /// <summary>Единственный на всё окно менеджер подсказок.</summary>
        readonly ToolTip tip = new ToolTip
        {
            AutoPopDelay = 15000,   // сколько подсказка висит на экране
            InitialDelay = 350,     // задержка перед появлением
            ReshowDelay = 100,      // задержка при переходе между элементами
            ShowAlways = true       // показывать даже в неактивном окне
        };

        // Элементы управления: инструмент, форма и тип нового тела, скорость времени.
        readonly ComboBox mode = new ComboBox(), shape = new ComboBox(), bodyType = new ComboBox(), speed = new ComboBox();

        // Параметры мира: вектор гравитации и шаг интегрирования.
        readonly NumericUpDown gx = Num(-50, 50, 0, 2), gy = Num(-50, 50, -9.81m, 2), dt = Num(1, 50, 16.667m, 3);

        /// <summary>Число подшагов на кадр: точнее, но дороже.</summary>
        readonly NumericUpDown substeps = Num(1, 8, 2, 0);

        // Параметры нового тела.
        readonly NumericUpDown sizeX = Num(.2m, 6, 1, 2), sizeY = Num(.2m, 6, 1, 2), density = Num(.1m, 100, 1, 2);

        // Строки («подпись + поле») блока «Параметры нового тела».
        // Храним ссылки, чтобы скрывать их целиком, а не только поле ввода.
        Panel rowWidth, rowHeight;
        // Метка для всех элементов блока «Параметры нового тела» (кроме заголовка и выпадающих списков).
        static readonly object newBodyTag = new object();
        Label labelWidth;                                  // подпись «Ширина» / «Диаметр»
        Label newBodyTitle;                                // заголовок блока
        Label widthCaption;                                // подпись «Ширина» / «Диаметр»
        Label lastCaption;                                 // временно: подпись последней созданной строки
        readonly Control[] newBodyControls = new Control[0];   // все элементы блока (для скрытия)
        readonly NumericUpDown friction = Num(0, 1, .35m, 2), restitution = Num(0, 1, .2m, 2);
        readonly NumericUpDown vx = Num(-100, 100, 0, 2), vy = Num(-100, 100, 0, 2);

        // Свойства выбранного тела — изменяются мгновенно.
        readonly NumericUpDown selWidth = Num(.2m, 6, 1, 2), selHeight = Num(.2m, 6, 1, 2);
        readonly NumericUpDown selDensity = Num(.1m, 100, 1, 2), selMass = Num(0, 10000, 0, 3);
        readonly NumericUpDown selFriction = Num(0, 1, .35m, 2), selRestitution = Num(0, 1, .2m, 2);
        readonly NumericUpDown selVx = Num(-100, 100, 0, 2), selVy = Num(-100, 100, 0, 2);
        // Контейнер полей блока «Параметры нового тела». Строки в нём выстраиваются по явному
        // списку порядка (RebuildNewBodyRows), а не по порядку добавления.
        readonly FlowLayoutPanel newBodyBox = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Width = 262,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        // Все строки блока нового тела в запасе: нужны, чтобы вернуть скрытые (высота у круга).
        readonly List<Control> newBodyRows = new List<Control>();
        // Порядок полей блока нового тела. Первое слово подписи сопоставляется со строкой.
        static readonly string[] newBodyOrder = { "Ширина", "Высота", "Плотность", "Трение", "Упругость", "Скорость X", "Скорость Y" };

        readonly Label massInfo = new Label { AutoSize = true, ForeColor = Color.FromArgb(90, 100, 120) };

        readonly CheckBox gravity = new CheckBox { Text = "Гравитация включена", Checked = true, AutoSize = true };
        // Показ векторов скорости: 0 — выключено, 1 — только выбранное тело, 2 — у всех тел.
        readonly ComboBox velocityView = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 262 };
        // Вектор без паузы: показывать стрелки и во время симуляции.
        readonly CheckBox velocityWhileRunning = new CheckBox { Text = "Вектор без паузы", Checked = false, AutoSize = true };
        readonly Label selectedLabel = new Label { Text = "Объект не выбран", AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
        readonly GroupBox groupSelected = new GroupBox { Text = "Свойства выбранного тела", Width = 262, Height = 330 };
        // Строки полей выбранного тела. Пересобираются при выборе тела и смене его формы.
        readonly List<Panel> selectedRows = new List<Panel>();   // строки блока выбранного тела (создаются один раз)
        Label selectedWidthCaption;   // подпись «Ширина» / «Диаметр» в блоке выбранного тела

        // Состояние взаимодействия.
        SceneBody selected;
        FixedMouseJoint mouseJoint;
        bool pauseDragging;   // перетаскивание тела на паузе: двигаем тело напрямую, без джоинта
        SceneData baseline;
        bool running, syncing;
        double previous, accumulator, fpsElapsed;
        int renderedFrames, fps;
        float zoom = 1, scale, ox, oy;

        // Смещение камеры (в пикселях холста). Меняется средней кнопкой мыши.
        float panX, panY;
        // Положение курсора в момент нажатия средней кнопки, чтобы считать сдвиг.
        Point panStart;
        bool panning;
        readonly float[] speeds = { .25f, .5f, 1, 2, 4 };

        // ------------------------------------------------------------ конструктор

        [System.Runtime.InteropServices.DllImport("winmm.dll")]
        static extern uint timeBeginPeriod(uint period);
        [System.Runtime.InteropServices.DllImport("winmm.dll")]
        static extern uint timeEndPeriod(uint period);

        public MainForm()
        {
            // По умолчанию Windows тикает примерно раз в 15,6 мс, и таймер формы не чаще ~64 раз в секунду.
            // timeBeginPeriod(1) просит 1 мс; парный вызов timeEndPeriod — при закрытии формы.
            try { timeBeginPeriod(1); } catch (Exception) { }
            FormClosed += (s, e) => { try { timeEndPeriod(1); } catch (Exception) { } };

            Text = "Симулятор физики 2D — C# / Windows Forms";
            Width = 1320; Height = 860;
            MinimumSize = new Size(1000, 700);
            Font = new Font("Segoe UI", 9);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            BuildToolbar();
            BuildSidePanel();

            status.Text = "Готово. Нажмите «Пуск», чтобы начать симуляцию.";
            canvas.Dock = DockStyle.Fill;
            canvas.Render = Draw;
            canvas.MouseDown += MouseDownScene;
            canvas.MouseMove += MouseMoveScene;
            canvas.MouseUp += MouseUpScene;
            canvas.MouseWheel += WheelScene;
            canvas.MouseEnter += (s, e) => { };
            Hint(canvas, "Область симуляции.\nЛКМ — выбрать или потянуть тело.\nПКМ — вернуть инструмент выбора.\nКолесо мыши — масштаб.");

            // Порядок важен для докинга WinForms: элементы, добавленные позже, получают
            // «низший» приоритет. Поэтому сначала добавляем панели с фиксированными краями
            // (верхняя панель уже добавлена в BuildToolbar), затем статус, затем холст,
            // который заполняет всё оставшееся место. Так мир центрируется ровно в
            // свободной области и отступы слева/справа/сверху/снизу совпадают.
            Controls.Add(side);
            Controls.Add(status);
            Controls.Add(canvas);
            // Метка координат живёт внутри холста и привязана к правому нижнему углу,
            // поэтому при сворачивании и изменении размера её положение задаёт Anchor.
            // infoLabel добавляется первой: метка с большим индексом докается раньше и занимает самый
            // правый край, поэтому координаты курсора остаются у края, а служебная информация левее.
            status.Controls.Add(infoLabel);
            status.Controls.Add(cursorInfo);
            // WinForms докает элементы с наибольшим индексом первыми, а Fill-элемент
            // должен получать остаток, поэтому холст ставим на индекс 0 (докается последним).
            // Иначе он занимает всю форму и мир центрируется со смещением под боковую панель.
            Controls.SetChildIndex(canvas, 0);


            UpdateNewBodyPanel();

            Shown += (s, e) => StartFrameThread();
            FormClosing += (s, e) => frameLoopStop = true;
            Shown += (s, e) => SetScene(PhysicsWorld.Demo());
            KeyDown += KeyDownHandler;
            KeyUp += KeyUpHandler;
            Deactivate += ClearHeldKeys;

            UpdateSelectedPanel();

            // Оформление окна: цвета, шрифты, плоские кнопки. Не меняет логику и порядок элементов.
            Theme.Apply(this);
            // Повторно после показа формы: к этому моменту у NumericUpDown уже есть внутреннее поле.
            Shown += (s, e) => { Theme.Apply(this); ApplyBarColors(); };
            // Строки, которые сейчас скрыты (например, высота у круга), выпадают из дерева контролов,
            // и Theme.Apply их не видит. Красим их отдельно, чтобы при переключении формы они были в теме.
            foreach (Control row in newBodyRows) Theme.Apply(row);
            Theme.Apply(rowWidth);
            Theme.Apply(rowHeight);
            status.Font = Theme.SmallFont;
            status.Padding = new Padding(10, 0, StatusRightPadding, 0);
            status.AutoEllipsis = true;
            status.BackColor = Theme.Canvas;
            status.ForeColor = Theme.TextMuted;
            ApplyBarColors();
            canvas.BackColor = Theme.Canvas;
        }

        // ------------------------------------------------------------- интерфейс

        /// <summary>Создаёт всплывающую подсказку и одновременно текст строки состояния.</summary>
        /// <summary>Ставит метку координат в строку состояния, левее боковой панели.</summary>
        /// <summary>Цвета строки состояния, метки координат и холста. Вызывается после Theme.Apply.</summary>
        void ApplyBarColors()
        {
            status.Font = Theme.SmallFont;
            status.Padding = new Padding(10, 0, StatusRightPadding, 0);
            status.AutoEllipsis = true;
            status.BackColor = Theme.Canvas;
            status.ForeColor = Theme.TextMuted;
            cursorInfo.Font = Theme.SmallFont;
            cursorInfo.Padding = new Padding(0, 0, 10, 0);
            cursorInfo.BackColor = Theme.Canvas;
            cursorInfo.ForeColor = Theme.TextMuted;
            infoLabel.Font = Theme.SmallFont;
            infoLabel.Padding = new Padding(0, 0, 10, 0);
            infoLabel.BackColor = Theme.Canvas;
            infoLabel.ForeColor = Theme.TextMuted;
            canvas.BackColor = Theme.Canvas;
        }


        /// <summary>Пишет текст в строку состояния. Цвета строки всегда одни и те же.</summary>
        void SetStatus(string text)
        {
            status.Text = text;
            status.BackColor = Theme.Canvas;
            status.ForeColor = Theme.TextMuted;
        }

        void Hint(Control control, string text)
        {
            tip.SetToolTip(control, text);
            control.MouseEnter += (s, e) => SetStatus(text.Replace("\n", "  "));
        }

        /// <summary>Создаёт числовое поле с диапазоном и точностью.</summary>
        static NumericUpDown Num(decimal min, decimal max, decimal value, int decimals)
        {
            var n = new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                Value = value,
                DecimalPlaces = decimals,
                Increment = decimals >= 3 ? .001m : .1m,
                Width = 90
            };
            return n;
        }

        /// <summary>Добавляет кнопку на панель и навешивает подсказку.</summary>
        Button AddButton(Control parent, string text, Action action, string hint)
        {
            var button = new Button { Text = text, AutoSize = true, Height = 32, Margin = new Padding(3) };
            button.Click += (s, e) => action();
            parent.Controls.Add(button);
            Hint(button, hint);
            return button;
        }

        /// <summary>Создаёт строку «подпись + поле ввода» на боковой панели.</summary>
        Panel Field(string label, Control input, string hint)
        {
            var row = new Panel { Width = 262, Height = 28 };
            var caption = new Label { Text = label, Location = new Point(0, 5), AutoSize = true };
            row.Controls.Add(caption);
            lastCaption = caption;   // запоминаем подпись: её текст меняется при смене формы
            input.Location = new Point(140, 2);
            input.Width = 118;
            row.Controls.Add(input);
            Hint(input, hint);
            return row;
        }

        /// <summary>Помечает строку как часть блока «Параметры нового тела».</summary>
        Panel MarkNew(Panel row)
        {
            row.Tag = newBodyTag;
            return row;
        }

        /// <summary>Заголовок группы на боковой панели.</summary>
        Label Title(string text)
        {
            var label = new Label
            {
                Text = text,
                AutoSize = true,
                Margin = new Padding(3, 12, 3, 4),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.FromArgb(60, 80, 120)
            };
            return label;
        }

        /// <summary>Строит верхнюю панель команд.</summary>
        void BuildToolbar()
        {
            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 84,
                Padding = new Padding(5),
                WrapContents = true
            };

            AddButton(toolbar, "▶ Пуск", Start,
                "Запустить симуляцию.\nТела начинают двигаться под действием гравитации и столкновений.");
            AddButton(toolbar, "❚❚ Пауза", Pause,
                "Приостановить расчёт.\nПоложение и скорости тел сохраняются, сцену можно рассматривать и настраивать.");
            AddButton(toolbar, "Шаг", StepOnce,
                "Выполнить ровно один шаг физического движка.\nУдобно для покадрового разбора движения.");
            AddButton(toolbar, "↶ Сброс", Restore,
                "Вернуть сцену к состоянию, зафиксированному при её создании или загрузке.\nВосстанавливаются положение и скорости тел.");
            AddButton(toolbar, "Демо", () => SetScene(PhysicsWorld.Demo()),
                "Загрузить демонстрационную сцену.\nДве наклонные плоскости, горка тел и башня из кубов.");
            AddButton(toolbar, "Удалить", DeleteSelected,
                "Удалить выбранное тело со сцены.\nМожно также нажать клавишу Delete.");
            AddButton(toolbar, "Очистить", Clear,
                "Удалить все тела, оставив только границы области и настройки мира.");
            AddButton(toolbar, "Сохранить", Save,
                "Сохранить сцену в XML-файл.\nЗаписываются параметры мира, положения, углы и скорости тел.");
            AddButton(toolbar, "Загрузить", LoadScene,
                "Загрузить сцену из XML-файла.\nНекорректный файл не разрушит текущую сцену — будет выведено сообщение.");

            // Множитель скорости времени.
            var speedHost = new Panel { Width = 150, Height = 34, Margin = new Padding(6, 3, 3, 3) };
            speedHost.Controls.Add(new Label { Text = "Скорость:", Location = new Point(0, 8), AutoSize = true });
            speed.Location = new Point(66, 4);
            speed.Width = 80;
            speed.DropDownStyle = ComboBoxStyle.DropDownList;
            speed.Items.AddRange(new object[] { "0.25×", "0.5×", "1×", "2×", "4×" });
            speed.SelectedIndex = 2;
            speedHost.Controls.Add(speed);
            Hint(speed, "Скорость течения времени.\nМножитель не меняет шаг интегрирования —\nменяется только число шагов за кадр.");
            toolbar.Controls.Add(speedHost);

            AddButton(toolbar, "Вписать сцену", () => { zoom = 1; panX = 0; panY = 0; canvas.Invalidate(); },
                "Вернуть масштаб 1:1 и вписать всю область симуляции в окно.");

            Controls.Add(toolbar);
        }

        /// <summary>Строит правую панель: инструмент, параметры нового тела, свойства выбранного, мир.</summary>
        void BuildSidePanel()
        {
            // --- Инструмент ---
            side.Controls.Add(Title("Инструмент"));
            mode.DropDownStyle = ComboBoxStyle.DropDownList;
            mode.Items.AddRange(new object[] { "Выбор / перетаскивание", "Создать объект" });
            mode.SelectedIndex = 0;
            mode.Width = 262;
            mode.SelectedIndexChanged += (s, e) =>
            {
                canvas.Hint = HintText();
                canvas.Invalidate();
                UpdateNewBodyPanel();   // блок параметров нового тела показываем только при «Создать объект»
            };
            side.Controls.Add(mode);
            Hint(mode, "Инструмент работы в области симуляции.\n«Выбор» — двигать тела мышью.\n«Создать объект» — щелчок создаёт тело по параметрам ниже.");

            // --- Параметры нового тела ---
            side.Controls.Add(newBodyTitle = Title("Параметры нового тела"));

            shape.DropDownStyle = ComboBoxStyle.DropDownList;
            shape.Items.AddRange(new object[] { "Круг", "Прямоугольник" });
            shape.SelectedIndex = 0;
            shape.Width = 262;
            // При смене формы меняем набор полей: для круга скрываем «Высота» и переименовываем «Ширина».
            shape.SelectedIndexChanged += (s, e) => UpdateNewBodyPanel();
            side.Controls.Add(shape);
            Hint(shape, "Форма создаваемого тела.\nДля круга задаётся диаметр, для прямоугольника — ширина и высота.");

            bodyType.DropDownStyle = ComboBoxStyle.DropDownList;
            bodyType.Items.AddRange(new object[] { "Статическое", "Кинематическое", "Динамическое" });
            bodyType.SelectedIndex = 2;
            bodyType.Width = 262;
            side.Controls.Add(bodyType);
            Hint(bodyType, "Тип создаваемого тела.\nСтатическое — неподвижно.\nКинематическое — движется с заданной скоростью.\nДинамическое — подчиняется физике.");

            rowWidth = Field("Ширина, м", sizeX, "Ширина нового тела в метрах.\nДля круга это диаметр.");
            widthCaption = lastCaption;
            newBodyBox.Controls.Add(MarkNew(rowWidth));
            rowHeight = Field("Высота, м", sizeY, "Высота нового тела в метрах.\nДля круга не используется.");
            newBodyBox.Controls.Add(MarkNew(rowHeight));
            newBodyBox.Controls.Add(MarkNew(Field("Плотность", density, "Плотность материала, кг/м².\nМасса = плотность × площадь.")));
            newBodyBox.Controls.Add(MarkNew(Field("Трение", friction, "Коэффициент трения от 0 до 1.\n0 — гладкая поверхность, 1 — сильное сцепление.")));
            newBodyBox.Controls.Add(MarkNew(Field("Упругость", restitution, "Коэффициент восстановления от 0 до 1.\n0 — удар без отскока, 1 — упругий отскок.")));
            newBodyBox.Controls.Add(MarkNew(Field("Скорость X", vx, "Начальная горизонтальная скорость нового тела, м/с.")));
            newBodyBox.Controls.Add(MarkNew(Field("Скорость Y", vy, "Начальная вертикальная скорость нового тела, м/с.")));

            // --- Свойства выбранного тела (применяются мгновенно) ---
            foreach (Control row in newBodyBox.Controls) newBodyRows.Add(row);
            side.Controls.Add(newBodyBox);

            side.Controls.Add(Title("Выбранное тело"));
            side.Controls.Add(selectedLabel);
            side.Controls.Add(groupSelected);

            // Блок выбранного тела повторяет поля блока «Параметры нового тела»:
            // тот же порядок и те же элементы, а набор меняется от формы (для круга нет «Высоты»).
            // Масса вычисляется из плотности и площади, поэтому выводится текстом, а не полем.
            BuildSelectedFields();

            Hint(selWidth, "Ширина выбранного тела в метрах.\nИзменение применяется мгновенно.");
            Hint(selHeight, "Высота выбранного тела в метрах.\nИзменение применяется мгновенно.");
            Hint(selDensity, "Плотность выбранного тела.\nМасса = плотность × площадь и видна рядом.");
            Hint(selMass, "Масса выбранного тела.\nПлотность = масса ÷ площадь.");
            Hint(selFriction, "Трение выбранного тела (0–1).");
            Hint(selRestitution, "Упругость выбранного тела (0–1).");
            Hint(selVx, "Горизонтальная скорость выбранного тела.");
            Hint(selVy, "Вертикальная скорость выбранного тела.");

            // --- Параметры мира ---
            side.Controls.Add(Title("Мир"));
            side.Controls.Add(Field("Гравитация X", gx, "Горизонтальная составляющая ускорения свободного падения, м/с².\nПрименяется сразу ко всем телам сцены."));
            side.Controls.Add(Field("Гравитация Y", gy, "Вертикальная составляющая ускорения свободного падения, м/с².\nОсь Y в физическом мире направлена вверх, поэтому вниз — это отрицательное значение.\n-9,81 — земное значение, применяется сразу."));
            side.Controls.Add(gravity);
            velocityView.Items.AddRange(new object[] { "Векторы: выключены", "Векторы: только выбранное", "Векторы: у всех тел" });
            velocityView.SelectedIndex = 1;
            side.Controls.Add(velocityView);
            side.Controls.Add(velocityWhileRunning);
            Hint(velocityView, "Показ стрелок скорости.\nВыключены, только у выбранного тела или у всех тел.");
            Hint(velocityWhileRunning, "Показывать стрелки скорости и во время симуляции, а не только на паузе.");
            velocityView.SelectedIndexChanged += (s, e) => canvas.Invalidate();
            velocityWhileRunning.CheckedChanged += (s, e) => canvas.Invalidate();
            Hint(gravity, "Включить или отключить гравитацию.\nПри отключении вектор обнуляется, тела сохраняют скорость.");

            side.Controls.Add(Field("Шаг, мс", dt, "Фиксированный шаг интегрирования в миллисекундах.\n16,667 мс соответствует 1/60 секунды."));

            side.Controls.Add(Field("Подшаги", substeps,
                "Сколько шагов расчёта выполнять за один кадр.\n" +
                "1 — быстро, но менее точно. 2–4 — заметно точнее: падение и столкновения считаются мельче.\n" +
                "Большие значения дают плавность, но нагружают процессор."));

            // --- Обработчики мгновенного применения ---
            selWidth.ValueChanged += (s, e) => ApplySelected();
            selHeight.ValueChanged += (s, e) => ApplySelected();
            selDensity.ValueChanged += (s, e) => { if (Selected) ApplyDensity(); };
            selMass.ValueChanged += (s, e) => { if (Selected) ApplyMass(); };
            selFriction.ValueChanged += (s, e) => ApplySelected();
            selRestitution.ValueChanged += (s, e) => ApplySelected();
            selVx.ValueChanged += (s, e) => ApplySelected();
            selVy.ValueChanged += (s, e) => ApplySelected();

            gx.ValueChanged += (s, e) => { physics.Settings.GravityX = (float)gx.Value; physics.UpdateGravity(); };
            gy.ValueChanged += (s, e) => { physics.Settings.GravityY = (float)gy.Value; physics.UpdateGravity(); };
            gravity.CheckedChanged += (s, e) => { physics.Settings.GravityEnabled = gravity.Checked; physics.UpdateGravity(); };
            dt.ValueChanged += (s, e) => { physics.Settings.Step = (float)dt.Value / 1000f; accumulator = 0; };
            substeps.ValueChanged += (s, e) => { if (!syncing) physics.Settings.SubSteps = (int)substeps.Value; };

            // Глобальные трение и упругость для новых тел хранятся в параметрах мира: так они
            // попадают в файл сцены и восстанавливаются при загрузке. Существующие тела не меняются.
            friction.ValueChanged += (s, e) => { if (!syncing) physics.Settings.DefaultFriction = (float)friction.Value; };
            restitution.ValueChanged += (s, e) => { if (!syncing) physics.Settings.DefaultRestitution = (float)restitution.Value; };
        }

        /// <summary>Размещает на группе подпись и поле по координатам.</summary>
        /// <summary>
        /// Строит поля выбранного тела в том же порядке, что и блок нового тела:
        /// ширина (диаметр для круга), высота (только для прямоугольника), плотность,
        /// трение, упругость, скорость X, скорость Y. Масса — строкой под полями.
        /// Порядок задаётся здесь, а не зависит от того, в каком месте формы строки добавлены.
        /// </summary>
        void BuildSelectedFields()
        {
            bool circle = selected != null && selected.Data.Shape == "Circle";

            // Строки создаются один раз и переиспользуются; выстраиваются заново при каждой смене формы.
            if (selectedRows.Count == 0)
            {
                selectedRows.Add(SelectedRow("Ширина, м", selWidth, out selectedWidthCaption));
                selectedRows.Add(SelectedRow("Высота, м", selHeight, out _));
                selectedRows.Add(SelectedRow("Плотность", selDensity, out _));
                selectedRows.Add(SelectedRow("Трение", selFriction, out _));
                selectedRows.Add(SelectedRow("Упругость", selRestitution, out _));
                selectedRows.Add(SelectedRow("Скорость X", selVx, out _));
                selectedRows.Add(SelectedRow("Скорость Y", selVy, out _));
                if (!selectedHintsSet) SetSelectedHints();
            }

            // Порядок тот же, что у блока нового тела; для круга строки «Высота» нет.
            selectedWidthCaption.Text = circle ? "Диаметр, м" : "Ширина, м";
            groupSelected.SuspendLayout();
            foreach (var row in selectedRows) groupSelected.Controls.Remove(row);
            int top = 22;
            foreach (var row in selectedRows)
            {
                bool isHeight = CaptionOf(row).StartsWith("Высота");
                if (isHeight && circle) continue;
                row.Top = top;
                groupSelected.Controls.Add(row);
                top += 28;
            }
            // Масса выводится текстом под полями (вычисляется движком).
            if (!groupSelected.Controls.Contains(massInfo)) groupSelected.Controls.Add(massInfo);
            massInfo.Location = new Point(12, top + 6);
            groupSelected.Height = top + 60;
            groupSelected.ResumeLayout();
        }

        /// <summary>Строка «подпись + поле» для блока выбранного тела.</summary>
        bool selectedHintsSet;
        string lastSelectedShape;     // форма, для которой построен блок выбранного тела

        /// <summary>Подсказки полей блока выбранного тела (вешаются один раз).</summary>
        void SetSelectedHints()
        {
            selectedHintsSet = true;
            Hint(selWidth, "Ширина выбранного тела в метрах.\nДля круга это диаметр.\nИзменение применяется сразу.");
            Hint(selHeight, "Высота выбранного тела в метрах.\nИзменение применяется сразу.");
            Hint(selDensity, "Плотность выбранного тела, кг/м².\nМасса = плотность × площадь.");
            Hint(selFriction, "Коэффициент трения выбранного тела (0–1).\nПрименяется сразу.");
            Hint(selRestitution, "Упругость выбранного тела (0–1).\nПрименяется сразу.");
            Hint(selVx, "Горизонтальная скорость выбранного тела, м/с.");
            Hint(selVy, "Вертикальная скорость выбранного тела, м/с.");
        }

        Panel SelectedRow(string label, NumericUpDown input, out Label caption)
        {
            var row = new Panel { Size = new Size(250, 26) };
            caption = new Label { Text = label, Location = new Point(12, 4), AutoSize = true };
            row.Controls.Add(caption);
            input.Location = new Point(130, 0);
            input.Width = 118;
            row.Controls.Add(input);
            return row;
        }

        void SideField(GroupBox group, string label, Control input, int top, string hint)
        {
            group.Controls.Add(new Label { Text = label, Location = new Point(12, top + 3), AutoSize = true });
            input.Location = new Point(130, top);
            input.Width = 118;
            group.Controls.Add(input);
            Hint(input, hint);
        }

        /// <summary>
        /// Выстраивает строки блока нового тела в порядке newBodyOrder. Для круга строки «Высота» нет.
        /// Строки берутся из запаса newBodyRows, поэтому скрытая высота возвращается на место.
        /// </summary>
        void RebuildNewBodyRows(bool circle)
        {
            // Подпись ширины меняется до выстраивания, чтобы строка сразу была с правильным текстом.
            if (widthCaption != null)
                widthCaption.Text = circle ? "Диаметр, м" : "Ширина, м";

            newBodyBox.SuspendLayout();
            newBodyBox.Controls.Clear();
            foreach (string key in newBodyOrder)
            {
                if (key == "Высота" && circle) continue;
                Control row = key == "Ширина" ? rowWidth
                            : key == "Высота" ? rowHeight
                            : newBodyRows.Find(r => CaptionOf(r).StartsWith(key));
                if (row != null) newBodyBox.Controls.Add(row);
            }
            newBodyBox.ResumeLayout();
        }

        /// <summary>Текст подписи строки «подпись + поле».</summary>
        static string CaptionOf(Control row)
        {
            foreach (Control c in row.Controls)
                if (c is Label) return ((Label)c).Text;
            return "";
        }

        /// <summary>
        /// Показывает или скрывает блок «Параметры нового тела».
        /// Блок виден только в режиме «Создать объект». Для круга скрывается поле «Высота»,
        /// а поле «Ширина» переименовывается в «Диаметр», потому что у круга одна величина.
        /// </summary>
        void UpdateNewBodyPanel()
        {
            bool creating = mode.SelectedIndex == 1;
            bool circle = shape.SelectedIndex == 0;

            newBodyTitle.Visible = creating;
            bodyType.Visible = creating;
            shape.Visible = creating;
            newBodyBox.Visible = creating;
            rowHeight.Visible = !circle;   // строка высоты есть только у прямоугольника
            RebuildNewBodyRows(circle);


            side.PerformLayout();
        }

        /// <summary>Текст подсказки поверх холста, зависит от выбранного инструмента.</summary>
        string HintText()
        {
            return mode.SelectedIndex == 1
                ? "СОЗДАНИЕ: щёлкните в свободной области"
                : "ВЫБОР: щёлкните тело или потяните его";
        }

        // ------------------------------------------------------- ход симуляции

        /// <summary>Есть ли выбранное тело.</summary>
        bool Selected { get { return selected != null; } }

        /// <summary>Запускает расчёт.</summary>
        void Start()
        {
            running = true;
            // Фиксируем точку отсчёта, чтобы после паузы не накопилась
            previous = clock.Elapsed.TotalSeconds; // «задолженность» по времени кадра

            status.Text = "Симуляция запущена.";
        }

        /// <summary>Останавливает расчёт, сохраняя состояние тел.</summary>
        void Pause()
        {
            running = false;
            ReleaseJoint();
            status.Text = "Пауза. Сцену можно рассматривать и настраивать.";
        }

        /// <summary>Выполняет один шаг и остаётся на паузе.</summary>
        void StepOnce()
        {
            Pause();
            physics.Step();
            canvas.Invalidate();
            status.Text = "Выполнен один шаг физического движка.";
        }

        /// <summary>Возвращает сцену к снимку, сделанному при её создании или загрузке (SetScene).</summary>
        void Restore()
        {
            if (baseline == null) return;
            Pause();
            selected = null;
            SetScene(baseline);
            status.Text = "Сцена восстановлена к исходному состоянию.";
        }

        /// <summary>Удаляет все тела, сохраняя параметры мира.</summary>
        void Clear()
        {
            Pause();
            selected = null;
            var empty = new SceneData
            {
                GravityX = physics.Settings.GravityX,
                GravityY = physics.Settings.GravityY,
                GravityEnabled = physics.Settings.GravityEnabled,
                Step = physics.Settings.Step,
                SubSteps = physics.Settings.SubSteps,   // число подшагов — тоже параметр мира, он не сбрасывается
                DefaultFriction = physics.Settings.DefaultFriction,
                DefaultRestitution = physics.Settings.DefaultRestitution
            };
            SetScene(empty);
            status.Text = "Сцена очищена.";
        }

        /// <summary>Заменяет сцену целиком и запоминает её как исходное состояние.</summary>
        void SetScene(SceneData scene)
        {
            physics.Load(scene);
            baseline = physics.Capture();
            selected = null;
            // Поля мира синхронизируются с загруженными значениями без генерации событий.
            syncing = true;
            gx.Value = Clamp((decimal)physics.Settings.GravityX, gx);
            gy.Value = Clamp((decimal)physics.Settings.GravityY, gy);
            dt.Value = Clamp((decimal)(physics.Settings.Step * 1000), dt);
            substeps.Value = Clamp(physics.Settings.SubSteps, substeps);
            friction.Value = Clamp((decimal)physics.Settings.DefaultFriction, friction);
            restitution.Value = Clamp((decimal)physics.Settings.DefaultRestitution, restitution);
            gravity.Checked = physics.Settings.GravityEnabled;
            syncing = false;
            UpdateSelectedPanel();
            canvas.Invalidate();
        }

        /// <summary>Кадр таймера: продвигает физику и перерисовывает сцену.</summary>
        /// <summary>Запускает поток-метроном, который с заданной частотой просит форму сделать кадр.</summary>
        void StartFrameThread()
        {
            if (frameThread != null) return;
            frameThread = new Thread(FrameLoop) { IsBackground = true, Name = "FrameClock", Priority = ThreadPriority.AboveNormal };
            frameThread.Start();
        }

        /// <summary>
        /// Метроном: ждёт до времени следующего кадра и ставит кадр в очередь формы.
        /// Если предыдущий кадр ещё не выполнен, новый не ставится (кадры не копятся).
        /// Ожидание: Sleep(1) пока далеко и короткое активное ожидание в конце, так точнее такта таймера.
        /// </summary>
        void FrameLoop()
        {
            var watch = Stopwatch.StartNew();
            double next = 0;
            while (!frameLoopStop)
            {
                double remaining = next - watch.Elapsed.TotalSeconds;
                if (remaining > 0.0015) { Thread.Sleep(1); continue; }
                if (remaining > 0) { Thread.Yield(); continue; }
                next = watch.Elapsed.TotalSeconds + MaxFrameSeconds;

                if (Interlocked.Exchange(ref framePending, 1) == 0)
                {
                    try
                    {
                        if (IsDisposed || !IsHandleCreated) break;
                        BeginInvoke((Action)RunFrame);
                    }
                    catch (ObjectDisposedException) { break; }
                    catch (InvalidOperationException) { break; }
                }
            }
        }

        /// <summary>Кадр в потоке интерфейса: шаги физики и немедленная перерисовка холста.</summary>
        void RunFrame()
        {
            try
            {
                Tick();
                canvas.Update();    // Tick уже пометил холст; рисуем сразу, не дожидаясь низкоприоритетного WM_PAINT
            }
            finally { Interlocked.Exchange(ref framePending, 0); }
        }

        void Tick()
        {
            double now = clock.Elapsed.TotalSeconds;
            double delta = now - previous;
            previous = now;
            if (delta > .1) delta = .1; // после подвисания не «прыгаем» вперёд
            PanWithKeys(delta);

            if (running)
            {
                // Накопитель позволяет расчёту идти фиксированным шагом,
                // даже если кадры приходят с разной частотой.
                float timeScale = speeds[Math.Max(0, speed.SelectedIndex)];
                accumulator += delta * timeScale;
                int steps = 0;
                while (accumulator >= physics.Settings.Step && steps < 8)
                {
                    physics.Step();
                    accumulator -= physics.Settings.Step;
                    steps++;
                }
                if (steps == 8) accumulator = 0; // защита от «спирали смерти»
            }

            // Счётчик FPS: число кадров за последнюю секунду.
            fpsElapsed += delta;
            if (fpsElapsed >= .5)
            {
                fps = (int)Math.Round(renderedFrames / fpsElapsed);
                renderedFrames = 0;
                fpsElapsed = 0;
                infoLabel.Text = string.Format(CultureInfo.InvariantCulture,
                    "FPS: {0}   |   тел: {1}   |   контактов: {2}   |   время: {3:0.00} с",
                    fps, physics.Bodies.Count, physics.World.ContactCount, physics.Time);
            }

            canvas.Invalidate();
        }

        // ------------------------------------------------------------- отрисовка

        /// <summary>Рисует сетку, тела и служебную информацию.</summary>
        void Draw(Graphics g)
        {
            renderedFrames++;   // считаем именно отрисовки, а не срабатывания таймера
            // Сглаживание — самая дорогая часть отрисовки. При большом числе тел переключаемся на
            // быстрый режим: разница на глаз почти незаметна, а кадров заметно больше.
            // Порог 100 тел выбран по замерам: до него AntiAlias укладывается в бюджет кадра.
            g.SmoothingMode = physics.Bodies.Count > 100 ? SmoothingMode.HighSpeed : SmoothingMode.AntiAlias;

            UpdateViewport();

            DrawGrid(g);

            var state = g.Save();
            g.TranslateTransform(ox, oy);
            g.ScaleTransform(scale, -scale);      // ось Y направлена вверх, как в физике
            g.TranslateTransform(0, -PhysicsWorld.Height);

            // Границы области симуляции.
            using (var border = new Pen(Color.FromArgb(100, 123, 156), 2 / scale))
                g.DrawRectangle(border, 0, 0, PhysicsWorld.Width, PhysicsWorld.Height);

            foreach (var item in physics.Bodies)
            {
                var b = item.Body;
                var d = item.Data;
                var save = g.Save();
                g.TranslateTransform(b.Position.X, b.Position.Y);
                // Угол берём как есть, БЕЗ смены знака: ось Y уже перевёрнута выше через
                // ScaleTransform(scale, -scale), поэтому локальная система координат тела
                // совпадает с физической (Y вверх, положительный угол - против часовой).
                // Лишний минус зеркально разворачивал картинку относительно физики:
                // тело рисовалось под одним углом, а сталкивалось под другим.
                g.RotateTransform(b.Rotation * 180f / (float)Math.PI);

                using (var fill = new SolidBrush(Color.FromArgb(d.Color)))
                using (var pen = new Pen(item == selected ? Color.White : Color.FromArgb(170, 220, 235, 255),
                                         (item == selected ? 3 : 1) / scale))
                {
                    if (d.Shape == "Circle")
                    {
                        g.FillEllipse(fill, -d.Width / 2, -d.Width / 2, d.Width, d.Width);
                        g.DrawEllipse(pen, -d.Width / 2, -d.Width / 2, d.Width, d.Width);
                        // Радиус-вектор делает вращение круга заметным.
                        g.DrawLine(pen, 0, 0, d.Width / 2, 0);
                    }
                    else
                    {
                        g.FillRectangle(fill, -d.Width / 2, -d.Height / 2, d.Width, d.Height);
                        g.DrawRectangle(pen, -d.Width / 2, -d.Height / 2, d.Width, d.Height);
                    }

                    if (b.BodyType == BodyType.Static)
                        g.DrawLine(pen, -.12f, 0, .12f, 0);
                }
                g.Restore(save);
            }

            // Стрелка скорости выбранного тела. Показываем на паузе: во время симуляции
            // тело само показывает движение. Длина пропорциональна скорости, толщина не зависит от зума.
            if (velocityView.SelectedIndex != 0 && (!running || velocityWhileRunning.Checked))
            {
                if (velocityView.SelectedIndex == 2)
                {
                    foreach (var item in physics.Bodies)
                        if (item.Body.BodyType == BodyType.Dynamic) DrawVelocityArrow(g, item.Body);
                }
                else if (selected != null && selected.Body.BodyType == BodyType.Dynamic)
                {
                    DrawVelocityArrow(g, selected.Body);
                }
            }

            g.Restore(state);

            canvas.Hint = HintText();
        }

        /// <summary>
        /// Стрелка линейной скорости тела в мировых координатах (Y вверх).
        /// Длина: 1 м/с = ArrowMetersPerSecond метров; стрелка ограничена ArrowMaxLength, чтобы не закрывать сцену.
        /// </summary>
        const float ArrowMetersPerSecond = 0.4f;
        const float ArrowMaxLength = 6f;

        void DrawVelocityArrow(Graphics g, Body body)
        {
            var v = body.LinearVelocity;
            float speed = v.Length();
            if (speed < 0.05f) return;   // почти неподвижное тело стрелку не получает

            float len = Math.Min(ArrowMaxLength, speed * ArrowMetersPerSecond);
            float ux = v.X / speed, uy = v.Y / speed;
            float x0 = body.Position.X, y0 = body.Position.Y;
            float x1 = x0 + ux * len, y1 = y0 + uy * len;

            // Наконечник: две линии назад от конца под углом 25 градусов.
            float head = Math.Min(0.35f, len * 0.3f);
            float a = (float)(25 * Math.PI / 180);
            float bx = -ux, by = -uy;
            float c = (float)Math.Cos(a), sn = (float)Math.Sin(a);
            float lx = bx * c - by * sn, ly = bx * sn + by * c;
            float rx = bx * c + by * sn, ry = -bx * sn + by * c;

            using (var pen = new Pen(Color.FromArgb(255, 255, 196, 90), 2 / scale))
            {
                pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                g.DrawLine(pen, x0, y0, x1, y1);
                g.DrawLine(pen, x1, y1, x1 + lx * head, y1 + ly * head);
                g.DrawLine(pen, x1, y1, x1 + rx * head, y1 + ry * head);
            }
        }

        /// <summary>Рисует координатную сетку с шагом 1 метр.</summary>
        void DrawGrid(Graphics g)
        {
            using (var pen = new Pen(Color.FromArgb(32, 255, 255, 255)))
            {
                for (int x = 0; x <= PhysicsWorld.Width; x++)
                {
                    float px = ox + x * scale;
                    g.DrawLine(pen, px, oy, px, oy + PhysicsWorld.Height * scale);
                }
                for (int y = 0; y <= PhysicsWorld.Height; y++)
                {
                    float py = oy + y * scale;
                    g.DrawLine(pen, ox, py, ox + PhysicsWorld.Width * scale, py);
                }
            }
        }

        // ------------------------------------------------------- работа мышью

        /// <summary>Переводит координаты курсора в метры мира.</summary>
        Vector2 WorldPoint(Point point)
        {
            float x = (point.X - ox) / scale;
            float y = PhysicsWorld.Height - (point.Y - oy) / scale;
            return new Vector2(x, y);
        }

        /// <summary>Создание тела или выбор и захват существующего.</summary>
        void MouseDownScene(object sender, MouseEventArgs e)
        {
            canvas.Focus();
            // Средняя кнопка: перетаскивание камеры. Не создаёт и не выбирает тела.
            if (e.Button == MouseButtons.Middle)
            {
                panning = true;
                panStart = e.Location;
                canvas.Cursor = Cursors.SizeAll;
                return;
            }
            if (e.Button == MouseButtons.Right) { mode.SelectedIndex = 0; return; }
            if (e.Button != MouseButtons.Left) return;

            var p = WorldPoint(e.Location);
            if (p.X < 0 || p.X > PhysicsWorld.Width || p.Y < 0 || p.Y > PhysicsWorld.Height) return;

            // --- режим создания ---
            if (mode.SelectedIndex == 1)
            {
                if (physics.Bodies.Count >= 1000)
                {
                    MessageBox.Show("Достигнут предел: 1000 тел на сцене.", "Симулятор",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                float w = (float)sizeX.Value;
                float h = shape.SelectedIndex == 0 ? w : (float)sizeY.Value;
                if (p.X < w / 2 || p.X > PhysicsWorld.Width - w / 2 ||
                    p.Y < h / 2 || p.Y > PhysicsWorld.Height - h / 2)
                {
                    status.Text = "Тело не помещается в границах области — уменьшите размеры.";
                    return;
                }

                selected = physics.Add(new BodyData
                {
                    Shape = shape.SelectedIndex == 0 ? "Circle" : "Box",
                    Type = bodyType.SelectedIndex,
                    X = p.X, Y = p.Y,
                    Width = w, Height = h,
                    Density = (float)density.Value,
                    Friction = (float)friction.Value,
                    Restitution = (float)restitution.Value,
                    Vx = (float)vx.Value, Vy = (float)vy.Value,
                    Color = bodyType.SelectedIndex == 0 ? unchecked((int)0xff72819b)
                          : bodyType.SelectedIndex == 1 ? unchecked((int)0xffd18fff)
                          : unchecked((int)0xff50d4c0)
                });

                UpdateSelectedPanel();
                canvas.Invalidate();
                return;
            }

            // --- режим выбора: ищем тело под курсором ---
            var fixture = physics.World.TestPoint(p);
            selected = fixture == null ? null : fixture.Body.Tag as SceneBody;
            UpdateSelectedPanel();

            if (selected == null) return;

            // Захват динамического тела соединением: тело остаётся участником
            // симуляции и сталкивается с остальными, а не просто телепортируется.
            // На паузе физика не шагает, поэтому джоинт не сдвинет тело. Двигаем тело напрямую.
            if (!running)
            {
                pauseDragging = true;
                selected.Body.Awake = true;
                return;
            }
            if (selected.Body.BodyType == BodyType.Dynamic && running)
            {
                mouseJoint = new FixedMouseJoint(selected.Body, p) { MaxForce = 1500 * selected.Body.Mass };
                physics.World.Add(mouseJoint);
                selected.Body.Awake = true;
            }
        }

        /// <summary>Тянет захваченное тело за курсором.</summary>
        void MouseMoveScene(object sender, MouseEventArgs e)
        {
            // Панорамирование: сдвиг камеры на разницу положения курсора.
            if (panning)
            {
                panX += e.X - panStart.X;
                panY += e.Y - panStart.Y;
                panStart = e.Location;
                canvas.Invalidate();
                return;
            }
            if (pauseDragging && selected != null && e.Button == MouseButtons.Left)
            {
                var b = selected.Body;
                // Переносим только положение. Скорость и вращение тела не меняются.
                b.Position = WorldPoint(e.Location);
                b.Awake = true;
                canvas.Invalidate();
                return;
            }
            if (mouseJoint != null) mouseJoint.WorldAnchorB = WorldPoint(e.Location);
            else if (e.Button == MouseButtons.None)
                cursorInfo.Text = string.Format(CultureInfo.InvariantCulture,
                    "X = {0:0.00} м, Y = {1:0.00} м", WorldPoint(e.Location).X, WorldPoint(e.Location).Y);
        }

        /// <summary>Прокрутка колеса меняет масштаб сцены.</summary>
        /// <summary>
        /// Пересчитывает масштаб и положение мира на холсте по текущим zoom и смещению камеры.
        /// Мир 24×16 м вписывается в клиентскую область, затем применяется зум и сдвиг.
        /// </summary>
        void UpdateViewport()
        {
            scale = Math.Min(canvas.ClientSize.Width / PhysicsWorld.Width,
                             canvas.ClientSize.Height / PhysicsWorld.Height) * 0.94f * zoom;
            ox = (canvas.ClientSize.Width - PhysicsWorld.Width * scale) / 2f + panX;
            oy = (canvas.ClientSize.Height - PhysicsWorld.Height * scale) / 2f + panY;
        }

        void WheelScene(object sender, MouseEventArgs e)
        {
            // Запоминаем мировую точку под курсором до изменения масштаба...
            Vector2 anchor = WorldPoint(e.Location);

            zoom *= e.Delta > 0 ? 1.1f : 1 / 1.1f;
            zoom = Math.Max(.4f, Math.Min(4, zoom));

            // ...и сдвигаем камеру так, чтобы эта точка осталась под курсором.
            // Без этого масштабирование шло бы от центра холста, и сцена уезжала бы в сторону.
            // Пересчитываем масштаб. Сдвиг камеры считаем напрямую: нужно, чтобы точка anchor
            // оказалась в пикселе курсора (e.X, e.Y) при новом масштабе.
            UpdateViewport();
            float baseX = (canvas.ClientSize.Width - PhysicsWorld.Width * scale) / 2f;   // центр без сдвига
            float baseY = (canvas.ClientSize.Height - PhysicsWorld.Height * scale) / 2f;
            panX = e.X - anchor.X * scale - baseX;
            panY = e.Y - (PhysicsWorld.Height - anchor.Y) * scale - baseY;
            UpdateViewport();

            canvas.Invalidate();
        }

        /// <summary>Освобождает захваченное тело.</summary>
        /// <summary>Завершает панорамирование и отпускает захваченное тело.</summary>
        void MouseUpScene(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle)
            {
                panning = false;
                canvas.Cursor = Cursors.Default;
                return;
            }
            pauseDragging = false;
            ReleaseJoint();
        }

        /// <summary>Удаляет выбранное тело (кнопкой или клавишей Delete).</summary>
        void DeleteSelected()
        {
            if (selected == null)
            {
                status.Text = "Сначала выберите тело на сцене.";
                return;
            }
            ReleaseJoint();              // отпускаем захват мышью, иначе соединение ссылается на удалённое тело
            physics.Remove(selected);
            selected = null;
            UpdateSelectedPanel();
            canvas.Invalidate();
        }

        void ReleaseJoint()
        {
            if (mouseJoint != null)
            {
                physics.World.Remove(mouseJoint);
                mouseJoint = null;
            }
        }

        // ------------------------------------------- мгновенное применение свойств

        /// <summary>Заполняет панель свойствами выбранного тела без запуска обработчиков.</summary>
        void UpdateSelectedPanel()
        {
            syncing = true;
            UpdateSelectedPanelCore();
            syncing = false;
        }

        /// <summary>Внутренняя часть синхронизации: значения полей и доступность.</summary>
        void UpdateSelectedPanelCore()
        {
            bool has = selected != null;
            groupSelected.Enabled = has;

            if (!has)
            {
                selectedLabel.Text = "Объект не выбран";
                massInfo.Text = "Выберите тело на сцене, чтобы\nизменить его свойства.";
                return;
            }

            var d = selected.Data;
            var b = selected.Body;

            selectedLabel.Text = d.Shape == "Circle" ? "Выбран: круг" : "Выбран: прямоугольник";

            // Набор полей зависит от формы: у круга нет высоты, подпись «Диаметр».
            // Перестраиваем, только если форма изменилась, чтобы не мешать вводу с клавиатуры.
            if (lastSelectedShape != d.Shape)
            {
                lastSelectedShape = d.Shape;
                BuildSelectedFields();
            }

            selWidth.Value = Clamp((decimal)d.Width, selWidth);
            selHeight.Value = Clamp((decimal)d.Height, selHeight);
            selDensity.Value = Clamp((decimal)d.Density, selDensity);
            selMass.Value = Clamp((decimal)b.Mass, selMass);
            selFriction.Value = Clamp((decimal)d.Friction, selFriction);
            selRestitution.Value = Clamp((decimal)d.Restitution, selRestitution);
            selVx.Value = Clamp((decimal)b.LinearVelocity.X, selVx);
            selVy.Value = Clamp((decimal)b.LinearVelocity.Y, selVy);

            // Для статических и кинематических тел масса не рассчитывается.
            bool dynamic = b.BodyType == BodyType.Dynamic;
            selMass.Enabled = dynamic;
            selDensity.Enabled = dynamic;
            selVx.Enabled = b.BodyType != BodyType.Static;
            selVy.Enabled = b.BodyType != BodyType.Static;

            massInfo.Text = dynamic
                ? string.Format(CultureInfo.InvariantCulture,
                    "Площадь: {0:0.00} м²   Масса: {1:0.00} кг\nИзменения применяются сразу.",
                    d.Area, b.Mass)
                : "Статическое или кинематическое тело:\nмасса не рассчитывается.";
        }

        /// <summary>
        /// Применяет к выбранному телу все поля панели.
        /// Вызывается из обработчиков ValueChanged, поэтому работает в реальном времени.
        /// </summary>
        void ApplySelected()
        {
            if (syncing || selected == null) return;

            // Тело будет пересоздано — соединение мыши на него больше не годится.
            if (Math.Abs((float)selWidth.Value - selected.Data.Width) > .0001f ||
                Math.Abs((float)selHeight.Value - selected.Data.Height) > .0001f)
                ReleaseJoint();

            physics.Apply(selected, new BodyData
            {
                Shape = selected.Data.Shape,
                Width = (float)selWidth.Value,
                Height = (float)selHeight.Value,
                Density = (float)selDensity.Value,
                Friction = (float)selFriction.Value,
                Restitution = (float)selRestitution.Value,
                Vx = (float)selVx.Value,
                Vy = (float)selVy.Value
            });

            // Обновляем массу и пояснение, не сбрасывая положения полей.
            massInfo.Text = selected.Body.BodyType == BodyType.Dynamic
                ? string.Format(CultureInfo.InvariantCulture,
                    "Площадь: {0:0.00} м²   Масса: {1:0.00} кг\nИзменения применяются сразу.",
                    selected.Data.Area, selected.Body.Mass)
                : "Статическое или кинематическое тело:\nмасса не рассчитывается.";

            // Масса вычисляется движком, поэтому поле нужно синхронизировать вручную.
            syncing = true;
            selMass.Value = Clamp((decimal)selected.Body.Mass, selMass);
            syncing = false;

            canvas.Invalidate();
        }

        /// <summary>Меняет только плотность: трение, упругость и скорость не трогаются.</summary>
        void ApplyDensity()
        {
            if (syncing || selected == null) return;

            physics.Apply(selected, new BodyData
            {
                Shape = selected.Data.Shape,
                Width = selected.Data.Width,
                Height = selected.Data.Height,
                Density = (float)selDensity.Value,
                Friction = selected.Data.Friction,
                Restitution = selected.Data.Restitution,
                Vx = selected.Body.LinearVelocity.X,
                Vy = selected.Body.LinearVelocity.Y
            });

            syncing = true;
            selMass.Value = Clamp((decimal)selected.Body.Mass, selMass);
            syncing = false;

            massInfo.Text = string.Format(CultureInfo.InvariantCulture,
                "Площадь: {0:0.00} м²   Масса: {1:0.00} кг\nИзменения применяются сразу.",
                selected.Data.Area, selected.Body.Mass);
            canvas.Invalidate();
        }

        /// <summary>Задаёт массу напрямую: плотность пересчитывается как масса ÷ площадь.</summary>
        void ApplyMass()
        {
            if (syncing || selected == null) return;

            float area = selected.Data.Area;
            if (area <= 0.0001f) return;

            float density = (float)selMass.Value / area;
            density = Math.Max(.1f, Math.Min(100f, density)); // держим плотность в допустимых пределах

            physics.Apply(selected, new BodyData
            {
                Shape = selected.Data.Shape,
                Width = selected.Data.Width,
                Height = selected.Data.Height,
                Density = density,
                Friction = selected.Data.Friction,
                Restitution = selected.Data.Restitution,
                Vx = selected.Body.LinearVelocity.X,
                Vy = selected.Body.LinearVelocity.Y
            });

            syncing = true;
            selDensity.Value = Clamp((decimal)density, selDensity);
            syncing = false;

            massInfo.Text = string.Format(CultureInfo.InvariantCulture,
                "Площадь: {0:0.00} м²   Масса: {1:0.00} кг\nПлотность: {2:0.00} кг/м²",
                area, selected.Body.Mass, density);
            canvas.Invalidate();
        }

        /// <summary>Ограничивает значение диапазоном поля ввода.</summary>
        static decimal Clamp(decimal value, NumericUpDown n)
        {
            return Math.Max(n.Minimum, Math.Min(n.Maximum, value));
        }

        // ------------------------------------------------- сохранение и клавиши

        /// <summary>Сохраняет сцену в файл с обработкой ошибок.</summary>
        void Save()
        {
            using (var dialog = new SaveFileDialog
            {
                Filter = "Сцена XML|*.xml",
                FileName = "scene.xml",
                Title = "Сохранить сцену"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    PhysicsWorld.SaveFile(dialog.FileName, physics.Capture());
                    status.Text = "Сцена сохранена: " + dialog.FileName;
                }
                catch (Exception ex)
                {
                    // Некорректные данные не должны приводить к падению программы.
                    MessageBox.Show("Не удалось сохранить сцену:\n" + ex.Message, "Ошибка сохранения",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        /// <summary>Загружает сцену из файла; при ошибке текущая сцена не меняется.</summary>
        void LoadScene()
        {
            using (var dialog = new OpenFileDialog
            {
                Filter = "Сцена XML|*.xml",
                Title = "Загрузить сцену"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var scene = PhysicsWorld.ReadFile(dialog.FileName);
                    Pause();
                    SetScene(scene);
                    status.Text = "Сцена загружена: " + dialog.FileName;
                }
                catch (Exception ex)
                {
                    // Файл проверяется до применения, поэтому старая сцена остаётся целой.
                    MessageBox.Show("Файл сцены повреждён или несовместим:\n" + ex.Message,
                        "Ошибка загрузки", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        /// <summary>Горячие клавиши: пробел — пуск/пауза, Delete — удаление, Esc — сброс выбора.</summary>
        /// <summary>Отпускание клавиши камеры: убираем её из набора зажатых.</summary>
        void KeyUpHandler(object sender, KeyEventArgs e)
        {
            heldKeys.Remove(e.KeyCode);
        }

        /// <summary>При потере фокуса очищаем набор, иначе камера уедет сама.</summary>
        void ClearHeldKeys(object sender, EventArgs e)
        {
            heldKeys.Clear();
        }

        static bool IsCameraKey(Keys k)
        {
            return k == Keys.W || k == Keys.A || k == Keys.S || k == Keys.D;
        }

        /// <summary>Фокус в поле ввода (число, текст, список): буквы не должны двигать сцену.</summary>
        bool InputFocused()
        {
            Control c = ActiveControl;
            // Выпадающий список (ComboBox со стилем DropDownList) текст не принимает, поэтому
            // клавиши 1, 2 и WASD должны работать, даже когда фокус стоит на нём.
            return c is TextBoxBase || c is NumericUpDown;
        }

        /// <summary>Двигает камеру, пока зажаты W A S D. Вызывается каждый кадр, в том числе на паузе.</summary>
        void PanWithKeys(double seconds)
        {
            if (heldKeys.Count == 0 || InputFocused()) return;
            float step = KeyPanSpeed * (float)seconds;
            float dx = 0, dy = 0;
            if (heldKeys.Contains(Keys.A)) dx += step;   // камера влево: сцена смещается вправо
            if (heldKeys.Contains(Keys.D)) dx -= step;
            if (heldKeys.Contains(Keys.W)) dy += step;   // камера вверх: сцена смещается вниз
            if (heldKeys.Contains(Keys.S)) dy -= step;
            if (dx == 0 && dy == 0) return;
            panX += dx;
            panY += dy;
            canvas.Invalidate();
        }

        void KeyDownHandler(object sender, KeyEventArgs e)
        {
            if (IsCameraKey(e.KeyCode) && !InputFocused())
            {
                heldKeys.Add(e.KeyCode);
                e.Handled = true;
                return;
            }
            // 1 и 2 — быстрое переключение инструмента. В полях ввода цифры остаются для ввода чисел.
            if ((e.KeyCode == Keys.D1 || e.KeyCode == Keys.NumPad1 || e.KeyCode == Keys.D2 || e.KeyCode == Keys.NumPad2)
                && !InputFocused())
            {
                mode.SelectedIndex = (e.KeyCode == Keys.D1 || e.KeyCode == Keys.NumPad1) ? 0 : 1;
                e.Handled = true;
                return;
            }
            // Пробел, Delete и Esc, как и остальные горячие клавиши, не срабатывают в поле ввода:
            // там они нужны для набора и правки числа (иначе Delete удалял бы выбранное тело).
            if (e.KeyCode == Keys.Space && !InputFocused())
            {
                if (running) Pause(); else Start();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Delete && selected != null && !InputFocused())
            {
                DeleteSelected();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape && !InputFocused())
            {
                ReleaseJoint();
                selected = null;
                UpdateSelectedPanel();
                canvas.Invalidate();
            }
        }
    }
}
