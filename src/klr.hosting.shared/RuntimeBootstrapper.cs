// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
#if K10
using System.Threading;
using System.Runtime.Loader;
#endif
using System.Threading.Tasks;
using Microsoft.Framework.Runtime.Common.CommandLine;

namespace klr.hosting
{
    internal static class RuntimeBootstrapper
    {
        private static readonly Dictionary<string, Assembly> _assemblyCache = new Dictionary<string, Assembly>();

        private static readonly char[] _libPathSeparator = new[] { ';' };

        public static int Execute(string[] args)
        {
            // If we're a console host then print exceptions to stderr
            var printExceptionsToStdError = Environment.GetEnvironmentVariable("KRE_CONSOLE_HOST") == "1";

            try
            {
                return ExecuteAsync(args).Result;
            }
            catch (Exception ex)
            {
                if (printExceptionsToStdError)
                {
                    PrintErrors(ex);
                    return 1;
                }

                throw;
            }
        }

        private static void PrintErrors(Exception ex)
        {
            var enableTrace = Environment.GetEnvironmentVariable("KRE_TRACE") == "1";

            while (ex != null)
            {
                if (ex is TargetInvocationException ||
                    ex is AggregateException)
                {
                    // Skip these exception messages as they are
                    // generic
                }
                else
                {
                    if (enableTrace)
                    {
                        Console.Error.WriteLine(ex);
                    }
                    else
                    {
                        Console.Error.WriteLine(ex.Message);
                    }
                }

                ex = ex.InnerException;
            }
        }

        public static Task<int> ExecuteAsync(string[] args)
        {
            if (args.Length == 0)
            {
                return Task.FromResult(1);
            }

            var enableTrace = Environment.GetEnvironmentVariable("KRE_TRACE") == "1";
#if NET45
            // TODO: Make this pluggable and not limited to the console logger
            if (enableTrace)
            {
                var listener = new ConsoleTraceListener();
                Trace.Listeners.Add(listener);
                Trace.AutoFlush = true;
            }
#endif
            var app = new CommandLineApplication("klr", throwOnUnexpectedArg: false);
            var optionLib = app.Option("--lib <LIB_PATHS>", "Paths used for library look-up",
                CommandOptionType.MultipleValue);
            app.HelpOptions("-h|--help", "-?");
            app.Execute(args);

            // Resolve the lib paths
            string[] searchPaths = ResolveSearchPaths(optionLib.Values, app.RemainingArguments);

            Func<string, Assembly> loader = _ => null;
            Func<byte[], Assembly> loadBytes = _ => null;
            Func<string, Assembly> loadFile = _ => null;

            Func<AssemblyName, Assembly> loaderCallback = assemblyName =>
            {
                string name = assemblyName.Name;

                // If the assembly was already loaded use it
                Assembly assembly;
                if (_assemblyCache.TryGetValue(name, out assembly))
                {
                    return assembly;
                }

                assembly = loader(name) ?? ResolveHostAssembly(loadFile, searchPaths, name);

                if (assembly != null)
                {
#if K10
                    ExtractAssemblyNeutralInterfaces(assembly, loadBytes);
#endif

                    _assemblyCache[name] = assembly;
                }

                return assembly;
            };
#if K10
            var loaderImpl = new DelegateAssemblyLoadContext(loaderCallback);
            loadBytes = bytes => loaderImpl.LoadBytes(bytes, null);
            loadFile = path => loaderImpl.LoadFile(path);

            AssemblyLoadContext.InitializeDefaultContext(loaderImpl);
            
            if (loaderImpl.EnableMultiCoreJit())
            {
                loaderImpl.StartMultiCoreJitProfile("startup.prof");
            }
#else
            var loaderImpl = new LoaderEngine();
            loadBytes = bytes => loaderImpl.LoadBytes(bytes, null);
            loadFile = path => loaderImpl.LoadFile(path);

            ResolveEventHandler handler = (sender, a) =>
            {
                // Special case for retargetable assemblies on desktop
                if (a.Name.EndsWith("Retargetable=Yes"))
                {
                    return Assembly.Load(a.Name);
                }

                return loaderCallback(new AssemblyName(a.Name));
            };

            AppDomain.CurrentDomain.AssemblyResolve += handler;
            AppDomain.CurrentDomain.AssemblyLoad += (object sender, AssemblyLoadEventArgs loadedArgs) =>
            {
                // Skip loading interfaces for dynamic assemblies
                if (loadedArgs.LoadedAssembly.IsDynamic)
                {
                    return;
                }

                ExtractAssemblyNeutralInterfaces(loadedArgs.LoadedAssembly, loadBytes);
            };
#endif

            try
            {
                var assembly = Assembly.Load(new AssemblyName("klr.host"));

                // Loader impl
                // var loaderEngine = new DefaultLoaderEngine(loaderImpl);
                var loaderEngineType = assembly.GetType("klr.host.DefaultLoaderEngine");
                var loaderEngine = Activator.CreateInstance(loaderEngineType, loaderImpl);

                // The following code is doing:
                // var hostContainer = new klr.host.HostContainer();
                // var rootHost = new klr.host.RootHost(loaderEngine, searchPaths);
                // hostContainer.AddHost(rootHost);
                // var bootstrapper = new klr.host.Bootstrapper(hostContainer, loaderEngine);
                // bootstrapper.Main(bootstrapperArgs);

                var hostContainerType = assembly.GetType("klr.host.HostContainer");
                var rootHostType = assembly.GetType("klr.host.RootHost");

                var hostContainer = Activator.CreateInstance(hostContainerType);
                var rootHost = Activator.CreateInstance(rootHostType, new object[] { loaderEngine, searchPaths });

                MethodInfo addHostMethodInfo = hostContainerType.GetTypeInfo().GetDeclaredMethod("AddHost");
                var disposable = (IDisposable)addHostMethodInfo.Invoke(hostContainer, new[] { rootHost });
                var hostContainerLoad = hostContainerType.GetTypeInfo().GetDeclaredMethod("Load");

                loader = (Func<string, Assembly>)hostContainerLoad.CreateDelegate(typeof(Func<string, Assembly>), hostContainer);

                var bootstrapperType = assembly.GetType("klr.host.Bootstrapper");
                var mainMethod = bootstrapperType.GetTypeInfo().GetDeclaredMethod("Main");
                var bootstrapper = Activator.CreateInstance(bootstrapperType, hostContainer, loaderEngine);

                try
                {
                    var bootstrapperArgs = new object[]
                    {
                        app.RemainingArguments.ToArray()
                    };

                    var task = (Task<int>)mainMethod.Invoke(bootstrapper, bootstrapperArgs);

                    return task.ContinueWith(async (t, state) =>
                    {
                        // Dispose the host
                        ((IDisposable)state).Dispose();

#if NET45
                        AppDomain.CurrentDomain.AssemblyResolve -= handler;
#endif
                        return await t;
                    },
                    disposable).Unwrap();
                }
                catch
                {
                    // If we throw synchronously then dispose then rethtrow
                    disposable.Dispose();
                    throw;
                }
            }
            catch
            {
#if NET45
                AppDomain.CurrentDomain.AssemblyResolve -= handler;
#endif
                throw;
            }
        }

