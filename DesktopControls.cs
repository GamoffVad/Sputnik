using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Automation;

namespace Sputnik;

internal sealed class DesktopControls : IDisposable
{
    internal const double DefaultOrbSize = 52;
    internal const double DefaultSphereOffset = 10;
    internal const double MinimumSphereOffset = -400;
    internal const double MaximumSphereOffset = 80;
    private const double Gap = 10;
    private const double MinimumSphereGap = 5;
    private const int MaxSpheresPerArc = 9;
    private static readonly (string Title, string Action, string Icon, string Hint)[] Entries =
    [
        ("Быстрый чат", "quick", "chat", "3 клика"),
        ("Открыть чат", "chat", "chat", ""),
        ("Заметки", "notes", "note", "Ctrl + 2 клика"),
        ("Сайты и программы", "favorites", "link", ""),
        ("Напоминания", "reminders", "bell", ""),
        ("Инструменты", "tools", "settings", ""),
        ("Движения", "motion", "play", "›"),
        ("Питомцы", "pets", "pet", ""),
        ("Внешний вид", "theme", "palette", "›"),
        ("Модель и API", "model", "settings", ""),
        ("Управление", "manage", "settings", ""),
        ("Каналы отправки", "sharing", "link", ""),
        ("Скрыть в трей", "tray", "tray", ""),
        ("Закрыть питомца", "exit", "close", ""),
    ];
    internal static IEnumerable<(string Title, string Action)> Sections =>
        Entries.Where(entry => entry.Action != "exit").Select(entry => (entry.Title, entry.Action)).Concat(new[] {
            ("Все заметки", "note-list"), ("Памятные даты", "dates"), ("Календарь", "calendar"), ("Не беспокоить", "notifications"),
            ("Быстрые команды", "commands"), ("Фокус", "focus"), ("Профили", "profiles"), ("Синхронизация", "sync"), ("Модули", "modules") });
    internal sealed record Favorite(string Kind, string Title, string Target, string Icon, string SiteIcon = "", bool Monochrome = false)
    {
        internal string IconSource { get; init; } = "";

        internal static Favorite FromSettings(JsonObject item) =>
            new(item["kind"]?.ToString() ?? "", item["title"]?.ToString() ?? "", item["target"]?.ToString() ?? "",
                item["icon"]?.ToString() ?? "", item["siteIcon"]?.ToString() ?? "", item["monochrome"]?.GetValue<bool>() == true)
            { IconSource = item["iconSource"]?.ToString() ?? "" };
    }

    private readonly Window pet;
    private readonly Action<string> open;
    private readonly Action exit;
    private Window[] spheres = [];
    private Favorite[] favorites = [];
    private int spherePage;
    private int spheresPerPage;
    private double orbSize = DefaultOrbSize;
    private double sphereSpread = 1;
    private double sphereOffset = DefaultSphereOffset;
    private ControlTemplate? sphereTemplate;
    private readonly MouseHook mouseCallback;
    private IntPtr hook;
    private Window? menu;
    private readonly Dictionary<string, Button> menuItems = [];
    internal bool HasContextMenu => menu is { IsVisible: true };
    private QuickChatWindow? quickChat;
    private TextBlock? menuTitle;
    private TextBlock? notesCheck;
    private bool spheresVisible;
    private int visibilityRevision;
    private bool disposed;

    public DesktopControls(Window pet, Action<string> open, Action exit)
    {
        this.pet = pet;
        this.open = open;
        this.exit = exit;
        mouseCallback = MousePressed;

    }

    public void SetFavorites(Favorite[] items)
    {
        if (items.SequenceEqual(favorites)) return;
        favorites = items;
        spherePage = 0;
        bool wasVisible = spheresVisible;
        RebuildSpheres();
        if (wasVisible) ShowSpheres();
    }

    internal void RefreshSiteIcons()
    {
        bool wasVisible = spheresVisible;
        RebuildSpheres();
        if (wasVisible) ShowSpheres();
    }

    public void SetSphereSize(double size)
    {
        if (orbSize == size) return;
        orbSize = size;
        sphereTemplate = OrbTemplate(size);
        int pageSize = SpherePageSize();
        if (spheresPerPage != pageSize)
        {
            int first = spherePage * spheresPerPage;
            spherePage = first / pageSize;
            bool wasVisible = spheresVisible;
            RebuildSpheres();
            if (wasVisible) ShowSpheres();
            return;
        }
        foreach (var sphere in spheres) ResizeSphere(sphere);
        PositionSpheres();
    }

