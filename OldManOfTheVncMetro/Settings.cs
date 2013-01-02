//-------------------------------------------------------------------------------------------------
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
//-------------------------------------------------------------------------------------------------

namespace OldManOfTheVncMetro
{
    using System;
    using System.Threading.Tasks;
    using Windows.Security.Cryptography;
    using Windows.Security.Cryptography.DataProtection;
    using Windows.Storage;

    internal static class Settings
    {
        public static async Task<string> GetLocalSetting(string name, string defaultValue = "", bool isEncrypted = false)
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

        public static async void SetLocalSetting(string name, string value, bool isEncrypted = false)
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
