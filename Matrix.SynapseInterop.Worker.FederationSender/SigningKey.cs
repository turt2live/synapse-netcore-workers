﻿using System;
using System.IO;
using System.Linq;
using Microsoft.EntityFrameworkCore.Internal;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sodium;

namespace Matrix.SynapseInterop.Worker.FederationSender
{
    public class SigningKey
    {
        private static readonly JsonSerializerSettings CanonicalSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.None
        };

        public readonly string Name;
        private readonly KeyPair Pair;
        public readonly string PublicKey;
        public readonly string Type;

        public SigningKey(string keyContents)
        {
            var split = keyContents.Split(" ");

            if (split.Length != 3)
                throw new InvalidDataException("Key file is in the wrong format. Should be '$type $name $key'");

            Type = split[0];
            Name = split[1];
            // Synapse uses unpadded base64, so we need to add our own padding to make this work.
            var b64Seed = split[2].Trim();

            while (b64Seed.Length % 4 != 0) b64Seed += "=";

            Pair = PublicKeyAuth.GenerateKeyPair(Convert.FromBase64String(b64Seed));
            PublicKey = Convert.ToBase64String(Pair.PublicKey);
        }

        public static SigningKey ReadFromFile(string keyFile)
        {
            var content = File.ReadAllText(keyFile);
            return new SigningKey(content);
        }

        public string SignJson(JObject jsonObject)
        {
            jsonObject.Remove("signatures");
            jsonObject.Remove("unsigned");
            var ordered = SortPropertiesAlphabetically(jsonObject);
            var json = JsonConvert.SerializeObject(ordered, CanonicalSettings);
            var b64 = Convert.ToBase64String(PublicKeyAuth.SignDetached(json, Pair.PrivateKey));
            return b64.TrimEnd('=');
        }

        // https://stackoverflow.com/a/28557035 - Ordering members.
        public static JObject SortPropertiesAlphabetically(JObject original)
        {
            var result = new JObject();
            var enumerable = original.Properties().ToList().OrderByOrdinal(p => p.Name);

            foreach (var property in enumerable)
            {
                var value = property.Value as JObject;
                var valueArr = property.Value as JArray;

                if (value != null)
                {
                    value = SortPropertiesAlphabetically(value);
                    result.Add(property.Name, value);
                }
                else if (valueArr != null)
                {
                    var res = new JArray();

                    foreach (var jToken in valueArr)
                        if (jToken is JObject o)
                            res.Add(SortPropertiesAlphabetically(o));
                        else
                            res.Add(jToken);

                    result.Add(property.Name, res);
                }
                else
                {
                    result.Add(property.Name, property.Value);
                }
            }

            return result;
        }
    }
}
