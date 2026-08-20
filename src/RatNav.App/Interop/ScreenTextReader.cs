using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Versioning;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace RatNav.App.Interop;

/// <summary>
/// Reads the text under the mouse pointer.
///
/// <para>This is how RatNav answers "what is this thing for?" without knowing anything about the
/// game's insides. It takes a picture of a patch of the <b>desktop</b> — the same pixels a
/// screenshot tool or a screen reader sees — and runs Windows' own OCR over it. Nothing is read
/// from the game's memory, nothing is injected into it, and the game is not asked anything. It is
/// looking at the screen, which is the same thing the player is doing.</para>
///
/// <para>The OCR engine is the one built into Windows 10 and 11. That matters for an open-source
/// tool: no model to download, no native binaries to ship or sign, no network call, and it works
/// on a machine that has never been online.</para>
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public static class ScreenTextReader
{
    /// <summary>
    /// How large a patch to read, in pixels. A tooltip is a tall panel beside the cursor, and
    /// this is generous enough to catch it wherever it opened without reading half the screen.
    /// </summary>
    private const int Width = 620;
    private const int Height = 460;

    private static readonly OcrEngine? Engine = CreateEngine();

    /// <summary>False when Windows has no OCR language pack, so the UI can say why rather than fail.</summary>
    public static bool Available => Engine is not null;

    /// <summary>
    /// Text lines from a region centred on a point, best-effort. An empty list means nothing
    /// legible was there, which is a normal outcome, not an error.
    /// </summary>
    public static async Task<IReadOnlyList<string>> ReadAroundAsync(int screenX, int screenY)
    {
        if (Engine is null) return [];

        // Biased below and right of the cursor, which is where Windows and the game both prefer to
        // open a tooltip, while still catching one that flipped to avoid a screen edge.
        var region = new Rectangle(screenX - Width / 3, screenY - Height / 4, Width, Height);

        try
        {
            using var bitmap = Capture(region);
            using var software = await ToSoftwareBitmapAsync(bitmap);

            var result = await Engine.RecognizeAsync(software);

            return
            [
                .. result.Lines
                    .Select(line => line.Text.Trim())
                    .Where(text => text.Length > 0)
            ];
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ExternalException or IOException)
        {
            // A capture can fail while the screen is changing, or on a secured window. Reading
            // nothing is the right answer; a crash mid-raid is not.
            return [];
        }
    }

    /// <summary>
    /// Every line of text on one screen, best-effort.
    ///
    /// <para>Whole-screen rather than a region, because the list this exists to read is drawn in a
    /// different place on different maps and resolutions, and guessing wrong means reading
    /// nothing. It costs a fraction of a second on a keypress, which is the right trade for a
    /// thing pressed once a raid.</para>
    /// </summary>
    public static async Task<IReadOnlyList<string>> ReadScreenAsync(int screenX, int screenY)
    {
        if (Engine is null) return [];

        try
        {
            var bounds = System.Windows.Forms.Screen
                .FromPoint(new Point(screenX, screenY)).Bounds;

            using var bitmap = Capture(bounds);
            using var software = await ToSoftwareBitmapAsync(bitmap);

            var result = await Engine.RecognizeAsync(software);

            return
            [
                .. result.Lines
                    .Select(line => line.Text.Trim())
                    .Where(text => text.Length > 0)
            ];
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ExternalException or IOException)
        {
            return [];
        }
    }


    /// <summary>Where the pointer is, in screen pixels.</summary>
    public static (int X, int Y) CursorPosition()
    {
        var point = System.Windows.Forms.Cursor.Position;
        return (point.X, point.Y);
    }

    private static Bitmap Capture(Rectangle region)
    {
        // Clamped so a cursor near an edge still yields a valid rectangle.
        var bounds = System.Windows.Forms.Screen.FromPoint(new Point(region.X, region.Y)).Bounds;

        var x = Math.Max(bounds.Left, Math.Min(region.X, bounds.Right - 1));
        var y = Math.Max(bounds.Top, Math.Min(region.Y, bounds.Bottom - 1));
        var width = Math.Min(region.Width, bounds.Right - x);
        var height = Math.Min(region.Height, bounds.Bottom - y);

        var bitmap = new Bitmap(Math.Max(1, width), Math.Max(1, height), PixelFormat.Format32bppArgb);

        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(x, y, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);

        return bitmap;
    }

    /// <summary>
    /// GDI+ bitmap to the WinRT one the OCR engine wants. Routed through an in-memory PNG because
    /// the two stacks share no pixel buffer type, and a few hundred KB encoded once per keypress
    /// is far cheaper than the marshalling code the direct route would need.
    /// </summary>
    private static async Task<SoftwareBitmap> ToSoftwareBitmapAsync(Bitmap bitmap)
    {
        using var memory = new MemoryStream();
        bitmap.Save(memory, ImageFormat.Png);
        memory.Position = 0;

        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(memory.ToArray().AsBuffer());
        stream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(stream);
        return await decoder.GetSoftwareBitmapAsync();
    }

    private static OcrEngine? CreateEngine()
    {
        try
        {
            return OcrEngine.TryCreateFromUserProfileLanguages();
        }
        catch (Exception ex) when (ex is COMException or TypeLoadException or DllNotFoundException)
        {
            // Older Windows, or a stripped install with no OCR component.
            return null;
        }
    }
}
