using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Simple-AI-Tag-Tool: everything the info pane shows about one image.
    /// The cheap facts (file system, image header) are filled synchronously by
    /// <see cref="Collect"/>; the expensive ones (unique colours, embedded
    /// metadata, comfydbg) are filled later by background jobs, which check
    /// <see cref="Path"/> against the current image before touching the pane.
    /// </summary>
    public class DazzleImageInfo
    {
        public string Path;
        public string Folder => System.IO.Path.GetDirectoryName(Path);
        public string FileName => System.IO.Path.GetFileName(Path);

        // file system
        public long SizeBytes;
        public DateTime Created, Modified, Accessed;
        public FileAttributes Attributes;
        public bool FileMissing;

        // image header
        public string Format = "?";      // "PNG", "JPEG", ...
        public string Compression = "?"; // "Deflate", "DCT", ...
        public int Width, Height;
        public int BitsPerPixel;
        public float DpiX, DpiY;
        public bool DpiAssumed;          // no DPI in the file: 96 assumed for print size
        public TimeSpan LoadTime;
        public bool LoadedFromCache;
        public bool DecodeFailed;        // the file exists but no Image could be made from it

        // dataset
        public int Position, Total;     // 1-based position among the visible rows
        public int SelectedCount = 1;

        // filled later
        public long? UniqueColors;      // null = not counted yet
        public string UniqueColorsNote; // "counting...", "not counted (image too large)"

        public List<(string Group, string Key, string Value)> Extracted = new List<(string, string, string)>();
        public string EmbeddedPrompt, EmbeddedNegative, EmbeddedWorkflowJson;
        public string ExtractedNote;    // "reading...", or an error

        public string ComfyPromptsJson; // raw comfydbg prompt --json
        public string ComfyFingerprint; // raw comfydbg detect text
        public string ComfyNote;        // "running comfydbg...", "no embedded workflow", "comfydbg not installed", ...

        public static DazzleImageInfo Collect(string path, Image img, TimeSpan loadTime, bool fromCache, int position, int total, int selectedCount)
        {
            var info = new DazzleImageInfo { Path = path, LoadTime = loadTime, LoadedFromCache = fromCache, Position = position, Total = total, SelectedCount = selectedCount };
            try
            {
                var fi = new FileInfo(path);
                info.SizeBytes = fi.Length;
                info.Created = fi.CreationTime;
                info.Modified = fi.LastWriteTime;
                info.Accessed = fi.LastAccessTime;
                info.Attributes = fi.Attributes;
            }
            catch (Exception)
            {
                info.FileMissing = true;
            }
            if (img != null)
            {
                try
                {
                    info.Width = img.Width;
                    info.Height = img.Height;
                    info.BitsPerPixel = Image.GetPixelFormatSize(img.PixelFormat);
                    info.DpiX = img.HorizontalResolution;
                    info.DpiY = img.VerticalResolution;
                    (info.Format, info.Compression) = FormatOf(img.RawFormat, path);
                }
                catch (ArgumentException)
                {
                    // image disposed under us (cache off): leave the header fields blank
                }
            }
            // System.Drawing reports the SCREEN's dpi (96, 144 on a scaled display...) for a file
            // that carries none, so its value proves nothing. Print size starts as "assumed 96 dpi";
            // the metadata pass replaces it with a resolution actually stored in the file (EXIF
            // X/YResolution or PNG pHYs).
            info.DpiX = info.DpiY = 96f;
            info.DpiAssumed = true;
            return info;
        }

        private static (string, string) FormatOf(ImageFormat raw, string path)
        {
            if (raw.Equals(ImageFormat.Png)) return ("PNG", "Deflate");
            if (raw.Equals(ImageFormat.Jpeg)) return ("JPEG", "DCT");
            if (raw.Equals(ImageFormat.Gif)) return ("GIF", "LZW");
            if (raw.Equals(ImageFormat.Bmp)) return ("BMP", "none");
            if (raw.Equals(ImageFormat.Tiff)) return ("TIFF", "varies");
            if (raw.Equals(ImageFormat.MemoryBmp))
            {
                // decoded by WebPWrapper or ScreenLister: go by the extension
                string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".webp") return ("WebP", "VP8 / VP8L");
                if (Extensions.VideoExtensions.Contains(ext)) return (ext.TrimStart('.').ToUpperInvariant() + " (video frame)", "video codec");
                return (ext.TrimStart('.').ToUpperInvariant(), "?");
            }
            return (raw.ToString(), "?");
        }

        public string Aspect
        {
            get
            {
                if (Width <= 0 || Height <= 0) return "";
                int g = Gcd(Width, Height);
                string exact = (Width / g) + ":" + (Height / g);
                string near = NearestCommonRatio(Width, Height);
                return near != null && near != exact ? exact + " (about " + near + ")" : exact;
            }
        }

        private static readonly (int w, int h)[] CommonRatios = { (1, 1), (4, 3), (3, 4), (3, 2), (2, 3), (16, 9), (9, 16), (5, 4), (4, 5), (21, 9), (9, 21), (7, 9), (9, 7) };

        private static string NearestCommonRatio(int w, int h)
        {
            double r = (double)w / h;
            foreach (var c in CommonRatios)
                if (Math.Abs(r - (double)c.w / c.h) / ((double)c.w / c.h) <= 0.01)
                    return c.w + ":" + c.h;
            return null;
        }

        private static int Gcd(int a, int b) { while (b != 0) { int t = a % b; a = b; b = t; } return a; }

        public string PrintSize
        {
            get
            {
                if (Width <= 0 || Height <= 0) return "";
                double wIn = Width / DpiX, hIn = Height / DpiY;
                string s = string.Format(CultureInfo.InvariantCulture, "{0:0.00} x {1:0.00} in;  {2:0.00} x {3:0.00} cm", wIn, hIn, wIn * 2.54, hIn * 2.54);
                return DpiAssumed ? s + "  (assumed 96 dpi)" : s + "  (" + DpiX.ToString("0.#", CultureInfo.InvariantCulture) + " dpi)";
            }
        }

        public string ColorDepth
        {
            get
            {
                if (BitsPerPixel <= 0) return "";
                string count = BitsPerPixel >= 24 ? "16.7 million" : BitsPerPixel == 16 ? "65,536" : BitsPerPixel == 8 ? "256" : BitsPerPixel == 4 ? "16" : BitsPerPixel == 1 ? "2" : "";
                return count + " (" + BitsPerPixel + " BPP)";
            }
        }

        public string AttributesText
        {
            get
            {
                var parts = new List<string>();
                if (Attributes.HasFlag(FileAttributes.ReadOnly)) parts.Add("read-only");
                if (Attributes.HasFlag(FileAttributes.Hidden)) parts.Add("hidden");
                if (Attributes.HasFlag(FileAttributes.System)) parts.Add("system");
                if (Attributes.HasFlag(FileAttributes.Archive)) parts.Add("archive");
                if (Attributes.HasFlag(FileAttributes.Compressed)) parts.Add("compressed");
                if (Attributes.HasFlag(FileAttributes.Encrypted)) parts.Add("encrypted");
                if (Attributes.HasFlag(FileAttributes.ReparsePoint)) parts.Add("link");
                return parts.Count == 0 ? "normal" : string.Join(", ", parts);
            }
        }

        public static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " bytes";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.00", CultureInfo.InvariantCulture) + " KB (" + bytes.ToString("N0") + " bytes)";
            return (bytes / 1048576.0).ToString("0.00", CultureInfo.InvariantCulture) + " MB (" + bytes.ToString("N0") + " bytes)";
        }

        /// <summary>
        /// Count distinct colours. Runs on a background thread over its own copy of the
        /// pixels, so the preview's Image is only read while being copied.
        /// </summary>
        public const long MaxPixelsToCount = 50L * 1000 * 1000;

        public static long? CountUniqueColors(Bitmap source, out string note)
        {
            note = null;
            long pixels = (long)source.Width * source.Height;
            if (pixels > MaxPixelsToCount)
            {
                note = "not counted (image too large)";
                return null;
            }
            // one pass over a 32bpp copy keeps the loop simple for every source format
            using (var bmp = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                    g.DrawImageUnscaled(source, 0, 0);
                var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var seen = new HashSet<int>();
                    int[] row = new int[bmp.Width];
                    for (int y = 0; y < bmp.Height; y++)
                    {
                        Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                        for (int x = 0; x < row.Length; x++)
                            seen.Add(row[x]);
                    }
                    return seen.Count;
                }
                finally
                {
                    bmp.UnlockBits(data);
                }
            }
        }
    }
}
