using System;
using SpaceCG.IO;

namespace SpaceCG.Extensions
{
    internal static partial class TransportExtensions
    {
        public static string[] SplitParams(this string parameters)
        {
            if (string.IsNullOrWhiteSpace(parameters)) return Array.Empty<string>();

            if (parameters.IndexOf(',') != -1)
                return parameters.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            if (parameters.IndexOf(':') != -1)
                return parameters.Split(new char[] { ':' }, StringSplitOptions.RemoveEmptyEntries);

            if (parameters.IndexOf(';') != -1)
                return parameters.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            throw new ArgumentException("无效的参数格式。", nameof(parameters));
        }

        public static string ReadLine(this ITransportChannel channel)
        {
            return "";
        }
    }
}
