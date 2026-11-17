using System.Reflection;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

partial class Check
{
    static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value) yield return value;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Find<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    static void Layout(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }
    static void Render(Window window, string name)
    {
        Layout(window);
        var bitmap = new RenderTargetBitmap((int)(window.ActualWidth * 1.5), (int)(window.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = System.IO.File.Create("artifacts/workspace-check/" + name + ".png");
        encoder.Save(file);
    }
    [STAThread]
    static void Main(string[] args)
    {
        AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", System.IO.Path.GetFullPath("bin/Release/net10.0-windows/"));
        Application app = args.Contains("--sphere-preview-only") ? new TestDesktopApp() : new Application();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Sputnik;component/Components/Controls.xaml") });
        var assembly = typeof(Sputnik.App).Assembly;
        var palette = assembly.GetType("Sputnik.ThemePalette")!;
        var theme = assembly.GetType("Sputnik.PetTheme")!;
        void SetTheme(string surface, string background) => palette.GetMethod("Fill")!.Invoke(null, new object[] { app.Resources, Activator.CreateInstance(theme, "#A8B7EF", surface, background)! });
        SetTheme("#262B35", "#1C2028");
        app.Resources["ThemeFont"] = new FontFamily("Segoe UI");
        var data = System.IO.Path.GetFullPath("artifacts/workspace-check/data-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(data);
        var workspace = JsonNode.Parse("""
        {"favorites":[
          {"id":"one","kind":"site","title":"Google","target":"https://google.com","icon":"globe"},
          {"id":"two","kind":"site","title":"Pinterest · коллекция референсов","target":"https://pinterest.com/collection/very-long-folder-name-and-reference-ideas","icon":"heart"},
          {"id":"three","kind":"app","title":"Блокнот","target":"C:\\Windows\\notepad.exe","icon":"cpu"},
          {"id":"four","kind":"site","title":"GitHub — рабочие проекты","target":"https://github.com/GamoffVad/Sputnik","icon":"code"}
        ],"messages":[]}
        """)!;
        System.IO.File.WriteAllText(System.IO.Path.Combine(data,"workspace.json"),workspace.ToJsonString());
        var window = new Sputnik.MainWindow { Width = 450, Height = 438, Left = 100, Top = 100 };
        if(args.Any(a=>a.StartsWith("--interactive-"))) { window.ShowInTaskbar=true; window.Title="Спутник — проверка оформления"; }
        typeof(Sputnik.MainWindow).GetField("data", Flags)!.SetValue(window, data);
        object Call(string method, params object[] args) => typeof(Sputnik.MainWindow).GetMethod(method, Flags)!.Invoke(window,args)!;
        try
        {
            window.Show();
            if (args.Contains("--sphere-preview-only")) { SpherePreview(window, data, app); return; }
            if (args.Contains("--glass-icons-only")) { GlassIcons(window,data); return; }
            if (args.Contains("--clipping-only") || args.Any(a=>a.StartsWith("--interactive-")))
            {
                window.Width=404; window.Height=424;
                SetTheme("#342820", "#2A2420");
                workspace["petScale"]=130; workspace["sphereScale"]=160;
                workspace["sphereSpread"]=65; workspace["sphereOffset"]=65; workspace["petSmoothing"]=true;
                if(args.Contains("--interactive-glass"))
                {
                    workspace["windowGlass"]=true; workspace["windowTransparency"]=65;
                    var stripes=new System.Windows.Controls.Primitives.UniformGrid { Columns=6 };
                    for(int stripe=0;stripe<30;stripe++) stripes.Children.Add(new Border {Background=stripe%2==0?Brushes.LightSteelBlue:Brushes.MidnightBlue});
                    var backdrop=new Window {Title="QA backdrop",Width=500,Height=500,Left=80,Top=80,WindowStyle=WindowStyle.None,ShowInTaskbar=false,Content=stripes};
                    backdrop.Show(); window.Owner=backdrop; window.Activate();
                }
                System.IO.File.WriteAllText(System.IO.Path.Combine(data,"workspace.json"),workspace.ToJsonString());
                Call("OpenSection","chat"); Layout(window);
                Call("OpenSection","theme"); Layout(window);
                Render(window,"appearance-user-size");
                if(window.AllowsTransparency) throw new Exception("Settings must use an opaque native window");
                foreach(var viewer in Find<ScrollViewer>(window))
                    Console.WriteLine($"Scroll: {viewer.ActualWidth}x{viewer.ActualHeight}; extent={viewer.ExtentHeight}; viewport={viewer.ViewportHeight}; offset={viewer.VerticalOffset}");
                foreach(var button in Find<Button>(window).Where(b=>AutomationProperties.GetName(b) is "Сохранить" or "Сбросить" or "Показать сферы"))
                {
                    Console.WriteLine($"Button {AutomationProperties.GetName(button)}: {button.TransformToAncestor(window).TransformBounds(new Rect(button.RenderSize))}");
                    var bounds=button.TransformToAncestor(window).TransformBounds(new Rect(button.RenderSize));
                    Assert(bounds.Top>0 && bounds.Bottom<window.ActualHeight,"Action button clipped by window");
                }
                if(args.Any(a=>a.StartsWith("--interactive-")))
                {
                    window.IsVisibleChanged += (_,_) => { if(!window.IsVisible) app.Dispatcher.BeginInvoke(new Action(()=>app.Shutdown())); };
                    app.Run();
                }
                return;
            }
            if (args.Contains("--components")) { Components(window, data); return; }
            if (args.Contains("--notes-only"))
            {
                Call("OpenSection", "theme");
                Layout(window);
                ComboBox ColorMode() => Find<ComboBox>(window).Single(x => AutomationProperties.GetName(x) == "Цвет новых заметок");
                Button SaveAppearance() => Find<Button>(window).Single(x => AutomationProperties.GetName(x) == "Сохранить");
                if (ColorMode().SelectedIndex != 0) throw new Exception("Existing behavior must remain default");
                var notesType = assembly.GetType("Sputnik.StickyNotes")!;
                object NewManager() => Activator.CreateInstance(notesType, data, new Action<Window>(w=>{w.Left=100;w.Top=100;}),new Action(()=>{}))!;
                var manager = NewManager();
                string notePath = System.IO.Path.Combine(data, "notes.json");
                JsonArray ReadNotes() => JsonNode.Parse(System.IO.File.ReadAllText(notePath))!.AsArray();
                void CloseNotes()
                {
                    foreach (Window note in app.Windows.Cast<Window>().Where(w=>w.GetType().Name=="StickyNoteWindow").ToArray()) note.Close();
                }
                void CreateNote() => notesType.GetMethod("Create",Flags)!.Invoke(manager,null);
                try
                {
                    CreateNote(); CloseNotes(); CreateNote(); CloseNotes();
                    if (ReadNotes().Any(n=>n!["Color"] is not null)) throw new Exception("Default notes must inherit pet theme");
                    ColorMode().SelectedIndex=1;
                    Render(window,"notes-color-settings");
                    SaveAppearance().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (window.IsVisible) throw new Exception("Preference save did not close window");
                    var originalNotes=ReadNotes().ToJsonString();
                    string previous="";
                    var seen=new HashSet<string>();
                    for (int i=0;i<7;i++)
                    {
                        CreateNote();
                        var color=ReadNotes().Last()!["Color"]!.GetValue<string>();
                        if (color==previous) throw new Exception("Consecutive automatic note colors repeat");
                        seen.Add(color); previous=color;
                        if(i==1) Render(app.Windows.Cast<Window>().Single(w=>w.GetType().Name=="StickyNoteWindow"),"note-auto-pink");
                        CloseNotes();
                    }
                    if (seen.Count!=5) throw new Exception("Automatic palette does not cycle through five colors");
                    if (new JsonArray(ReadNotes().Take(2).Select(n=>n!.DeepClone()).ToArray()).ToJsonString()!=originalNotes)
                        throw new Exception("Existing notes changed when enabling automatic colors");
                    manager=NewManager();
                    CreateNote(); CloseNotes();
                    if(ReadNotes().Last()!["Color"]!.GetValue<string>()==previous) throw new Exception("Restart lost color sequence");
                    var manual=ReadNotes(); manual.Last()!["Color"]="blue";
                    System.IO.File.WriteAllText(notePath,manual.ToJsonString());
                    manager=NewManager(); CreateNote(); CloseNotes();
                    if(ReadNotes().Last()!["Color"]!.GetValue<string>()!="purple") throw new Exception("Previous custom color not respected");
                    window.Show(); Call("OpenSection","theme"); Layout(window);
                    if(ColorMode().SelectedIndex!=1) throw new Exception("Auto color preference not persisted");
                    ColorMode().SelectedIndex=0;
                    SaveAppearance().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    CreateNote(); CloseNotes();
                    if(ReadNotes().Last()!["Color"] is not null) throw new Exception("Switching back did not restore theme color");
                    window.Show(); Call("OpenSection","theme"); Layout(window);
                    var target=((System.Windows.Interop.HwndSource)PresentationSource.FromVisual(window)).CompositionTarget;
                    if(target.RenderMode!=System.Windows.Interop.RenderMode.SoftwareOnly) throw new Exception("Panel rendering mode not applied");
                    for(int i=0;i<12;i++)
                    {
                        window.Width=i%2==0?320:500;
                        var sliders=Find<Slider>(window).ToArray();
                        foreach(var slider in sliders) slider.Value=i%2==0?slider.Minimum:slider.Maximum;
                        Layout(window);
                        if(Find<Slider>(window).Count()!=5 || Find<TextBlock>(window).Count(t=>t.Text.StartsWith("Размер питомца ·"))!=1)
                            throw new Exception("Duplicate appearance controls after resize/update");
                    }
                    window.Width=450;
                    Render(window,"appearance-redraw");
                    Console.WriteLine("PASS: default pet-theme notes, five-color cycle without adjacent duplicates, persisted mode/colors after restart, previous manual color, existing notes unchanged, switch back without restart; software-rendered panel and 12 resize/slider updates.");
                }
                finally { CloseNotes(); }
                return;
            }
            if (args.Contains("--appearance-only"))
            {
                Call("OpenSection", "theme");
                Layout(window);
                Button Action(string label) => Find<Button>(window).Single(x => AutomationProperties.GetName(x) == label);
                Assert(!Find<Button>(window).Any(b=>AutomationProperties.GetName(b)=="Показать сферы"),"Manual sphere preview button remains");
                foreach (string label in new[] { "Сохранить", "Сбросить" })
                {
                    var button = Action(label);
                    if (button.Content is not System.Windows.Shapes.Path || !Equals(button.ToolTip, label))
                        throw new Exception("Appearance action lacks icon/tooltip: " + label);
                }
                var mode = Find<ComboBox>(window).First();
                mode.SelectedIndex = 1;
                Layout(window);
                var background = Find<TextBox>(window).Single(x => AutomationProperties.GetName(x) == "Фон");
                background.Text = "invalid";
                Action("Сохранить").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (!window.IsVisible || !Find<TextBlock>(window).Any(x => x.Text.Contains("#RRGGBB")))
                    throw new Exception("Invalid appearance save must show error and keep window open");
                background.Text = "#181C20";
                var petScale = Find<Slider>(window).Single(x => AutomationProperties.GetName(x) == "Размер питомца");
                petScale.Value = 130;
                Render(window, "appearance-icons");
                Action("Сохранить").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var saved = JsonNode.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(data,"workspace.json")))!;
                if (window.IsVisible || saved["petScale"]!.GetValue<double>() != 130 || saved["appearanceMode"]!.GetValue<string>() != "custom")
                    throw new Exception("Save did not persist settings and close appearance window");
                window.Show();
                Call("OpenSection", "theme");
                Layout(window);
                Action("Сбросить").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Layout(window);
                saved = JsonNode.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(data,"workspace.json")))!;
                if (!window.IsVisible || saved["petScale"] is not null || saved["customTheme"] is not null)
                    throw new Exception("Reset failed");
                Render(window, "appearance-icons-default");
                Console.WriteLine("PASS: two appearance icon actions with tooltips/accessibility names; invalid save stays open with error; valid save persists and closes; reset restores defaults. Isolated test settings.");
                return;
            }
            Call("OpenSection", "chat");
            var history = JsonNode.Parse("""
            [{"role":"user","content":"Помоги спланировать рабочий день."},
             {"role":"assistant","content":"Начните с одной главной задачи. Отведите на неё первый час, затем сделайте короткий перерыв.\n\nВстречи и небольшие дела удобно собрать во второй половине дня."},
             {"role":"user","content":"А если всё время отвлекаюсь?"},
             {"role":"assistant","content":"Попробуйте интервалы по 25 минут. Записывайте отвлекающие мысли в заметку и возвращайтесь к ним во время перерыва."}]
            """)!.AsArray();
            Call("RefreshChatHistory", history, false);
            Render(window,"chat-dark");
            var input = Find<TextBox>(window).Single(x=>AutomationProperties.GetName(x)=="Сообщение модели");
            var submit = Find<Button>(window).Single(x=>AutomationProperties.GetName(x)=="Отправить сообщение (Enter)");
            if (submit.IsEnabled || !input.AcceptsReturn || input.MaxHeight != 96) throw new Exception("Composer defaults failed");
            input.Text = "Первая строка\nВторая строка";
            if (!submit.IsEnabled) throw new Exception("Send not enabled for draft");
            var panel = (StackPanel)typeof(Sputnik.MainWindow).GetField("chatHistory",Flags)!.GetValue(window)!;
            var first = panel.Children[0];
            history.Add(new JsonObject { ["role"]="assistant",["content"]="Новое сообщение" });
            Call("RefreshChatHistory",history,false);
            if (!ReferenceEquals(first,panel.Children[0])) throw new Exception("Existing history was recreated");
            if (Find<TextBox>(panel).Any(x=>!x.IsReadOnly)) throw new Exception("Message can be edited");
            input.Clear();
            window.Width=320;
            Render(window,"chat-narrow");
            history.Add(new JsonObject { ["role"]="assistant",["content"]=new string('W',500)+"\n"+string.Join("\n",Enumerable.Repeat("Длинный ответ для проверки прокрутки.",30)) });
            Call("RefreshChatHistory",history,false);
            Layout(window);
            if (Find<ScrollViewer>(panel).Any(x=>x.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled)) throw new Exception("Horizontal message scrolling enabled");
            window.Width=450;
            SetTheme("#F4F5FA", "#FFFFFF");
            Call("RefreshChatHistory",JsonArray.Parse(history.ToJsonString())!.AsArray(),false);
            Render(window,"chat-light-long");
            Call("OpenSection","favorites");
            Render(window,"favorites-light");
            if (Find<Button>(window).Count(x=>AutomationProperties.GetName(x).StartsWith("Открыть «"))!=4) throw new Exception("Favorite launch rows missing");
            var edit=Find<Button>(window).First(x=>AutomationProperties.GetName(x).StartsWith("Изменить «"));
            edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout(window);
            if (!Find<TextBox>(window).Any(x=>AutomationProperties.GetName(x)=="Название" && x.Text=="Google")) throw new Exception("Favorite edit lost its data");
            Call("OpenSection","favorites");
            SetTheme("#262B35", "#1C2028");
            window.Width=320;
            Render(window,"favorites-narrow-dark");
            window.Width=450;
            Render(window,"favorites-dark");
            Call("OpenSection","model");
            Layout(window);
            var save=Find<Button>(window).Single(x=>Equals(x.Content,"Сохранить"));
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!window.IsVisible || System.IO.File.Exists(System.IO.Path.Combine(data,"model.json"))) throw new Exception("Invalid save closed window");
            var url=Find<TextBox>(window).Single(x=>AutomationProperties.GetName(x)=="Базовый URL");
            url.Text="http://localhost:11434/v1";
            var model=Find<ComboBox>(window).Single(x=>AutomationProperties.GetName(x)=="ID модели");
            model.Text="local-test-model";
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (window.IsVisible || !System.IO.File.Exists(System.IO.Path.Combine(data,"model.json"))) throw new Exception("Valid save did not close window");
            Console.WriteLine("PASS: compact chat, multiline draft, incremental selectable history, long text, favorite actions, valid save closes and invalid save stays open. No network requests.");
            var quickType=assembly.GetType("Sputnik.QuickChatWindow")!;
            var quick=(Window)Activator.CreateInstance(quickType,new Action<Window>(w=>{w.Left=100;w.Top=100;}),new Action(()=>{}),new Func<string,Task<string>>(_=>Task.FromResult("Короткий ответ для проверки.")))!;
            quickType.GetMethod("Compose")!.Invoke(quick,null);
            Render(quick,"quick-compose");
            quickType.GetMethod("ShowAnswer",Flags)!.Invoke(quick,new object[]{"Запишите одну главную задачу и начните с неё. Остальное можно отложить до короткого перерыва."});
            Render(quick,"quick-answer");
            if (!Find<TextBox>(quick).Any(x=>x.IsReadOnly)) throw new Exception("Quick answer cannot be selected");
            if (quick.ResizeMode != ResizeMode.NoResize || Find<Thumb>(quick).Any(t=>AutomationProperties.GetName(t).StartsWith("Размер окна:"))) throw new Exception("Quick chat should not resize");
            quick.Close();
            Console.WriteLine("PASS: quick chat composer and selectable answer; six layouts rendered.");
            var staticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            window.Show();
            foreach (string sectionName in new[] { "chat", "theme", "model", "manage", "sharing", "favorites", "reminders", "pets", "motion" })
            {
                Call("OpenSection", sectionName);
                Layout(window);
                if (window.ResizeMode != ResizeMode.NoResize || Find<Thumb>(window).Any(t=>AutomationProperties.GetName(t).StartsWith("Размер окна:")))
                    throw new Exception("Unexpected resize controls: " + sectionName);
            }
            var controlsType=assembly.GetType("Sputnik.DesktopControls")!;
            var place=controlsType.GetMethod("PlaceBesideMenu",staticFlags)!;
            int placements=0;
            foreach(var area in new[]{new Rect(0,0,1920,1040),new Rect(-1280,-200,1280,984),new Rect(0,40,800,560)})
            foreach(double menuX in new[]{area.Left,area.Left+(area.Width-270)/2,area.Right-270})
            foreach(double menuY in new[]{area.Top,area.Bottom-456})
            foreach(double rowY in new[]{40d,120d,270d,410d})
            foreach(var desired in new[]{new Size(450,438),new Size(760,850),new Size(300,240)})
            {
                var menuRect=new Rect(menuX,menuY,270,456);
                var itemRect=new Rect(menuX+8,menuY+rowY,252,30);
                var result=(Rect)place.Invoke(null,new object[]{menuRect,itemRect,desired,area,new Size(300,240)})!;
                if (!area.Contains(result)) throw new Exception("Panel outside screen: "+result);
                if (Math.Abs(result.Top-Math.Clamp(itemRect.Top,area.Top,area.Bottom-result.Height))>.001) throw new Exception("Panel not aligned with clicked item");
                if (Math.Max(menuRect.Left-area.Left-10,area.Right-menuRect.Right-10)>=300 && result.IntersectsWith(menuRect)) throw new Exception("Panel overlaps menu despite available space");
                placements++;
            }
            var pet = new Window {Width=200,Height=200,Left=SystemParameters.WorkArea.Right-210,Top=SystemParameters.WorkArea.Bottom-210,WindowStyle=WindowStyle.None};
            pet.Show();
            var controls=Activator.CreateInstance(controlsType,pet,new Action<string>(_=>{}),new Action(()=>{}))!;
            try
            {
                controlsType.GetMethod("ShowMenu")!.Invoke(controls,new object[]{"Тестовый питомец",false});
                var menu=(Window)controlsType.GetField("menu",Flags)!.GetValue(controls)!;
                Layout(menu);
                if (menu.ResizeMode != ResizeMode.NoResize || Find<Thumb>(menu).Any()) throw new Exception("Menu should not resize");
                controlsType.GetMethod("PositionPanel")!.Invoke(controls,new object[]{window,"theme"});
                Call("OpenSection","theme");
                Render(window,"appearance-anchored");
                var items=(System.Collections.IDictionary)controlsType.GetField("menuItems",Flags)!.GetValue(controls)!;
                var item=(Button)items["theme"]!;
                var local=item.TransformToAncestor(menu).TransformBounds(new Rect(item.RenderSize));
                var area=(Rect)controlsType.GetMethod("WorkArea",Flags)!.Invoke(controls,new object[]{false})!;
                double expected=Math.Clamp(menu.Top+local.Top,area.Top,area.Bottom-window.Height);
                if (Math.Abs(window.Top-expected)>.1) throw new Exception("Real menu row anchor lost");
                if (new Rect(window.Left,window.Top,window.Width,window.Height).IntersectsWith(new Rect(menu.Left,menu.Top,menu.Width,menu.Height))) throw new Exception("Real panel overlaps menu");
                Console.WriteLine($"PASS: all nine sections, quick chat and menu fixed without resize grips; {placements} menu placements and actual WPF menu anchor.");
            }
            finally { ((IDisposable)controls).Dispose(); pet.Close(); }
        }
        finally { window.Close(); app.Shutdown(); }
    }
}