        private static string[] ResolveSearchPaths(IEnumerable<string> libPaths, List<string> remainingArgs)
        {
            var searchPaths = new List<string>();

            var defaultLibPath = Environment.GetEnvironmentVariable("KRE_DEFAULT_LIB");

            if (!string.IsNullOrEmpty(defaultLibPath))
            {
                // Add the default lib folder if specified
                searchPaths.AddRange(ExpandSearchPath(defaultLibPath));
            }

            // Add the expanded search libs to the list of paths
            searchPaths.AddRange(libPaths.SelectMany(ExpandSearchPath));

            // If a .dll or .exe is specified then turn this into
            // --lib {path to dll/exe} [dll/exe name]
            if (remainingArgs.Any())
            {
                var application = remainingArgs[0];
                var extension = Path.GetExtension(application);

                if (!string.IsNullOrEmpty(extension) &&
                    extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    // Add the directory to the list of search paths
                    searchPaths.Add(Path.GetDirectoryName(application));

                    // Modify the argument to be the dll/exe name
                    remainingArgs[0] = Path.GetFileNameWithoutExtension(application);
                }
            }

            return searchPaths.ToArray();
        }

        private static IEnumerable<string> ExpandSearchPath(string libPath)
        {
            // Expand ; separated arguments
            return libPath.Split(_libPathSeparator, StringSplitOptions.RemoveEmptyEntries)
                          .Select(Path.GetFullPath);
        }

        private static void ExtractAssemblyNeutralInterfaces(Assembly assembly, Func<byte[], Assembly> loadBytes)
        {
            // Embedded assemblies end with .dll
            foreach (var name in assembly.GetManifestResourceNames())
            {
                if (name.EndsWith(".dll"))
                {
                    var assemblyName = Path.GetFileNameWithoutExtension(name);

                    if (_assemblyCache.ContainsKey(assemblyName))
                    {
                        continue;
                    }

                    // We're creating 2 streams under the covers on core clr
                    var ms = new MemoryStream();
                    assembly.GetManifestResourceStream(name).CopyTo(ms);
                    _assemblyCache[assemblyName] = loadBytes(ms.ToArray());
                }
            }
        }

        private static Assembly ResolveHostAssembly(Func<string, Assembly> loadFile, IList<string> searchPaths, string name)
        {
            foreach (var searchPath in searchPaths)
            {
                var path = Path.Combine(searchPath, name + ".dll");

                if (File.Exists(path))
                {
                    return loadFile(path);
                }
            }

            return null;
        }
    }
}
