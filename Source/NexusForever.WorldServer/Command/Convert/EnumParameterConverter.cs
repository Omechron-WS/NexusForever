using System;
using NexusForever.WorldServer.Command.Context;

namespace NexusForever.WorldServer.Command.Convert
{
    public class EnumParameterConverter<T> : IParameterConvert where T : Enum
    {
        public object Convert(ICommandContext context, ParameterQueue queue)
        {
            return Parse(queue.Dequeue());
        }

        internal static T Parse(string token)
        {
            if (token.Contains(',', StringComparison.Ordinal))
                throw new FormatException("Composite enum values are not supported.");

            return (T)Enum.Parse(typeof(T), token);
        }
    }
}
