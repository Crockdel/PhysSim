using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Forms;
using PhysicsSandbox;
static class Final {
  static object G(object o, string n) { return o.GetType().GetField(n, BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o); }
  static void Main() {
    var f = new MainForm();
    var physics = (PhysicsWorld)G(f, "physics");
    var status = (Control)G(f, "status");
    var stop = new Timer { Interval = 4000 };
    double t0 = 0; var sw = new Stopwatch();
    stop.Tick += (s, e) => { stop.Stop(); sw.Stop();
      Console.WriteLine("симуляция " + (physics.Time - t0).ToString("0.00") + " с за " + sw.Elapsed.TotalSeconds.ToString("0.00") + " с реального; подшаги=" + physics.Settings.SubSteps);
      Console.WriteLine(status.Text);
      f.Close(); };
    f.Shown += (s, e) => {
      typeof(MainForm).GetMethod("SetScene", BindingFlags.Instance|BindingFlags.NonPublic).Invoke(f, new object[]{ PhysicsWorld.Demo() });
      typeof(MainForm).GetMethod("Start", BindingFlags.Instance|BindingFlags.NonPublic).Invoke(f, null);
      t0 = physics.Time; sw.Start(); stop.Start(); };
    Application.Run(f);
  }
}
