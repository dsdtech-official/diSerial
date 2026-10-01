using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Build.Framework;
using Mono.Cecil;

// Normalize Windows debug paths.
public sealed class NormalizeWindowsDebugPaths : Microsoft.Build.Utilities.Task
{
    [Required] public string AssemblyFile { get; set; }
    [Required] public string RepositoryRoot { get; set; }
    [Required] public string BuildTasksAssembly { get; set; }
    [Required] public ITaskItem[] References { get; set; }

    // Normalize Windows debug paths.
    public override bool Execute()
    {
        try
        {
            Assembly.LoadFrom(BuildTasksAssembly);
            return Normalize();
        }
        catch (Exception exception)
        {
            Log.LogErrorFromException(exception, true);
            return false;
        }
    }

    // Rewrite Windows debug symbols.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool Normalize()
    {
        var root = Path.GetFullPath(RepositoryRoot).Replace('\\', '/').TrimEnd('/') + "/";
        using (var resolver = new ReferenceResolver(References))
        using (var assembly = AssemblyDefinition.ReadAssembly(AssemblyFile,
            new ReaderParameters { ReadSymbols = true, InMemory = true, AssemblyResolver = resolver }))
        {
            if (!assembly.MainModule.HasSymbols)
                throw new InvalidOperationException("The application has no debug symbols.");

            var visited = new HashSet<Mono.Cecil.Cil.Document>();
            var mapped = 0;
            foreach (var type in AllTypes(assembly.MainModule.Types))
            foreach (var method in type.Methods)
            foreach (var point in method.DebugInformation.SequencePoints)
            {
                var document = point.Document;
                if (!visited.Add(document)) continue;
                var path = document.Url.Replace('\\', '/');
                if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    document.Url = "/_/" + path.Substring(root.Length);
                    mapped++;
                }
                else if (!path.StartsWith("/_/", StringComparison.Ordinal))
                    throw new InvalidOperationException("Unmapped source document: " + path);
            }
            if (visited.Count == 0)
                throw new InvalidOperationException("The application PDB has no source documents.");

            using (var image = new MemoryStream())
            using (var symbols = new MemoryStream())
            {
                assembly.Write(image, new WriterParameters
                {
                    WriteSymbols = true,
                    SymbolStream = symbols,
                    SymbolWriterProvider = (Mono.Cecil.Cil.ISymbolWriterProvider)Activator.CreateInstance(
                        typeof(AssemblyDefinition).Assembly.GetType(
                            "Mono.Cecil.Cil.PortablePdbWriterProvider", true), true)
                });
                File.WriteAllBytes(AssemblyFile, image.ToArray());
                File.WriteAllBytes(Path.ChangeExtension(AssemblyFile, ".pdb"), symbols.ToArray());
            }
            Log.LogMessage(MessageImportance.High,
                "Normalized Windows debug metadata: {0} source documents, {1} mapped; relative PDB name.",
                visited.Count, mapped);
        }
        return true;
    }

    // Enumerate types recursively.
    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var nested in AllTypes(type.NestedTypes)) yield return nested;
        }
    }

    // Resolve build-time assemblies.
    private sealed class ReferenceResolver : IAssemblyResolver
    {
        private readonly Dictionary<string, string> paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AssemblyDefinition> cache = new Dictionary<string, AssemblyDefinition>(StringComparer.OrdinalIgnoreCase);

        // Initialize reference resolution.
        public ReferenceResolver(ITaskItem[] references)
        {
            foreach (var reference in references)
                paths[Path.GetFileNameWithoutExtension(reference.ItemSpec)] = reference.ItemSpec;
        }

        // Resolve an assembly reference.
        public AssemblyDefinition Resolve(AssemblyNameReference name)
        {
            return Resolve(name, new ReaderParameters());
        }

        // Resolve an assembly reference.
        public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
        {
            AssemblyDefinition result;
            if (cache.TryGetValue(name.Name, out result)) return result;
            string path;
            if (!paths.TryGetValue(name.Name, out path))
                throw new InvalidOperationException("Missing build reference: " + name.FullName);
            result = AssemblyDefinition.ReadAssembly(path,
                new ReaderParameters { InMemory = true, AssemblyResolver = this });
            cache[name.Name] = result;
            return result;
        }

        // Release cached assemblies.
        public void Dispose()
        {
            foreach (var assembly in cache.Values) assembly.Dispose();
        }
    }
}