    public void SetSphereSpread(double spread)
    {
        if (sphereSpread == spread) return;
        sphereSpread = spread;
        PositionSpheres();
    }

    public void SetSphereOffset(double offset)
    {
        if (sphereOffset == offset) return;
        sphereOffset = offset;
        PositionSpheres();
    }

    private void ResizeSphere(Window sphere)
    {
        sphere.Width = sphere.Height = orbSize;
        var button = (Button)sphere.Content;
        button.Width = button.Height = orbSize;
        button.Template = sphereTemplate ??= OrbTemplate(orbSize);
        var icon = (Viewbox)button.Content;
        icon.Width = icon.Height = orbSize * (((FrameworkElement)icon.Child).Width / DefaultOrbSize);
    }

    private int SpherePageSize()
    {
        Rect area = SphereArea(WorkArea(), orbSize, MinimumSphereGap);
        double diameter = Math.Min(area.Width, area.Height);
        double radius = Math.Max(orbSize, (diameter - orbSize) / 2);
        return (int)Compatibility.Clamp((int)(Math.PI / (2 * Math.Asin((orbSize + MinimumSphereGap) / (2 * radius)))) + 1, 1, MaxSpheresPerArc);
    }

    private void RebuildSpheres()
    {
        spheresVisible = false;
        visibilityRevision++;
        foreach (var sphere in spheres) sphere.Close();
        int size = spheresPerPage = SpherePageSize();
        int pages = Math.Max(1, (favorites.Length + size - 1) / size);
        spheres = favorites.Skip(spherePage * size).Take(size).Select(item =>
        {
            var icon = new System.Windows.Shapes.Path
            {
                Data = FavoriteIcons.Get(item.Icon) ?? FavoriteIcons.Get(item.Kind == "site" ? "globe" : "cpu"),
                Width = 21, Height = 21, StrokeThickness = 1.55,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round, Stretch = Stretch.Uniform,
            };
            icon.UseTheme(Shape.StrokeProperty, "Accent");
            var toggle = new Button
            {
                Cursor = Cursors.Hand,
                Content = new Viewbox { Child = ReadFavoriteIcon(item) ?? icon, Stretch = Stretch.Uniform },
            };
            var popover = HoverCard.Attach(toggle, item.Title,
                pages == 1 ? null : $"{spherePage + 1}/{pages} · Колесо мыши — следующая страница");
            AutomationProperties.SetName(toggle, item.Title);
            toggle.Click += (_, _) =>
            {
                try { MainWindow.OpenTarget(item.Kind, item.Target); }
                catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
                { popover.ShowMessage(error.Message); }
            };
            toggle.PreviewMouseWheel += (_, args) =>
            {
                if (pages <= 1) return;
                args.Handled = true;
                spherePage = (spherePage + (args.Delta > 0 ? pages - 1 : 1)) % pages;
                RebuildSpheres();
                ShowSpheres();
            };
            var sphere = FloatingWindow(toggle, orbSize, orbSize, activate: false);
            sphere.Topmost = pet.Topmost;
            ResizeSphere(sphere);
            return sphere;
        }).ToArray();
        StopWatchingWhenIdle();
    }

    internal static FrameworkElement? ReadFavoriteIcon(Favorite item)
    {
        try
        {
            BitmapSource? source;
            if (item.Kind == "app" && item.IconSource == "app")
            {
                source = FavoriteIcons.ReadApplicationIcon(item.Target);
                if (source is not null && item.Monochrome) source = SiteIconMask.Create(source);
            }
            else if (item.Kind == "site" && item.IconSource != "library" && SiteIcons.IsCachedName(item.SiteIcon))
            {
                string folder = System.IO.Path.Combine(App.DataDirectory, "site-icons");
                string? name = item.Monochrome ? SiteIconMask.Ensure(folder, item.SiteIcon) : item.SiteIcon;
                if (name is null) return null;
                using var stream = System.IO.File.OpenRead(System.IO.Path.Combine(folder, name));
                source = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            }
            else return null;
            if (source is null) return null;
            if (!item.Monochrome)
            {
                var image = new Image { Width = 26, Height = 26, Source = source, Stretch = Stretch.Uniform,
                    UseLayoutRounding = true, SnapsToDevicePixels = true };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                return image;
            }
            var shape = new Rectangle { Width = 24, Height = 24, OpacityMask = new ImageBrush(source) { Stretch = Stretch.Uniform } };
            RenderOptions.SetBitmapScalingMode(shape, BitmapScalingMode.HighQuality);
            shape.UseLayoutRounding = shape.SnapsToDevicePixels = true;
            shape.UseTheme(Shape.FillProperty, "Accent");
            return shape;
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException or NotSupportedException or
            ArgumentException or System.IO.FileFormatException or ExternalException) { return null; }
    }

