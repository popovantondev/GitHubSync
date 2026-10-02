using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class GenerateIcons
{
    [STAThread] private static void Main(string[] args)
    {
        var svg=XDocument.Load(args[0]); string output=args[1]; Directory.CreateDirectory(output);
        for(int frame=0;frame<=12;frame++) {
            string stem=frame==0 ? "sync" : "sync-frame-"+frame;
            int[] sizes=frame==0 ? new[]{16,20,24,32,40,48,64,128,256} : new[]{16,20,24,32,40,48};
            var pixels=sizes.Select(size=>Render(svg,size,frame==0 ? 0 : (frame-1)*30)).ToArray();
            using(var file=File.Create(Path.Combine(output,stem+".ico"))) using(var writer=new BinaryWriter(file)) {
                writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)sizes.Length); int offset=6+16*sizes.Length;
                for(int i=0;i<sizes.Length;i++) {writer.Write((byte)(sizes[i]==256 ? 0 : sizes[i]));writer.Write((byte)(sizes[i]==256 ? 0 : sizes[i]));writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);writer.Write(pixels[i].Length);writer.Write(offset);offset+=pixels[i].Length;}
                foreach(var png in pixels)writer.Write(png);
            }
            if(frame==0) File.WriteAllBytes(Path.Combine(output,"sync.png"),pixels[pixels.Length-1]);
        }
    }
    private static byte[] Render(XDocument svg,int size,int angle)
    {
        var visual=new DrawingVisual();
        using(var draw=visual.RenderOpen()) {
            draw.PushTransform(new ScaleTransform(size/256.0,size/256.0));
            foreach(var element in svg.Root.Elements()) {
                var color=(Color)ColorConverter.ConvertFromString((string)element.Attribute("fill"));var brush=new SolidColorBrush(color);
                if(element.Name.LocalName=="rect") draw.DrawRoundedRectangle(brush,null,new Rect(0,0,256,256),52,52);
                else {
                    var geometry=Geometry.Parse((string)element.Attribute("d"));
                    bool arrows=element.Attribute("data-animate")!=null;
                    if(arrows) draw.PushTransform(new RotateTransform(angle,128,128));
                    draw.DrawGeometry(brush,null,geometry);
                    if(arrows) draw.Pop();
                }
            }
            draw.Pop();
        }
        var bitmap=new RenderTargetBitmap(size,size,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using(var stream=new MemoryStream()){encoder.Save(stream);return stream.ToArray();}
    }
}
