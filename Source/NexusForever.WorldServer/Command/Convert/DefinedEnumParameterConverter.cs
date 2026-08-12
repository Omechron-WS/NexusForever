using System;
using NexusForever.WorldServer.Command.Context;

namespace NexusForever.WorldServer.Command.Convert
{
    public sealed class DefinedEnumParameterConverter<T> : IParameterConvert where T : Enum
    {
        public object Convert(ICommandContext context, ParameterQueue queue)
        {
            T value = EnumParameterConverter<T>.Parse(queue.Dequeue());
            if (!Enum.IsDefined(typeof(T), value))
                throw new FormatException($"Value is not defined for {typeof(T).Name}.");

            return value;
        }
    }
}
