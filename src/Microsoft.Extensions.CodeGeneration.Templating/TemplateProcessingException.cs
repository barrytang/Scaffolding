// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Extensions.CodeGeneration.Templating
{
    public class TemplateProcessingException : Exception
    {
        public TemplateProcessingException([NotNull]IEnumerable<string> messages, string generatedCode)
            : base(FormatMessage(messages))
        {
            Messages = messages;
            GeneratedCode = generatedCode;
        }

        public string GeneratedCode { get; private set; }

        public IEnumerable<string> Messages { get; private set; }

        public override string Message
        {
            get
            {
                return "Template Processing Failed:" + FormatMessage(Messages);
            }
        }

        private static string FormatMessage(IEnumerable<string> messages)
        {
            return String.Join(Environment.NewLine, messages);
        }
    }
}