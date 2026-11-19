using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

partial class Check
{
    static void PhotoSharing(string data)
    {
        var asm=typeof(Sputnik.App).Assembly;
        Type T(string name)=>asm.GetType("Sputnik."+name)!;
        byte[] Png(byte red)
        {
            var pixels=Enumerable.Repeat(new byte[]{red,20,40,255},64*48).SelectMany(p=>p).ToArray();
            var image=BitmapSource.Create(64,48,96,96,PixelFormats.Bgra32,null,pixels,64*4);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream=new MemoryStream();encoder.Save(stream);return stream.ToArray();
        }
        var first=Png(120); var second=Png(230);
        var document=new FlowDocument();var p=new Paragraph(new Run("Подпись & фото"));document.Blocks.Add(p);
        p.Inlines.Add((InlineUIContainer)Invoke(T("StickyNoteContent"),"ImageInline",first,null!));
        var nested=new Span();nested.Inlines.Add((InlineUIContainer)Invoke(T("StickyNoteContent"),"ImageInline",second,null!));
        var item=new ListItem(new Paragraph(nested));document.Blocks.Add(new System.Windows.Documents.List(item));
        var images=(byte[][])Invoke(T("NoteAttachments"),"Images",document);
        Assert(images.Length==2 && images[0].SequenceEqual(first) && images[1].SequenceEqual(second),"Image extraction/order lost original PNG bytes");
        var files=(string[])Invoke(T("NoteAttachments"),"Export",images,Path.Combine(data,"photo export"));
        Assert(File.ReadAllBytes(files[0]).SequenceEqual(first) && File.ReadAllBytes(files[1]).SequenceEqual(second),"Export changed images");
        var multiple=(DataObject)Invoke(T("NoteAttachments"),"PhotoClipboard",new object[]{files});
        Assert(multiple.GetFileDropList().Count==2,"Multiple image clipboard lost files");
        var single=(DataObject)Invoke(T("NoteAttachments"),"PhotoClipboard",new object[]{new[]{files[0]}});
        Assert(single.GetImage().PixelWidth==64 && single.GetImage().PixelHeight==48 && single.GetDataPresent("PNG"),"Image clipboard resized photo or omitted PNG");
        foreach(string flag in new[]{"--","-sendpath"})
        {
            var command=(System.Diagnostics.ProcessStartInfo)Invoke(T("NoteAttachments"),"TelegramCommand",@"C:\Program Files\Telegram Desktop\Telegram.exe",flag,files);
            Assert(!command.UseShellExecute && command.Arguments.StartsWith(flag+" ") && files.All(file=>command.Arguments.Contains("\""+file+"\"")),"Telegram command lost photos or quoting");
        }
        Reject(()=>Invoke(T("NoteAttachments"),"TelegramCommand",@"C:\cmd.exe","--",files),"Wrong executable");
        Reject(()=>Invoke(T("NoteAttachments"),"TelegramCommand",@"C:\Telegram.exe","-quit",files),"Wrong Telegram action");
        p.Inlines.Remove(p.Inlines.FirstInline); // Photo-only notes must remain shareable.
        Assert(((byte[][])Invoke(T("NoteAttachments"),"Images",document)).Length==2,"Photo-only note rejected");
        Console.WriteLine("PASS: Unicode caption, photo-only and nested/list images; original PNG bytes/resolution exported; single/multiple photo clipboard; current/legacy Telegram commands and quoted paths. No messenger launched or messages sent.");
    }
}
