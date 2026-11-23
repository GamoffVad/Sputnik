using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Sputnik;

public partial class TimePicker : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(TimeSpan), typeof(TimePicker),
        new FrameworkPropertyMetadata(TimeSpan.Zero, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (sender, _) => ((TimePicker)sender).Refresh()),
        value => value is TimeSpan time && time >= TimeSpan.Zero && time < TimeSpan.FromDays(1));

    private bool updating;
    private TextBox active = null!;
    public event EventHandler? ValueChanged;

    public TimeSpan Value
    {
        get => (TimeSpan)GetValue(ValueProperty);
        set
        {
            // A shortcut can reapply the last valid value while a segment is empty.
            if (Value == value) Refresh();
            else SetValue(ValueProperty, value);
        }
    }

    public TimePicker()
    {
        InitializeComponent();
        active = Hours;
        foreach (var segment in new[] { Hours, Minutes })
        {
            segment.GotKeyboardFocus += (_, _) => { active = segment; segment.SelectAll(); };
            segment.PreviewMouseLeftButtonDown += (_, e) =>
            {
                if (segment.IsKeyboardFocusWithin) return;
                segment.Focus();
                e.Handled = true;
            };
            segment.PreviewTextInput += (_, e) => e.Handled = !Accepts(segment, e.Text);
            DataObject.AddPastingHandler(segment, (_, e) =>
            {
                if (e.DataObject.GetData(DataFormats.UnicodeText) is not string text || !Accepts(segment, text))
                    e.CancelCommand();
            });
            segment.TextChanged += (_, _) =>
            {
                if (updating || !TryRead(out var time)) return;
                updating = true;
                try { SetCurrentValue(ValueProperty, time); }
                finally { updating = false; }
                ValueChanged?.Invoke(this, EventArgs.Empty);
            };
            segment.LostKeyboardFocus += (_, _) =>
            {
                if (Read(segment, out int number)) segment.Text = number.ToString("00", CultureInfo.InvariantCulture);
            };
            segment.PreviewKeyDown += SegmentKeyDown;
            segment.PreviewMouseWheel += (_, e) =>
            {
                if (!segment.IsKeyboardFocusWithin) return;
                Adjust(segment, e.Delta > 0 ? 1 : -1);
                e.Handled = true;
            };
        }
        Refresh();
    }

    public bool TryGetValue(out TimeSpan time)
    {
        if (!TryRead(out time)) return false;
        SetCurrentValue(ValueProperty, time);
        Refresh();
        return true;
    }

    private bool Read(TextBox segment, out int number) =>
        int.TryParse(segment.Text, NumberStyles.None, CultureInfo.InvariantCulture, out number) &&
        number >= 0 && number < (segment == Hours ? 24 : 60);

    private bool TryRead(out TimeSpan time)
    {
        time = default;
        if (!Read(Hours, out int hours) || !Read(Minutes, out int minutes)) return false;
        time = new TimeSpan(hours, minutes, 0);
        return true;
    }

    private bool Accepts(TextBox segment, string input)
    {
        string next = segment.Text.Remove(segment.SelectionStart, segment.SelectionLength).Insert(segment.SelectionStart, input);
        return next.Length is > 0 and <= 2 && next.All(c => c is >= '0' and <= '9') &&
            int.TryParse(next, out int number) && number < (segment == Hours ? 24 : 60);
    }

    private void Refresh()
    {
        if (updating || Hours is null) return;
        updating = true;
        try
        {
            Hours.Text = Value.Hours.ToString("00", CultureInfo.InvariantCulture);
            Minutes.Text = Value.Minutes.ToString("00", CultureInfo.InvariantCulture);
        }
        finally { updating = false; }
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Adjust(TextBox segment, int delta)
    {
        int limit = segment == Hours ? 24 : 60;
        int number = Read(segment, out int current) ? current : (segment == Hours ? Value.Hours : Value.Minutes);
        segment.Text = ((number + delta + limit) % limit).ToString("00", CultureInfo.InvariantCulture);
        segment.Focus();
        segment.SelectAll();
    }

    private void SegmentKeyDown(object sender, KeyEventArgs e)
    {
        var segment = (TextBox)sender;
        switch (e.Key)
        {
            case Key.Up: Adjust(segment, 1); break;
            case Key.Down: Adjust(segment, -1); break;
            case Key.Right when segment == Hours && segment.SelectionStart + segment.SelectionLength == segment.Text.Length:
            case Key.OemSemicolon when segment == Hours:
                Minutes.Focus(); Minutes.SelectAll(); break;
            case Key.Left when segment == Minutes && segment.SelectionStart == 0:
                Hours.Focus(); Hours.SelectAll(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void Increase(object sender, RoutedEventArgs e) => Adjust(active, 1);
    private void Decrease(object sender, RoutedEventArgs e) => Adjust(active, -1);
}
