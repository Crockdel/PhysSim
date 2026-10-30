using System;
using System.Reflection;
using System.Windows.Forms;
using PhysicsSandbox;
static class Tool {
  static object G(object o, string n) { return o.GetType().GetField(n, BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o); }
  static void Key(Form f, Keys k) {
    var m = typeof(MainForm).GetMethod("KeyDownHandler", BindingFlags.Instance|BindingFlags.NonPublic);
    m.Invoke(f, new object[]{ f, new KeyEventArgs(k) });
  }
  static void Main() {
    var f = new MainForm(); f.Show(); Application.DoEvents();
    var mode = (ComboBox)G(f, "mode");
    mode.SelectedIndex = 0;
    Key(f, Keys.D2); Console.WriteLine("клавиша 2 -> режим " + mode.SelectedIndex + " (ожидаем 1 - создание)");
    Key(f, Keys.D1); Console.WriteLine("клавиша 1 -> режим " + mode.SelectedIndex + " (ожидаем 0 - выбор)");
    Key(f, Keys.NumPad2); Console.WriteLine("NumPad 2 -> режим " + mode.SelectedIndex + " (ожидаем 1)");
    var tb = (NumericUpDown)G(f, "sizeX"); f.Controls.Add(tb); tb.Focus(); Application.DoEvents();
    mode.SelectedIndex = 0;
    Key(f, Keys.D2); Console.WriteLine("фокус в числе, клавиша 2 -> режим " + mode.SelectedIndex + " (ожидаем 0, не меняется)");
    f.Close();
  }
}
