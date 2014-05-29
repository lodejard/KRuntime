// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Microsoft.Framework.Runtime.Common.CommandLine
{
    public class CommandLineApplication : CommandInfo
    {
        // Indicates whether the parser should throw an exception when it runs into an unexpected argument.
        // If this field is set to false, the parser will stop parsing when it sees an unexpected argument, and all
        // remaining arguments, including the first unexpected argument, will be stored in RemainingArguments property.
        private readonly bool _throwOnUnexpectedArg;
        private readonly string _appName;

        public CommandLineApplication(string appName, bool throwOnUnexpectedArg = true)
        {
            _appName = appName;
            _throwOnUnexpectedArg = throwOnUnexpectedArg;
        }

        public new CommandLineApplication Command(string name, Action<CommandInfo> configuration)
        {
            return (CommandLineApplication)base.Command(name, configuration);
        }

        public int Execute(params string[] args)
        {
            CommandInfo command = this;
            CommandOption option = null;
            IEnumerator<CommandArgument> arguments = null;

            for (var index = 0; index < args.Length; index++)
            {
                var arg = args[index];
                var processed = false;
                if (!processed && option == null)
                {
                    string[] longOption = null;
                    string[] shortOption = null;
                    if (arg.StartsWith("/"))
                    {
                        longOption = arg.Substring(1).Split(new[] { ':', '=' }, 2);
                    }
                    else if (arg.StartsWith("--"))
                    {
                        longOption = arg.Substring(2).Split(new[] { ':', '=' }, 2);
                    }
                    else if (arg.StartsWith("-"))
                    {
                        shortOption = arg.Substring(1).Split(new[] { ':', '=' }, 2);
                    }
                    if (longOption != null)
                    {
                        processed = true;
                        option = command.GetAllOptions().SingleOrDefault(opt => string.Equals(opt.LongName, longOption[0], StringComparison.Ordinal));
                        if (option == null)
                        {
                            throw new Exception(string.Format("TODO: unknown option '{0}'", arg));
                        }
                        if (longOption.Length == 2)
                        {
                            if (!option.TryParse(longOption[1]))
                            {
                                throw new Exception(string.Format("TODO: unexpected value '{0}' for option '{1}'", longOption[1], option.LongName));
                            }
                            option = null;
                        }
                        else if (option.OptionType == CommandOptionType.NoValue)
                        {
                            // No value is needed for this option
                            option.TryParse(null);
                            option = null;
                        }
                    }
                    if (shortOption != null)
                    {
                        processed = true;
                        option = command.GetAllOptions().SingleOrDefault(opt => string.Equals(opt.ShortName, shortOption[0], StringComparison.Ordinal));
                        if (option == null)
                        {
                            throw new Exception(string.Format("TODO: unknown option '{0}'", arg));
                        }
                        if (shortOption.Length == 2)
                        {
                            if (!option.TryParse(shortOption[1]))
                            {
                                throw new Exception(string.Format("TODO: unexpected value '{0}' for option '{1}'", shortOption[1], option.LongName));
                            }
                            option = null;
                        }
                        else if (option.OptionType == CommandOptionType.NoValue)
                        {
                            // No value is needed for this option
                            option.TryParse(null);
                            option = null;
                        }
                    }
                }

                if (!processed && option != null)
                {
                    processed = true;
                    if (!option.TryParse(arg))
                    {
                        throw new Exception(string.Format("TODO: unexpected value '{0}' for option '{1}'", arg, option.LongName));
                    }
                    option = null;
                }

                if (!processed && arguments == null)
                {
                    foreach (var subcommand in command.Commands)
                    {
                        if (subcommand.Name == arg)
                        {
                            processed = true;
                            command = subcommand;
                        }
                    }
                }
                if (!processed)
                {
                    if (arguments == null)
                    {
                        arguments = ((IEnumerable<CommandArgument>)command.Arguments).GetEnumerator();
                    }
                    if (arguments.MoveNext())
                    {
                        processed = true;
                        arguments.Current.Value = arg;
                    }
                }
                if (!processed)
                {
                    if (_throwOnUnexpectedArg)
                    {
                        throw new Exception(string.Format("TODO: unexpected argument '{0}'", arg));
                    }
                    else
                    {
                        // All remaining arguments are stored for further use
                        RemainingArguments.AddRange(new ArraySegment<string>(args, index, args.Length - index));
                        break;
                    }
                }
            }

            if (option != null)
            {
                throw new Exception(string.Format("TODO: missing value for option"));
            }

            return command.Invoke();
        }

        // Helper method that adds help options
        public CommandLineApplication HelpOptions(params string[] templates)
        {
            var options = new List<CommandOption>();
            foreach (var t in templates)
            {
                options.Add(Option(t, "Display help information", CommandOptionType.NoValue));
            }

            this.OnExecute(() =>
            {
                var showHelp = options.Aggregate(false, (acc, option) => option.HasValue() | acc);
                if (showHelp)
                {
                    DisplayHelp(this, null);
                }
                return 0;
            });

            return this;
        }

        // Helper method that adds a "help" command
        public CommandLineApplication HelpCommand()
        {
            Command("help", c =>
            {
                c.Description = "Display help information";

                var argCommand = c.Argument("[command]", "Command that help information explains");

                c.OnExecute(() =>
                {
                    DisplayHelp(this, argCommand.Value);
                    return 0;
                });
            });

            this.OnExecute(() => this.Execute("help"));

            return this;
        }

        private void DisplayHelp(CommandLineApplication app, string commandName)
        {
            var headerBuilder = new StringBuilder("Usage: " + _appName);
            CommandInfo target;

            if (commandName == null)
            {
                target = app;
            }
            else
            {
                target = app.Commands.SingleOrDefault(cmd => string.Equals(cmd.Name, commandName, StringComparison.OrdinalIgnoreCase));
                if (target == null)
                {
                    Console.WriteLine("Unknown command {0}", commandName);
                    return;
                }
                headerBuilder.AppendFormat(" {0}", commandName);
            }

            var optionsBuilder = new StringBuilder();
            var commandsBuilder = new StringBuilder();
            var argumentsBuilder = new StringBuilder();

            if (target.Arguments.Any())
            {
                headerBuilder.Append(" [arguments]");

                argumentsBuilder.AppendLine();
                argumentsBuilder.AppendLine("Arguments:");
                var maxArgLen = MaxArgumentLength(target.Arguments);
                var outputFormat = string.Format("  {{0, -{0}}}{{1}}\n", maxArgLen + 2);
                foreach (var arg in target.Arguments)
                {
                    argumentsBuilder.AppendFormat(outputFormat, arg.Name, arg.Description);
                }
            }

            if (target.Options.Any())
            {
                headerBuilder.Append(" [options]");

                optionsBuilder.AppendLine();
                optionsBuilder.AppendLine("Options:");
                var maxOptLen = MaxOptionTemplateLength(target.Options);
                var outputFormat = string.Format("  {{0, -{0}}}{{1}}\n", maxOptLen + 2);
                foreach (var opt in target.Options)
                {
                    optionsBuilder.AppendFormat(outputFormat, opt.Template, opt.Description);
                }
            }

            if (target.Commands.Any())
            {
                headerBuilder.Append(" [command]");

                commandsBuilder.AppendLine();
                commandsBuilder.AppendLine("Commands:");
                var maxCmdLen = MaxCommandLength(target.Commands);
                var outputFormat = string.Format("  {{0, -{0}}}{{1}}\n", maxCmdLen + 2);
                foreach (var cmd in target.Commands)
                {
                    commandsBuilder.AppendFormat(outputFormat, cmd.Name, cmd.Description);
                }

                commandsBuilder.AppendLine();
                commandsBuilder.AppendFormat("Use \"{0} help [command]\" for more information about a command.", _appName);
            }
            headerBuilder.AppendLine();
            Console.WriteLine("{0}{1}{2}{3}", headerBuilder, argumentsBuilder, optionsBuilder, commandsBuilder);
        }

        private int MaxOptionTemplateLength(IEnumerable<CommandOption> options)
        {
            var maxLen = 0;
            foreach (var opt in options)
            {
                maxLen = opt.Template.Length > maxLen ? opt.Template.Length : maxLen;
            }
            return maxLen;
        }

        private int MaxCommandLength(IEnumerable<CommandInfo> commands)
        {
            var maxLen = 0;
            foreach (var cmd in commands)
            {
                maxLen = cmd.Name.Length > maxLen ? cmd.Name.Length : maxLen;
            }
            return maxLen;
        }

        private int MaxArgumentLength(IEnumerable<CommandArgument> arguments)
        {
            var maxLen = 0;
            foreach (var arg in arguments)
            {
                maxLen = arg.Name.Length > maxLen ? arg.Name.Length : maxLen;
            }
            return maxLen;
        }
    }
}
