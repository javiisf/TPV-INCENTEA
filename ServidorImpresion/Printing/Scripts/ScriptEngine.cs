using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Serilog;

namespace ServidorImpresion
{
    public sealed class ScriptEngine
    {
        private readonly string _scriptsFolder;
        private readonly ConcurrentDictionary<string, (DateTime Modified, Assembly Asm)> _cache = new();

        public ScriptEngine(string scriptsFolder)
        {
            _scriptsFolder = scriptsFolder;
        }

        /// <summary>
        /// Devuelve los nombres (sin extensión) de los scripts disponibles en la carpeta.
        /// </summary>
        public string[] ListScripts()
        {
            if (!Directory.Exists(_scriptsFolder)) return [];
            return Directory.GetFiles(_scriptsFolder, "*.cs")
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .OrderBy(n => n)
                .ToArray();
        }

        /// <summary>
        /// Carga y compila el script {name}.cs. Devuelve null si el archivo no existe.
        /// Lanza InvalidOperationException si el script tiene errores de compilación.
        /// </summary>
        public ITicketScript? Load(string name)
        {
            string path = Path.Combine(_scriptsFolder, name + ".cs");
            if (!File.Exists(path)) return null;

            DateTime modified = File.GetLastWriteTimeUtc(path);

            if (_cache.TryGetValue(path, out var cached) && cached.Modified == modified)
                return Instantiate(cached.Asm);

            string source = File.ReadAllText(path, Encoding.UTF8);
            var asm = Compile(source, name);
            _cache[path] = (modified, asm);
            return Instantiate(asm);
        }

        private static ITicketScript Instantiate(Assembly asm)
        {
            var type = asm.GetTypes().FirstOrDefault(t =>
                !t.IsAbstract && typeof(ITicketScript).IsAssignableFrom(t));

            if (type is null)
                throw new InvalidOperationException("El script no contiene ninguna clase que implemente ITicketScript.");

            return (ITicketScript)Activator.CreateInstance(type)!;
        }

        private static Assembly Compile(string source, string scriptName)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source);

            var references = AppDomain.CurrentDomain
                .GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location))
                .Cast<MetadataReference>()
                .ToList();

            // Aseguramos referencias básicas del runtime
            var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            foreach (var dll in new[] { "System.Runtime.dll", "System.Collections.dll", "netstandard.dll" })
            {
                var p = Path.Combine(runtimeDir, dll);
                if (File.Exists(p))
                    references.Add(MetadataReference.CreateFromFile(p));
            }

            var compilation = CSharpCompilation.Create(
                assemblyName: "Script_" + scriptName,
                syntaxTrees: [syntaxTree],
                references: references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var ms  = new MemoryStream();
            using var pdb = new MemoryStream();
            var emitOpts = new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb);
            var result = compilation.Emit(ms, pdbStream: pdb, options: emitOpts);

            if (!result.Success)
            {
                var errors = string.Join("\n", result.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.ToString()));
                Log.Warning("ScriptEngine: error compilando {Script}:\n{Errors}", scriptName, errors);
                throw new InvalidOperationException($"Error compilando '{scriptName}.cs':\n{errors}");
            }

            // Carga en el ALC por defecto (sin aislamiento). Los scripts son código
            // interno de confianza; el aislamiento por ALC añadiría complejidad sin
            // beneficio real para este caso de uso.
            // El PDB embebido permite obtener números de línea exactos en excepciones de runtime.
            return Assembly.Load(ms.ToArray(), pdb.ToArray());
        }
    }
}
