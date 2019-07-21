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
    public sealed class GenerateExports : Task
    {
        [Required]
        public ITaskItem GenerationFile { get; set; }
        
        [Required]
        public string IntermediateOutputPath { get; set; }

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

			var outputFilePath = Path.Combine(IntermediateOutputPath, $"linkcommands");
            using (var file = File.Open(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            using (var writer = new StreamWriter(file))
            {
            	foreach (var definition in definitions)
	            	writer.WriteLine($"/link /export:{definition.Name}");
            }
            
            return true;
        }
    }
}
