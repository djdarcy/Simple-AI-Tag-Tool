using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Simple-AI-Tag-Tool: the one external call to the user's `comfydbg` CLI.
    /// Prompts are resolved natively (DazzleComfyPrompts); this runs only
    /// `comfydbg detect FILE`, which compares the workflow's recorded versions
    /// with what is installed in ComfyUI's own Python environment -- something
    /// no in-process code can know. Best effort: a missing or broken comfydbg
    /// yields a note, never an exception.
    /// </summary>
    public static class DazzleComfydbg
    {
        public const string NotInstalledNote = "comfydbg not installed (pip install comfydbg), or not on PATH; set its path in Settings > UI";
        private const int TimeoutMs = 20000;
        private static bool notInstalled; // once we fail to start it, stop trying for this session

        private static string Exe => string.IsNullOrWhiteSpace(Program.Settings.DazzleComfydbgPath) ? "comfydbg" : Program.Settings.DazzleComfydbgPath;

        public static Task<string> ReadFingerprintAsync(string file)
        {
            return Task.Run(() =>
            {
                if (notInstalled) return NotInstalledNote;
                var run = Run("detect \"" + file + "\"");
                if (run.failedToStart) { notInstalled = true; return NotInstalledNote; }
                if (run.timedOut) return "comfydbg timed out after " + TimeoutMs / 1000 + " s";
                string text = (run.stdout + run.stderr).Trim();
                return text.Length == 0 ? "comfydbg detect printed nothing" : text;
            });
        }

        private static (bool failedToStart, bool timedOut, int exitCode, string stdout, string stderr) Run(string args)
        {
            var psi = new ProcessStartInfo(Exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            psi.Environment["PYTHONIOENCODING"] = "utf-8";
            try
            {
                using (var p = Process.Start(psi))
                {
                    var outTask = p.StandardOutput.ReadToEndAsync();
                    var errTask = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(TimeoutMs))
                    {
                        try { p.Kill(); } catch (Exception) { }
                        return (false, true, -1, "", "");
                    }
                    return (false, false, p.ExitCode, outTask.Result, errTask.Result);
                }
            }
            catch (Win32Exception)
            {
                return (true, false, -1, "", "");
            }
            catch (Exception e)
            {
                return (false, false, -1, "", e.Message);
            }
        }
    }
}
