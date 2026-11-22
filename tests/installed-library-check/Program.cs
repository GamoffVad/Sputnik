using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY",Path.GetFullPath(args[0])+Path.DirectorySeparatorChar);
        var packages=typeof(Sputnik.App).Assembly.GetType("Sputnik.PetPackages")!;
        var stage=packages.GetMethod("Stage",BindingFlags.Public|BindingFlags.Static)!;
        var catalog=JsonNode.Parse(File.ReadAllText(Path.Combine(args[1],"catalog.json")))!["pets"]!.AsArray();
        var report=new JsonArray();int errors=0;
        foreach(var pet in catalog.OfType<JsonObject>().Where(p=>args.Length<4||p["id"]!.ToString()==args[3]))
        {
            string id=pet["id"]!.ToString();bool bundled=pet["bundled"]!.GetValue<bool>();
            try
            {
                var result=(JsonObject)stage.Invoke(null,[Path.GetFullPath(Path.Combine(args[1],pet["file"]!.ToString())),
                    Path.GetFullPath(Path.Combine(args[2],"pending")),Path.GetFullPath(Path.Combine(args[2],"library"))])!;
                if(bundled)throw new Exception("Bundled identity unexpectedly accepted for replacement");
                if(result["id"]!.ToString()!=id||result["animations"]!.AsArray().Count!=pet["animations"]!.GetValue<int>())throw new Exception("Animation identity or count changed");
                report.Add(new JsonObject{["id"]=id,["passed"]=true,["animations"]=result["animations"]!.AsArray().Count});
                Console.WriteLine("PASS production Stage: "+id+", "+result["animations"]!.AsArray().Count+" animations");
            }
            catch(Exception ex)
            {
                var error=ex is TargetInvocationException invocation?invocation.InnerException!:ex;
                if(bundled&&error is InvalidDataException&&error.Message.Contains("уже входит"))
                {report.Add(new JsonObject{["id"]=id,["passed"]=true,["bundledIdentityProtected"]=true});Console.WriteLine("PASS built-in identity protection: "+id);}
                else{errors++;report.Add(new JsonObject{["id"]=id,["passed"]=false,["error"]=error.Message});Console.WriteLine("FAIL "+id+": "+error.Message);}
            }
        }
        Directory.CreateDirectory(args[2]);File.WriteAllText(Path.Combine(args[2],"import-report.json"),report.ToJsonString());
        return errors==0?0:1;
    }
}
