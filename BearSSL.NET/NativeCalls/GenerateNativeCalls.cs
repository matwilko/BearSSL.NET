using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System.Text.RegularExpressions;

namespace BearSSL
{
    public sealed class GenerateNativeCalls : Task
    {
        [Output]
        public string[] GeneratedFiles { get; set; }

        [Required]
        public ITaskItem GenerationFile { get; set; }
        
        [Required]
        public string IntermediateOutputPath { get; set; }

        private struct ParentRuntime
        {
            public string RuntimeIdentifier { get; }
            public string Constant { get; }
            public string Platform { get; }
            public string LibraryExtension { get; }

            private ParentRuntime(string runtimeIdentifier, string constant, string platform, string libraryExtension)
            {
                RuntimeIdentifier = runtimeIdentifier;
                Constant = constant;
                Platform = platform;
                LibraryExtension = libraryExtension;
            }

            public static ParentRuntime Linux => new ParentRuntime("linux", "LINUX", "Linux", "so");
            public static ParentRuntime Windows => new ParentRuntime("win", "WIN", "Windows", "dll");
            public static ParentRuntime OsX => new ParentRuntime("osx", "OSX", "OSX", "dylib");

            public static IEnumerable<ParentRuntime> All { get; } = new [] { Linux, Windows, OsX };

            public static bool operator ==(ParentRuntime a, ParentRuntime b) => a.RuntimeIdentifier == b.RuntimeIdentifier;
            public static bool operator !=(ParentRuntime a, ParentRuntime b) => a.RuntimeIdentifier != b.RuntimeIdentifier;
            public override bool Equals(object o) => (o is ParentRuntime pr) && this == pr;
            public override int GetHashCode() => RuntimeIdentifier.GetHashCode();
        }

        private struct Runtime
        {
            public string RuntimeIdentifier { get; }
            public string Constant { get; }
            public ParentRuntime Parent { get; }

            public bool Is64Bit { get; }

            public string LibraryName => $"bearssl.{Parent.RuntimeIdentifier.ToString().ToLower()}.{(Is64Bit ? "x64" : "x86")}.{Parent.LibraryExtension}";
            public string ClassName { get; }

            private Runtime(string runtimeIdentifier, string constant, ParentRuntime parent, bool is64Bit, string className)
            {
                RuntimeIdentifier = runtimeIdentifier;
                Constant = constant;
                Parent = parent;
                Is64Bit = is64Bit;
                ClassName = className;
            }

            public static IEnumerable<Runtime> All { get; } = new []
            {
                new Runtime("win-x86",   "WIN_X86",    ParentRuntime.Windows, false, "Win32"),
                new Runtime("win-x64",   "WIN_X64",    ParentRuntime.Windows, true,  "Win64"),
                new Runtime("linux-x86", "LINUX_X86", ParentRuntime.Linux,    false, "Linux32"),
                new Runtime("linux-x64", "LINUX_X64", ParentRuntime.Linux,    true,  "Linux64"),
                new Runtime("osx-x64",   "OSX_X64",   ParentRuntime.OsX,      true,  "MacOs64")
            };
        }

        private struct Definition
        {
            private static Regex NameExtractor { get; } = new Regex("^.*?([a-zA-Z_][a-zA-Z0-9_]*)\\(.*?\\)$", RegexOptions.Compiled);
            private static Regex ParameterExtractor { get; } = new Regex(@"^.*?\((?:|(?:.*?([a-zA-Z_][a-zA-Z0-9_]+))(?:,.*?([a-zA-Z_][a-zA-Z0-9_]+))*)\)$", RegexOptions.Compiled);
            private static Regex ReturnExtractor { get; } = new Regex(@"^(.*?) [A-Za-z_][A-Za-z0-9_]*\\(.*?\\)$", RegexOptions.Compiled);

            private string FullDefinition { get; }
            public string Name { get; }
            public string[] Parameters { get; }
            public string ReturnType { get; }
            public bool IsVoid => ReturnType == "void";

            public Definition(string definition)
            {
                FullDefinition = definition;
                Name = NameExtractor.Match(definition).Groups[1].Value;
                Parameters = ParameterExtractor.Match(definition).Groups.Cast<Group>().Skip(1).Where(g => g.Success).Select(g => g.Value).ToArray();
                ReturnType = ReturnExtractor.Match(definition).Groups[1].Value;
            }

