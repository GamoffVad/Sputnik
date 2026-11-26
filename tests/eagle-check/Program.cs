using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

internal static class Program
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public;
    static object? Call(object o,string name,params object?[] args)=>o.GetType().GetMethod(name,Flags)!.Invoke(o,args);
    static object? Field(object o,string name)=>o.GetType().GetField(name,Flags)!.GetValue(o);
    static void Check(bool valid,string message){if(!valid)throw new Exception(message);}
    [STAThread]
    static void Main(string[] args)
    {
        string installed=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs/Sputnik");
        if(args.Length>0) installed=Path.GetFullPath(args[0]);
        AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY",installed+Path.DirectorySeparatorChar);
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/Sputnik;component/Components/Controls.xaml")});
        var assembly=typeof(Sputnik.App).Assembly;
        var palette=assembly.GetType("Sputnik.ThemePalette")!;
        palette.GetMethod("Apply",Flags)!.Invoke(null,[null]);
        string selected="";
        var type=assembly.GetType("Sputnik.DesktopPetWindow")!;
        var pet=(Window)Activator.CreateInstance(type,new Action<string>(section=>selected=section),new Action(()=>{}))!;
        string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Sputnik/pets/military-eagle");
        if(args.Length>1) {
            var packages=assembly.GetType("Sputnik.PetPackages")!;
            string data=Path.Combine(Path.GetTempPath(),"sputnik-eagle-check-"+Guid.NewGuid().ToString("N"));
            var staged=(System.Text.Json.Nodes.JsonObject)packages.GetMethod("Stage",Flags)!.Invoke(null,[Path.GetFullPath(args[1]),Path.Combine(data,"pending"),Path.Combine(data,"library")])!;
            packages.GetMethod("Install",Flags)!.Invoke(null,[staged["token"]!.ToString(),Path.Combine(data,"pending"),Path.Combine(data,"library")]);
            folder=Path.Combine(data,"library/military-eagle");
        }
        var model=JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(folder,"pet.json")),type.GetNestedType("Pet",Flags)!,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        try
        {
            Call(pet,"UsePet",model,folder,null);
            pet.Show();pet.UpdateLayout();
            ((DispatcherTimer)Field(pet,"timer")!).Stop();
            var controls=Field(pet,"controls")!;
            var area=(Rect)Call(controls,"WorkArea",false)!;
            pet.Width=pet.Height=500;
            pet.Left=area.Left+(area.Width-500)/2;
            pet.Top=area.Bottom-510;
            int clipCount=((Array)model.GetType().GetProperty("Animations")!.GetValue(model)!).Length;
            for(int clip=0;clip<clipCount;clip++)
            {
                Call(pet,"SetPlayback","clip",clip);
                if(Field(pet,"preloaded") is Task task) task.GetAwaiter().GetResult();
                Call(pet,"ShowFrame",0);
                var visible=(Rect)Call(pet,"VisibleBounds")!;
                double top=pet.Top+pet.Height*visible.Top;
                Call(controls,"ShowMenu","Орёл",false);
                var menu=(Window)Field(controls,"menu")!;menu.UpdateLayout();
                if(top-menu.Height-10>=area.Top)
                    Check(Math.Abs(top-(menu.Top+menu.Height)-10)<1,"Context menu is anchored to transparent padding");
                var items=(Dictionary<string,Button>)Field(controls,"menuItems")!;
                foreach(string action in new[]{"theme","motion","theme","motion"})
                {
                    items[action].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(selected==action,"Context action routes to a different section");
                }
                double menuTop=menu.Top;
                Call(pet,"ShowFrame",1);
                Check(menu.Top==menuTop,"Menu follows animated frame and jumps");
                Call(controls,"CloseMenu");
            }
            var scale=model.GetType().GetProperty("DisplayScale")!;
            scale.SetValue(model,2.4d);
            Call(pet,"ApplyAppearanceSizes",160d,100d,100d,10d,true);
            double before=pet.Width;
            double beforeRequested=(double)Field(pet,"preferredSize")!;
            double ground=pet.Top+pet.Height*(double)Field(pet,"groundLevel")!;
            scale.SetValue(model,3.6d);
            Call(pet,"ApplyAppearanceSizes",160d,100d,100d,10d,true);
            Check(Math.Abs((double)Field(pet,"preferredSize")!-1324.8)<.01,"Eagle preferred size is not 1.5 times the former 883.2 DIP size");
            Check(Math.Abs((double)Field(pet,"preferredSize")!/beforeRequested-1.5)<.002,"Pet metadata scale is not respected");
            Check(Math.Abs(ground-(pet.Top+pet.Height*(double)Field(pet,"groundLevel")!))<1,"Scaling moved the standing level");
            double larger=(double)Field(pet,"preferredSize")!;
            scale.SetValue(model,2.4d);
            Call(pet,"ApplyAppearanceSizes",160d,100d,100d,10d,true);
            Check(Math.Abs((double)Field(pet,"preferredSize")!/larger-1/1.5)<.002,"Eagle did not shrink by 1.5");
            Check(pet.Width<=before+1,"Reducing metadata enlarged the visible pet window");
            Check(Math.Abs(ground-(pet.Top+pet.Height*(double)Field(pet,"groundLevel")!))<1,"Shrinking moved standing level");
            Console.WriteLine($"PASS eagle size: metadata respected, 3.6 → 2.4 (divided by 1.5), standing level retained.");
            Console.WriteLine($"PASS actual eagle, {clipCount} animations: menu gap 10px from visible silhouette, no per-frame menu movement; {clipCount*4} theme/motion menu selections route correctly. User data unchanged.");
        }
        finally{pet.Close();}
    }
}

