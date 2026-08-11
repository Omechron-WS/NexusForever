using System;
using System.Collections.Generic;
using System.Text;

namespace NexusForever.WorldServer.Command
{
    /// <summary>
    /// Tokenizes command text while preserving quoted string parameters.
    /// </summary>
    internal static class CommandTokenizer
    {
        /// <summary>
        /// Tokenize command text, collapsing whitespace outside quoted parameters.
        /// </summary>
        public static bool TryTokenize(string commandText, out string[] tokens)
        {
            ArgumentNullException.ThrowIfNull(commandText);

            var result = new List<string>();
            int index = 0;
            while (index < commandText.Length)
            {
                while (index < commandText.Length && char.IsWhiteSpace(commandText[index]))
                    index++;

                if (index == commandText.Length)
                    break;

                if (commandText[index] == '"')
                {
                    if (!TryReadQuotedToken(commandText, ref index, out string token))
                    {
                        tokens = [];
                        return false;
                    }

                    result.Add(token);
                    continue;
                }

                int start = index;
                while (index < commandText.Length && !char.IsWhiteSpace(commandText[index]))
                    index++;
                result.Add(commandText[start..index]);
            }

            tokens = result.ToArray();
            return true;
        }

        private static bool TryReadQuotedToken(string commandText, ref int index, out string token)
        {
            var builder = new StringBuilder();
            builder.Append(commandText[index++]);

            while (index < commandText.Length)
            {
                char value = commandText[index++];
                builder.Append(value);

                if (value == '\\' && index < commandText.Length)
                {
                    char escaped = commandText[index];
                    if (escaped is '"' or '\\')
                    {
                        builder.Append(escaped);
                        index++;
                    }

                    continue;
                }

                if (value != '"')
                    continue;

                if (index < commandText.Length && !char.IsWhiteSpace(commandText[index]))
                {
                    token = null;
                    return false;
                }

                token = builder.ToString();
                return true;
            }

            token = null;
            return false;
        }
    }
}
