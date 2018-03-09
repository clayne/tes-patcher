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

using Patcher.Data.Archives;
using Patcher.Data.Plugins;
using Patcher.Data.Plugins.Content;
using Patcher.Data.Plugins.Content.Records;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Patcher.Data
{
    public abstract class DataContext : IDisposable
    {
        readonly IList<string> ignorePlugins = new List<string>();
        public IList<string> IgnorePlugins { get { return ignorePlugins; } }

        public IDataFileProvider DataFileProvider { get; private set; }

        public bool AsyncFormIndexing { get; set; }
        public bool AsyncFormLoading { get; set; }
        public int AsyncFormLoadingWorkerThreshold { get; set; }
        public int AsyncFromLoadingMaxWorkers { get; set; }

        PluginIndex plugins = new PluginIndex();
        public PluginIndex Plugins { get { return plugins; } }

        FormRepository forms = new FormRepository();
        public FormRepository Forms { get { return forms; } }

        SortedDictionary<FormKind, Type> formKindToRecordTypeMap = new SortedDictionary<FormKind, Type>();

        readonly ArchiveManager archives;
        public ArchiveManager Archives {  get { return archives; } }

        ISet<FormKind> ignoredFormKinds = null;
        public ISet<FormKind> IgnoredFormKinds
        {
            get
            {
                if (ignoredFormKinds == null)
                    ignoredFormKinds = new HashSet<FormKind>(GetIgnoredFormKinds());

                return ignoredFormKinds;
            }
        }

        public string GameTitle { get { return GetGameTitle(); } }

        public static DataContext CreateContext(IDataFileProvider dataFileProvider)
        {
            // Look for classes that extend DataContext 
            var types = Assembly.GetExecutingAssembly().GetTypes().Where(t => t.IsSubclassOf(typeof(DataContext)));

            foreach (var type in types)
            {
                // having attribute DataContextAttribute
                var dataContextAttribute = (DataContextAttribute)type.GetCustomAttributes(typeof(DataContextAttribute), false).FirstOrDefault();
                if (dataContextAttribute != null)
                {
                    DataContext context = (DataContext)Activator.CreateInstance(type);

                    // Look for plugin specified by the attribute
                    if (dataFileProvider.GetDataFile(FileMode.Open, dataContextAttribute.PluginFileName).Exists() && dataFileProvider.DataFolderPath.Contains(context.GetGameInstallPath()))
                    {
                        // Pass data provider to the context
                        context.DataFileProvider = dataFileProvider;
                        return context;
                    }
                }
            }

            throw new InvalidDataException("Unable to determine a suitable data context.");
        }

        protected DataContext()
        {
            archives = new ArchiveManager(this);
        }

        /// <summary>
        /// When overrdien by a deviced class, provides the list of types that will not be indexed.
        /// </summary>
        /// <returns>Returns array of FormType representing form types that will not be indexed.</returns>
        protected abstract IEnumerable<FormKind> GetIgnoredFormKinds();
        protected abstract IEnumerable<string> GetDefaultArchives();
        protected abstract IPluginListProvider GetPluginListProvider();
        protected abstract string GetGameTitle();
        protected abstract string GetGameInstallPath();
        protected abstract IEnumerable<Form> GetHardcodedForms(byte plugin);
        protected abstract string GetDefaultLanguage();
        protected abstract string GetArchiveExtension();

        private void QuerySupportedTypes()
        {
            var assembly = Assembly.GetExecutingAssembly();

            foreach (Type type in assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(GenericFormRecord))))
            {
                var gameAttributes = type.GetCustomAttributes(typeof(GameAttribute), false).Cast<GameAttribute>().ToArray();
                if (gameAttributes.Where(a => a.Title == GetGameTitle()).Any())
                    AddSupportedType(type);
            }
        }

        private void AddSupportedType(Type type)
        {
            var recinfo = InfoProvider.GetRecordInfo(type);
            formKindToRecordTypeMap.Add((FormKind)recinfo.Signature, type);
        }

        public Form CreateForm(FormKind kind)
        {
            return new Form()
            {
                FormKind = kind,
                Record = CreateGenericFormRecord(kind)
            };
        }

        public void Load()
        {
            Log.Info("Indexing files in default archives");

            using (var progress = Display.StartProgress("Indexing default archives"))
            {
                long total = GetDefaultArchives().Count();
                long current = 0;

                foreach (string filename in GetDefaultArchives())
                {
                    Log.Fine("Indexing files in archive: " + filename);
                    archives.AddArchive(filename);
                    progress.Update(current++, total, filename);
                }
            }

            QuerySupportedTypes();

            Log.Info("Indexing forms in active plugins");

            var sw2 = new Stopwatch();
            sw2.Start();

            foreach (var plugin in GetPluginListProvider().Plugins)
            {
                if (!ignorePlugins.Contains(plugin.Filename))
                {
                    LoadPlugin(plugin.Filename);
                }
            }

            sw2.Stop();
            Log.Fine("Indexing forms in all plugins finished in {0}ms", sw2.ElapsedMilliseconds);

            //if (detectedUnsupportedFormKinds.Count > 0)
            //{
            //    Log.Warning("Detected {0} unsupported form types", detectedUnsupportedFormKinds.Count);
            //    Log.Warning("Unsupported form types are: {0}", string.Join(",", detectedUnsupportedFormKinds));
            //    Log.Warning("Note: Forms of unsupported type can be referenced but cannot be edited or copied");
            //}
        }

        public Plugin CreatePlugin(string pluginFilename)
        {
            var plugin = new Plugin(this, pluginFilename, PluginMode.Create);
            plugins.AddPlugin(plugin);
            return plugin;
        }

        private void LoadPlugin(string pluginFilename)
        {
            Plugin plugin = new Plugin(this, pluginFilename, PluginMode.Open);

            // Ensure masters are loaded first
            foreach (var masterFilename in plugin.MasterFiles)
            {
                if (!plugins.Exists(masterFilename))
                {
                    LoadPlugin(masterFilename);
                }
            }

            // Add plugin to the index 
            byte number = plugins.AddPlugin(plugin);

            // Insert hardcoded forms for this plugin
            // Before normal forms are loaded form the plugin file
            InsertHardcodedForms(number);

            // Finally plugin can be loaded only after all masters have been added and loaded
            plugin.Load();

            LoadPluginArchive(pluginFilename);
        }

        private void InsertHardcodedForms(byte pluginNumber)
        {
            foreach (var form in GetHardcodedForms(pluginNumber))
            {
                form.Flags |= FormFlags.Hardcoded;
                form.FilePosition = -1;

                Forms.Add(form);
                Log.Fine("Hardcoded form inserted: {0}", form);
            }
        }

        private void LoadPluginArchive(string pluginFilename)
        {
            // Try to load archive related to this plugin
            string archiveFilenameWithoutExtension = Path.GetFileNameWithoutExtension(pluginFilename);

            string defaultArchiveFilename = string.Format("{0}.{1}", archiveFilenameWithoutExtension, GetArchiveExtension());
            var defaultArchiveFile = DataFileProvider.GetDataFile(FileMode.Open, defaultArchiveFilename);
            if (defaultArchiveFile.Exists())
            {
                Log.Fine("Indexing files in archive: " + defaultArchiveFile.Name);
                archives.AddArchive(defaultArchiveFile.FullPath);
            }

            string searchPattern = string.Format("{0} - *.{1}", archiveFilenameWithoutExtension, GetArchiveExtension());
            foreach (var archiveDataFile in DataFileProvider.FindDataFiles(string.Empty, searchPattern, false))
            {
                Log.Fine("Indexing files in archive: " + archiveDataFile.Name);
                archives.AddArchive(archiveDataFile.FullPath);
            }
        }

        public void LoadForms()
        {
            LoadForms(false, null);
        }

        public void LoadForms(Func<Form, bool> predicate)
        {
            LoadForms(false, predicate);
        }

        public void LoadForms(bool lazyLoading)
        {
            LoadForms(lazyLoading, null);
        }

        public void LoadForms(bool lazyLoading, Func<Form, bool> predicate)
        {
            Log.Info("Loading forms ({0})", lazyLoading ? "lazy" : "full");

            Stopwatch sw = new Stopwatch();
            sw.Start();
            foreach (Plugin plugin in Plugins)
            {
                plugin.LoadForms(lazyLoading, predicate);
            }
            sw.Stop();

            Log.Fine("Loading forms from all plugins finished in {0}ms", sw.ElapsedMilliseconds);
        }

        public void Dispose()
        {
            foreach (Plugin plugin in plugins)
            {
                plugin.Dispose();
            }
        }

        public virtual float GetLatestPluginVersion()
        {
            return 1.7f;
        }

        public virtual ushort GetLatestFormVersion()
        {
            return 43;
        }

        public string GetLanguage()
        {
            return GetDefaultLanguage();
        }

        internal virtual RecordWriter CreateWriter(Stream stream)
        {
            return new RecordWriter(stream, this);
        }

        internal virtual RecordReader CreateReader(Stream stream)
        {
            return new RecordReader(stream, this);
        }

        internal virtual RecordMetadata CreateRecordMetaData()
        {
            return new RecordMetadata();
        }

        internal virtual GroupMetadata CreateGroupMetaData()
        {
            return new GroupMetadata();
        }

        internal virtual FieldMetadata CreateFieldMetaData()
        {
            return new FieldMetadata();
        }

        internal virtual HeaderRecord CreateHeader()
        {
            return new Tes4();
        }

        public virtual bool IsSupportedFormKind(FormKind kind)
        {
            return formKindToRecordTypeMap.ContainsKey(kind);
        }

        public virtual bool IsSupportedFormKind(string name)
        {
            // Compare as strings to prevent registering new FormType values
            // FormType (t.Key) will be converted to string because the operator is implicit
            return formKindToRecordTypeMap.Where(t => t.Key == name).Any();
        }

        public virtual GenericFormRecord CreateGenericFormRecord(FormKind kind)
        {
            var recordType = GetRecordType(kind);
            var recinf = InfoProvider.GetRecordInfo(recordType);
            var record = recinf.CreateInstance();
            record.Version = recinf.Version > 0 ? (ushort)recinf.Version : GetLatestFormVersion();
            return record;
        }

        public FormKind GetRecordFormKind(Type recordType)
        {
            var recinf = InfoProvider.GetRecordInfo(recordType);
            return (FormKind)recinf.Signature;
        }

        private Type GetRecordType(FormKind kind)
        {
            return IsSupportedFormKind(kind) ? formKindToRecordTypeMap[kind] : typeof(DummyRecord);
        }

        public virtual Field CreateField(Type type)
        {
            var fldinf = InfoProvider.GetFieldInfo(type);
            return fldinf.CreateInstance();
        }

        public T CreateField<T>() where T: Field
        {
            return (T)CreateField(typeof(T));
        }
    }
}
