using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Threading;

class Check
{
    [STAThread]
    static void Main()
    {
        System.IO.Directory.CreateDirectory("artifacts/fixed-area-check");
        AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", System.IO.Path.GetFullPath("bin/Release/net10.0-windows/"));
        var controls = typeof(Sputnik.App).Assembly.GetType("Sputnik.DesktopControls")!;
        var arrange = controls.GetMethod("Arrange", BindingFlags.Static | BindingFlags.NonPublic)!;
        int layouts = 0;
        foreach (var area in new[] { new Rect(0, 0, 1920, 1040), new Rect(-1280, -200, 1280, 984), new Rect(0, 40, 800, 560), new Rect(0, 0, 100, 100) })
        foreach (double orbSize in new[] { 31.2, 52, 83.2 })
        foreach (double spread in new[] { 0d, 1d })
        foreach (double offset in new[] { -400d, -100d, 0d, 10d, 80d })
        foreach (int count in new[] { 0, 1, 5, 9 })
        foreach (var origin in new[] { area.TopLeft, area.BottomRight, new Point(area.Left - 100, area.Top + 100), new Point(area.Right - 50, area.Top - 80), new Point(area.Left + area.Width / 2, area.Top + area.Height / 2) })
        {
            var points = (Point[])arrange.Invoke(null, new object[] { new Rect(origin, new Size(230, 230)), area, count, orbSize, spread, offset, 115d })!;
            double gap = 5 + spread * orbSize / 2;
            double marginX = Math.Min(gap, Math.Max(0, (area.Width - orbSize) / 2));
            double marginY = Math.Min(gap, Math.Max(0, (area.Height - orbSize) / 2));
            if (points.Length != count) throw new Exception("Incorrect sphere count");
            foreach (var point in points)
            {
                if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) ||
                    point.X < area.Left + marginX - .001 || point.Y < area.Top + marginY - .001 ||
                    point.X + orbSize > area.Right - marginX + .001 || point.Y + orbSize > area.Bottom - marginY + .001)
                    throw new Exception($"Sphere exceeds screen margin: {area}, {point}, {orbSize}");
            }
            if (points.Length >= 3)
            {
                var arcCenter = origin + new Vector(115, 115);
                double radius = (points[0] + new Vector(orbSize / 2, orbSize / 2) - arcCenter).Length;
                foreach (var point in points)
                    if (Math.Abs((point + new Vector(orbSize / 2, orbSize / 2) - arcCenter).Length - radius) > .001)
                        throw new Exception("The arc center is not the pet center");
            }
            layouts++;
        }
        Console.WriteLine($"PASS: all spheres inside screen margins in {layouts} layouts, including offscreen pets.");
        foreach (int count in new[] { 1, 5, 9 })
        foreach (double spread in new[] { 0d, 1d })
        {
            double previous = double.PositiveInfinity;
            foreach (double offset in new[] { 80d, 10d, 0d, -50d, -100d, -141d, -400d })
            {
                var points = (Point[])arrange.Invoke(null, new object[] { new Rect(500, 400, 230, 230), new Rect(0, 0, 2000, 1600), count, 52d, spread, offset, 115d })!;
                foreach (var point in points)
                {
                    double distance = (point + new Vector(26, 26) - new Point(615, 515)).Length;
                    double spacingRadius = count <= 1 ? 0 : (52 + 5 + spread * 26) / (2 * Math.Sin(Math.PI / (count - 1) / 2));
                    double expected = Math.Max(spacingRadius, Math.Max(0, 141 + offset));
                    if (Math.Abs(distance - expected) > .001 || distance > previous + .001)
                        throw new Exception("Spheres cannot approach the character center");
                }
                previous = count <= 1 ? Math.Max(0, 141 + offset) : Math.Max((52 + 5 + spread * 26) / (2 * Math.Sin(Math.PI / (count - 1) / 2)), Math.Max(0, 141 + offset));
            }
        }
        Console.WriteLine("PASS: spheres move continuously inside pet bounds and reach its center.");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Sputnik;component/Components/Controls.xaml") });
        var type = typeof(Sputnik.App).Assembly.GetType("Sputnik.DesktopPetWindow")!;
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var window = (Window)Activator.CreateInstance(type, new Action<string>(_ => { }), new Action(() => { }))!;
        object Call(string name, params object[] args) => type.GetMethod(name, flags)!.Invoke(window, args)!;
        // Read fixture archives without changing the user's installed pet library.
        var fixtureRoot = System.IO.Path.GetFullPath("artifacts/fixed-area-check/library-" + Guid.NewGuid().ToString("N"));
        var packages = typeof(Sputnik.App).Assembly.GetType("Sputnik.PetPackages")!;
        void SelectPet(string petId)
        {
            if (petId == "klyaksa") { Call("SelectFromStore", petId, null); return; }
            string library = System.IO.Path.Combine(fixtureRoot, "pets");
            string pending = System.IO.Path.Combine(fixtureRoot, "pending");
            string folder = System.IO.Path.Combine(library, petId);
            if (!System.IO.Directory.Exists(folder))
            {
                var preview = (JsonObject)packages.GetMethod("Stage")!.Invoke(null, new object[] {
                    System.IO.Path.GetFullPath($"PET/{petId}/exports/{petId}.pet"), pending, library })!;
                packages.GetMethod("Install")!.Invoke(null, new object[] { preview["token"]!.ToString(), pending, library });
            }
            var metadata = System.IO.File.ReadAllText(System.IO.Path.Combine(folder, "pet.json"));
            var pet = JsonSerializer.Deserialize(metadata, type.GetNestedType("Pet", BindingFlags.NonPublic)!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            Call("UsePet", pet, folder, null);
        }
        try
        {
            Call("UpdateFromStore");
            SelectPet("knight-watchman");
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var timer = (DispatcherTimer)type.GetField("timer", flags)!.GetValue(window)!;
            timer.Stop();
            Point Support()
            {
                if (type.GetField("supportPosition", flags)!.GetValue(window) is Point target) return target;
                var anchor = (Point)type.GetField("supportAnchor", flags)!.GetValue(window)!;
                var origin = (Point?)type.GetField("restingOrigin", flags)!.GetValue(window) ?? new Point(window.Left, window.Top);
                return new Point(origin.X + window.Width * anchor.X, origin.Y + window.Height * anchor.Y);
            }
            Point PhysicalSupport()
            {
                var anchor = (Point)type.GetField("supportAnchor", flags)!.GetValue(window)!;
                return window.PointToScreen(new Point(window.Width * anchor.X, window.Height * anchor.Y));
            }
            var baseline = Support();
            var physicalBaseline = PhysicalSupport();
            foreach (string petId in new[] { "cloth-ninja", "klyaksa", "hudi", "kudryashka", "sand-watchman-clean", "quiet-ninja", "knight-watchman" })
            {
                SelectPet(petId);
                var selectedPet = type.GetField("active", flags)!.GetValue(window)!;
                if (!Equals(selectedPet.GetType().GetProperty("Id")!.GetValue(selectedPet), petId))
                    throw new Exception($"Fixture pet {petId} fell back to a different pet.");
                foreach (double scale in new[] { 100d, 160d, 80d, 100d })
                {
                    Call("ApplyAppearanceSizes", scale, 100d, 100d, 10d, false);
                    if ((Support() - baseline).Length > .01)
                        throw new Exception($"Pet support moved when switching/resizing {petId}: {Support()} versus {baseline}");
                    var physicalDelta = PhysicalSupport() - physicalBaseline;
                    if (Math.Max(Math.Abs(physicalDelta.X), Math.Abs(physicalDelta.Y)) > 1)
                        throw new Exception($"Pet support moved beyond one native window-positioning pixel: {physicalDelta}");
                }
                var stable = Support();
                Call("ShowFrame", 0);
                if ((Support() - stable).Length > .01) throw new Exception("Support follows animated frame bounds");
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            }
            Console.WriteLine("PASS: seven pet switches and 28 size changes preserve the same visible support point despite different transparent padding and display scales.");
            var originalOrigin = window.PointToScreen(new Point());
            var grabbedPoint = new Point(window.Width / 2, window.Height / 2);
            var originalPointer = window.PointToScreen(grabbedPoint);
            type.GetField("dragging", flags)!.SetValue(window, true);
            foreach (var dragPointer in new[] { new Point(150, 10), new Point(-50, -20), new Point(600, 100) })
            {
                if (!(bool)Call("MoveDragTo", dragPointer, grabbedPoint)) throw new Exception("Native drag failed");
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                if ((window.PointToScreen(grabbedPoint) - dragPointer).Length > 2)
                    throw new Exception("Grabbed point detached from dragPointer");
                if (dragPointer.Y <= 10 && window.PointToScreen(new Point()).Y >= 0)
                    throw new Exception("Transparent top edge still prevents upward movement");
            }
            Call("MoveDragTo", originalPointer, grabbedPoint);
            type.GetField("dragging", flags)!.SetValue(window, false);
            Console.WriteLine("PASS: native drag moves transparent window above screen top while keeping the grabbed point at the pointer.");

            var active = type.GetField("active", flags)!.GetValue(window)!;
            var id = active.GetType().GetProperty("Id")!.GetValue(active);
            if (!Equals(id, "knight-watchman")) throw new Exception("Unexpected active pet");
            var clips = (Array)active.GetType().GetProperty("Animations")!.GetValue(active)!;
            var area = SystemParameters.WorkArea;
            type.GetField("placementArea", flags)!.SetValue(window, area);
            type.GetField("restingOrigin", flags)!.SetValue(window, new Point(area.Left - 30, area.Top - 30));
            foreach (var position in new[] {
                new Point(area.Left - 30, area.Top - 30),
                new Point(area.Right - 20, area.Bottom - 20),
                new Point(area.Left - 200, area.Top + 100),
                new Point(area.Left + 300, area.Top + 150) })
            {
                type.GetField("restingOrigin", flags)!.SetValue(window, position);
                type.GetField("supportPosition", flags)!.SetValue(window, null);
                Call("PositionAtRest");
                Call("ApplyPetSize");
                if ((new Point(window.Left, window.Top) - position).Length > .01)
                    throw new Exception($"Drop position was constrained: {position}");
            }
            var pointer = System.Runtime.InteropServices.Marshal.AllocHGlobal(16);
            try
            {
                int[] rectangle = { -100, -80, 130, 150 };
                System.Runtime.InteropServices.Marshal.Copy(rectangle, 0, pointer, 4);
                object[] arguments = { IntPtr.Zero, 0x0216, IntPtr.Zero, pointer, false };
                type.GetMethod("HandleScreenChange", flags)!.Invoke(window, arguments);
                int[] result = new int[4];
                System.Runtime.InteropServices.Marshal.Copy(pointer, result, 0, 4);
                if ((bool)arguments[4] || !result.SequenceEqual(rectangle))
                    throw new Exception("Native dragging was constrained");
            }
            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(pointer); }
            var expected = new Rect(window.Left, window.Top, window.Width, window.Height);
            int checkedFrames = 0;
            for (int clip = 0; clip < clips.Length; clip++)
            {
                Call("SetPlayback", "clip", clip);
                var animation = clips.GetValue(clip)!;
                int count = (int)animation.GetType().GetProperty("FrameCount")!.GetValue(animation)!;
                foreach (int frame in new[] { 0, count / 2, count - 1 })
                {
                    Call("ShowFrame", frame);
                    var content = (Rect)Call("VisibleContent");
                    var actual = new Rect(window.Left, window.Top, window.Width, window.Height);
                    if (content != new Rect(0, 0, 1, 1) || actual != expected)
                        throw new Exception($"Area changed for clip {clip}, frame {frame}: {actual}, {content}");
                    var visibleBounds = (Rect)Call("VisibleBounds");
                    double silhouette = Math.Sqrt(Math.Pow(visibleBounds.Width * window.Width, 2) + Math.Pow(visibleBounds.Height * window.Height, 2)) / 2;
                    if (!double.IsFinite(silhouette) || silhouette <= 0 || silhouette > Math.Sqrt(window.Width * window.Width + window.Height * window.Height) / 2 + 1)
                        throw new Exception("Invalid visible silhouette radius");
                    var petFrame = new Rect(500, 400, window.Width, window.Height);
                    var orbit = (Point[])arrange.Invoke(null, new object[] { petFrame, new Rect(0, 0, 4000, 3000), 5, 52d, 1d, 10d, silhouette })!;
                    foreach (var point in orbit)
                    {
                        var center = new Point(petFrame.Left + petFrame.Width / 2, petFrame.Top + petFrame.Height / 2);
                        double visualGap = (point + new Vector(26, 26) - center).Length - 26 - silhouette;
                        if (visualGap < 10 - .001) throw new Exception("Visual pet clearance was lost");
                    }
                    checkedFrames++;
                }
            }
            Console.WriteLine($"PASS: {id}, {clips.Length} animations, {checkedFrames} frames, fixed {expected.Width} x {expected.Height} area and 10-pixel visual silhouette clearance; free placement beyond edges and unconstrained native drag.");
            Call("ApplyAppearanceSizes", 100d, 100d, 100d, -100d, true);
            var petControls = type.GetField("controls", flags)!.GetValue(window)!;
            if ((double)controls.GetField("sphereOffset", flags)!.GetValue(petControls)! != -100)
                throw new Exception("Appearance settings clamped the negative offset");
            var favoriteType = controls.GetNestedType("Favorite", BindingFlags.Public | BindingFlags.NonPublic)!;
            var favorites = Array.CreateInstance(favoriteType, 1);
            favorites.SetValue(Activator.CreateInstance(favoriteType, "site", "Test", "https://example.com", "globe", "", false), 0);
            controls.GetMethod("SetFavorites", flags)!.Invoke(petControls, new object[] { favorites });
            controls.GetMethod("ShowSpheres", flags)!.Invoke(petControls, null);
            var spheres = (Window[])controls.GetField("spheres", flags)!.GetValue(petControls)!;
            if (spheres.Length != 1 || !spheres[0].IsVisible || spheres[0].Owner != window || !spheres[0].Topmost)
                throw new Exception("Sphere ownership does not guarantee z order above the pet");
            Console.WriteLine("PASS: negative offset through appearance settings; sphere shown above its owner.");
            var fixedPosition = new Point(spheres[0].Left, spheres[0].Top);
            for (int clip = 0; clip < clips.Length; clip++)
            {
                Call("SetPlayback", "clip", clip);
                Call("ShowFrame", 20);
                if ((new Point(spheres[0].Left, spheres[0].Top) - fixedPosition).Length > .001)
                    throw new Exception("Spheres followed animation frames");
            }
            window.Left += 100;
            window.Top += 80;
            controls.GetMethod("FollowPet", flags)!.Invoke(petControls, null);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            if ((new Point(spheres[0].Left, spheres[0].Top) - fixedPosition).Length > .001)
                throw new Exception("Spheres followed pet movement");
            controls.GetMethod("ShowSpheres", flags)!.Invoke(petControls, null);
            if ((new Point(spheres[0].Left, spheres[0].Top) - fixedPosition).Length > .001)
                throw new Exception("Visible spheres moved on repeated click");
            controls.GetMethod("HideSpheres", flags)!.Invoke(petControls, null);
            controls.GetMethod("ShowSpheres", flags)!.Invoke(petControls, null);
            if ((new Point(spheres[0].Left, spheres[0].Top) - fixedPosition).Length < 1)
                throw new Exception("Reopened spheres did not use the new pet position");
            Console.WriteLine("PASS: fixed sphere positions through all animations and pet movement; reanchor only after reopening.");
            controls.GetMethod("HideSpheres", flags)!.Invoke(petControls, null);
            if ((bool)controls.GetField("spheresVisible", flags)!.GetValue(petControls)!)
                throw new Exception("Drag-start hide does not dismiss spheres");
            Call("SelectFromStore", "klyaksa", null);
            Call("ShowFrame", 0);
            var petImage = (System.Windows.Controls.Image)type.GetField("sprite", flags)!.GetValue(window)!;
            var visible = (Rect)Call("VisibleBounds");
            var drawing = new System.Windows.Media.DrawingVisual();
            using (var dc = drawing.RenderOpen())
            {
                dc.DrawRectangle(System.Windows.Media.Brushes.White, null, new Rect(0, 0, 1600, 1200));
                var origins = new[] { new Point(210, 150), new Point(420, 150), new Point(420, 220), new Point(-90, 220) };
                for (int sample = 0; sample < origins.Length; sample++)
                {
                    dc.PushTransform(new System.Windows.Media.TranslateTransform(sample % 2 * 800, sample / 2 * 600));
                    dc.PushClip(new System.Windows.Media.RectangleGeometry(new Rect(0, 0, 800, 600)));
                    dc.DrawRectangle(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(36, 42, 46)), null, new Rect(0, 0, 800, 600));
                    var frame = new Rect(origins[sample], new Size(368, 368));
                    dc.DrawImage(petImage.Source, frame);
                    var body = new Rect(frame.Left + visible.Left * frame.Width, frame.Top + visible.Top * frame.Height, visible.Width * frame.Width, visible.Height * frame.Height);
                    var positions = (Point[])arrange.Invoke(null, new object[] { body, new Rect(0, 0, 800, 600), 3, 83.2, .9, 10d, null })!;
                    foreach (var position in positions)
                        dc.DrawEllipse(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(62, 70, 109)), new System.Windows.Media.Pen(System.Windows.Media.Brushes.LightSteelBlue, 1), position + new Vector(41.6, 41.6), 41.6, 41.6);
                    dc.Pop(); dc.Pop();
                }
            }
            var orbitBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1600, 1200, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            orbitBitmap.Render(drawing);
            var orbitEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            orbitEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(orbitBitmap));
            using (var file = System.IO.File.Create("artifacts/fixed-area-check/semicircle-layouts.png")) orbitEncoder.Save(file);


            var workspace = new Sputnik.MainWindow();
            var panel = new System.Windows.Controls.StackPanel();
            var title = new System.Windows.Controls.TextBlock { Text = "Движения", FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
            title.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "ThemeInk");
            panel.Children.Add(title);
            typeof(Sputnik.MainWindow).GetMethod("PopulateMotions", flags)!.Invoke(workspace,
                new object[] { panel, "knight-watchman", new[] { ("Шорох на посту", true), ("Прыжок с балансом", true) } });
            var buttons = ((System.Windows.Controls.WrapPanel)panel.Children[1]).Children.Cast<System.Windows.Controls.Button>().ToArray();
            foreach (var button in buttons)
                if (button.Content is not System.Windows.Shapes.Path || string.IsNullOrEmpty(System.Windows.Automation.AutomationProperties.GetName(button)) || button.ToolTip is null)
                    throw new Exception("Motion icon accessibility missing");
            var preview = new System.Windows.Controls.Border { Child = panel, Padding = new Thickness(16), Width = 400, Height = 200 };
            preview.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "ThemeSurface");
            preview.Measure(new Size(400, 200));
            preview.Arrange(new Rect(0, 0, 400, 200));
            preview.UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(800, 400, 192, 192, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(preview);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using (var output = System.IO.File.Create("artifacts/fixed-area-check/motion-icons.png")) encoder.Save(output);
            workspace.Close();
            Console.WriteLine("PASS: motion icons, tooltips, accessibility names; rendered preview.");
        }
        finally { window.Close(); app.Shutdown(); }
    }
}



