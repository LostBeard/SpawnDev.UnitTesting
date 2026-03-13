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
                // Read both streams concurrently to prevent pipe buffer deadlock.
                // Sequential reads can deadlock when the child writes >4KB to one
                // stream while we're blocked waiting on the other.
                var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                var stderrTask = proc.StandardError.ReadToEndAsync();
                using var cts = new CancellationTokenSource(timeout);
                try
                {
                    await proc.WaitForExitAsync(cts.Token);
                    ret.ExitCode = proc.ExitCode;
                }
                catch (OperationCanceledException)
                {
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    ret.ExitCode = -1;
                }
                await Task.WhenAll(stdoutTask, stderrTask);
                ret.StdOut = stdoutTask.Result;
                ret.StdErr = stderrTask.Result;
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
