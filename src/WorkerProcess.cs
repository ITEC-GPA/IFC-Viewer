using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace IfcViewer
{
    public static class WorkerProcess
    {
        public static string Run(string python, string script, string[] arguments, int timeoutMilliseconds = 1800000)
        {
            if (!File.Exists(python)) throw new FileNotFoundException("Runtime IFC mancante. Eseguire Setup.cmd nella cartella di Meerkat.", python);
            if (!File.Exists(script)) throw new FileNotFoundException("Worker IFC mancante. Ripristinare la distribuzione completa di Meerkat.", script);
            if (timeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException("timeoutMilliseconds");
            // Rhino sets PYTHONHOME/PYTHONPATH for its embedded Python. Do not
            // import that standard library into a different worker interpreter.
            // Unlike -I, -E -s retains the script folder for sibling IFC modules.
            var start = new ProcessStartInfo(python, "-E -s -X utf8 " + string.Join(" ", new[] { script }.Concat(arguments).Select(Engine.Quote)))
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            using (var process = Process.Start(start))
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(timeoutMilliseconds))
                {
                    // The worker may exit between the timeout and Kill.
                    try { process.Kill(); }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { if (!process.HasExited) throw; }
                    process.WaitForExit();
                    System.Threading.Tasks.Task.WaitAll(stdout, stderr);
                    throw new TimeoutException("Worker " + Path.GetFileName(script) + " interrotto per timeout. Verificare la destinazione prima di ripetere una scrittura.");
                }
                string output = stdout.Result, error = stderr.Result;
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("Worker " + Path.GetFileName(script) + " (exit " + process.ExitCode + "): " + (string.IsNullOrWhiteSpace(error) ? output : error));
                return output;
            }
        }
    }
}
