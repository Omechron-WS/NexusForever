using System;
using System.Text;
using NexusForever.WorldServer.Command.Context;

namespace NexusForever.WorldServer.Command.Convert
{
    [Convert(typeof(string))]
    public class StringParameterConverter : IParameterConvert
    {
        public virtual object Convert(ICommandContext context, ParameterQueue queue)
        {
            string parameter = queue.Dequeue();
            if (!parameter.StartsWith('"'))
                return parameter;

            if (parameter.Length < 2 || !parameter.EndsWith('"'))
                throw new FormatException("Quoted string parameter is not terminated.");

            var sb = new StringBuilder();
            ReadOnlySpan<char> value = parameter.AsSpan(1, parameter.Length - 2);
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == '\\'
                    && i + 1 < value.Length
                    && value[i + 1] is '"' or '\\')
                {
                    sb.Append(value[++i]);
                    continue;
                }

                sb.Append(value[i]);
            }

            return sb.ToString();
        }
    }
}
