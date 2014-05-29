// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Framework.Runtime.Common.CommandLine;
using Microsoft.Framework.PackageManager.Packing;
using Microsoft.Framework.Runtime;
using Microsoft.Framework.Runtime.Common;
using System.Diagnostics;
using System.Threading;
using System.Net;

namespace Microsoft.Framework.PackageManager
{
    public class Program : IReport
    {
        private readonly IApplicationEnvironment _environment;

        public Program(IApplicationEnvironment environment)
        {
            _environment = environment;
            Thread.GetDomain().SetData(".appDomain", this);
            ServicePointManager.DefaultConnectionLimit = 1024;
        }

        public int Main(string[] args)
        {
            _originalForeground = Console.ForegroundColor;

            var app = new CommandLineApplication("kpm");

            var optionVerbose = app.Option("-v|--verbose", "Show verbose output", CommandOptionType.NoValue);
            app.HelpOptions("-h|--help", "-?");
            app.HelpCommand();

            app.Command("restore", c =>
            {
                c.Description = "Restore packages";

                var argProject = c.Argument("[project]", "Project to restore, default is current directory");
                var optSource = c.Option("-s|--source <FEED>", "A list of packages sources to use for this command",
                    CommandOptionType.MultipleValue);
                var optFallbackSource = c.Option("-f|--fallbacksource <FEED>",
                    "A list of packages sources to use as a fallback", CommandOptionType.MultipleValue);

                c.OnExecute(() =>
                {
                    try
                    {
                        var command = new RestoreCommand(_environment);
                        command.Report = this;
                        command.RestoreDirectory = argProject.Value;
                        if (optSource.HasValue())
                        {
                            command.Sources = optSource.Values;
                        }
                        if (optFallbackSource.HasValue())
                        {
                            command.FallbackSources = optFallbackSource.Values;
                        }
                        var success = command.ExecuteCommand();

                        return success ? 0 : 1;
                    }
                    catch (Exception ex)
                    {
                        this.WriteLine("----------");
                        this.WriteLine(ex.ToString());
                        this.WriteLine("----------");
                        this.WriteLine("Restore failed");
                        this.WriteLine(ex.Message);
                        return 1;
                    }
                });
            });

            app.Command("pack", c =>
            {
                c.Description = "Bundle application for deployment";

                var argProject = c.Argument("[project]", "Path to project, default is current directory");
                var optionOut = c.Option("-o|--out <PATH>", "Where does it go", CommandOptionType.SingleValue);
                var optionZipPackages = c.Option("-z|--zippackages", "Bundle a zip full of packages",
                    CommandOptionType.NoValue);
                var optionOverwrite = c.Option("--overwrite", "Remove existing files in target folders",
                    CommandOptionType.NoValue);
                var optionRuntime = c.Option("--runtime <KRE>", "Names or paths to KRE files to include",
                    CommandOptionType.MultipleValue);
                var optionAppFolder = c.Option("--appfolder <NAME>",
                    "Determine the name of the application primary folder", CommandOptionType.SingleValue);

                c.OnExecute(() =>
                {
                    Console.WriteLine("verbose:{0} out:{1} zip:{2} project:{3}",
                        optionVerbose.HasValue(),
                        optionOut.Value(),
                        optionZipPackages.HasValue(),
                        argProject.Value);

                    var options = new PackOptions
                    {
                        OutputDir = optionVerbose.Value(),
                        ProjectDir = argProject.Value ?? System.IO.Directory.GetCurrentDirectory(),
                        AppFolder = optionAppFolder.Value(),
                        RuntimeTargetFramework = _environment.TargetFramework,
                        ZipPackages = optionZipPackages.HasValue(),
                        Overwrite = optionOverwrite.HasValue(),
                        Runtimes = optionRuntime.HasValue() ?
                            string.Join(";", optionRuntime.Values).
                                Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries) :
                            new string[0],
                    };

                    var manager = new PackManager(options);
                    if (!manager.Package())
                    {
                        return -1;
                    }

                    return 0;
                });
            });

            return app.Execute(args);
        }

        object _lock = new object();
        ConsoleColor _originalForeground;
        void SetColor(ConsoleColor color)
        {
            Console.ForegroundColor = (ConsoleColor)(((int)Console.ForegroundColor & 0x08) | ((int)color & 0x07));
        }
        void SetBold(bool bold)
        {
            Console.ForegroundColor = (ConsoleColor)(((int)Console.ForegroundColor & 0x07) | (bold ? 0x08 : 0x00));
        }

        public void WriteLine(string message)
        {
            var sb = new System.Text.StringBuilder();
            lock (_lock)
            {
                var escapeScan = 0;
                for (; ;)
                {
                    var escapeIndex = message.IndexOf("\x1b[", escapeScan);
                    if (escapeIndex == -1)
                    {
                        var text = message.Substring(escapeScan);
                        sb.Append(text);
                        Console.Write(text);
                        break;
                    }
                    else
                    {
                        var startIndex = escapeIndex + 2;
                        var endIndex = startIndex;
                        while (endIndex != message.Length &&
                            message[endIndex] >= 0x20 &&
                            message[endIndex] <= 0x3f)
                        {
                            endIndex += 1;
                        }

                        var text = message.Substring(escapeScan, escapeIndex - escapeScan);
                        sb.Append(text);
                        Console.Write(text);
                        if (endIndex == message.Length)
                        {
                            break;
                        }

                        switch (message[endIndex])
                        {
                            case 'm':
                                int value;
                                if (int.TryParse(message.Substring(startIndex, endIndex - startIndex), out value))
                                {
                                    switch (value)
                                    {
                                        case 1:
                                            SetBold(true);
                                            break;
                                        case 22:
                                            SetBold(false);
                                            break;
                                        case 30:
                                            SetColor(ConsoleColor.Black);
                                            break;
                                        case 31:
                                            SetColor(ConsoleColor.Red);
                                            break;
                                        case 32:
                                            SetColor(ConsoleColor.Green);
                                            break;
                                        case 33:
                                            SetColor(ConsoleColor.Yellow);
                                            break;
                                        case 34:
                                            SetColor(ConsoleColor.Blue);
                                            break;
                                        case 35:
                                            SetColor(ConsoleColor.Magenta);
                                            break;
                                        case 36:
                                            SetColor(ConsoleColor.Cyan);
                                            break;
                                        case 37:
                                            SetColor(ConsoleColor.Gray);
                                            break;
                                        case 39:
                                            SetColor(_originalForeground);
                                            break;
                                    }
                                }
                                break;
                        }

                        escapeScan = endIndex + 1;
                    }
                }
                Console.WriteLine();
            }
            Trace.WriteLine(sb.ToString());
        }
    }
}
