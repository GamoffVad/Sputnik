using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

sealed class TestApp : Sputnik.App { protected override void OnStartup(StartupEventArgs e) { } }
static class Program
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    static Type TypeOf(object o)=>o as Type??(o is Sputnik.App?typeof(Sputnik.App):o.GetType());
    static object? Call(object o,string name,params object?[] args)=>TypeOf(o).GetMethod(name,Flags)!.Invoke(o is Type?null:o,args);
    static object? Field(object o,string name)=>TypeOf(o).GetField(name,Flags)!.GetValue(o is Type?null:o);
    static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
    static IEnumerable<T> Find<T>(DependencyObject root) where T:DependencyObject
    {
        if(root is T value)yield return value;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var item in Find<T>(VisualTreeHelper.GetChild(root,i)))yield return item;
    }
    [STAThread]
    static void Main(string[] args)
    {
        string host=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
        AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY",host+Path.DirectorySeparatorChar);
        var app=new TestApp{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/Sputnik;component/Components/Controls.xaml")});
        var assembly=typeof(Sputnik.App).Assembly;
        Call(assembly.GetType("Sputnik.ThemePalette")!,"Fill",app.Resources,Activator.CreateInstance(assembly.GetType("Sputnik.PetTheme")!,"#C18D6B","#352923","#221B17"));
        app.Resources["ThemeFont"]=new FontFamily("Segoe UI");
        var type=assembly.GetType("Sputnik.DesktopPetWindow")!;
        var pet=(Window)Activator.CreateInstance(type,new Action<string>(_=>{}),new Action(()=>{}))!;
        var catalogue=JsonNode.Parse(File.ReadAllText(Path.Combine(host,"wwwroot/assets/pets.json")))!.AsArray();
        var model=JsonSerializer.Deserialize(catalogue[0]!.ToJsonString(),type.GetNestedType("Pet",Flags)!,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        Call(pet,"UsePet",model,Path.Combine(host,"wwwroot"),null);
        typeof(Sputnik.App).GetField("pet",Flags)!.SetValue(app,pet);
        var panel=new Sputnik.MainWindow{Width=426,Height=480,Left=100,Top=100};
        var data=Path.Combine(output,"data");Directory.CreateDirectory(data);File.WriteAllText(Path.Combine(data,"workspace.json"),"{}");
        typeof(Sputnik.MainWindow).GetField("data",Flags)!.SetValue(panel,data);
        typeof(Sputnik.App).GetField("workspace",Flags)!.SetValue(app,panel);
        void Layout(){panel.UpdateLayout();panel.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);}
        void Open(){Call(panel,"OpenSection","motion");Layout();}
        Button Transport()=>Find<Button>(panel).Single(b=>AutomationProperties.GetName(b) is "Пауза" or "Продолжить");
        CheckBox Shuffle()=>Find<CheckBox>(panel).Single(c=>AutomationProperties.GetName(c)=="Случайный порядок");
        void Click(Button b){b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Layout();}
        void Toggle(){var toggle=Shuffle();toggle.IsChecked=toggle.IsChecked!=true;toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));Layout();}
        void Snapshot(string name)
        {
            Layout();var bitmap=new RenderTargetBitmap((int)panel.ActualWidth,(int)panel.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(panel);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var f=File.Create(Path.Combine(output,name+".png"));encoder.Save(f);
        }
        try
        {
            panel.Show();Open();((DispatcherTimer)Field(pet,"timer")!).Stop();
            Assert(AutomationProperties.GetName(Transport())=="Пауза","Initial transport is not pause");
            Assert(!Find<Button>(panel).Any(b=>AutomationProperties.GetName(b)=="Случайный порядок"),"Shuffle remains an always-primary button");
            Click(Transport());Assert((bool)Field(pet,"paused")!&&AutomationProperties.GetName(Transport())=="Продолжить","Pause icon/state not synchronized");
            Snapshot("paused-shuffle-on");
            Toggle();Assert(!(bool)Field(pet,"shuffleMotions")!&&Shuffle().IsChecked==false,"Shuffle off did not reach controller");
            Assert((bool)Field(pet,"paused")!,"Shuffle toggle unexpectedly resumed playback");
            var saved=JsonNode.Parse(File.ReadAllText(Path.Combine(data,"workspace.json")))!;
            Assert(saved["shuffleMotions"]!.GetValue<bool>()==false,"Shuffle off was not persisted");
            Open();Assert(Shuffle().IsChecked==false&&AutomationProperties.GetName(Transport())=="Продолжить","Reopen lost playback state");
            Snapshot("paused-shuffle-off");
            Click(Transport());Assert(!(bool)Field(pet,"paused")!,"Play did not resume");
            Call(pet,"SetPlayback","clip",0);
            var names=((Array)model.GetType().GetProperty("Animations")!.GetValue(model)!).Cast<object>().Select(c=>c.GetType().GetProperty("Name")!.GetValue(c)!.ToString()).ToArray();
            for(int i=1;i<Math.Min(4,names.Length);i++)
            {
                Call(pet,"PlayNext",false);
                Assert(Field(pet,"current")!.GetType().GetProperty("Name")!.GetValue(Field(pet,"current"))!.ToString()==names[i],"Disabled shuffle does not play in source order");
            }
            Toggle();Assert((bool)Field(pet,"shuffleMotions")!&&Shuffle().IsChecked==true,"Shuffle on did not reach controller");
            Assert(JsonNode.Parse(File.ReadAllText(Path.Combine(data,"workspace.json")))!["shuffleMotions"]!.GetValue<bool>(),"Shuffle on was not persisted");
            Snapshot("playing-shuffle-on");
            foreach(double width in new[]{320d,426d})
            {
                panel.Width=width;Layout();
                var bounds=Shuffle().TransformToAncestor(panel).TransformBounds(new Rect(Shuffle().RenderSize));
                Assert(bounds.Left>=0&&bounds.Right<=panel.ActualWidth,"Playback toolbar is clipped");
            }
            Console.WriteLine("PASS single play/pause control and icon, actual shuffle on/off, paused toggle, sequential playback, persisted mode, reopen, 320/426px fit; screenshots saved. Isolated settings only.");
        }
        finally{panel.Close();pet.Close();}
    }
}
