// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

#if NET45
using System.IO;
using System.Reflection;

namespace klr.hosting
{
    public class LoaderEngine
    {
        public Assembly LoadFile(string path)
        {
            return Assembly.LoadFile(path);
        }

        public Assembly LoadStream(Stream assemblyStream, Stream pdbStream)
        {
            byte[] assemblyBytes = GetStreamAsByteArray(assemblyStream);
            byte[] pdbBytes = null;

            if (pdbStream != null)
            {
                pdbBytes = GetStreamAsByteArray(pdbStream);
            }

            return Assembly.Load(assemblyBytes, pdbBytes);
        }

        private byte[] GetStreamAsByteArray(Stream stream)
        {
            // Fast path assuming the stream is a memory stream
            var ms = stream as MemoryStream;
            if (ms != null)
            {
                return ms.ToArray();
            }

            // Otherwise copy the bytes
            using (ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }
    }
}
#endif