using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

internal static class IconBuilder
{
    private static readonly int[] IconSizes = { 16, 24, 32, 48, 64, 128, 256 };

    private static int Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("Usage: IconBuilder <source.png> <clean.png> <output.ico>");
            return 1;
        }

        using (Bitmap original = new Bitmap(args[0]))
        using (Bitmap transparent = MakeOuterBackgroundTransparent(original))
        using (Bitmap square = CropToSquare(transparent))
        using (Bitmap clean = Resize(square, 1024))
        {
            clean.Save(args[1], ImageFormat.Png);
            WriteIcon(square, args[2]);
        }

        Console.WriteLine("Generated " + args[1]);
        Console.WriteLine("Generated " + args[2]);
        return 0;
    }

    private static unsafe Bitmap MakeOuterBackgroundTransparent(Bitmap original)
    {
        Bitmap image = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(image))
        {
            graphics.DrawImageUnscaled(original, 0, 0);
        }

        int width = image.Width;
        int height = image.Height;
        bool[] background = new bool[width * height];
        int[] queue = new int[width * height];
        int head = 0;
        int tail = 0;

        BitmapData data = image.LockBits(new Rectangle(0, 0, width, height),
            ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            for (int x = 0; x < width; x++)
            {
                EnqueueIfBackground(data, width, height, x, 0, background, queue, ref tail);
                EnqueueIfBackground(data, width, height, x, height - 1, background, queue, ref tail);
            }

            for (int y = 1; y < height - 1; y++)
            {
                EnqueueIfBackground(data, width, height, 0, y, background, queue, ref tail);
                EnqueueIfBackground(data, width, height, width - 1, y, background, queue, ref tail);
            }

            while (head < tail)
            {
                int index = queue[head++];
                int x = index % width;
                int y = index / width;
                if (x > 0) EnqueueIfBackground(data, width, height, x - 1, y, background, queue, ref tail);
                if (x + 1 < width) EnqueueIfBackground(data, width, height, x + 1, y, background, queue, ref tail);
                if (y > 0) EnqueueIfBackground(data, width, height, x, y - 1, background, queue, ref tail);
                if (y + 1 < height) EnqueueIfBackground(data, width, height, x, y + 1, background, queue, ref tail);
            }

            for (int y = 0; y < height; y++)
            {
                byte* row = (byte*)data.Scan0 + y * data.Stride;
                for (int x = 0; x < width; x++)
                {
                    row[x * 4 + 3] = background[y * width + x] ? (byte)0 : (byte)255;
                }
            }
        }
        finally
        {
            image.UnlockBits(data);
        }

        return image;
    }

    private static unsafe void EnqueueIfBackground(BitmapData data, int width, int height,
        int x, int y, bool[] background, int[] queue, ref int tail)
    {
        int index = y * width + x;
        if (background[index]) return;

        byte* pixel = (byte*)data.Scan0 + y * data.Stride + x * 4;
        int blue = pixel[0];
        int green = pixel[1];
        int red = pixel[2];
        int maximum = Math.Max(red, Math.Max(green, blue));
        int minimum = Math.Min(red, Math.Min(green, blue));

        // The generated checkerboard is connected to the canvas edge and consists
        // only of bright near-neutral pixels. The white prompt is enclosed by the
        // dark icon body, so the edge flood cannot reach it.
        if (maximum < 85 || maximum - minimum > 18) return;

        background[index] = true;
        queue[tail++] = index;
    }

    private static Bitmap CropToSquare(Bitmap image)
    {
        int left = image.Width;
        int top = image.Height;
        int right = -1;
        int bottom = -1;

        BitmapData data = image.LockBits(new Rectangle(0, 0, image.Width, image.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                for (int y = 0; y < image.Height; y++)
                {
                    byte* row = (byte*)data.Scan0 + y * data.Stride;
                    for (int x = 0; x < image.Width; x++)
                    {
                        if (row[x * 4 + 3] == 0) continue;
                        if (x < left) left = x;
                        if (x > right) right = x;
                        if (y < top) top = y;
                        if (y > bottom) bottom = y;
                    }
                }
            }
        }
        finally
        {
            image.UnlockBits(data);
        }

        if (right < left || bottom < top) throw new InvalidOperationException("No icon artwork was found.");
        int contentWidth = right - left + 1;
        int contentHeight = bottom - top + 1;
        int padding = Math.Max(8, Math.Max(contentWidth, contentHeight) / 50);
        int side = Math.Max(contentWidth, contentHeight) + padding * 2;
        Bitmap square = new Bitmap(side, side, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(square))
        {
            graphics.Clear(Color.Transparent);
            int x = (side - contentWidth) / 2;
            int y = (side - contentHeight) / 2;
            graphics.DrawImage(image, new Rectangle(x, y, contentWidth, contentHeight),
                new Rectangle(left, top, contentWidth, contentHeight), GraphicsUnit.Pixel);
        }

        return square;
    }

    private static Bitmap Resize(Bitmap source, int size)
    {
        Bitmap result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(result))
        {
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(source, new Rectangle(0, 0, size, size));
        }
        return result;
    }

    private static void WriteIcon(Bitmap source, string path)
    {
        byte[][] images = new byte[IconSizes.Length][];
        for (int i = 0; i < IconSizes.Length; i++)
        {
            using (Bitmap resized = Resize(source, IconSizes[i]))
            using (MemoryStream stream = new MemoryStream())
            {
                resized.Save(stream, ImageFormat.Png);
                images[i] = stream.ToArray();
            }
        }

        using (FileStream file = File.Create(path))
        using (BinaryWriter writer = new BinaryWriter(file))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)IconSizes.Length);
            int offset = 6 + IconSizes.Length * 16;
            for (int i = 0; i < IconSizes.Length; i++)
            {
                int size = IconSizes[i];
                writer.Write((byte)(size >= 256 ? 0 : size));
                writer.Write((byte)(size >= 256 ? 0 : size));
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write(images[i].Length);
                writer.Write(offset);
                offset += images[i].Length;
            }

            for (int i = 0; i < images.Length; i++) writer.Write(images[i]);
        }
    }
}
