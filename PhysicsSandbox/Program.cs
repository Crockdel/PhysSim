// ============================================================================
//  Program.cs — точка входа приложения.
//
//  Настраивает визуальные стили Windows и запускает главную форму.
//  Отдельный файл нужен, чтобы форма оставалась только формой.
// ============================================================================

using System;
using System.Windows.Forms;

namespace PhysicsSandbox
{
    internal static class Program
    {
        /// <summary>Включает современное оформление элементов управления и запускает окно.</summary>
        [STAThread]
        static void Main()
        {
            // Без этого вызова кнопки и поля выглядят как в Windows 98.
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
