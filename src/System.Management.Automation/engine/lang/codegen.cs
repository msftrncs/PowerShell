// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Text;

using Dbg = System.Management.Automation.Diagnostics;

namespace System.Management.Automation.Language
{
    /// <summary>
    /// Contains utility methods for use in applications that generate PowerShell code.
    /// </summary>
    public static class CodeGeneration
    {
        /// <summary>
        /// Escapes content so that it is safe for inclusion in a single-quoted string.
        /// For example: "'" + EscapeSingleQuotedStringContent(userContent) + "'"
        /// </summary>
        /// <param name="value">The content to be included in a single-quoted string.</param>
        /// <returns>Content with all single-quotes escaped.</returns>
        public static string EscapeSingleQuotedStringContent(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                sb.Append(c);
                if (CharExtensions.IsSingleQuote(c))
                {
                    // double-up quotes to escape them
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Escapes content so that it is safe for inclusion in a block comment.
        /// For example: "&lt;#" + EscapeBlockCommentContent(userContent) + "#&gt;"
        /// </summary>
        /// <param name="value">The content to be included in a block comment.</param>
        /// <returns>Content with all block comment characters escaped.</returns>
        public static string EscapeBlockCommentContent(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("<#", "<`#")
                .Replace("#>", "#`>");
        }

        /// <summary>
        /// Escapes content so that it is safe for inclusion in a string that will later be used as a
        /// format string. If this is to be embedded inside of a single-quoted string, be sure to also
        /// call EscapeSingleQuotedStringContent.
        /// For example: "'" + EscapeSingleQuotedStringContent(EscapeFormatStringContent(userContent)) + "'" -f $args.
        /// </summary>
        /// <param name="value">The content to be included in a format string.</param>
        /// <returns>Content with all curly braces escaped.</returns>
        public static string EscapeFormatStringContent(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                sb.Append(c);
                if (CharExtensions.IsCurlyBracket(c))
                {
                    // double-up curly brackets to escape them
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Escapes content so that it is safe for inclusion in a string that will later be used in a variable
        /// name reference. This is only valid when used within PowerShell's curly brace naming syntax.
        ///
        /// For example: '${' + EscapeVariableName('value') + '}'
        /// </summary>
        /// <param name="value">The content to be included as a variable name.</param>
        /// <returns>Content with all curly braces and back-ticks escaped.</returns>
        public static string EscapeVariableName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("`", "``")
                .Replace("}", "`}")
                .Replace("{", "`{");
        }

        /// <summary>
        /// Single-quote and escape a member name if it requires quoting, otherwise passing it unmodified.
        /// For example: QuoteMemberName(userContent)
        /// </summary>
        /// <param name="value">The content to be used as a member name in a member access.</param>
        /// <returns>Content quoted and escaped if required for use as a member name.</returns>
        public static string QuoteMemberName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            // determine if any character is not a standard indentifier character
            bool requiresQuote = !value[0].IsIdentifierStart();
            if (!requiresQuote)
            {
                foreach (char c in value.Substring(1))
                {
                    if (!c.IsIdentifierFollow())
                    {
                        requiresQuote = true;
                        break;
                    }
                }
            }

            // quote the content if required.
            return requiresQuote ?
                "'" + EscapeSingleQuotedStringContent(value) + "'" :
                value;
        }

        /// <summary>
        /// Quote an argument, if needed, or if specifically requested, escaping characters accordingly,
        /// handling escaping of wildcard patterns when argument is not already taken literally.
        /// For example: QuoteArgument(userContent, quoteInUse, isLiteralArgument)
        /// </summary>
        /// <param name="value">The content to be used as an argument value taken literally.</param>
        /// <param name="quoteInUse">The character to be quoted with.</param>
        /// <param name="isLiteralArgument">Treat the argument as taken literally, wildcard escaping not required.</param>
        /// <returns>Content quoted and escaped if required for use as an argument value.</returns>
        public static string QuoteArgument(string value, char quoteInUse, bool isLiteralArgument)
        {
            return string.IsNullOrEmpty(value) ?
                string.Empty :
                QuoteArgument(isLiteralArgument ? value : WildcardPattern.Escape(value), quoteInUse);
        }

        /// <summary>
        /// Quote an argument, if needed, or if specifically requested, escaping characters accordingly
        /// For example: QuoteArgument(userContent, quoteInUse)
        /// </summary>
        /// <param name="value">The content to be used as an argument value taken literally.</param>
        /// <param name="quoteInUse">The character to be quoted with.</param>
        /// <returns>Content quoted and escaped if required for use as an argument value.</returns>
        public static string QuoteArgument(string value, char quoteInUse)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            char quoteToUse = quoteInUse;
            if (quoteToUse == 0)
                if (ShouldArgumentNotBeBareword(value))
                    // argument value not compatible with bareword
                    quoteToUse = '\'';
                else
                    // return unmodified argument value
                    return value;

            // quote and escape argument as needed
            return quoteToUse + (quoteToUse.IsDoubleQuote() ? EscapeDoubleQuotedStringContent(value) :
                EscapeSingleQuotedStringContent(value)) + quoteToUse;
        }

        private static bool ShouldArgumentNotBeBareword(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            // Rules for bareword arguments:
            //   characters that cannot be in the first position: "@#<>"
            //   Pattern that cannot be in the first position: /[1-6]>/
            bool requiresQuote = "@#<>".IndexOf(value[0]) != -1 ||
                value.Length > 1 && '1' <= value[0] && value[0] <= '6' && value[1] == '>';

            if (!requiresQuote)
            {
                bool lastCharWasDollar = false;
                foreach (char c in value)
                {
                    //   Characters that cannot appear anywhere 
                    //     ForceStartNewToken
                    //     IsSingleQuote
                    //     IsDoubleQuote
                    if (c.ForceStartNewToken() || c.IsSingleQuote() || c.IsDoubleQuote() || c == '`')
                    {
                        requiresQuote = true;
                        break;
                    }
                    if (lastCharWasDollar)
                        //   IsVariableStart characters cannot appear after a `$`
                        if (c.IsVariableStart())
                        {
                            requiresQuote = true;
                            break;
                        }
                    lastCharWasDollar = c == '$';
                }
            }
            return requiresQuote;
        }

        /// <summary>
        /// Escapes content so that it is safe for inclusion in a double-quoted string.
        /// For example: "\"" + EscapeDoubleQuotedStringContent(userContent) + "\""
        /// </summary>
        /// <param name="value">The content to be included in a double-quoted string.</param>
        /// <returns>Content with all `$`, backticks and double-quotes escaped.</returns>
        public static string EscapeDoubleQuotedStringContent(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            StringBuilder sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (c == '$')
                    // escape `$`
                    sb.Append('`');
                sb.Append(c);
                if (CharExtensions.IsDoubleQuote(c) || c == '`')
                    // double-up quotes & backticks to escape them
                    sb.Append(c);
            }

            return sb.ToString();
        }
    }
}
