using System.Runtime.InteropServices;
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
    private const double Gap = 10;
    private const int MaxSpheresPerArc = 9;
    private static readonly (string Title, string Action, string Icon, string Hint)[] Entries =
    [
        ("Быстрый чат", "quick", "chat", "3 клика"),
        ("Открыть чат", "chat", "chat", ""),
        ("Заметки", "notes", "note", "Ctrl + 2 клика"),
        ("Сайты и программы", "favorites", "link", ""),
        ("Напоминания", "reminders", "bell", ""),
        ("Движения", "motion", "play", "›"),
        ("Питомцы", "pets", "pet", ""),
        ("Внешний вид", "theme", "palette", "›"),
        ("Модель и API", "model", "settings", ""),
        ("Управление", "manage", "settings", ""),
        ("Каналы отправки", "sharing", "link", ""),
        ("Закрыть питомца", "exit", "close", ""),
    ];
    internal static IEnumerable<(string Title, string Action)> Sections =>
        Entries.Where(entry => entry.Action != "exit").Select(entry => (entry.Title, entry.Action));
    internal sealed record Favorite(string Kind, string Title, string Target, string Icon, string SiteIcon = "", bool Monochrome = false);

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
        FollowPet();
    }

    public void SetSphereSpread(double spread)
    {
        if (sphereSpread == spread) return;
        sphereSpread = spread;
        FollowPet();
    }

    public void SetSphereOffset(double offset)
    {
        if (sphereOffset == offset) return;
        sphereOffset = offset;
        FollowPet();
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
        Rect area = WorkArea();
        double diameter = Math.Min(area.Width, area.Height);
        double radius = Math.Max(orbSize, (diameter - orbSize) / 2);
        return (int)Compatibility.Clamp((int)(Math.PI / (2 * Math.Asin((orbSize + 5) / (2 * radius)))) + 1, 1, MaxSpheresPerArc);
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
                Content = new Viewbox { Child = ReadSiteIcon(item) ?? icon, Stretch = Stretch.Uniform },
                ToolTip = pages == 1 ? item.Title : $"{item.Title} · {spherePage + 1}/{pages} · Колесо мыши",
            };
            AutomationProperties.SetName(toggle, item.Title);
            toggle.Click += (_, _) =>
            {
                try { MainWindow.OpenTarget(item.Kind, item.Target); }
                catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
                { toggle.ToolTip = error.Message; }
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
            ResizeSphere(sphere);
            return sphere;
        }).ToArray();
        StopWatchingWhenIdle();
    }

    private static FrameworkElement? ReadSiteIcon(Favorite item)
    {
        if (item.Kind != "site" || !SiteIcons.IsCachedName(item.SiteIcon)) return null;
        try
        {
            string folder = System.IO.Path.Combine(App.DataDirectory, "site-icons");
            string? name = item.Monochrome ? SiteIconMask.Ensure(folder, item.SiteIcon) : item.SiteIcon;
            if (name is null) return null;
            using var stream = System.IO.File.OpenRead(System.IO.Path.Combine(folder, name));
            var source = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            if (!item.Monochrome) return new Image { Width = 26, Height = 26, Source = source, Stretch = Stretch.Uniform };
            var shape = new Rectangle { Width = 24, Height = 24, OpacityMask = new ImageBrush(source) { Stretch = Stretch.Uniform } };
            shape.UseTheme(Shape.FillProperty, "Accent");
            return shape;
        }
        catch (Exception error) when (error is System.IO.IOException or NotSupportedException or System.IO.FileFormatException) { return null; }
    }

    public void ShowSpheres()
    {
        if (spheresVisible || spheres.Length == 0) return;
        spheresVisible = true;
        visibilityRevision++;
        FollowPet();

        for (int index = 0; index < spheres.Length; index++)
        {
            Window sphere = spheres[index];
            if (!sphere.IsVisible)
            {
                sphere.Opacity = 0;
                sphere.Show();
            }
            SetInteractive(sphere, true);
            Animate(sphere, true, index * 24);
        }
        WatchOutside();
    }

    public void ShowMenu(string name, bool notesVisible = false)
    {
        CloseMenu();
        var stack = new StackPanel();
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
        menu = FloatingWindow(frame, 270, 456, activate: true);
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
        window.IsVisibleChanged += (_, _) =>
        {
            quickChat = window.IsVisible ? window : null;
            if (quickChat is not null) WatchOutside();
            else StopWatchingWhenIdle();
        };
    }

    public void PositionPanel(Window window, string section)
    {
        Rect area = WorkArea();
        var size = section switch
        {
            "context" => (270d, 456d),
            "sticky" => (340d, 320d),
            "motion" => (470d, 126d + 34d * Math.Max(1, Math.Ceiling(
                ((pet as DesktopPetWindow)?.AnimationCount ?? 0) / (area.Width >= 410 ? 2d : 1d)))),
            "manage" => (394d, 390d),
            "theme" => (394d, 418d),
            "pets" => (394d, 340d),
            "favorites" => (490d, 492d),
            _ => (460d, 520d),
        };
        window.Width = Math.Min(size.Item1, area.Width);
        window.Height = Math.Min(size.Item2, area.Height);
        if (section == "context")
        {
            Rect sprite = PetBounds();
            double above = sprite.Top - Gap - window.Height;
            double below = sprite.Bottom + Gap;
            window.Left = Compatibility.Clamp(sprite.Left + (sprite.Width - window.Width) / 2, area.Left, area.Right - window.Width);
            window.Top = above < area.Top && below + window.Height <= area.Bottom
                ? below : Compatibility.Clamp(above, area.Top, area.Bottom - window.Height);
            return;
        }
        double right = pet.Left + pet.Width + Gap;
        double left = pet.Left - window.Width - Gap;
        window.Left = Compatibility.Clamp(right + window.Width <= area.Right ? right : left, area.Left, area.Right - window.Width);
        window.Top = Compatibility.Clamp(pet.Top + pet.Height / 2 - window.Height / 2, area.Top, area.Bottom - window.Height);
        KeepMenuClear(window, area);
    }

    public void FollowPet()
    {
        if (menu is { IsVisible: true }) PositionPanel(menu, "context");
        if (!spheresVisible && !spheres.Any(sphere => sphere.IsVisible)) return;
        Rect area = WorkArea();
        Rect sprite = PetBounds();
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
        Rect sprite = PetBounds();
        window.Left = Compatibility.Clamp(sprite.Left + (sprite.Width - window.Width) / 2, area.Left, Math.Max(area.Left, area.Right - window.Width));
        double above = sprite.Top - window.Height - Gap;
        double below = sprite.Bottom + Gap;
        window.Top = above >= area.Top || below + window.Height > area.Bottom
            ? Math.Max(area.Top, above) : below;
        KeepMenuClear(window, area);
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

    // Space the shortcuts evenly across the semicircle.
    internal static Point[] Arrange(Rect sprite, Rect area, int count, double orbSize = DefaultOrbSize,
        double spread = 1, double offsetFromPet = DefaultSphereOffset)
    {
        if (count == 0) return [];
        double halfOrb = orbSize / 2;
        Point center = new(sprite.Left + sprite.Width / 2, sprite.Top + sprite.Height / 2);
        double fullStep = count <= 1 ? 0 : Math.PI / (count - 1);
        double spacingRadius = count <= 1 ? 0 : (orbSize + 5) / (2 * Math.Sin(fullStep / 2));
        double clearance = halfOrb + offsetFromPet;
        double spreadRadius = Math.Max(spacingRadius,
            Math.Sqrt(sprite.Width * sprite.Width + sprite.Height * sprite.Height) / 2 + halfOrb + DefaultSphereOffset);
        double minStep = 2 * Math.Asin(Math.Min(1, (orbSize + 5) / (2 * spreadRadius)));
        double step = Math.Min(fullStep, minStep + (fullStep - minStep) * spread);
        spacingRadius = count <= 1 ? 0 : (orbSize + 5) / (2 * Math.Sin(step / 2));
        var offsets = new double[count];
        for (int index = 0; index < count; index++)
            offsets[index] = (index - (count - 1) / 2d) * step;
        var candidate = new Point[count];
        var best = new Point[count];
        double bestCost = double.PositiveInfinity;

        for (int rotation = -12; rotation <= 12; rotation++)
        {
            double direction = -Math.PI / 2 + rotation * Math.PI / 24;
            double left = 0, right = 0, top = 0, bottom = 0;
            double preferredRadius = spacingRadius;
            for (int index = 0; index < count; index++)
            {
                double angle = direction + offsets[index];
                var unit = new Point(Math.Cos(angle), Math.Sin(angle));
                candidate[index] = unit;
                left = index == 0 ? unit.X : Math.Min(left, unit.X);
                right = index == 0 ? unit.X : Math.Max(right, unit.X);
                top = index == 0 ? unit.Y : Math.Min(top, unit.Y);
                bottom = index == 0 ? unit.Y : Math.Max(bottom, unit.Y);
                double horizontal = (sprite.Width / 2 + clearance) / Math.Max(.001, Math.Abs(unit.X));
                double vertical = (sprite.Height / 2 + clearance) / Math.Max(.001, Math.Abs(unit.Y));
                preferredRadius = Math.Max(preferredRadius, Math.Min(horizontal, vertical));
            }

            double radius = Math.Max(0, Math.Min(preferredRadius, Math.Min(
                (area.Width - orbSize) / Math.Max(right - left, .001),
                (area.Height - orbSize) / Math.Max(bottom - top, .001))));
            // At the top of the monitor, also try fitting the arc in the free strip beside the pet.
            Score(area.Left, area.Right, radius);
            if (sprite.Top - area.Top < radius + halfOrb)
            {
                ScoreSide(area.Left, sprite.Left);
                ScoreSide(sprite.Right, area.Right);
            }

            void ScoreSide(double start, double end)
            {
                double sideRadius = Math.Min(radius, (end - start - orbSize) / Math.Max(right - left, .001));
                if (sideRadius >= spacingRadius) Score(start, end, sideRadius);
            }

            void Score(double start, double end, double actualRadius)
            {
                double minX = start + halfOrb - left * actualRadius;
                double minY = area.Top + halfOrb - top * actualRadius;
                Point anchor = new(
                    Compatibility.Clamp(center.X, minX, Math.Max(minX, end - halfOrb - right * actualRadius)),
                    Compatibility.Clamp(center.Y, minY, Math.Max(minY, area.Bottom - halfOrb - bottom * actualRadius)));
                double cost = (anchor - center).LengthSquared + rotation * rotation * 40
                    + Math.Pow(preferredRadius - actualRadius, 2) * 4;

                for (int index = 0; index < count; index++)
                {
                    Point unit = candidate[index];
                    Point orb = new(anchor.X + unit.X * actualRadius, anchor.Y + unit.Y * actualRadius);
                    double dx = Math.Max(sprite.Left - orb.X, orb.X - sprite.Right);
                    double dy = Math.Max(sprite.Top - orb.Y, orb.Y - sprite.Bottom);
                    double overlap = Math.Max(0, halfOrb - Math.Max(dx, dy));
                    if (overlap > .001) cost += 100000 + overlap * overlap * 24;
                    cost += Math.Pow(Math.Max(0, clearance - Math.Max(dx, dy)), 2) * 2;
                    if (orb.Y > center.Y + .001 && dx < halfOrb - .001)
                        cost += 100000 + Math.Pow(orb.Y - center.Y, 2) * 5;
                }

                if (cost >= bestCost) return;
                bestCost = cost;
                for (int index = 0; index < count; index++)
                    best[index] = new Point(anchor.X + candidate[index].X * actualRadius - halfOrb,
                        anchor.Y + candidate[index].Y * actualRadius - halfOrb);
            }
            if (bestCost == 0) break;
        }
        return best;
    }

    private Rect PetBounds()
    {
        double width = pet.Width;
        if (pet.Content is Image { Source: ImageSource source } && source.Width > 0 && source.Height > 0)
            width *= Math.Min(1, source.Width / source.Height);
        return new Rect(pet.Left + (pet.Width - width) / 2, pet.Top, width, pet.Height);
    }

    internal Rect WorkArea()
    {
        var handle = new WindowInteropHelper(pet).Handle;
        if (handle == IntPtr.Zero) return SystemParameters.WorkArea;
        IntPtr monitor = MonitorFromWindow(handle, 2);
        if (!TryWorkArea(monitor, out var work)) return SystemParameters.WorkArea;
        Point top = pet.PointFromScreen(new Point(work.Left, work.Top));
        Point bottom = pet.PointFromScreen(new Point(work.Right, work.Bottom));
        return new Rect(new Point(pet.Left + top.X, pet.Top + top.Y), new Size(bottom.X - top.X, bottom.Y - top.Y));
    }

    internal static bool ConstrainDrag(IntPtr window, IntPtr moveRect)
    {
        IntPtr monitor = GetCursorPos(out var cursor) ? MonitorFromPoint(cursor, 2) : MonitorFromWindow(window, 2);
        if (!TryWorkArea(monitor, out var work)) return false;
        var frame = Marshal.PtrToStructure<NativeRect>(moveRect);
        var fitted = ClampPetFrame(new Rect(frame.Left, frame.Top, frame.Right - frame.Left, frame.Bottom - frame.Top),
            new Rect(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top));
        if (frame.Left == fitted.Left && frame.Top == fitted.Top) return false;
        frame.Right += (int)fitted.Left - frame.Left;
        frame.Bottom += (int)fitted.Top - frame.Top;
        frame.Left = (int)fitted.Left;
        frame.Top = (int)fitted.Top;
        Marshal.StructureToPtr(frame, moveRect, false);
        return true;
    }

    internal static Rect ClampPetFrame(Rect frame, Rect area) =>
        new(Compatibility.Clamp(frame.Left, area.Left, Math.Max(area.Left, area.Right - frame.Width)),
            Compatibility.Clamp(frame.Top, area.Top, Math.Max(area.Top, area.Bottom - frame.Height)), frame.Width, frame.Height);

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
                if (chat is { IsVisible: true } && !IsHit(chat, hit)) chat.Dismiss();
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

    private void CloseMenu()
    {
        var openMenu = menu;
        menu = null;
        menuTitle = null;
        notesCheck = null;
        openMenu?.Close();
        StopWatchingWhenIdle();
    }

    private void HideSpheres()
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
        return new Window
        {
            Title = activate ? "Меню питомца" : "Быстрое действие Спутника",
            Width = width, Height = height, ShowActivated = activate, Topmost = true, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true, Background = Brushes.Transparent, Content = content,
        };
    }

    private static ControlTemplate OrbTemplate(double size)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Fill";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(size / 2));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetResourceReference(Border.BorderBrushProperty, "ThemeOrbRim");
        border.SetResourceReference(Border.BackgroundProperty, "ThemeOrb");
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        foreach (DependencyProperty property in new[] { UIElement.IsMouseOverProperty, UIElement.IsKeyboardFocusedProperty })
        {
            var trigger = new Trigger { Property = property, Value = true };
            trigger.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("ThemeOrbRimFocus"), "Fill"));
            template.Triggers.Add(trigger);
        }
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
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr handle, ref MonitorInfo info);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr handle, int index, int value);
}
