// <copyright file="Settings.cs" company="Paul C. Roberts">
//  Copyright 2012 Paul C. Roberts
//
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file 
//  except in compliance with the License. You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software distributed under the 
//  License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, 
//  either express or implied. See the License for the specific language governing permissions and 
//  limitations under the License.
// </copyright>

namespace OldManOfTheVncMetro
{
    using System;
    using System.Threading.Tasks;
    using Windows.Security.Cryptography;
    using Windows.Security.Cryptography.DataProtection;
    using Windows.Storage;

    /// <summary>A static class that abstracts access to local settings.</summary>
    internal static class Settings
    {
        /// <summary>Get the value of a local setting.</summary>
        /// <param name="name">The name of the setting.</param>
        /// <param name="defaultValue">The default value.</param>
        /// <param name="isEncrypted">Is the setting encrypted.</param>
        /// <returns>The value of the setting if available, or the default value.</returns>
        public static async Task<string> GetLocalSettingAsync(string name, string defaultValue = "", bool isEncrypted = false)
        {
            object value = ApplicationData.Current.LocalSettings.Values[name];
            if (value == null)
            {
                return defaultValue;
            }

            if (isEncrypted)
            {
                try
                {
                    var dpp = new DataProtectionProvider();
                    var decrypted = await dpp.UnprotectAsync(CryptographicBuffer.DecodeFromBase64String(value.ToString()));

                    return CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf8, decrypted);
                }
                catch
                {
                    return defaultValue;
                }
            }
            else
            {
                return value.ToString();
            }
        }

        /// <summary>Sets the value of a local setting.</summary>
        /// <param name="name">The name of the setting.</param>
        /// <param name="value">The value to set.</param>
        /// <param name="isEncrypted">Indicates if the value should be encrypted.</param>
        public static async void SetLocalSettingAsync(string name, string value, bool isEncrypted = false)
        {
            if (isEncrypted)
            {
                var dpp = new DataProtectionProvider("LOCAL=user");
                var encrypted = await dpp.ProtectAsync(CryptographicBuffer.ConvertStringToBinary(value, BinaryStringEncoding.Utf8));
                var base64 = CryptographicBuffer.EncodeToBase64String(encrypted);

                ApplicationData.Current.LocalSettings.Values[name] = base64;
            }
            else
            {
                ApplicationData.Current.LocalSettings.Values[name] = value;
            }
        }
    }
}
