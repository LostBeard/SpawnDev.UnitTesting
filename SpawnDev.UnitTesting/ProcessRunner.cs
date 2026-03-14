using System.Diagnostics;
using System.Text;

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
                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();
                using var proc = new Process();
                proc.StartInfo = psi;
                proc.EnableRaisingEvents = true;

                // Use event-based async reads to avoid pipe buffer deadlocks.
                // ReadToEndAsync can deadlock when the child fills one pipe buffer
                // while the parent is blocked waiting on the other.
                proc.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null) outputBuilder.AppendLine(e.Data);
                };
                proc.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null) errorBuilder.AppendLine(e.Data);
                };

                var exitTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                proc.Exited += (s, e) => exitTcs.TrySetResult(true);

                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                // Wait for exit or timeout
                using var cts = new CancellationTokenSource(timeout);
                using var reg = cts.Token.Register(() => exitTcs.TrySetResult(false));
                var exited = await exitTcs.Task;

                if (exited)
                {
                    // Process exited normally — call WaitForExit() to flush remaining buffered output
                    proc.WaitForExit();
                    ret.ExitCode = proc.ExitCode;
                }
                else
                {
                    // Timeout — kill the process
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    ret.ExitCode = -1;
                }

                ret.StdOut = outputBuilder.ToString();
                ret.StdErr = errorBuilder.ToString();
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
