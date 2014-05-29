// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Framework.Runtime.Common.CommandLine
{
    public class CommandOption
    {
        public CommandOption(string template, CommandOptionType optionType)
        {
            Template = template;
            OptionType = optionType;
            Values = new List<string>();

            foreach (var part in Template.Split(new[] { ' ', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.StartsWith("--"))
                {
                    LongName = part.Substring(2);
                }
                else if (part.StartsWith("-"))
                {
                    ShortName = part.Substring(1);
                }
                else if (part.StartsWith("<") && part.EndsWith(">"))
                {
                    ValueName = part.Substring(1, part.Length - 2);
                }
                else
                {
                    throw new Exception("TODO: invalid template pattern " + template);
                }
            }

            if (LongName == null && ShortName == null)
            {
                throw new Exception("TODO: invalid template pattern " + template);
            }
        }

        public string Template { get; set; }
        public string ShortName { get; set; }
        public string LongName { get; set; }
        public string ValueName { get; set; }
        public string Description { get; set; }
        public List<string> Values { get; private set; }
        public CommandOptionType OptionType { get; private set; }

        public bool TryParse(string value)
        {
            switch (OptionType)
            {
                case CommandOptionType.MultipleValue:
                    Values.Add(value);
                    break;
                case CommandOptionType.SingleValue:
                    if (Values.Any())
                    {
                        return false;
                    }
                    Values.Add(value);
                    break;
                case CommandOptionType.NoValue:
                    if (value != null)
                    {
                        return false;
                    }
                    // Add a value to indicate that this option was specified
                    Values.Add("on");
                    break;
                default:
                    break;
            }
            return true;
        }

        public bool HasValue()
        {
            return Values.Any();
        }

        public string Value()
        {
            return HasValue() ? Values[0] : null;
        }
    }
}