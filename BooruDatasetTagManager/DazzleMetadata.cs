using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Simple-AI-Tag-Tool: embedded metadata for the "Preview Extracted Info" tab.
    /// Uses MetadataExtractor directly for EXIF and other directories, and BDTM's
    /// own Diffusion.IO reader for the generation prompt and raw workflow JSON.
    /// Runs on a background thread; returns rows, never throws.
    /// </summary>
    public static class DazzleMetadata
    {
        public class Result
        {
            public List<(string Group, string Key, string Value)> Rows = new List<(string, string, string)>();
            public string Prompt, NegativePrompt;
            public string PromptGraphJson;   // the API-format graph the resolver walks (PNG "prompt" chunk / WebP EXIF "Prompt:")
            public string WorkflowJson;      // the UI-format graph with node packages and versions (PNG "workflow" / WebP EXIF "Workflow:")
            public bool HasWorkflowChunk => PromptGraphJson != null || WorkflowJson != null;
            public float? DpiX, DpiY;      // from EXIF / pHYs when present
            public string Note;
        }

        private static readonly string[] SkipDirectories = { "File Type", "File", "Huffman", "JpegComment" };

        public static Result Read(string path)
        {
            var r = new Result();
            IReadOnlyList<MetadataExtractor.Directory> dirs;
            try
            {
                dirs = ImageMetadataReader.ReadMetadata(path);
            }
            catch (Exception e)
            {
                r.Note = "could not read metadata: " + e.Message;
                dirs = Array.Empty<MetadataExtractor.Directory>();
                // MetadataExtractor gives up on a PNG with a malformed trailing chunk (seen: a
                // ComfyUI output padded with 2.7 MB of zeros after IDAT). The text chunks sit
                // before the damage, so read them the way Pillow does: stop at the first bad chunk.
                if (Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var (key, body) in ReadPngTextChunks(path))
                    {
                        if (key == "prompt") r.PromptGraphJson = r.PromptGraphJson ?? body;
                        else if (key == "workflow") r.WorkflowJson = r.WorkflowJson ?? body;
                        else r.Rows.Add(("Other text", key, Shorten(body)));
                    }
                    if (r.PromptGraphJson != null || r.WorkflowJson != null)
                        r.Note += " (text chunks recovered directly)";
                }
            }
            foreach (var dir in dirs)
            {
                if (SkipDirectories.Contains(dir.Name))
                    continue;
                string group = dir.Name.StartsWith("Exif") || dir.Name == "GPS" || dir.Name == "Interoperability" ? "EXIF" : dir.Name;
                foreach (var tag in dir.Tags)
                {
                    string value = tag.Description ?? "";
                    if (value.Length == 0)
                        continue;
                    // PNG text chunks arrive as "key: value"; the big generation blobs get their own handling
                    if (dir.Name.StartsWith("PNG-") && tag.Name == "Textual Data")
                    {
                        int colon = value.IndexOf(':');
                        string key = colon > 0 ? value.Substring(0, colon).Trim() : tag.Name;
                        string body = colon > 0 ? value.Substring(colon + 1).Trim() : value;
                        if (key == "workflow" || key == "prompt")
                        {
                            if (key == "prompt") r.PromptGraphJson = body; else r.WorkflowJson = body;
                            r.Rows.Add(("Other text", key, body.Length + " characters of JSON (see Workflow)"));
                            continue;
                        }
                        r.Rows.Add(("Other text", key, Shorten(body)));
                        continue;
                    }
                    if (dir is ExifIfd0Directory || dir is ExifSubIfdDirectory)
                    {
                        // unit 3 = centimetres, otherwise inches
                        float unit = dir.TryGetInt32(ExifDirectoryBase.TagResolutionUnit, out var ru) && ru == 3 ? 2.54f : 1f;
                        if (tag.Type == ExifDirectoryBase.TagXResolution && dir.TryGetRational(tag.Type, out var xr) && xr.ToDouble() > 0) r.DpiX = (float)xr.ToDouble() * unit;
                        if (tag.Type == ExifDirectoryBase.TagYResolution && dir.TryGetRational(tag.Type, out var yr) && yr.ToDouble() > 0) r.DpiY = (float)yr.ToDouble() * unit;
                    }
                    if (dir.Name == "PNG-pHYs")
                    {
                        // pixels per unit, unit 1 = metres; 0 = unknown unit (aspect only), which gives no dpi
                        if (tag.Name == "Pixels Per Unit X" && int.TryParse(value.Split(' ')[0], out int px) && PngUnitIsMetres(dir)) r.DpiX = px * 0.0254f;
                        if (tag.Name == "Pixels Per Unit Y" && int.TryParse(value.Split(' ')[0], out int py) && PngUnitIsMetres(dir)) r.DpiY = py * 0.0254f;
                    }
                    r.Rows.Add((group, tag.Name, Shorten(value)));
                }
            }
            // ComfyUI's WebP writer puts the graphs in a TIFF EXIF block: "Workflow:{...}" in
            // ImageDescription (0x010e) and "Prompt:{...}" in Make (0x010f). BDTM's own reader only
            // tries the A1111 layout on WebP, so without this every WebP showed "no workflow"
            // (139 of 139 local WebP outputs, port-check 2026-10-01).
            foreach (var dir in dirs.OfType<ExifIfd0Directory>())
            {
                r.WorkflowJson = r.WorkflowJson ?? JsonAfterMarker(dir.GetDescription(ExifDirectoryBase.TagImageDescription), "Workflow:");
                r.PromptGraphJson = r.PromptGraphJson ?? JsonAfterMarker(dir.GetDescription(ExifDirectoryBase.TagMake), "Prompt:");
            }

            try
            {
                var fp = Diffusion.IO.Metadata.ReadFromFile(path);
                if (fp != null && !fp.NoMetadata && !fp.HasError)
                {
                    // the scanner's fallbacks (stealth-PNG alpha, a sidecar .txt) can yield whitespace: that is no prompt
                    r.Prompt = string.IsNullOrWhiteSpace(fp.Prompt) ? null : fp.Prompt.Trim();
                    r.NegativePrompt = string.IsNullOrWhiteSpace(fp.NegativePrompt) ? null : fp.NegativePrompt.Trim();
                    // fp.Workflow is the API-format "prompt" chunk, despite its name (Metadata.cs ReadComfyUIParameters)
                    r.PromptGraphJson = r.PromptGraphJson ?? (string.IsNullOrEmpty(fp.Workflow) ? null : fp.Workflow);
                    void Add(string k, object v) { if (v != null && v.ToString().Length > 0 && v.ToString() != "0") r.Rows.Add(("Generation", k, v.ToString())); }
                    Add("Model", fp.Model);
                    Add("Model hash", fp.ModelHash);
                    Add("Sampler", fp.Sampler);
                    Add("Steps", fp.Steps);
                    Add("CFG scale", fp.CFGScale);
                    Add("Seed", fp.Seed);
                    Add("Clip skip", fp.ClipSkip);
                    Add("Other parameters", fp.OtherParameters);
                }
            }
            catch (Exception e)
            {
                r.Rows.Add(("Generation", "error", e.Message));
            }
            return r;
        }

        private static bool PngUnitIsMetres(MetadataExtractor.Directory dir)
        {
            var unit = dir.Tags.FirstOrDefault(t => t.Name == "Unit Specifier")?.Description ?? "";
            return unit.StartsWith("Metre", StringComparison.OrdinalIgnoreCase) || unit.StartsWith("Meter", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>tEXt / iTXt (uncompressed) chunks of a PNG, in file order, stopping at the first chunk that is not a PNG chunk.</summary>
        private static List<(string key, string body)> ReadPngTextChunks(string path)
        {
            var found = new List<(string, string)>();
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var br = new BinaryReader(fs))
                {
                    if (fs.Length < 8) return found;
                    fs.Position = 8; // signature
                    while (fs.Position + 8 <= fs.Length)
                    {
                        var lenBytes = br.ReadBytes(4);
                        int len = (lenBytes[0] << 24) | (lenBytes[1] << 16) | (lenBytes[2] << 8) | lenBytes[3];
                        var typeBytes = br.ReadBytes(4);
                        if (len < 0 || typeBytes.Any(b => !((b >= 65 && b <= 90) || (b >= 97 && b <= 122))))
                            break; // not a chunk: padding, truncation, garbage
                        string type = System.Text.Encoding.ASCII.GetString(typeBytes);
                        if (type == "IEND") break;
                        if ((type == "tEXt" || type == "iTXt") && len > 0 && len < 64 * 1024 * 1024)
                        {
                            var data = br.ReadBytes(len);
                            int nul = Array.IndexOf(data, (byte)0);
                            if (nul > 0)
                            {
                                string key = System.Text.Encoding.Latin1.GetString(data, 0, nul);
                                int bodyStart = nul + 1;
                                if (type == "iTXt")
                                {
                                    // compression flag, compression method, language tag\0, translated keyword\0
                                    if (bodyStart + 2 <= data.Length && data[bodyStart] == 0)
                                    {
                                        bodyStart += 2;
                                        int l1 = Array.IndexOf(data, (byte)0, bodyStart); if (l1 < 0) { fs.Position += 4; continue; }
                                        int l2 = Array.IndexOf(data, (byte)0, l1 + 1); if (l2 < 0) { fs.Position += 4; continue; }
                                        bodyStart = l2 + 1;
                                    }
                                    else { fs.Position += 4; continue; } // compressed iTXt: not needed for ComfyUI files
                                }
                                var enc = type == "iTXt" ? System.Text.Encoding.UTF8 : System.Text.Encoding.Latin1;
                                found.Add((key, enc.GetString(data, bodyStart, data.Length - bodyStart)));
                            }
                            fs.Position += 4; // CRC
                        }
                        else
                            fs.Position += len + 4L;
                    }
                }
            }
            catch (Exception) { }
            return found;
        }

        /// <summary>"Workflow:{...}" -> the balanced JSON object after the marker, or null. Port of comfydbg's brace scan.</summary>
        private static string JsonAfterMarker(string text, string marker)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int idx = text.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0) return null;
            int start = idx + marker.Length, depth = 0, end = start;
            for (int i = start; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}' && --depth == 0) { end = i + 1; break; }
            }
            return end > start ? text.Substring(start, end - start) : null;
        }

        private static string Shorten(string s)
        {
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length > 400 ? s.Substring(0, 400) + "…" : s;
        }
    }
}
