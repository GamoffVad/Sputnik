using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

static class Check
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    static object? Call(object target,string name,params object?[] args)
    {
        var type=target as Type??target.GetType();
        try{return type.GetMethod(name,Flags)!.Invoke(target is Type?null:target,args);}
        catch(TargetInvocationException error)when(error.InnerException!=null){throw error.InnerException;}
    }
    static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
    [STAThread]
    static void Main(string[] args)
    {
        string host=Path.GetFullPath(args[0]), package=Path.GetFullPath(args[1]), data=Path.GetFullPath(args[2]);
        AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY",host+Path.DirectorySeparatorChar);
        var assembly=typeof(Sputnik.App).Assembly;
        var packages=assembly.GetType("Sputnik.PetPackages")!;
        var preview=(JsonObject)Call(packages,"Stage",package,Path.Combine(data,"pending"),Path.Combine(data,"pets"))!;
        Assert(preview["animations"]!.AsArray().Count==13&&preview["neutral"]==null,"Wrong clips or reference still present");
        Call(packages,"Install",preview["token"]!.ToString(),Path.Combine(data,"pending"),Path.Combine(data,"pets"));
        string folder=Path.Combine(data,"pets","christmas-tree"),json=File.ReadAllText(Path.Combine(folder,"pet.json"));
        if(args.Length>3&&args[3]=="install-only") {Console.WriteLine("PASS production Stage/Install: 13 animations, no reference");return;}
        var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
        app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/Sputnik;component/Components/Controls.xaml")});
        Call(assembly.GetType("Sputnik.ThemePalette")!,"Fill",app.Resources,Activator.CreateInstance(assembly.GetType("Sputnik.PetTheme")!,"#91B879","#273328","#182119"));
        app.Resources["ThemeFont"]=new FontFamily("Segoe UI");
        var type=assembly.GetType("Sputnik.DesktopPetWindow")!;
        var pet=JsonSerializer.Deserialize(json,type.GetNestedType("Pet",BindingFlags.NonPublic)!,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        var window=(Window)Activator.CreateInstance(type,new Action<string>(_=>{}),new Action(()=>{}))!;
        object? Field(string name)=>type.GetField(name,Flags)!.GetValue(window);
        int visited=0;var hashes=new HashSet<string>();
        try
        {
            Call(window,"UsePet",pet,folder,null);
            ((DispatcherTimer)Field("timer")!).Stop();
            var clips=JsonNode.Parse(json)!["animations"]!.AsArray();
            foreach(var clip in clips) Assert(clip!["frameCount"]!.GetValue<int>()==127&&clip["fps"]!.GetValue<int>()==24&&clip["neutral"]==null,"Reference or incorrect timing");
            for(int c=0;c<clips.Count;c++)
            {
                Call(window,"SetPlayback","clip",c);
                var clip=clips[c]!;var sheets=clip["sheets"]!.AsArray();int page=-1;BitmapSource? atlas=null;
                for(int frame=0;frame<127;frame++)
                {
                    if(Field("preloaded") is Task next)next.GetAwaiter().GetResult();
                    Call(window,"ShowFrame",frame);
                    int p=0,local=frame;
                    while(local>=sheets[p]!["frames"]!.GetValue<int>()){local-=sheets[p]!["frames"]!.GetValue<int>();p++;}
                    if(page!=p){atlas=(BitmapSource)Call(type,"ReadSheet",Path.Combine(folder,sheets[p]!["file"]!.ToString()))!;page=p;}
                    var expected=new CroppedBitmap(atlas!,new Int32Rect(local%4*960,local/4*960,960,960));
                    var source=((Image)Field("sprite")!).Source as BitmapSource;
                    Assert(source!=null&&source.PixelWidth==960&&source.PixelHeight==960,"Frame missing or resized");
                    byte[] Pixels(BitmapSource bitmap){var format=new FormatConvertedBitmap(bitmap,PixelFormats.Pbgra32,null,0);byte[] bytes=new byte[960*960*4];format.CopyPixels(bytes,960*4,0);return bytes;}
                    var actual=Pixels(source!);Assert(actual.SequenceEqual(Pixels(expected)),$"Unexpected reference/blend or skipped frame: {c}/{frame}");
                    if(frame is 0 or 63 or 126)hashes.Add(Convert.ToHexString(SHA256.HashData(actual)));
                    visited++;
                }
            }
            Assert(visited==1651&&hashes.Count>13,"Missing or static motions");
            Console.WriteLine($"PASS actual WPF player: {visited} native frames exactly match imported PNG cells; no reference substitution or blending; 13 motions, 24 fps");
        }
        finally{window.Close();app.Shutdown();}
    }
}
