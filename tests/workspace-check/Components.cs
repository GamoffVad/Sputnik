using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

partial class Check
{
    sealed class TestDesktopApp : Sputnik.App
    {
        // Exercise the real preview bridge without starting desktop services,
        // registering autostart, or contacting the installed singleton.
        protected override void OnStartup(StartupEventArgs e) { }
    }
    static void GlassIcons(Sputnik.MainWindow window,string data)
    {
        void Open(string section) { Invoke(window,"OpenSection",section); Layout(window); }
        Open("theme");
        var glass=Find<CheckBox>(window).Single(c=>AutomationProperties.GetName(c)=="Матовое стекло");
        var amount=Find<Slider>(window).Single(s=>AutomationProperties.GetName(s)=="Прозрачность окон");
        glass.IsChecked=true;
        foreach(double value in new[]{0d,25d,70d})
        {
            amount.Value=value; Layout(window);
            Assert(!window.AllowsTransparency && window.Opacity==1,"Glass restored layered window or faded text");
            var brush=(Brush)window.Resources["ThemeWindowSurface"];
            Assert(Math.Abs(brush.Opacity-(1-value/100))<.001,"Native glass unavailable or wrong tint: "+brush.Opacity);
        }
        amount.Value=35; Layout(window); Render(window,"appearance-glass");
        Find<Button>(window).Single(b=>AutomationProperties.GetName(b)=="Сохранить").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var saved=JsonNode.Parse(File.ReadAllText(Path.Combine(data,"workspace.json")))!;
        Assert(!window.IsVisible && saved["windowGlass"]!.GetValue<bool>() && saved["windowTransparency"]!.GetValue<double>()==35,"Glass save/close");
        window.Show(); Open("theme");
        Assert(Find<CheckBox>(window).Single(c=>AutomationProperties.GetName(c)=="Матовое стекло").IsChecked==true,"Glass reload");
        Find<Button>(window).Single(b=>AutomationProperties.GetName(b)=="Сбросить").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(window);
        Assert(((Brush)window.Resources["ThemeWindowSurface"]).Opacity==1,"Glass reset failed");
        Open("favorites");
        foreach(var button in Find<Button>(window).Where(b=>AutomationProperties.GetName(b).StartsWith("Открыть «")||AutomationProperties.GetName(b).StartsWith("Изменить «")))
        {
            Assert(button.BorderThickness==new Thickness(0) && button.ToolTip==null,"Favorite retains border or native tooltip");
            var icon=(System.Windows.Shapes.Path)button.Content;
            Assert(button.ActualWidth-button.Padding.Left-button.Padding.Right>=icon.Width*1.08,"Icon animation is clipped");
        }
        Find<Button>(window).Single(b=>AutomationProperties.GetName(b)=="Добавить сайт или программу").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(window);
        Assert(Find<Button>(window).Single(b=>AutomationProperties.GetName(b)=="Назад").Content is System.Windows.Shapes.Path,"Back is not a chevron");
        var icons=Find<ComboBox>(window).Single(c=>AutomationProperties.GetName(c)=="Значок");
        Assert(icons.Items.Count>20 && icons.Items.Cast<ComboBoxItem>().Skip(1).All(i=>i.Content is System.Windows.Shapes.Path p && !p.Data.Bounds.IsEmpty),"Icon grid contains text/missing geometry");
        icons.SelectedIndex=1; icons.IsDropDownOpen=true; Layout(window);
        var selected=(ComboBoxItem)icons.SelectedItem;
        var selectedIcon=(System.Windows.Shapes.Path)selected.Content;
        Assert(selected.ActualWidth-selected.Padding.Left-selected.Padding.Right>=selectedIcon.ActualWidth,"Selected picker icon clipped");
        Render(window,"favorite-icon-picker"); icons.IsDropDownOpen=false;
        Console.WriteLine($"PASS: native glass at 0/25/70%, crisp text, save/reopen/reset; favorites borderless without native hints; chevron Back; {icons.Items.Count-1} visual icon choices, selected icon fits.");
    }
    static void SpherePreview(Sputnik.MainWindow window,string data,Application app)
    {
        var asm=window.GetType().Assembly;
        var petType=asm.GetType("Sputnik.DesktopPetWindow")!;
        var controlsType=asm.GetType("Sputnik.DesktopControls")!;
        var pet=(Window)Activator.CreateInstance(petType,new Action<string>(_=>{}),new Action(()=>{}))!;
        try
        {
            Invoke(pet,"SelectFromStore","klyaksa",null!); pet.Left=800; pet.Top=300; pet.Show(); Layout(pet);
            typeof(Sputnik.App).GetField("pet",Flags)!.SetValue(app,pet);
            var controls=petType.GetField("controls",Flags)!.GetValue(pet)!;
            var favoriteType=controlsType.GetNestedType("Favorite",BindingFlags.NonPublic|BindingFlags.Public)!;
            var favorites=Array.CreateInstance(favoriteType,1);
            favorites.SetValue(Activator.CreateInstance(favoriteType,"site","QA","https://example.test","globe","",false),0);
            Invoke(controls,"SetFavorites",new object[]{favorites});
            Invoke(window,"OpenSection","theme"); Layout(window);
            bool Visible()=>(bool)controlsType.GetField("spheresVisible",Flags)!.GetValue(controls)!;
            Assert(!Visible(),"Appearance opening must not show spheres");
            foreach(string name in new[]{"Размер сфер","Между сферами","Отступ от питомца"})
            {
                var slider=Find<Slider>(window).Single(s=>AutomationProperties.GetName(s)==name);
                slider.Focus(); slider.Value=slider.Minimum+20; Layout(window);
                Assert(Visible(),"Changing slider must show spheres: "+name);
                var frame=(Border)slider.Template.FindName("Focus",slider);
                Assert(frame.BorderThickness==new Thickness(0),"Slider has focus border: "+name);
                Invoke(controls,"HideSpheres");
            }
            Find<Slider>(window).Single(s=>AutomationProperties.GetName(s)=="Размер питомца").Value=125; Layout(window);
            Assert(!Visible(),"Pet size alone should not reopen spheres");
            Assert(!Find<Button>(window).Any(b=>AutomationProperties.GetName(b)=="Показать сферы"),"Manual preview button remains");
            Render(window,"appearance-auto-spheres-no-border");
            Console.WriteLine("PASS: sphere size/spread/offset each automatically show hidden spheres; pet size alone does not; slider focus has no border; manual preview button removed.");
        }
        finally
        {
            typeof(Sputnik.App).GetField("pet",Flags)!.SetValue(app,null);
            pet.Close();
        }
    }
    static object Invoke(object target, string method, params object[] args)
    {
        try { return (target is Type t ? t : target.GetType()).GetMethod(method, Flags | BindingFlags.Static)!.Invoke(target is Type ? null : target,args)!; }
        catch(TargetInvocationException ex) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException!).Throw(); throw; }
    }
    static void Assert(bool condition, string name) { if(!condition) throw new Exception(name); }
    static void Reject(Action action, string name)
    {
        try { action(); } catch(Exception ex) when(ex is InvalidOperationException or InvalidDataException or ArgumentException) { return; }
        throw new Exception("Accepted invalid input: " + name);
    }
    static void Pump(Task task)
    {
        var deadline=DateTime.UtcNow.AddSeconds(15);
        while(!task.IsCompleted && DateTime.UtcNow<deadline) { Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle); Thread.Sleep(5); }
        Assert(task.IsCompleted,"Task timed out"); task.GetAwaiter().GetResult();
    }
    static byte[] Pixels(FrameworkElement element)
    {
        element.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)element.ActualWidth,(int)element.ActualHeight,96,96,PixelFormats.Pbgra32);
        bitmap.Render(element); var pixels=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];
        bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0); return pixels;
    }
    sealed class FakeHttp : HttpMessageHandler
    {
        public Func<HttpRequestMessage,Task<HttpResponseMessage>> Reply = _=>Task.FromResult(Response("{}"));
        public List<string> Paths = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        { Paths.Add(request.RequestUri!.AbsolutePath); return Reply(request); }
    }
    static HttpResponseMessage Response(string text, HttpStatusCode status=HttpStatusCode.OK) => new(status) { Content=new StringContent(text,System.Text.Encoding.UTF8,"application/json") };
    static void Components(Sputnik.MainWindow window,string data)
    {
        var asm=window.GetType().Assembly;
        Type T(string name)=>asm.GetType("Sputnik."+name)!;
        object Call(string name,params object[] args)=>Invoke(window,name,args);
        void Open(string name){Call("OpenSection",name); Layout(window);}
        void Click(string name)=>Find<Button>(window).Single(b=>Equals(b.Content,name)||AutomationProperties.GetName(b)==name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        TextBox Input(string name)=>Find<TextBox>(window).Single(b=>AutomationProperties.GetName(b)==name);
        string store=Path.Combine(data,"workspace.json");
        JsonObject State()=>JsonNode.Parse(File.ReadAllText(store))!.AsObject();
        void Write(JsonObject state)=>File.WriteAllText(store,state.ToJsonString());

        // Render page A, then visit every page and return to A. Pixels must be identical.
        Open("theme"); window.Focus();
        var host=(ContentControl)window.FindName("PanelHost");
        var baseline=Pixels(host);
        foreach(string section in new[]{"chat","favorites","model","manage","sharing","reminders","pets","motion"})
        {
            Open(section); var previous=(FrameworkElement)host.Content;
            Render(window,"section-"+section);
            Open("theme"); window.Focus(); Layout(window);
            Assert(!previous.IsLoaded,"Detached page remains loaded: "+section);
            Assert(Pixels(host).SequenceEqual(baseline),"Residual pixels after "+section);
        }
        Render(window,"transition-chat-to-appearance");
        Console.WriteLine("PASS 8 page transitions: previous visual unloaded; appearance pixels identical after every section.");

        // Favorites CRUD through real buttons. Library icon avoids external site requests.
        Open("favorites"); Click("Добавить сайт или программу"); Layout(window);
        Click("Сохранить"); Assert(Find<TextBlock>(window).Any(t=>t.Text=="Укажите название и адрес."),"Favorite empty validation");
        Input("Название").Text="QA favorite"; Input("Адрес или путь").Text="example.test/path?q=тест";
        Find<ComboBox>(window).Last().SelectedIndex=1; Click("Сохранить"); Layout(window);
        Assert(State()["favorites"]!.AsArray().Count==5,"Favorite add");
        Click("Изменить «QA favorite»"); Layout(window); Input("Название").Text="QA renamed"; Click("Сохранить"); Layout(window);
        Click("Изменить «QA renamed»"); Layout(window); Click("Удалить");
        Assert(State()["favorites"]!.AsArray().Count==4,"Favorite delete");
        Reject(()=>Invoke(typeof(Sputnik.MainWindow),"OpenTarget","app","missing.exe"),"Missing executable");
        Reject(()=>Invoke(typeof(Sputnik.MainWindow),"OpenTarget","site","file:///C:/Windows"),"Non-http website");
        Console.WriteLine("PASS favorites: empty form, add, edit, delete, icon selection, invalid executable and URL.");

        // Reminder editor and scheduler: no actual user reminders involved.
        Open("reminders"); Click("Добавить"); Layout(window);
        Input("Название").Text="QA reminder"; Input("Время (ЧЧ:ММ)").Text="12:34";
        Find<ComboBox>(window).Single().SelectedIndex=11; Input("Интервал в минутах").Text="7";
        Click("Сохранить"); Layout(window);
        var reminder=State()["reminders"]![0]!.AsObject();
        Assert(reminder["repeat"]!.ToString()=="custom" && reminder["repeatMinutes"]!.GetValue<int>()==7,"Reminder custom interval");
        Click("Изменить"); Layout(window); Input("Название").Text="QA edited"; Click("Сохранить"); Layout(window);
        var state=State(); state["reminders"]![0]!["due"]=DateTimeOffset.UtcNow.AddMinutes(-20).ToUnixTimeMilliseconds(); Write(state);
        var scheduler=Activator.CreateInstance(T("ReminderScheduler"),Flags,null,new object[]{data,new Action<Window>(w=>{w.Left=200;w.Top=200;})},null)!;
        try
        {
            Invoke(scheduler,"Check");
            var notice=(Window)T("ReminderScheduler").GetField("notice",Flags)!.GetValue(scheduler)!;
            Assert(notice!=null && notice.ResizeMode==ResizeMode.NoResize,"Reminder notification"); Layout(notice);
            Find<Button>(notice).Single(b=>Equals(b.Content,"На 10 мин")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(State()["reminders"]![0]!["due"]!.GetValue<long>()>DateTimeOffset.UtcNow.AddMinutes(9).ToUnixTimeMilliseconds(),"Snooze");
            foreach(string repeat in new[]{"none","5","60","daily","weekly","custom"})
            {
                state=State(); var r=state["reminders"]![0]!; r["repeat"]=repeat; r["done"]=false;
                r["due"]=DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeMilliseconds(); r.AsObject().Remove("scheduled"); Write(state);
                Invoke(T("ReminderScheduler"),"Update",store,reminder["id"]!.ToString(),"done");
                r=State()["reminders"]![0]!;
                Assert(repeat=="none" ? r["done"]!.GetValue<bool>() : r["due"]!.GetValue<long>()>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),"Repeat "+repeat);
            }
        }
        finally { ((IDisposable)scheduler).Dispose(); }
        Open("reminders"); Click("Изменить"); Layout(window); Click("Удалить");
        Assert(State()["reminders"]!.AsArray().Count==0,"Reminder delete");
        Console.WriteLine("PASS reminders: create/edit/delete, custom interval, real notification, snooze, six recurrence variants.");

        // Rich note editing, resize, pin and persistence including image/list/check data.
        var notes=Activator.CreateInstance(T("StickyNotes"),data,new Action<Window>(w=>{w.Left=100;w.Top=100;}),new Action(()=>{}))!;
        Invoke(notes,"Create");
        Window Note()=>Application.Current.Windows.Cast<Window>().Single(w=>w.GetType().Name=="StickyNoteWindow");
        var note=Note(); Layout(note);
        var editor=Find<RichTextBox>(note).Single(); editor.Document.Blocks.Clear();
        var run=new Run("QA formatted text"); var paragraph=new Paragraph(run); editor.Document.Blocks.Add(paragraph);
        editor.Selection.Select(run.ContentStart,run.ContentEnd);
        foreach(var name in new[]{"Полужирный (Ctrl+B)","Курсив (Ctrl+I)","Подчёркивание (Ctrl+U)","Зачёркивание"})
            Find<Button>(note).Single(b=>AutomationProperties.GetName(b)==name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        editor.Selection.Select(editor.Document.ContentStart,editor.Document.ContentStart);
        Find<Button>(note).Single(b=>AutomationProperties.GetName(b)=="Чек-лист").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ((CheckBox)((InlineUIContainer)paragraph.Inlines.FirstInline).Child).IsChecked=true;
        var list=new System.Windows.Documents.List(new ListItem(new Paragraph(new Run("List item")))) { MarkerStyle=TextMarkerStyle.Decimal }; editor.Document.Blocks.Add(list);
        byte[] png=(byte[])Invoke(T("StickyNoteContent"),"ToPng",BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,new byte[16],8));
        paragraph.Inlines.Add((InlineUIContainer)Invoke(T("StickyNoteContent"),"ImageInline",png,null!));
        var grip=Find<Thumb>(note).Single(t=>AutomationProperties.GetName(t)=="Изменить размер заметки");
        double width=note.Width,height=note.Height;
        grip.RaiseEvent(new DragDeltaEventArgs(45,35) { RoutedEvent=Thumb.DragDeltaEvent }); Layout(note);
        Assert(Math.Abs(note.Width-width-45)<1 && Math.Abs(note.Height-height-35)<1,$"Note drag resize: {width}x{height} -> {note.Width}x{note.Height}");
        grip.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(note),0,Key.Right) { RoutedEvent=Keyboard.KeyDownEvent });
        Assert(Math.Abs(note.Width-width-55)<1,"Note keyboard resize");
        Find<Button>(note).Single(b=>AutomationProperties.GetName(b)=="Закрепить поверх окон").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(note.Topmost,"Note pin"); Render(note,"note-formatted-resized"); note.Close();
        string savedNote=File.ReadAllText(Path.Combine(data,"notes.json"));
        Assert(new[]{"\"Bold\":true","\"Italic\":true","\"Underline\":true","\"Strike\":true","\"Checked\":true","\"Numbered\":true","\"Png\":","\"Pinned\":true"}.All(savedNote.Contains),"Rich formatting persistence");
        Invoke(notes,"Show",false); note=Note(); Layout(note);
        Assert(Math.Abs(note.Width-width-55)<1 && Math.Abs(note.Height-height-35)<1 && note.Topmost,"Note size/pin reopen");
        string shareText=(string)Invoke(T("NoteSharing"),"Text",Find<RichTextBox>(note).Single().Document);
        Assert(shareText.Contains("[x]") && shareText.Contains("1. List item") && shareText.Contains("QA formatted text"),"Rich note text export: "+shareText);
        Find<Button>(note).Single(b=>AutomationProperties.GetName(b)=="Действия с заметкой").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(note);
        var menu=Find<Popup>(note).Single(p=>p.IsOpen);
        Find<Button>(menu.Child).Single(b=>AutomationProperties.GetName(b)=="Голубой").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(File.ReadAllText(Path.Combine(data,"notes.json")).Contains("blue"),"Note color");
        Find<Button>(note).Single(b=>AutomationProperties.GetName(b)=="Поделиться заметкой").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(note);
        var sharingPopup=Find<Popup>(note).Single(p=>p.IsOpen);
        Assert(Find<Button>(sharingPopup.Child).Count()>=3,"Note share popup"); sharingPopup.IsOpen=false;
        Find<Button>(note).Single(b=>AutomationProperties.GetName(b)=="Действия с заметкой").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Find<Button>(menu.Child).Single(b=>AutomationProperties.GetName(b)=="Удалить заметку").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(JsonNode.Parse(File.ReadAllText(Path.Combine(data,"notes.json")))!.AsArray().Count==0,"Note deletion");
        Console.WriteLine("PASS notes: four formatting buttons, checklist, numbered list/image serialization, mouse/keyboard resize, pin/color, close/reopen, share popup, deletion.");

        var sharing=Activator.CreateInstance(T("NoteSharing"),data)!;
        var channels=(Array)Invoke(sharing,"Load");
        Assert(channels.Length==3,"Default sharing channels");
        foreach(var channel in channels)
        {
            string uri=(string)Invoke(T("NoteSharing"),"BuildUri",channel,"QA","Привет & # ?\nстрока");
            Assert(uri.Contains("%26") && uri.Contains("%23") && uri.Contains("%0A"),"Share encoding");
        }
        Invoke(sharing,"Save",new object[]{channels}); Assert(((Array)Invoke(sharing,"Load")).Length==3,"Channel persistence");
        var channelType=T("NoteSharing").GetNestedType("Channel",BindingFlags.NonPublic)!;
        foreach(string template in new[]{"javascript:{text}","http://example.test/?text={text}","https://example.test/{text}","https://user:pass@example.test/?t={text}","https://example.test/"})
            Reject(()=>Invoke(T("NoteSharing"),"BuildUri",Activator.CreateInstance(channelType,"QA",template)!,"Title","Text"),template);
        Console.WriteLine("PASS sharing: three channel URI encodings, local persistence, five invalid templates; no messages sent.");
        Open("sharing"); Click("Добавить"); Layout(window);
        Input("Название").Text="QA channel"; Input("Ссылка для отправки").Text="https://example.test/?text={text}"; Click("Сохранить"); Layout(window);
        Assert(((Array)Invoke(sharing,"Load")).Length==4,"Channel UI add");
        Find<Button>(window).Last(b=>Equals(b.Content,"Изменить")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(window);
        Input("Название").Text="QA channel renamed"; Click("Сохранить"); Layout(window);
        Assert(Find<TextBlock>(window).Any(t=>t.Text=="QA channel renamed"),"Channel UI edit");
        Find<Button>(window).Last(b=>Equals(b.Content,"Изменить")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(window); Click("Удалить");
        Assert(((Array)Invoke(sharing,"Load")).Length==3,"Channel UI delete");
        Open("favorites"); Click("Добавить сайт или программу"); Layout(window);
        void Escape()=>window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),0,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent});
        Escape(); Layout(window); Assert(window.IsVisible && Find<TextBlock>(window).Any(t=>t.Text=="Сайты и программы"),"Escape returns from editor");
        Escape(); Assert(!window.IsVisible,"Escape hides panel"); window.Show();
        Console.WriteLine("PASS channel editor add/edit/delete and Escape editor/back/hide navigation.");

        // HTTP fixtures never contact real providers and contain no user secrets.
        var fake=new FakeHttp(); ((HttpClient)typeof(Sputnik.MainWindow).GetField("http",Flags)!.GetValue(window)!).Dispose();
        typeof(Sputnik.MainWindow).GetField("http",Flags)!.SetValue(window,new HttpClient(fake));
        JsonObject Draft(string url="https://api.example.test/v1")=>new(){["url"]=url,["model"]="qa-model",["key"]="synthetic-qa-secret"};
        var draft=Draft(); Call("SaveModel",draft);
        Assert(!File.ReadAllText(Path.Combine(data,"model.json")).Contains("synthetic-qa-secret"),"API key plaintext persisted");
        string Reply(string body,HttpStatusCode status=HttpStatusCode.OK)
        {
            fake.Reply=_=>Task.FromResult(Response(body,status));
            var task=(Task<string>)Call("Send",draft,new JsonArray(new JsonObject{["role"]="user",["content"]="QA"}),false); Pump(task); return task.Result;
        }
        Assert(Reply("{\"choices\":[{\"message\":{\"content\":\"Ответ QA\"}}]}")=="Ответ QA","API success");
        Reject(()=>Reply("denied",HttpStatusCode.Unauthorized),"HTTP 401");
        Reject(()=>Reply("<html>proxy</html>"),"Non-JSON response");
        foreach(string body in new[]{"{}","{\"choices\":[]}","[]","{\"choices\":[{\"message\":{\"content\":123}}]}","{\"choices\":{}}"})
        {
            bool rejected=false;
            try { Reply(body); }
            catch(InvalidOperationException ex) { rejected=ex.Message=="Модель вернула пустой ответ."; }
            Assert(rejected,"Malformed API response must show friendly error: "+body);
        }
        var connection=(ValueTuple<Uri,string>)Invoke(typeof(Sputnik.MainWindow),"ModelConnection",new JsonObject{["url"]="https://api.example.test/v1"},JsonNode.Parse(File.ReadAllText(Path.Combine(data,"model.json")))!);
        Assert(connection.Item2=="synthetic-qa-secret","Saved key reuse");
        connection=(ValueTuple<Uri,string>)Invoke(typeof(Sputnik.MainWindow),"ModelConnection",new JsonObject{["url"]="https://different.example.test/v1"},JsonNode.Parse(File.ReadAllText(Path.Combine(data,"model.json")))!);
        Assert(string.IsNullOrEmpty(connection.Item2),"Key leaked to changed provider");
        connection=(ValueTuple<Uri,string>)Invoke(typeof(Sputnik.MainWindow),"ModelConnection",new JsonObject{["url"]="https://api.example.test/v1",["clearKey"]=true},JsonNode.Parse(File.ReadAllText(Path.Combine(data,"model.json")))!);
        Assert(string.IsNullOrEmpty(connection.Item2),"Clear saved key");
        fake.Reply=req=>Task.FromResult(req.RequestUri!.AbsolutePath=="/models" ? Response("{}",HttpStatusCode.NotFound) : Response("{\"data\":[{\"id\":\"a\"},{\"id\":\"a\"},{\"id\":\"b\"},{\"id\":4}]}"));
        var catalog=(Task)Call("ListModels",Draft("http://localhost")); Pump(catalog);
        var catalogValue=catalog.GetType().GetProperty("Result")!.GetValue(catalog)!;
        Assert(((string[])catalogValue.GetType().GetProperty("Models")!.GetValue(catalogValue)!).SequenceEqual(new[]{"a","b"}),"Model catalog dedup/filter");
        Assert(fake.Paths.Contains("/models") && fake.Paths.Contains("/v1/models"),"Model /v1 fallback");
        Open("model");
        var provider=Find<ComboBox>(window).First(); var apiKey=Find<PasswordBox>(window).Single();
        for(int i=0;i<provider.Items.Count-1;i++)
        {
            apiKey.Password="transient-qa-key"; provider.SelectedIndex=i;
            Assert(Uri.TryCreate(Input("Базовый URL").Text,UriKind.Absolute,out _),"Provider URL "+i);
            Assert(apiKey.Password.Length==0,"Provider change failed to clear typed key "+i);
        }
        Input("Базовый URL").Text="https://api.example.test/v1";
        var modelBox=Find<ComboBox>(window).Single(c=>AutomationProperties.GetName(c)=="ID модели"); modelBox.Text="qa-model";
        fake.Reply=_=>Task.FromResult(Response("{\"choices\":[{\"message\":{\"content\":\"OK\"}}]}"));
        Click("Проверить подключение"); Layout(window);
        Assert(Find<TextBlock>(window).Any(t=>t.Text=="Подключение работает"),"Connection test button");
        fake.Reply=_=>Task.FromResult(Response("{\"data\":[{\"id\":\"qa-a\"},{\"id\":\"qa-b\"}]}"));
        modelBox.IsDropDownOpen=true; Layout(window);
        Assert(modelBox.Items.Count==2,"Model dropdown loading"); modelBox.IsDropDownOpen=false;
        Console.WriteLine("PASS provider presets, typed-key clearing, connection-test button and model dropdown with mocked HTTP.");
        // Complete a pending chat request after navigating to Appearance.
        var completion=new TaskCompletionSource<HttpResponseMessage>(); fake.Reply=_=>completion.Task;
        Open("chat"); var send=(Task<string>)Call("SendChat","QA delayed question"); Open("theme"); window.Focus(); Layout(window); baseline=Pixels(host);
        completion.SetResult(Response("{\"choices\":[{\"message\":{\"content\":\"QA delayed answer\"}}]}")); Pump(send); Layout(window);
        Assert(Pixels(host).SequenceEqual(baseline),"Late chat response painted over appearance");
        Assert(State()["messages"]!.AsArray().Last()!["content"]!.ToString()=="QA delayed answer","Late response not saved");
        Console.WriteLine("PASS API: encrypted key, success/401/invalid JSON/malformed schemas, model catalog fallback/dedup, delayed chat response isolated from Appearance.");

        var petType=T("PetPackages"); string pending=Path.Combine(data,"preview"), library=Path.Combine(data,"pets"); Directory.CreateDirectory(library);
        var manifest=JsonNode.Parse("""{"schemaVersion":1,"id":"qa-pet","name":"QA pet","theme":{"name":"QA","accent":"#FFFFFF","surface":"#222222","background":"#111111"},"animations":[{"name":"Idle","file":"sheet.png","frameWidth":2,"frameHeight":2,"columns":1,"frameCount":1,"fps":1}]}""")!;
        string Package(JsonNode m)
        {
            string path=Path.Combine(data,Guid.NewGuid()+".zip"); using var zip=ZipFile.Open(path,ZipArchiveMode.Create);
            using(var writer=new StreamWriter(zip.CreateEntry("manifest.json").Open())) writer.Write(m.ToJsonString());
            using(var file=zip.CreateEntry("sheet.png").Open()) file.Write(png); return path;
        }
        var preview=(JsonObject)Invoke(petType,"Stage",Package(manifest),pending,library);
        Invoke(petType,"Install",preview["token"]!.ToString(),pending,library);
        Assert(((JsonArray)Invoke(petType,"Installed",library)).Count==1,"Pet install");
        Reject(()=>Invoke(petType,"Stage",Package(manifest),pending,library),"Duplicate pet");
        manifest["id"]="qa-other"; manifest["animations"]![0]!["file"]="../sheet.png";
        Reject(()=>Invoke(petType,"Stage",Package(manifest),pending,library),"Pet path traversal");
        Reject(()=>Invoke(petType,"Install","../escape",pending,library),"Invalid stage token");
        Console.WriteLine("PASS pet packages: PNG/manifest staging, install/list, duplicate ID, path traversal and token validation.");

        // Real loopback HTTP tests icon discovery and disk cache, without external sites.
        var listener=new System.Net.Sockets.TcpListener(IPAddress.Loopback,0); listener.Start();
        int port=((IPEndPoint)listener.LocalEndpoint).Port, requests=0;
        var server=Task.Run(async ()=>
        {
            try
            {
                while(true)
                {
                    using var client=await listener.AcceptTcpClientAsync(); using var stream=client.GetStream();
                    using var reader=new StreamReader(stream,leaveOpen:true);
                    string first=await reader.ReadLineAsync() ?? "";
                    while(!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
                    Interlocked.Increment(ref requests);
                    bool icon=first.Contains("/icon.png");
                    byte[] body=icon ? png : System.Text.Encoding.UTF8.GetBytes("<html><head><link rel='icon' href='/icon.png'></head></html>");
                    byte[] header=System.Text.Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: {(icon?"image/png":"text/html")}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(header); await stream.WriteAsync(body);
                }
            }
            catch(Exception ex) when(ex is System.Net.Sockets.SocketException or ObjectDisposedException) { }
        });
        string folder=Path.Combine(data,"site-icons"); var icons=Activator.CreateInstance(T("SiteIcons"),folder)!;
        try
        {
            string url=$"http://127.0.0.1:{port}/";
            var request=(Task<string>)Invoke(icons,"Find",url); Pump(request);
            Assert(request.Result!=null && File.Exists(Path.Combine(folder,request.Result)),"Site icon cache");
            int count=requests; request=(Task<string>)Invoke(icons,"Find",url); Pump(request);
            Assert(requests==count,"Icon cache hit makes network call");
            string mono=(string)Invoke(T("SiteIconMask"),"Ensure",folder,request.Result);
            Assert(File.Exists(Path.Combine(folder,mono)),"Monochrome icon generation");
            request=(Task<string>)Invoke(icons,"Find","file:///C:/Windows"); Pump(request); Assert(request.Result==null,"Nonweb icon rejected");
        }
        finally { listener.Stop(); Pump(server); }
        Console.WriteLine("PASS site icons: loopback HTML/icon download, PNG disk cache/cache hit, monochrome generation, nonweb rejection.");

        var hotkeys=Activator.CreateInstance(T("DesktopHotkeys"),new Action<string>(_=>{}))!;
        try
        {
            Invoke(hotkeys,"Attach",new System.Windows.Interop.WindowInteropHelper(window).Handle);
            Invoke(hotkeys,"Set","chat",7u,0x87u); // Ctrl+Alt+Shift+F24; removed in finally.
            Reject(()=>Invoke(hotkeys,"Set","theme",7u,0x87u),"Duplicate shortcut");
            Reject(()=>Invoke(hotkeys,"Set","theme",0u,0x41u),"Shortcut without modifier");
            Reject(()=>Invoke(hotkeys,"Set","theme",2u,0x7Bu),"Reserved F12");
            Invoke(hotkeys,"Set","chat",0u,0u);
        }
        finally { ((IDisposable)hotkeys).Dispose(); }
        Console.WriteLine("PASS hotkeys: Windows registration/unregistration, duplicate and invalid shortcut rejection; user bindings unchanged.");

        TaskCompletionSource<string> answer=new();
        var quick=(Window)Activator.CreateInstance(T("QuickChatWindow"),new Action<Window>(w=>{w.Left=100;w.Top=100;}),new Action(()=>{}),new Func<string,Task<string>>(_=>answer.Task))!;
        try
        {
            Invoke(quick,"Compose"); Layout(quick); Find<TextBox>(quick).Single().Text="QA";
            var submit=(Task)Invoke(quick,"Submit"); Layout(quick);
            Assert(Find<StackPanel>(quick).Any(p=>AutomationProperties.GetName(p)=="Ожидание ответа"),"Quick waiting state");
            Invoke(quick,"Dismiss"); answer.SetResult("QA answer"); Pump(submit); Assert(!quick.IsVisible,"Dismissed quick chat reopened on late reply");
            answer=new(); Invoke(quick,"Compose"); Layout(quick); Find<TextBox>(quick).Single().Text="QA error";
            submit=(Task)Invoke(quick,"Submit"); answer.SetException(new HttpRequestException("QA offline")); Pump(submit); Layout(quick);
            Assert(Find<TextBox>(quick).Any(t=>t.IsReadOnly && t.Text=="QA offline"),"Quick error response");
        }
        finally { quick.Close(); }
        Console.WriteLine("PASS quick chat: pending animation state, dismiss during send, late reply stays hidden, offline error shown.");
    }
}


