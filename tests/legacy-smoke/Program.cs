using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
class Check
{
    static IEnumerable<T> Find<T>(DependencyObject root) where T:DependencyObject
    {
        if(root is T match) yield return match;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var child in Find<T>(VisualTreeHelper.GetChild(root,i))) yield return child;
    }
    [STAThread] static void Main()
    {
        var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("pack://application:,,,/Sputnik;component/Components/Controls.xaml") });
        var data=Path.GetFullPath("artifacts/legacy-smoke/data-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(data); File.WriteAllText(Path.Combine(data,"workspace.json"),"{}");
        var window=new Sputnik.MainWindow { Left=100,Top=100 };
        var flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        typeof(Sputnik.MainWindow).GetField("data",flags).SetValue(window,data);
        try
        {
            window.Show();
            typeof(Sputnik.MainWindow).GetMethod("OpenSection",flags).Invoke(window,new object[]{"theme"});
            window.UpdateLayout(); window.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
            var mode=Find<ComboBox>(window).Single(c=>AutomationProperties.GetName(c)=="Цвет новых заметок");
            mode.SelectedIndex=1;
            if(window.ResizeMode != ResizeMode.NoResize || Find<Thumb>(window).Any(t=>AutomationProperties.GetName(t).StartsWith("Размер окна:"))) throw new Exception("Unexpected resize controls");
            if(((HwndSource)PresentationSource.FromVisual(window)).CompositionTarget.RenderMode!=RenderMode.SoftwareOnly) throw new Exception("Wrong rendering mode");
            var save=Find<Button>(window).SingleOrDefault(b=>AutomationProperties.GetName(b)=="Сохранить");
            if(save!=null) save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if((save!=null && window.IsVisible) || !File.ReadAllText(Path.Combine(data,"workspace.json")).Contains("\"noteColorMode\":\"auto\"")) throw new Exception("Legacy settings save failed");
            Console.WriteLine("PASS: .NET Framework 4.8 WPF initialization, fixed panel, auto-color selector and persistence on current Windows; process64="+Environment.Is64BitProcess);
        }
        finally { window.Close(); app.Shutdown(); }
    }
}
