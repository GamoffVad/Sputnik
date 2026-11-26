using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Diagnostics;
using System.Runtime.InteropServices;

sealed class TestApp : Sputnik.App { protected override void OnStartup(StartupEventArgs e) { } }
static class Program
{
    const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static Type TypeOf(object target)=>target as Type??(target is Sputnik.App?typeof(Sputnik.App):target.GetType());
    static object? Call(object target,string method,params object?[] args)=>TypeOf(target).GetMethod(method,Flags)!.Invoke(target is Type?null:target,args);
    static object? Field(object target,string field)=>TypeOf(target).GetField(field,Flags)!.GetValue(target is Type?null:target);
    static void Assert(bool condition,string text){if(!condition)throw new Exception(text);}
    static void Pump()=>Application.Current.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
    [STAThread]
    static void Main(string[] args)
    {
        string host=Path.GetFullPath(args[0]);
        AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY",host+Path.DirectorySeparatorChar);
        var app=new TestApp{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/Sputnik;component/Components/Controls.xaml")});
        var assembly=typeof(Sputnik.App).Assembly;
        Call(assembly.GetType("Sputnik.ThemePalette")!,"Apply",(object?)null);
        var petType=assembly.GetType("Sputnik.DesktopPetWindow")!;
        var pet=(Window)Activator.CreateInstance(petType,new Action<string>(section=>Call(app,"ShowWorkspace",section)),new Action(()=>{}))!;
        var catalogue=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(host,"wwwroot/assets/pets.json")))!.AsArray();
        var model=JsonSerializer.Deserialize(catalogue[0]!.ToJsonString(),petType.GetNestedType("Pet",Flags)!,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        Call(pet,"UsePet",model,Path.Combine(host,"wwwroot"),null);
        typeof(Sputnik.App).GetField("pet",Flags)!.SetValue(app,pet);
        int exits=0,quick=0;
        var tray=(IDisposable)Activator.CreateInstance(assembly.GetType("Sputnik.DesktopTray")!,Flags,null,
            [new Action(()=>Call(app,"RestoreFromTray")),new Action(()=>quick++),new Action(()=>Call(app,"HideToTray")),new Action(()=>exits++),new Func<bool>(()=>(bool)Field(app,"hiddenInTray")!)],null)!;
        typeof(Sputnik.App).GetField("tray",Flags)!.SetValue(app,tray);
        var note=new Window{Width=240,Height=180,Title="Isolated tray test"};
        var unused=new Window{Width=240,Height=180};
        try
        {
            pet.Show();note.Show();Pump();
            Assert((bool)tray.GetType().GetProperty("Available",Flags)!.GetValue(tray)!,"Shell did not accept notification icon");
            var position=pet.RestoreBounds;
            var controls=Field(pet,"controls")!;
            Call(controls,"ShowMenu","Fixture",false);Pump();
            var buttons=(Dictionary<string,Button>)Field(controls,"menuItems")!;
            buttons["tray"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
            Assert(!pet.IsVisible&&!note.IsVisible&&(bool)Field(app,"hiddenInTray")!,"Hide left application windows visible");
            Assert(!((DispatcherTimer)Field(pet,"timer")!).IsEnabled&&!((Stopwatch)Field(pet,"elapsed")!).IsRunning,"Hidden pet continues animation work");
            Call(app,"HideToTray");
            IntPtr handle=((HwndSource)Field(tray,"source")!).Handle;
            SendMessage(handle,0x8000+55,new IntPtr(1),new IntPtr(0x202));Pump();
            Assert(pet.IsVisible&&note.IsVisible&&!unused.IsVisible&&!(bool)Field(app,"hiddenInTray")!,"Native tray click did not restore original visible windows");
            Assert(pet.RestoreBounds==position,"Tray restore moved/resized pet");
            Assert(((DispatcherTimer)Field(pet,"timer")!).IsEnabled,"Animation timer did not resume");
            Call(pet,"SetPlayback","pause",0);
            Call(app,"HideToTray");Call(app,"RestoreFromTray");Pump();
            Assert(!((Stopwatch)Field(pet,"elapsed")!).IsRunning,"Paused animation resumed unexpectedly");
            for(int i=0;i<3;i++){Call(app,"HideToTray");Call(app,"RestoreFromTray");}
            SendMessage(handle,0x8000+55,new IntPtr(1),new IntPtr(0x205));Pump();
            var menu=(ContextMenu)Field(tray,"menu")!;
            var items=menu.Items.OfType<MenuItem>().ToArray();
            Assert(items.Select(item=>item.Header).SequenceEqual(new[]{"Скрыть в трей","Быстрый чат","Выход"}),"Tray context actions missing");
            items[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            items[2].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert(quick==1&&exits==1,"Tray quick/exit actions were not dispatched");
            menu.IsOpen=false;
            SendMessage(handle,RegisterWindowMessage("TaskbarCreated"),IntPtr.Zero,IntPtr.Zero);
            Assert((bool)tray.GetType().GetProperty("Available",Flags)!.GetValue(tray)!,"Tray icon did not recover after Explorer restart message");
            Console.WriteLine("PASS native tray icon/callback, hide/restore visible windows, repeated hide, preserved placement and pause, no hidden animation work, quick/exit actions, Explorer recovery and disposal; no user data written.");
        }
        finally {tray.Dispose();tray.Dispose();note.Close();unused.Close();pet.Close();}
    }
    [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int RegisterWindowMessage(string name);
}
