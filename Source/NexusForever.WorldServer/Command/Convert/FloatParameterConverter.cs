using System;
using System.Globalization;
using NexusForever.WorldServer.Command.Context;

namespace NexusForever.WorldServer.Command.Convert
{
    [Convert(typeof(float))]
    public class FloatParameterConverter : IParameterConvert
    {
        public object Convert(ICommandContext context, ParameterQueue queue)
        {
            return ParseFinite(queue.Dequeue());
        }

        internal static float ParseFinite(string value)
        {
            float result = float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
            if (!float.IsFinite(result))
                throw new FormatException("Non-finite floating-point values are not supported.");

            return result;
        }
    }
}
