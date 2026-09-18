using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LLM2Swarm
{
    public enum LocalModel { Ministral3B, Qwen3_4B }
    public sealed class LocalInterpretationResult
    {
        public Interpretation response;
        public string rawJson, requestDirectory;
        public double elapsedSeconds;
    }
    public static class LocalInterpreter
    {
        public const string ModelFile = "Ministral-3-3B-Instruct-2512-Q4_K_M.gguf";
        public static string ModelFileFor(LocalModel model) => model == LocalModel.Qwen3_4B ? "Qwen3-4B-Q4_K_M.gguf" : ModelFile;
        public static string ManifestFor(LocalModel model) => model == LocalModel.Qwen3_4B ? "installed-qwen3-4b.json" : "installed.json";
        public static string ChatTemplateFor(LocalModel model) => model == LocalModel.Qwen3_4B ? "chatml" : null;
        public static string DisplayName(LocalModel model) => model == LocalModel.Qwen3_4B ? "Qwen3 4B · Q4_K_M" : "Ministral 3B · Q4_K_M";
        public static bool Available(string root, LocalModel model) => Available(root,ModelFileFor(model),ManifestFor(model));
        public static bool Available(string root, string modelFile = ModelFile, string manifestFile = "installed.json") =>
            File.Exists(Path.Combine(root,"runtime","llama-cli.exe")) &&
            File.Exists(Path.Combine(root,"models",modelFile)) && File.Exists(Path.Combine(root,manifestFile));
        // Windows command-line quoting, not shell execution. User text is only ever passed in a file.
        static string Quote(string value)
        {
            if (value.Contains("\"") || value.EndsWith("\\")) throw new ArgumentException("Ruta no admitida.");
            return "\""+value+"\"";
        }
        public static async Task<LocalInterpretationResult> InterpretAsync(string root, string assets, string logRoot,
            string instruction, int gpuLayers, CancellationToken cancellation, string modelFile = ModelFile,
            string manifestFile = "installed.json", string chatTemplate = null)
        {
            if (!Available(root,modelFile,manifestFile)) throw new FileNotFoundException("Falta instalar el motor/modelo local solicitado.");
            if (string.IsNullOrWhiteSpace(instruction) || instruction.Length > 2000) throw new ArgumentException("Escribe una instrucción de hasta 2000 caracteres.");
            string request = Path.Combine(logRoot,Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(request);
            string promptFile = Path.Combine(request,"instruction.txt"), outputFile = Path.Combine(request,"conversation.txt");
            instruction = instruction.Trim().Replace("\r\n","\n").Replace("\r","\n");
            File.WriteAllText(promptFile,instruction,new UTF8Encoding(false));
            File.Copy(Path.Combine(assets,"system.txt"),Path.Combine(request,"system.txt"));
            File.Copy(Path.Combine(assets,"response.schema.json"),Path.Combine(request,"response.schema.json"));
            File.Copy(Path.Combine(root,manifestFile),Path.Combine(request,"installed.json"));
            string args = "-m "+Quote(Path.Combine(root,"models",modelFile))+" -c 4096 -n 512 -ngl "+gpuLayers+
                " --temp 0 --seed 42 --single-turn --simple-io --no-display-prompt --no-warmup --offline --log-disable --no-escape"+
                (string.IsNullOrEmpty(chatTemplate) ? "" : " --no-jinja --chat-template "+Quote(chatTemplate))+
                " --system-prompt-file "+Quote(Path.Combine(request,"system.txt"))+
                " --json-schema-file "+Quote(Path.Combine(request,"response.schema.json"))+" -f "+Quote(promptFile)+" --output-file "+Quote(outputFile);
            File.WriteAllText(Path.Combine(request,"arguments.txt"),args);
            using (var process = new Process { StartInfo = new ProcessStartInfo {
                FileName=Path.Combine(root,"runtime","llama-cli.exe"), Arguments=args, WorkingDirectory=root,
                UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true, RedirectStandardError=true,
                StandardOutputEncoding=Encoding.UTF8, StandardErrorEncoding=Encoding.UTF8 } })
            {
                var watch = Stopwatch.StartNew();
                cancellation.ThrowIfCancellationRequested();
                process.Start();
                Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
                try
                {
                    await Task.Run(() => {
                        while (!process.WaitForExit(100))
                        {
                            cancellation.ThrowIfCancellationRequested();
                            if (watch.Elapsed.TotalSeconds > 120) throw new TimeoutException("La interpretación superó 120 segundos.");
                        }
                    });
                    cancellation.ThrowIfCancellationRequested();
                    File.WriteAllText(Path.Combine(request,"stdout.txt"),await stdout);
                    string errors = await stderr;
                    File.WriteAllText(Path.Combine(request,"stderr.txt"),errors);
                    if (process.ExitCode != 0) throw new InvalidOperationException("El motor local falló. Consulta "+request);
                    string transcript = File.ReadAllText(outputFile).Replace("\r\n","\n");
                    string prefix = "User:\n"+instruction+"\n\nAssistant:\n";
                    if (!transcript.StartsWith(prefix,StringComparison.Ordinal)) throw new InvalidOperationException("Formato de respuesta inesperado; no se preparó ninguna misión.");
                    string json = transcript.Substring(prefix.Length).Trim();
                    var response = Interpretation.Parse(json);
                    File.WriteAllText(Path.Combine(request,"response.json"),json);
                    File.WriteAllText(Path.Combine(request,"elapsed-seconds.txt"),watch.Elapsed.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    return new LocalInterpretationResult { response=response,rawJson=json,requestDirectory=request,elapsedSeconds=watch.Elapsed.TotalSeconds };
                }
                finally
                {
                    if (!process.HasExited) { process.Kill(); process.WaitForExit(); }
                    await Task.WhenAll(stdout,stderr);
                    File.WriteAllText(Path.Combine(request,"stdout.txt"),stdout.Result);
                    File.WriteAllText(Path.Combine(request,"stderr.txt"),stderr.Result);
                }
            }
        }
    }
}
