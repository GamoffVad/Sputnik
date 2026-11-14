using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Sputnik;

internal static class StickyNoteContent
{
    internal sealed record BlockData(string Kind, InlineData[]? Inlines = null, BlockData[][]? Items = null, bool Numbered = false);
    internal sealed record InlineData(string Kind, string? Text = null, bool Bold = false, bool Italic = false,
        bool Underline = false, bool Strike = false, bool Checked = false, byte[]? Png = null);

    public static BlockData[] Read(BlockCollection blocks) => blocks.SelectMany<Block, BlockData>(block => block switch
    {
        Paragraph paragraph => [new("text", ReadInlines(paragraph.Inlines).ToArray())],
        List list => [new("list", Items: list.ListItems.Select(item => Read(item.Blocks)).ToArray(),
            Numbered: list.MarkerStyle == TextMarkerStyle.Decimal)],
        Section section => Read(section.Blocks),
        _ => [],
    }).ToArray();

    private static IEnumerable<InlineData> ReadInlines(InlineCollection inlines)
    {
        foreach (Inline inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    yield return new("run", run.Text, run.FontWeight >= FontWeights.Bold,
                        run.FontStyle == FontStyles.Italic,
                        run.TextDecorations.Any(d => d.Location == TextDecorationLocation.Underline),
                        run.TextDecorations.Any(d => d.Location == TextDecorationLocation.Strikethrough));
                    break;
                case Span span:
                    foreach (var child in ReadInlines(span.Inlines)) yield return child;
                    break;
                case LineBreak:
                    yield return new("line");
                    break;
                case InlineUIContainer { Child: CheckBox check }:
                    yield return new("check", Checked: check.IsChecked == true);
                    break;
                case InlineUIContainer { Child: Image { Tag: byte[] png } }:
                    yield return new("image", Png: png);
                    break;
            }
        }
    }

    public static void Write(BlockCollection blocks, IEnumerable<BlockData> content, Func<bool, CheckBox> makeCheck)
    {
        foreach (var block in content)
        {
            if (block.Kind == "list" && block.Items is not null)
            {
                var list = new List { MarkerStyle = block.Numbered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc, Margin = new Thickness(0), Padding = new Thickness(23, 0, 0, 0) };
                foreach (var item in block.Items)
                {
                    var listItem = new ListItem();
                    Write(listItem.Blocks, item, makeCheck);
                    list.ListItems.Add(listItem);
                }
                blocks.Add(list);
                continue;
            }

            var paragraph = new Paragraph();
            foreach (var inline in block.Inlines ?? [])
            {
                switch (inline.Kind)
                {
                    case "line": paragraph.Inlines.Add(new LineBreak()); break;
                    case "check": paragraph.Inlines.Add(new InlineUIContainer(makeCheck(inline.Checked)) { BaselineAlignment = BaselineAlignment.Center }); break;
                    case "image" when inline.Png is not null: paragraph.Inlines.Add(ImageInline(inline.Png)); break;
                    case "run":
                        var run = new Run(inline.Text ?? "")
                        {
                            FontWeight = inline.Bold ? FontWeights.Bold : FontWeights.Normal,
                            FontStyle = inline.Italic ? FontStyles.Italic : FontStyles.Normal,
                        };
                        if (inline.Underline || inline.Strike)
                        {
                            var decorations = new TextDecorationCollection();
                            if (inline.Underline) decorations.Add(TextDecorations.Underline);
                            if (inline.Strike) decorations.Add(TextDecorations.Strikethrough);
                            run.TextDecorations = decorations;
                        }
                        paragraph.Inlines.Add(run);
                        break;
                }
            }
            blocks.Add(paragraph);
        }
    }

    public static byte[] ToPng(BitmapSource source)
    {
        int longest = Math.Max(source.PixelWidth, source.PixelHeight);
        if (longest > 1200)
        {
            double scale = 1200d / longest;
            source = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var buffer = new MemoryStream();
        encoder.Save(buffer);
        return buffer.ToArray();
    }

    public static InlineUIContainer ImageInline(byte[] png, TextPointer? position = null)
    {
        using var stream = new MemoryStream(png, writable: false);
        var bitmap = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        double scale = Math.Min(1, Math.Min(220d / bitmap.PixelWidth, 145d / bitmap.PixelHeight));
        var image = new Image { Source = bitmap, Tag = png, Width = bitmap.PixelWidth * scale, Height = bitmap.PixelHeight * scale, Stretch = Stretch.Uniform };
        System.Windows.Automation.AutomationProperties.SetName(image, "Изображение в заметке");
        var inline = position is null ? new InlineUIContainer(image) : new InlineUIContainer(image, position);
        inline.BaselineAlignment = BaselineAlignment.Center;
        return inline;
    }
}
