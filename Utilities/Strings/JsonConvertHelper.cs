using Newtonsoft.Json.Serialization;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace Utilities
{
    class IgnorePropertiesContractResolver : DefaultContractResolver
    {
        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            JsonProperty property = base.CreateProperty(member, memberSerialization);

            // Ignore properties of type Stream
            if (property.PropertyType == typeof(Stream))
            {
                property.Ignored = true;
            }
            


            return property;
        }
    }
    public static class JsonConvertHelper
    {
        public static string Serialize(object obj)
        {
            JsonSerializerSettings settings = new JsonSerializerSettings
            {
                ContractResolver = new IgnorePropertiesContractResolver(),
            };

            string json = JsonConvert.SerializeObject(obj, settings);
            return json;
        }
    }
}
