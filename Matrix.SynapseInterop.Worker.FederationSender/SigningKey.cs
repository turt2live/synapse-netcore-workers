using System;
using System.Buffers.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Sodium;
namespace Matrix.SynapseInterop.Worker.FederationSender
{
    public class SigningKey
    {
        public readonly string Type;
        public readonly string Name;
        public readonly string PublicKey;
        private readonly KeyPair Pair;

        private static readonly JsonSerializerSettings CanonicalSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.None,
        };

        public SigningKey(string keyContents)
        {
            var split = keyContents.Split(" ");
            if (split.Length != 3)
            {
                throw new InvalidDataException("Key file is in the wrong format. Should be '$type $name $key'");
            }

            Type = split[0];
            Name = split[1];
            // Synapse uses unpadded base64, so we need to add our own padding to make this work.
            var b64Seed = split[2].Trim();
            while (b64Seed.Length % 4 != 0)
            {
                b64Seed += "=";
            }

            Pair = PublicKeyAuth.GenerateKeyPair(
                Convert.FromBase64String(b64Seed)
            );
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
            string json = JsonConvert.SerializeObject(ordered, CanonicalSettings);
            string b64 = Convert.ToBase64String(PublicKeyAuth.SignDetached(json, Pair.PrivateKey));
            return b64.TrimEnd('=');
        }

        // https://stackoverflow.com/a/28557035 - Ordering members.
        public static JObject SortPropertiesAlphabetically(JObject original)
        {
            var result = new JObject();
            foreach (var property in original.Properties().ToList().OrderBy(p => p.Name))
            {
                var value = property.Value as JObject;
                var valueArr = property.Value as JArray;
                if (value != null)
                {
                    value = SortPropertiesAlphabetically(value);
                    result.Add(property.Name, value);
                } else if (valueArr != null)
                {
                    var res = new JArray();
                    foreach (var jToken in valueArr)
                    {
                        if (jToken is JObject o)
                        {
                            res.Add(SortPropertiesAlphabetically(o));
                        }
                        else
                        {
                            res.Add(jToken);
                        }
                    }
                    result.Add(property.Name, res);
                }
                else
                {
                    result.Add(property.Name, property.Value);
                }
            }

            return result;
        }

        public static void TestSigning()
        {
            var key = new SigningKey("ed25519 1 YJDBA9Xnr2sVqXD9Vj7XVUnmFZcZrlw8Md7kMW+3XA1");
            var json = @"{
                ""content"": {
                    ""body"": ""Here is the message content"",
                },
                ""event_id"": ""$0:domain"",
                ""origin"": ""domain"",
                ""origin_server_ts"": 1000000,
                ""type"": ""m.room.message"",
                ""room_id"": ""!r:domain"",
                ""sender"": ""@u:domain"",
                ""signatures"": {},
                ""unsigned"": {
                    ""age_ts"": 1000000
                }
            }";
            var signOne = key.SignJson(JObject.Parse(json));
        }
    }
}