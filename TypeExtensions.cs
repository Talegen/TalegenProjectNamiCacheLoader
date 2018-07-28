//-----------------------------------------------------------------------
// <copyright file="TypeExtensions.cs" company="Vasont Systems">
//     Copyright (c) 2016 Vasont Systems. All rights reserved.
// </copyright>
// <author>Rob Kennedy</author>
//-----------------------------------------------------------------------
namespace TalegenProjectNamiCacheLoader
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Globalization;
    using System.Linq;
    using System.Reflection;
    using System.Security.Cryptography;

    /// <summary>
    /// This class contains a number of extension methods for conversion of data values.
    /// </summary>
    public static class TypeExtensions
    {
        #region Private Constants
        /// <summary>
        /// Contains alpha-characters for password generation.
        /// </summary>
        private const string AlphanumericCharacters =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ" +
            "abcdefghijklmnopqrstuvwxyz" +
            "0123456789";
        #endregion

        /// <summary>
        /// This method is used to generate a random alpha-numeric string.
        /// </summary>
        /// <param name="length">Contains the length of the string.</param>
        /// <param name="characterSet">Optionally, contains the set of characters used for character selection.</param>
        /// <returns>Returns a string containing random characters.</returns>
        public static string RandomAlphaString(int length, IEnumerable<char> characterSet = null)
        {
            char[] result;

            if (length < 0 || length >= int.MaxValue)
            {
                length = 10;
            }

            if (characterSet == null)
            {
                characterSet = AlphanumericCharacters;
            }

            var characterArray = characterSet.Distinct().ToArray();

            var bytes = new byte[length * 8];

            using (RandomNumberGenerator rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(bytes);
                result = new char[length];

                for (int i = 0; i < length; i++)
                {
                    ulong value = BitConverter.ToUInt64(bytes, i * 8);
                    result[i] = characterArray[value % (uint)characterArray.Length];
                }
            }

            return new string(result);
        }

        /// <summary>
        /// Converts a string to an integer value returning default value for null or <see cref="DBNull"/> types.
        /// </summary>
        /// <param name="value">Contains the string value to convert.</param>
        /// <param name="defaultValue">Contains the default value to return if value cannot be converted.</param>
        /// <returns>Returns the converted or default integer value.</returns>
        public static int ToInt(this string value, int defaultValue = 0)
        {
            if (string.IsNullOrEmpty(value) || !int.TryParse(value, out var result))
            {
                result = defaultValue;
            }

            return result;
        }

        /// <summary>
        /// Converts an object to a string value returning default value on null or <see cref="DBNull"/> types.
        /// </summary>
        /// <param name="value">Contains the object value to convert.</param>
        /// <param name="defaultValue">Contains the default value to return if value cannot be converted.</param>
        /// <returns>Returns the converted or default string value.</returns>
        public static string ConvertToString(this object value, string defaultValue = "")
        {
            string result = defaultValue;

            if (value != null && value != DBNull.Value)
            {
                result = value.ToString();

                if (string.IsNullOrEmpty(result))
                {
                    result = defaultValue;
                }
            }

            return result;
        }
        
        /// <summary>
        /// This method is used to convert a string value to a boolean.
        /// </summary>
        /// <param name="value">Contains the value to convert.</param>
        /// <param name="defaultValue">Contains a default value to return when the value is invalid.</param>
        /// <returns>Returns the converted value or default value.</returns>
        public static bool ToBoolean(this string value, bool defaultValue = false)
        {
            bool result = defaultValue;
            string[] allowedPositives = { "T", "TRUE", "1", "Y", "YES", "O" };

            if (!string.IsNullOrWhiteSpace(value))
            {
                result = allowedPositives.Contains(value.ToUpperInvariant());
            }

            return result;
        }
        
        /// <summary>
        /// This method is used to recurse through an exception's inner exceptions and return a combined message string containing all error messages.
        /// </summary>
        /// <param name="ex">Contains the exception object to recurse.</param>
        /// <param name="recursionLevel">Contains the indentation level of the recursive messages.</param>
        /// <returns>Returns a string containing all related exception messages.</returns>
        public static string RecurseMessages(this Exception ex, int recursionLevel = 0)
        {
            string message = ex?.Message + Environment.NewLine;

            if (recursionLevel > 0)
            {
                message = new string('-', recursionLevel) + ">" + message;
            }

            if (ex?.InnerException != null)
            {
                message += ex.InnerException.RecurseMessages(++recursionLevel);
            }

            return message;
        }
    }
}