            public override string ToString() => FullDefinition;
        }

        public override bool Execute()
        {
            Directory.CreateDirectory(IntermediateOutputPath);

            var definitions = File.ReadAllLines(GenerationFile.ItemSpec).Select(l => new Definition(l)).ToArray();

            GeneratedFiles = GenerateRuntimes(definitions)
                     .Concat(GenerateReferenceAssemblyDefinitions(definitions))
                     .ToArray();

            return true;
        }

        private IEnumerable<string> GenerateRuntimes(Definition[] definitions)
        {
            foreach (var runtime in Runtime.All)
            {
                var outputFilePath = Path.Combine(IntermediateOutputPath, $"NativeCalls.{runtime.RuntimeIdentifier}.cs");
                using (var file = File.Open(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                using (var writer = new CodeWriter(file))
                {
                    writer.WriteLine($"#if {runtime.Constant} && !REFERENCE_ASSEMBLY");
                    writer.WriteLine("using System.Runtime.InteropServices;");
                    writer.WriteLine("namespace BearSSL");
                    writer.WriteLine("{");
                    writer.WriteLine("internal static class NativeCalls");
                    writer.WriteLine("{");
                    
                    foreach (var definition in definitions)
                    {
                        writer.WriteLine($@"[DllImport(""{runtime.LibraryName}"", EntryPoint = ""{definition.Name}"", CallingConvention = CallingConvention.Cdecl)]");
                        writer.WriteLine($"public static extern unsafe {definition};");
                    }

                    writer.WriteLine("}");
                    writer.WriteLine("}");
                    writer.WriteLine($"#endif");
                }

                yield return outputFilePath;
            }
        }
        
        private IEnumerable<string> GenerateReferenceAssemblyDefinitions(Definition[] definitions)
        {
            var outputFilePath = Path.Combine(IntermediateOutputPath, $"NativeCalls.ref.cs");
            using (var file = File.Open(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            using (var writer = new CodeWriter(file))
            {
                writer.WriteLine($"#if REFERENCE_ASSEMBLY");
                writer.WriteLine("using System.Runtime.InteropServices;");
                writer.WriteLine("namespace BearSSL");
                writer.WriteLine("{");
                writer.WriteLine("internal static class NativeCalls");
                writer.WriteLine("{");
                
                foreach (var definition in definitions)
                {
                    writer.WriteLine($"public static unsafe {definition} => throw null;");
                }

                writer.WriteLine("}");
                writer.WriteLine("}");
                writer.WriteLine($"#endif");
            }

            yield return outputFilePath;
        }

        private sealed class CodeWriter : StreamWriter
        {
            public CodeWriter(Stream stream) : base(stream)
            {
            }

            private int Indent { get; set; } = 0;

            private static string[] Indents { get; } = Enumerable.Range(0, 10).Select(i => new string(' ', i * 4)).ToArray();

            public override void WriteLine(string value)
            {
                if (value == "{")
                    base.WriteLine(Indents[Indent++] + value);
                else if (value == "}")
                    base.WriteLine(Indents[--Indent] + value);
                else if (value.StartsWith("?"))
                    base.WriteLine(Indents[++Indent] + value);
                else if (value.StartsWith(":"))
                    base.WriteLine(Indents[Indent--] + value);
                else
                    base.WriteLine(Indents[Indent] + value);
            }

            public void WriteLine(string value, bool indent)
            {
                if (indent)
                {
                    Indent++;
                    WriteLine(value);
                    Indent--;
                }
                else
                {
                    if (value == "{")
                        base.WriteLine(value);
                    else if (value == "}")
                        base.WriteLine(value);
                    else if (value.StartsWith("?"))
                        base.WriteLine(value);
                    else if (value.StartsWith(":"))
                        base.WriteLine(value);
                    else if (indent)
                        base.WriteLine(value);
                    else
                        base.WriteLine(value);
                }
            }

            public void WriteIndent()
            {
                base.Write(Indents[Indent]);
            }
        }
    }
}
