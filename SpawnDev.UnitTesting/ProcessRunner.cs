using System.Diagnostics;

namespace SpawnDev.UnitTesting
{
    /// <summary>
    /// Runs an exe or dotnet dll
    /// </summary>
    public class ProcessRunner
    {
        public string StdOut { get; set; } = "";
        public string StdErr { get; set; } = "";
        public int ExitCode { get; set; } = -1;
        public string Summary => $"ExitCode: {ExitCode}, StdOut: {StdOut}, StdErr: {StdErr}";
        public bool Success => ExitCode == 0;
        public string Text => string.IsNullOrWhiteSpace(StdOut) ? StdErr ?? "" : StdOut ?? "";
        public static async Task<ProcessRunner> Run(string exePath, string args = "", int timeout = 30_000)
        {
            var ret = new ProcessRunner();
            var psi = new ProcessStartInfo
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            var ext = Path.GetExtension(exePath);
            var isDll = ext.Equals(".dll", StringComparison.OrdinalIgnoreCase);
            if (isDll)
            {
                psi.FileName = "dotnet";
                psi.Arguments = $"\"{exePath}\" {args}";
            }
            else
            {
                psi.FileName = exePath;
                psi.Arguments = args;
            }
            try
            {
                using var proc = Process.Start(psi)!;
                ret.StdOut = await proc.StandardOutput.ReadToEndAsync();
                ret.StdErr = await proc.StandardError.ReadToEndAsync();
                var completed = proc.WaitForExit(timeout);
                if (!completed)
                {
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    ret.ExitCode = -1;
                }
                else
                {
                    ret.ExitCode = proc.ExitCode;
                }
            }
            catch (Exception ex)
            {
                ret.StdErr = ex.Message;
                ret.ExitCode = -1;
            }
            return ret;
        }
    }
}