    public bool ShowSpheres()
    {
        if (quickChat is { IsPresented: true }) return false;
        if (spheres.Length == 0) return false;
        if (spheresVisible) return true;
        spheresVisible = true;
        visibilityRevision++;
        PositionSpheres();

        for (int index = 0; index < spheres.Length; index++)
        {
            Window sphere = spheres[index];
            sphere.Topmost = pet.Topmost;
            if (!sphere.IsVisible)
            {
                sphere.Opacity = 0;
                sphere.Owner = pet;
                sphere.Show();
            }
            SetInteractive(sphere, true);
            Animate(sphere, true, index * 24);
        }
        WatchOutside();
        return true;
    }

    public void ShowMenu(string name, bool notesVisible = false)
    {
        CloseMenu();
        var stack = new StackPanel();
        stack.Children.Add(AppVersion.MenuLabel());
        var header = new DockPanel { LastChildFill = true };
        var dismiss = new Button
        {
            Width = 30, Height = 30, Content = "×", FontSize = 16, ToolTip = "Закрыть меню",
            Template = RowTemplate(), Cursor = Cursors.Hand, HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        dismiss.UseTheme(Control.ForegroundProperty, "Ink");
        AutomationProperties.SetName(dismiss, "Закрыть меню");
        dismiss.Click += (_, _) => CloseMenu();
        DockPanel.SetDock(dismiss, Dock.Right);
        header.Children.Add(dismiss);
        menuTitle = new TextBlock { Text = name, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(12, 6, 8, 9) };
        menuTitle.UseTheme(TextBlock.ForegroundProperty, "Accent");
        header.Children.Add(menuTitle);
        stack.Children.Add(header);

        for (int index = 0; index < Entries.Length; index++)
        {
            if (index == 5 || index == Entries.Length - 1)
            {
                var divider = new Border { Height = 1, Margin = new Thickness(11, 6, 11, 6) };
                divider.UseTheme(Border.BackgroundProperty, "Edge");
                stack.Children.Add(divider);
            }
            var (label, action, icon, hint) = Entries[index];
            var row = new Grid { Margin = new Thickness(11, 0, 11, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(31) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var mark = Glyph(icon);
            mark.HorizontalAlignment = HorizontalAlignment.Left;
            row.Children.Add(mark);
            var caption = new TextBlock { Text = label, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            caption.UseTheme(TextBlock.ForegroundProperty, "Ink");
            Grid.SetColumn(caption, 1);
            row.Children.Add(caption);
            var status = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            Grid.SetColumn(status, 2);
            row.Children.Add(status);
            var shortcut = new TextBlock { Text = hint, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            shortcut.UseTheme(TextBlock.ForegroundProperty, "Muted");
            status.Children.Add(shortcut);
            if (action == "notes")
            {
                notesCheck = new TextBlock { FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                notesCheck.UseTheme(TextBlock.ForegroundProperty, "Accent");
                SetNotesChecked(notesVisible);
                status.Children.Add(notesCheck);
            }
            var button = new Button
            {
                Height = 31, Content = row, Template = RowTemplate(), Cursor = Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
            };
            AutomationProperties.SetName(button, label);
            menuItems[action] = button;
            button.Click += (_, _) =>
            {
                if (action == "exit")
                {
                    CloseMenu();
                    exit();
                }
                else open(action);
            };
            stack.Children.Add(button);
        }

        stack.Width = 252;
        var content = new Viewbox { Child = stack, StretchDirection = StretchDirection.DownOnly, VerticalAlignment = VerticalAlignment.Top };
        var frame = new Border { Child = content, Padding = new Thickness(8), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12) };
        frame.UseTheme(Border.BackgroundProperty, "Surface");
        frame.UseTheme(Border.BorderBrushProperty, "Edge");
        menu = FloatingWindow(frame, 270, 84 + Entries.Length * 31, activate: true);
        menu.MinWidth = 240;
        menu.MinHeight = 240;
        menu.UseTheme(Window.FontFamilyProperty, "Font");
        menu.PreviewKeyDown += (_, args) => { if (args.Key == Key.Escape) CloseMenu(); };
        PositionPanel(menu, "context");
        menu.Show();
        WatchOutside();
    }

    public void SetNotesChecked(bool visible)
    {
        if (notesCheck is not null)
        {
            notesCheck.Text = "✓";
            notesCheck.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public void SetPetName(string name)
    {
        if (menuTitle is not null) menuTitle.Text = name;
    }

    public void WatchQuickChat(QuickChatWindow window)
    {
        window.Owner = pet;
        window.PresentationChanged += () =>
        {
            quickChat = window.IsPresented ? window : null;
            if (quickChat is not null)
            {
                // A triple-click can leave the first click's favorites visible.
                // Remove them immediately, including any in-flight fade, before
                // they can cover the active text field or intercept its clicks.
                spheresVisible = false;
                visibilityRevision++;
                foreach (var sphere in spheres)
                {
                    SetInteractive(sphere, false);
                    sphere.BeginAnimation(UIElement.OpacityProperty, null);
                    sphere.Hide();
                }
                CloseMenu();
                WatchOutside();
            }
            else StopWatchingWhenIdle();
        };
    }

    public void PositionPanel(Window window, string section)
    {
        Rect area = WorkArea();
        var size = section switch
        {
            "context" => (270d, 105d + Entries.Length * 31),
            "calendar" => (560d, 570d),
            "tools" or "note-list" or "dates" or "notifications" or "commands" or "focus" or "profiles" or "sync" or "modules" => (480d, 500d),
            "sticky" => (340d, 320d),
            "motion" => (426d, Math.Min(480d, 151d + 36d * Math.Max(1, (pet as DesktopPetWindow)?.AnimationCount ?? 0))),
            "manage" => (404d, 370d),
            "theme" => (404d, 480d),
            "pets" => (438d, 438d),
            "favorites" => (454d, 410d),
            "model" => (438d, 400d),
            "reminders" => (438d, 348d),
            "sharing" => (430d, 354d),
            _ => (450d, 438d),
        };
        if (section == "sticky" || window is NotificationWindow)
        {
            window.Width = Math.Max(window.MinWidth, Math.Min(window.Width, area.Width));
            window.Height = Math.Max(window.MinHeight, Math.Min(window.Height, area.Height));
        }
        else
        {
            window.Width = Math.Max(window.MinWidth, Math.Min(size.Item1, area.Width));
            window.Height = Math.Max(window.MinHeight, Math.Min(size.Item2, area.Height));
        }
        if (TryPositionAtMenu(window, section == "sticky" ? "notes" : section, area)) return;
        if (section == "context")
        {
            // Ignore transparent viewport padding; do not follow individual animation ticks.
            Rect sprite = SpherePetBounds();
            double above = sprite.Top - Gap - window.Height;
            double below = sprite.Bottom + Gap;
            window.Left = Compatibility.Clamp(sprite.Left + (sprite.Width - window.Width) / 2, area.Left, area.Right - window.Width);
            window.Top = above < area.Top && below + window.Height <= area.Bottom
                ? below : Compatibility.Clamp(above, area.Top, area.Bottom - window.Height);
            return;
        }
        var visible = PetBounds();
        double right = visible.Right + Gap;
        double left = visible.Left - window.Width - Gap;
        window.Left = Compatibility.Clamp(right + window.Width <= area.Right ? right : left, area.Left, area.Right - window.Width);
        window.Top = Compatibility.Clamp(visible.Top + visible.Height / 2 - window.Height / 2, area.Top, area.Bottom - window.Height);
        KeepMenuClear(window, area);
    }

    public void FollowPet()
    {
        if (menu is { IsVisible: true }) PositionPanel(menu, "context");
    }

    private void PositionSpheres()
    {
        if (!spheresVisible && !spheres.Any(sphere => sphere.IsVisible)) return;
        Rect area = WorkArea();
        Rect sprite = SpherePetBounds();
        Point[] positions = Arrange(sprite, area, spheres.Length, orbSize, sphereSpread, sphereOffset);
        for (int index = 0; index < spheres.Length; index++)
        {
            spheres[index].Left = positions[index].X;
            spheres[index].Top = positions[index].Y;
        }
    }

    public void PositionBubble(Window window)
    {
        Rect area = WorkArea();
        if (TryPositionAtMenu(window, "quick", area)) return;
        Rect sprite = PetBounds();
        window.Left = Compatibility.Clamp(sprite.Left + (sprite.Width - window.Width) / 2, area.Left, Math.Max(area.Left, area.Right - window.Width));
        double above = sprite.Top - window.Height - Gap;
        double below = sprite.Bottom + Gap;
        window.Top = above >= area.Top || below + window.Height > area.Bottom
            ? Math.Max(area.Top, above) : below;
        KeepMenuClear(window, area);
    }

    private bool TryPositionAtMenu(Window window, string section, Rect area)
    {
        if (menu is not { IsVisible: true } || menu == window ||
            !menuItems.TryGetValue(section, out var item) || !item.IsVisible) return false;
        menu.UpdateLayout();
        Rect local = item.TransformToAncestor(menu).TransformBounds(new Rect(item.RenderSize));
        var row = new Rect(menu.Left + local.Left, menu.Top + local.Top, local.Width, local.Height);
        Rect placed = PlaceBesideMenu(new Rect(menu.Left, menu.Top, menu.ActualWidth, menu.ActualHeight), row,
            new Size(window.Width, window.Height), area, new Size(window.MinWidth, window.MinHeight));
        window.Width = placed.Width;
        window.Height = placed.Height;
        window.Left = placed.Left;
        window.Top = placed.Top;
        return true;
    }

    internal static Rect PlaceBesideMenu(Rect menu, Rect item, Size desired, Rect area, Size minimum)
    {
        double rightSpace = Math.Max(0, area.Right - menu.Right - Gap);
        double leftSpace = Math.Max(0, menu.Left - area.Left - Gap);
        bool right = rightSpace >= desired.Width || (leftSpace < desired.Width && rightSpace >= leftSpace);
        double space = right ? rightSpace : leftSpace;
        double width = Math.Min(desired.Width, space >= minimum.Width ? space : area.Width);
        double height = Math.Min(desired.Height, area.Height);
        double x = right ? menu.Right + Gap : menu.Left - Gap - width;
        return new Rect(Compatibility.Clamp(x, area.Left, Math.Max(area.Left, area.Right - width)),
            Compatibility.Clamp(item.Top, area.Top, Math.Max(area.Top, area.Bottom - height)), width, height);
    }

    private void KeepMenuClear(Window window, Rect area)
    {
        if (menu is not { IsVisible: true } || menu == window) return;
        Rect blocked = new(menu.Left, menu.Top, menu.Width, menu.Height);
        blocked.Inflate(Gap, Gap);
        Rect panel = new(window.Left, window.Top, window.Width, window.Height);
        if (!panel.IntersectsWith(blocked)) return;

        Point[] slots =
        [
            new(blocked.Left - panel.Width, panel.Top), new(blocked.Right, panel.Top),
            new(panel.Left, blocked.Top - panel.Height), new(panel.Left, blocked.Bottom),
        ];
        Point? best = slots.Where(p => area.Contains(new Rect(p, panel.Size)))
            .OrderBy(p => new Rect(p, panel.Size).IntersectsWith(PetBounds()))
            .ThenBy(p => (p - panel.TopLeft).LengthSquared)
            .Select(p => (Point?)p).FirstOrDefault();
        if (best is { } position)
        {
            window.Left = position.X;
            window.Top = position.Y;
        }
    }

    private static Rect SphereArea(Rect area, double orbSize, double margin)
    {
        double horizontal = Math.Min(margin, Math.Max(0, (area.Width - orbSize) / 2));
        double vertical = Math.Min(margin, Math.Max(0, (area.Height - orbSize) / 2));
        area.Inflate(-horizontal, -vertical);
        return area;
    }

    // Keep the circle centered on the character; only use angles whose spheres fit on screen.
    internal static Point[] Arrange(Rect sprite, Rect area, int count, double orbSize = DefaultOrbSize,
        double spread = 1, double offsetFromPet = DefaultSphereOffset, double? visibleRadius = null)
    {
        if (count == 0) return [];
        double halfOrb = orbSize / 2;
        Point center = new(sprite.Left + sprite.Width / 2, sprite.Top + sprite.Height / 2);
        double gap = MinimumSphereGap + Compatibility.Clamp(spread, 0, 1) * orbSize / 2;
        Rect allowed = SphereArea(area, orbSize, gap);
        allowed = new Rect(allowed.Left + halfOrb, allowed.Top + halfOrb,
            Math.Max(0, allowed.Width - orbSize), Math.Max(0, allowed.Height - orbSize));
        Point nearest = new(Compatibility.Clamp(center.X, allowed.Left, allowed.Right),
            Compatibility.Clamp(center.Y, allowed.Top, allowed.Bottom));
        double minimumRadius = (nearest - center).Length;
        double maximumRadius = new[] { allowed.TopLeft, allowed.TopRight, allowed.BottomLeft, allowed.BottomRight }
            .Max(point => (point - center).Length);
        double spacingRadius = count <= 1 ? 0 : (orbSize + gap) / (2 * Math.Sin(Math.PI / (count - 1) / 2));
        double silhouetteRadius = visibleRadius ?? Math.Sqrt(sprite.Width * sprite.Width + sprite.Height * sprite.Height) / 2;
        double preferredRadius = Math.Max(spacingRadius, Math.Max(0, silhouetteRadius + halfOrb + offsetFromPet));
        if (count == 1 && preferredRadius == 0 && minimumRadius == 0)
            return [center - new Vector(halfOrb, halfOrb)];
        var best = Enumerable.Repeat(nearest - new Vector(halfOrb, halfOrb), count).ToArray();
        double bestCost = double.PositiveInfinity;

        // Include the exact requested radius, then explore circles intersecting the usable screen.
        for (int sample = -1; sample <= 96; sample++)
        {
            double radius = sample < 0 ? Compatibility.Clamp(preferredRadius, minimumRadius, maximumRadius)
                : minimumRadius + (maximumRadius - minimumRadius) * sample / 96;
            if (radius < .000001) continue;
            foreach (var arc in ScreenArcs(center, radius, allowed))
            {
                double span = count <= 1 ? 0 : Math.Min(Math.PI, arc.End - arc.Start);
                double step = count <= 1 ? 0 : span / (count - 1);
                double separation = count <= 1 ? orbSize + gap : 2 * radius * Math.Sin(step / 2);
                double crowding = Math.Max(0, orbSize + gap - separation);
                for (int turn = -1; turn <= 2; turn++)
                {
                    double above = -Math.PI / 2 + turn * 2 * Math.PI;
                    double middle = (arc.Start + arc.End) / 2;
                    double freedom = Math.Max(0, (arc.End - arc.Start - span) / 2);
                    double direction = middle + Compatibility.Clamp(above - middle, -freedom, freedom);
                    double cost = Math.Pow(radius - preferredRadius, 2) * 4
                        + Math.Pow(Math.PI - span, 2) * 200 + Math.Pow(direction - above, 2) * 10
                        + (crowding > .001 ? 1e9 + crowding * crowding * 1000 : 0);
                    if (cost >= bestCost) continue;
                    bestCost = cost;
                    for (int index = 0; index < count; index++)
                    {
                        double angle = direction - span / 2 + index * step;
                        best[index] = new Point(center.X + radius * Math.Cos(angle) - halfOrb,
                            center.Y + radius * Math.Sin(angle) - halfOrb);
                    }
                }
            }
            if (bestCost < .000001) break;
        }
        return best;
    }

    private static List<(double Start, double End)> ScreenArcs(Point center, double radius, Rect allowed)
    {
        double turn = 2 * Math.PI;
        var cuts = new List<double> { 0, turn };
        foreach (double edge in new[] { allowed.Left, allowed.Right })
        {
            double value = (edge - center.X) / radius;
            if (value < -1 || value > 1) continue;
            double angle = Math.Acos(value);
            cuts.Add(angle);
            cuts.Add(turn - angle);
        }
        foreach (double edge in new[] { allowed.Top, allowed.Bottom })
        {
            double value = (edge - center.Y) / radius;
            if (value < -1 || value > 1) continue;
            double angle = Math.Asin(value);
            cuts.Add((angle + turn) % turn);
            cuts.Add((Math.PI - angle + turn) % turn);
        }
        cuts.Sort();
        var arcs = new List<(double Start, double End)>();
        for (int index = 1; index < cuts.Count; index++)
        {
            double start = cuts[index - 1], end = cuts[index];
            if (end - start < .000001) continue;
            double middle = (start + end) / 2;
            if (!allowed.Contains(new Point(center.X + radius * Math.Cos(middle), center.Y + radius * Math.Sin(middle)))) continue;
            if (arcs.Count > 0 && Math.Abs(arcs[arcs.Count - 1].End - start) < .000001)
                arcs[arcs.Count - 1] = (arcs[arcs.Count - 1].Start, end);
            else arcs.Add((start, end));
        }
        if (arcs.Count > 1 && arcs[0].Start < .000001 && Math.Abs(arcs[arcs.Count - 1].End - turn) < .000001)
        {
            arcs[arcs.Count - 1] = (arcs[arcs.Count - 1].Start, arcs[0].End + turn);
            arcs.RemoveAt(0);
        }
        return arcs;
    }

    private Rect PetBounds()
    {
        var frame = new Rect(pet.Left, pet.Top, pet.Width, pet.Height);
        return pet is DesktopPetWindow sprite ? ScalePetBounds(frame, sprite.VisibleContent()) : frame;
    }

    private Rect SpherePetBounds()
    {
        Rect frame = new(pet.Left, pet.Top, pet.Width, pet.Height);
        return pet is DesktopPetWindow sprite ? ScalePetBounds(frame, sprite.VisibleBounds()) : frame;
    }

    internal Rect WorkArea(bool atCursor = false)
    {
        var handle = new WindowInteropHelper(pet).Handle;
        if (handle == IntPtr.Zero) return SystemParameters.WorkArea;
        IntPtr monitor;
        if (atCursor && GetCursorPos(out var cursor)) monitor = MonitorFromPoint(cursor, 2);
        else
        {
            var bounds = SpherePetBounds();
            var center = pet.PointToScreen(new Point(bounds.Left - pet.Left + bounds.Width / 2, bounds.Top - pet.Top + bounds.Height / 2));
            monitor = MonitorFromPoint(new NativePoint { X = (int)Math.Round(center.X), Y = (int)Math.Round(center.Y) }, 2);
        }
        if (!TryWorkArea(monitor, out var work)) return SystemParameters.WorkArea;
        Point top = pet.PointFromScreen(new Point(work.Left, work.Top));
        Point bottom = pet.PointFromScreen(new Point(work.Right, work.Bottom));
        return new Rect(new Point(pet.Left + top.X, pet.Top + top.Y), new Size(bottom.X - top.X, bottom.Y - top.Y));
    }

    internal static Rect ClampPetFrame(Rect frame, Rect area) =>
        new(Compatibility.Clamp(frame.Left, area.Left, Math.Max(area.Left, area.Right - frame.Width)),
            Compatibility.Clamp(frame.Top, area.Top, Math.Max(area.Top, area.Bottom - frame.Height)), frame.Width, frame.Height);

    internal static Rect ScalePetBounds(Rect frame, Rect content) =>
        new(frame.Left + frame.Width * content.X, frame.Top + frame.Height * content.Y,
            frame.Width * content.Width, frame.Height * content.Height);

    private static bool TryWorkArea(IntPtr monitor, out NativeRect work)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        bool found = monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info);
        work = info.Work;
        return found;
    }

    private IntPtr MousePressed(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && message.ToInt32() is 0x201 or 0x204 or 0x207 or 0x20B)
        {
            var point = Marshal.PtrToStructure<NativePoint>(data);
            var chat = quickChat;
            pet.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                if (disposed) return;
                IntPtr hit = GetAncestor(WindowFromPoint(point), 2);
                if (chat is { IsPresented: true } && !IsHit(chat.VisibleSurface, hit)) chat.Dismiss();
                GetWindowThreadProcessId(hit, out uint owner);
                if (owner == Compatibility.ProcessId)
                {
                    if (!IsHit(pet, hit) && !IsHit(menu, hit) && !spheres.Any(window => IsHit(window, hit))) HideSpheres();
                    return;
                }
                CloseMenu();
                HideSpheres();
            });
        }
        return CallNextHookEx(hook, code, message, data);
    }

    private static bool IsHit(Window? window, IntPtr hit) =>
        hit != IntPtr.Zero && window is { IsVisible: true } && new WindowInteropHelper(window).Handle == hit;

    private void WatchOutside()
    {
        if (hook == IntPtr.Zero) hook = SetWindowsHookEx(14, mouseCallback, GetModuleHandle(null), 0);
    }

    private void StopWatchingWhenIdle()
    {
        if (menu is not null || spheresVisible || quickChat is not null || hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(hook);
        hook = IntPtr.Zero;
    }

    internal void CloseMenu()
    {
        var openMenu = menu;
        menu = null;
        menuItems.Clear();
        menuTitle = null;
        notesCheck = null;
        openMenu?.Close();
        StopWatchingWhenIdle();
    }

    internal void HideSpheres()
    {
        if (!spheresVisible) return;
        spheresVisible = false;
        int revision = ++visibilityRevision;
        for (int index = 0; index < spheres.Length; index++)
        {
            Window sphere = spheres[index];
            SetInteractive(sphere, false);
            Animate(sphere, false, index * 12, () =>
            {
                if (revision == visibilityRevision) sphere.Hide();
            });
        }
        StopWatchingWhenIdle();
    }

    private static void Animate(Window window, bool show, int delay, Action? done = null)
    {
        bool animate = SystemParameters.ClientAreaAnimation;
        var fade = new DoubleAnimation(show ? 1 : 0, TimeSpan.FromMilliseconds(animate ? (show ? 230 : 160) : 0))
        {
            BeginTime = TimeSpan.FromMilliseconds(animate ? delay : 0),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        var element = (FrameworkElement)window.Content;
        var scale = (ScaleTransform)element.RenderTransform;
        var zoom = (DoubleAnimation)fade.Clone();
        zoom.To = show ? 1 : .75;
        if (done is not null) fade.Completed += (_, _) => done();
        window.BeginAnimation(UIElement.OpacityProperty, fade);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, zoom);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, zoom);
    }

    private static Window FloatingWindow(FrameworkElement content, double width, double height, bool activate)
    {
        content.RenderTransformOrigin = new Point(.5, .5);
        content.RenderTransform = new ScaleTransform(activate ? 1 : .75, activate ? 1 : .75);
        var window = new Window
        {
            Title = activate ? "Меню питомца" : "Быстрое действие Спутника",
            Width = width, Height = height, ShowActivated = activate, Topmost = true, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = !activate, Background = Brushes.Transparent, Content = content,
        };
        if (activate && content is Border frame) RoundedWindow.Apply(window, frame);
        return window;
    }

    private static ControlTemplate OrbTemplate(double size)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Fill";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(size / 2));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        border.SetResourceReference(Border.BackgroundProperty, "ThemeOrb");
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        {
            var trigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            trigger.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("ThemeOrbRimFocus"), "Fill"));
            template.Triggers.Add(trigger);
        }
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(UIElement.OpacityProperty, .82, "Fill"));
        template.Triggers.Add(focus);
        return template;
    }

    private static ControlTemplate RowTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Fill";
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetBinding(FrameworkElement.HorizontalAlignmentProperty,
            new System.Windows.Data.Binding(nameof(Control.HorizontalContentAlignment)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        content.SetBinding(FrameworkElement.VerticalAlignmentProperty,
            new System.Windows.Data.Binding(nameof(Control.VerticalContentAlignment)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.AppendChild(content);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        foreach (DependencyProperty property in new[] { UIElement.IsMouseOverProperty, UIElement.IsKeyboardFocusedProperty })
        {
            var trigger = new Trigger { Property = property, Value = true };
            trigger.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("ThemeHover"), "Fill"));
            template.Triggers.Add(trigger);
        }
        return template;
    }

    private static Shape Glyph(string name)
    {
        string data = name switch
        {
            "chat" => "M21 11.5 A8.5 8.5 0 0 1 12.5 20 L4 21 L5.2 16 A8.5 8.5 0 1 1 21 11.5 M8 10 H17 M8 14 H14",
            "note" => "M7 3 H18 Q21 3 21 6 V15 L15 21 H7 Q4 21 4 18 V6 Q4 3 7 3 M15 21 V17 Q15 15 17 15 H21 M8 8 H17 M8 12 H15",
            "link" => "M10 7 H7 A5 5 0 0 0 7 17 H10 M14 7 H17 A5 5 0 0 1 17 17 H14 M8 12 H16",
            "bell" => "M5 17 H19 L17 14 V9 A5 5 0 0 0 7 9 V14 Z M10 20 Q12 23 14 20",
            "pet" => "M15.5 7 A3.5 3.5 0 1 1 8.5 7 A3.5 3.5 0 1 1 15.5 7 M5 16 Q5 14 7 14 H17 Q19 14 19 16 Q19 21 12 21 Q5 21 5 16 Z",
            "play" => "M6 3 L20 12 6 21 Z",
            "palette" => "M5 18 L19 4 M11 4 H19 V12 M5 10 V18 H13",
            "tray" => "M3 14V20H21V14 M7 10L12 15 17 10 M12 3V15",
            "close" => "M4 4 L20 20 M20 4 L4 20",
            _ => "M12 3 V5 M12 19 V21 M3 12 H5 M19 12 H21 M6 6 L8 8 M16 16 L18 18 M6 18 L8 16 M16 8 L18 6 M17 12 A5 5 0 1 1 7 12 A5 5 0 1 1 17 12",
        };
        var icon = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(data), StrokeThickness = 1.6,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round, Stretch = Stretch.Uniform, Width = 20, Height = 20,
        };
        icon.UseTheme(Shape.StrokeProperty, "Accent");
        return icon;
    }

    private static void SetInteractive(Window window, bool enabled)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        const int transparent = 0x20;
        int style = GetWindowLong(handle, -20);
        SetWindowLong(handle, -20, enabled ? style & ~transparent : style | transparent);
    }

    public void Dispose()
    {
        disposed = true;
        quickChat = null;
        spheresVisible = false;
        visibilityRevision++;
        CloseMenu();
        foreach (var sphere in spheres) sphere.Close();
        StopWatchingWhenIdle();
    }

    private delegate IntPtr MouseHook(int code, IntPtr message, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Bounds, Work; public int Flags; }

    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int id, MouseHook callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr handle);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr handle, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? module);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr handle, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr handle, ref MonitorInfo info);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr handle, int index, int value);
}
