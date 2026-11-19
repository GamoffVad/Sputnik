using System.IO;
using System.Text.Json.Nodes;
using System.Windows.Controls;
using System.Windows.Automation;

partial class Check
{
    static void PetDistribution(Sputnik.MainWindow window, string data)
    {
        System.Threading.SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
        var asm = typeof(Sputnik.App).Assembly;
        Type T(string name) => asm.GetType("Sputnik." + name)!;
        string release = Path.GetFullPath("artifacts/separate-pets-1.3.14/release");
        string library = Path.Combine(data,"pets"), pending = Path.Combine(data,"preview");
        var catalog = JsonNode.Parse(File.ReadAllText("wwwroot/assets/pets.json"))!.AsArray();
        Assert(catalog.Count==1 && catalog[0]!["id"]!.ToString()=="klyaksa","Only Klyaksa may be built in");
        foreach (string file in Directory.GetFiles(release,"*.pet"))
        {
            var staged=(JsonObject)Invoke(T("PetPackages"),"Stage",file,pending,library);
            Invoke(T("PetPackages"),"Install",staged["token"]!.ToString(),pending,library);
            Assert(staged["theme"]?["accent"] is not null,"Pet palette missing");
        }
        Assert(((JsonArray)Invoke(T("PetPackages"),"Installed",library)).Count==5,"Five external pets expected");
        Invoke(window,"SelectPet","quiet-ninja",true);
        var state=JsonNode.Parse(File.ReadAllText(Path.Combine(data,"workspace.json")))!;
        Assert(state["appearanceMode"]!.ToString()=="pet" && state["themed"]!.GetValue<bool>(),"Pet palette selection");
        state["appearanceMode"]="custom"; File.WriteAllText(Path.Combine(data,"workspace.json"),state.ToJsonString());
        Invoke(window,"SelectPet","quiet-ninja",true);
        state=JsonNode.Parse(File.ReadAllText(Path.Combine(data,"workspace.json")))!;
        Assert(state["appearanceMode"]!.ToString()=="pet","Explicit pet palette must replace manual mode");

        string ninja=Path.Combine(release,"quiet-ninja.pet");
        Assert((bool)Invoke(T("PetPackageRequests"),"Enqueue",new[]{ninja,"--desktop-only","https://example.test/no.pet"},data),"Package open queue");
        var paths=(string[])Invoke(T("PetPackageRequests"),"Drain",data);
        Assert(paths.Length==1 && paths[0]==ninja && ((string[])Invoke(T("PetPackageRequests"),"Drain",data)).Length==0,"Package queue delivered twice or wrong path");

        // Upgrade fixture: a previously built-in active pet becomes a normal import.
        string migration=Path.Combine(data,"migration"), legacy=Path.Combine(migration,"legacy-bundled-pets");
        Directory.CreateDirectory(legacy);
        var old=JsonNode.Parse(File.ReadAllText("installer/pets/bundled-source.json"))!.AsArray();
        File.WriteAllText(Path.Combine(legacy,"pets.json"),old.ToJsonString());
        File.Copy("wwwroot/assets/hudi.png",Path.Combine(legacy,"hudi.png"));
        const string prefs="{\"active\":\"hudi\",\"petScale\":137,\"favorites\":[{\"title\":\"Keep me\"}]}";
        File.WriteAllText(Path.Combine(migration,"workspace.json"),prefs);
        Invoke(T("LegacyPetMigration"),"Run",migration);
        Assert(File.Exists(Path.Combine(migration,"pets/hudi/pet.json")),"Active legacy pet was not preserved");
        Assert(File.ReadAllText(Path.Combine(migration,"workspace.json"))==prefs,"Migration changed user preferences");
        Invoke(T("LegacyPetMigration"),"Run",migration);
        Assert(Directory.GetDirectories(Path.Combine(migration,"pets")).Length==1,"Migration not idempotent");
        string fresh=Path.Combine(data,"fresh"); Directory.CreateDirectory(fresh);
        Invoke(T("LegacyPetMigration"),"Run",fresh);
        Assert(!Directory.Exists(Path.Combine(fresh,"pets")),"Fresh install imported former built-ins");

        Pump((Task)Invoke(window,"OpenPetPackage",ninja)); Layout(window);
        Assert(Find<Button>(window).Any(b=>b.Content?.ToString()=="Обновить питомца"),"Existing .pet does not open update preview: " + string.Join(" | ",Find<TextBlock>(window).Select(t=>t.Text)));
        Render(window,"quiet-ninja-package-preview");
        Console.WriteLine("PASS: only Klyaksa built in; five independent pet packages with themes; explicit palette switching; file-open queue; active legacy migration, preserved settings, idempotency and clean install; Ninja update preview.");
    }
}
