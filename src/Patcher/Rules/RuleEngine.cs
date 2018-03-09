﻿/// Copyright(C) 2015 Unforbidable Works
///
/// This program is free software; you can redistribute it and/or
/// modify it under the terms of the GNU General Public License
/// as published by the Free Software Foundation; either version 2
/// of the License, or(at your option) any later version.
///
/// This program is distributed in the hope that it will be useful,
/// but WITHOUT ANY WARRANTY; without even the implied warranty of
/// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
/// GNU General Public License for more details.
///
/// You should have received a copy of the GNU General Public License
/// along with this program; if not, write to the Free Software
/// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

using Patcher.Data;
using Patcher.Data.Plugins;
using Patcher.Rules.Compiled;
using Patcher.Rules.Compiled.Helpers;
using Patcher.Rules.Proxies;
using Patcher.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Patcher.Rules
{
    public sealed class RuleEngine : IDisposable
    {
        readonly DataContext context;
        public DataContext Context { get { return context; } }

        readonly TagManager tagManager;
        public TagManager Tags { get { return tagManager; } }

        public string RulesFolder { get; set; }

        IDictionary<string, List<IRule>> rules = new Dictionary<string, List<IRule>>();

        readonly ProxyProvider proxyProvider;
        public ProxyProvider ProxyProvider { get { return proxyProvider; } }

        readonly HelperProvider helperProvider;
        internal HelperProvider HelperProvider { get { return helperProvider; } }

        public Plugin ActivePlugin { get; set; }

        public bool DebugAll { get; set; }

        public string DebugPluginFileName { get; set; }
        public string DebugRuleFileName { get; set; }

        public readonly static string CompiledRulesAssemblyPath = Path.Combine(Program.ProgramFolder, Program.ProgramCacheFolder, "Patcher.Rules.Compiled.dll");

        IDictionary<string, string> parameters = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public IDictionary<string, string> Params { get { return parameters; } }

        public RuleEngine(DataContext context)
        {
            this.context = context;

            proxyProvider = new ProxyProvider(this);
            tagManager = new TagManager(this);
            helperProvider = new HelperProvider(this);

            ExtractCompiledAssemblyFile();
        }

        public string GetParam(string name, string defaultValue)
        {
            if (parameters.ContainsKey(name))
                return parameters[name];

            return defaultValue;
        }

        public int GetParam(string name, int defaultValue)
        {
            if (parameters.ContainsKey(name))
            {
                string value = parameters[name];
                int parsed;
                if (int.TryParse(value, out parsed))
                    return parsed;
            }

            return defaultValue;
        }

        public float GetParam(string name, float defaultValue)
        {
            if (parameters.ContainsKey(name))
            {
                string value = parameters[name];
                float parsed;
                if (float.TryParse(value.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
                    return parsed;
            }

            return defaultValue;
        }

        private void ExtractCompiledAssemblyFile()
        {
            string ressourceName = @"costura.patcher.rules.compiled.dll.zip";
            using (var unzip = new DeflateStream(GetType().Assembly.GetManifestResourceStream(ressourceName), CompressionMode.Decompress))
            {
                var outputFile = context.DataFileProvider.GetDataFile(FileMode.Create, CompiledRulesAssemblyPath);
                outputFile.CopyFrom(unzip, true);
            }
        }

        public void Load()
        {
            Log.Info("Loading rules");

            using (var progress = Display.StartProgress("Loading rules"))
            {
                long total = context.Plugins.Count;
                long current = 0;

                foreach (var pluginFileName in context.Plugins.Select(p => p.FileName))
                {
                    bool retry;
                    do
                    {
                        retry = false;
                        try
                        {
                            var compiler = new RuleCompiler(this, pluginFileName);

                            string path = Path.Combine(Program.ProgramFolder, Program.ProgramRulesFolder, RulesFolder, pluginFileName);
                            var files = Context.DataFileProvider.FindDataFiles(path, "*.rules", false).ToArray();

                            total += files.Length;

                            foreach (var file in files)
                            {
                                using (var stream = file.Open())
                                {
                                    bool isDebugModeEnabled = DebugAll ||
                                        pluginFileName.Equals(DebugPluginFileName, StringComparison.OrdinalIgnoreCase) &&
                                        (DebugRuleFileName == null || Path.GetFileName(file.Name).Equals(DebugRuleFileName, StringComparison.OrdinalIgnoreCase));

                                    int count = 0;
                                    using (RuleReader reader = new RuleReader(stream))
                                    {
                                        foreach (var entry in reader.ReadRules())
                                        {
                                            if (entry.Select == null && entry.Update == null && entry.Inserts.Count() == 0)
                                            {
                                                Log.Warning("Rule {0} in file {1} ignored because it lacks any operation", entry.Name, pluginFileName);
                                                continue;
                                            }

                                            var metadata = new RuleMetadata()
                                            {
                                                PluginFileName = pluginFileName,
                                                RuleFileName = Path.GetFileName(file.Name),
                                                Name = entry.Name,
                                                Description = entry.Description,
                                            };

                                            Log.Fine("Loading rule {0}\\{1}@{2}", metadata.PluginFileName, metadata.RuleFileName, metadata.Name);

                                            try
                                            {
                                                compiler.Add(entry, metadata, isDebugModeEnabled);
                                            }
                                            catch (IllegalTokenException ex)
                                            {
                                                Display.ShowProblems("Illegal Token", ex.ToString(), new Problem()
                                                {
                                                    Message = ex.Message,
                                                    File = file.GetRelativePath(),
                                                    Solution = string.Format("Please avoid using the following tokens in rule code: {0}",
                                                        string.Join(", ", RuleCompiler.GetIllegalCodeTokens()))
                                                });
                                                throw;
                                            }

                                            count++;
                                        }
                                    }
                                    Log.Fine("Loaded {0} rule(s) from file {1}", count, stream.Name);
                                }

                                progress.Update(current, total, string.Format("{0}\\{1}", pluginFileName, Path.GetFileName(file.Name)));
                            }

                            if (compiler.HasRules)
                            {
                                try
                                {
                                    compiler.CompileAll();
                                }
                                catch (CompilerException ex)
                                {
                                    StringBuilder text = new StringBuilder();
                                    List<Problem> problems = new List<Problem>();
                                    foreach (System.CodeDom.Compiler.CompilerError error in ex.Errors)
                                    {
                                        if (!error.IsWarning)
                                        {
                                            text.AppendLine(error.ToString());

                                            problems.Add(new Problem()
                                            {
                                                Message = string.Format("{0}: {1}", error.ErrorNumber, error.ErrorText),
                                                File = DataFile.GetRelativePath(error.FileName),
                                                Line = error.Line,
                                                Column = error.Column,
                                                Solution = RuleCompiler.GetCompilerErrorHint(error)
                                            });
                                        }
                                    }
                                    text.Append(ex.ToString());

                                    Display.ShowProblems("Compiler Error(s)", text.ToString(), problems.ToArray());

                                    throw ex;
                                }

                                rules.Add(pluginFileName, new List<IRule>(compiler.CompiledRules));
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error("Error occured while loading rules for plugin {0} with message: {1}", pluginFileName, ex.Message);
                            Log.Fine(ex.ToString());

                            // Depending on the kind of error, offer a choice to reload rules for this plugin
                            // Rules can be reloaded only if illegal tokens have been detected or when compilation has failed
                            ChoiceOption result = ChoiceOption.Cancel;
                            if (ex.GetType() == typeof(IllegalTokenException) || ex.GetType() == typeof(CompilerException))
                            {
                                result = Display.Choice(string.Format("Try loading rules for plugin {0} again?", pluginFileName), ChoiceOption.Yes, ChoiceOption.Continue, ChoiceOption.Cancel);
                            }
                            else
                            {
                                result = Display.Choice("Continue loading rules?", ChoiceOption.Continue, ChoiceOption.Cancel);
                            }

                            if (result == ChoiceOption.Yes)
                            {
                                Log.Info("Rules for plugin {0} will be reloaded.", pluginFileName);
                                retry = true;
                            }
                            else if (result == ChoiceOption.Continue)
                            {
                                Log.Warning("Rules for plugin {0} skipped because an error occured: {1} ", pluginFileName, ex.Message);
                            }
                            else
                            {
                                Log.Warning("Rule loading has been aborted.");
                                throw new UserAbortException("Rule loading has been aborted by the user.");
                            }
                        }
                        finally
                        {
                            Display.ClearProblems();
                        }

                    } while (retry);
                }

                current++;
            }
        }

        public void Run()
        {
            int totalRulesToRun = rules.SelectMany(p => p.Value).Count();
            if (totalRulesToRun == 0)
            {
                Log.Warning("No rules loaded, nothing to do.");
                return;
            }

            using (var progress = Display.StartProgress("Executing rules"))
            {
                int run = 0;
                foreach (var pluginFileName in rules.Keys)
                {
                    foreach (var rule in rules[pluginFileName])
                    {
                        Log.Info("Executing rule {0}", rule);
                        progress.Update(run++, totalRulesToRun, "{0}", rule);

                        // Run rule
                        var runner = new RuleRunner(this, rule);
                        try
                        {
                            runner.Run();
                        }
#if !DEBUG
                        catch (Exception ex)
                        {
                            if (ex is CompiledRuleAssertException)
                            {
                                Log.Error("Assertion failed while executing rule {0} with message: {1}", rule, ex.Message);
                            }
                            else
                            {
                                Log.Error("Error occured while executing rule {0} with message: {1}", rule, ex.Message);
                            }
                            Log.Fine(ex.ToString());

                            // Determine were the exception occured
                            var stackTrace = new StackTrace(ex, true);
                            var frame = stackTrace.GetFrames().Where(f => f.GetMethod().DeclaringType.Namespace == "Patcher.Rules.Compiled.Generated").FirstOrDefault();
                            if (frame != null)
                            {
                                Display.ShowProblems("Runtime Error", ex.ToString(), new Problem()
                                {
                                    Message = string.Format("{0}: {1}", ex.GetType().FullName, ex.Message),
                                    File = DataFile.GetRelativePath(frame.GetFileName()),
                                    Line = frame.GetFileLineNumber(),
                                    Solution = RuleRunner.GetRuntimeErrorHint(ex)
                                });
                            }

                            var choice = Display.Choice("Continue executing rules?", ChoiceOption.Ok, ChoiceOption.Cancel);
                            if (choice == ChoiceOption.Cancel)
                            {
                                Log.Warning("Rule execution has been aborted.");
                                throw new UserAbortException("Rule execution has been aborted by the user.");
                            }
                            else
                            {
                                Log.Warning("The last rule has not been fully applied.");
                                continue;
                            }
                        }
#endif
                        finally
                        {
                            Display.ClearProblems();
                        }

                        Log.Info("Rule completed with {0} updates and {1} inserts", runner.Updated, runner.Created);
                    }

                    // After all rules of a plugin were run
                    // Clear tags
                    Tags.Clear();
                }
            }
        }

        public void Dispose()
        {
        }
    }
}
