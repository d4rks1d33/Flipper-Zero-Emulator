using System;
using System.Linq;

namespace Antmicro.OptionsParser
{
    public static class ParseHelper
    {
        public static bool TryParse<T>(string str, out T value)
        {
            object result;
            if(TryParse(str, typeof(T), out result))
            {
                value = (T)result;
                return true;
            }

            value = default(T);
            return false;
        }

        public static bool TryParse(string str, Type type, out object value)
        {
            if(str == null)
            {
                value = null;
                return false;
            }
            
            if(type == typeof(string))
            {
                value = str;
                return true;
            }
            
            if(type.IsEnum)
            {
                try 
                {
                    value = Enum.Parse(type, str, true);
                    return true;
                }
                catch(ArgumentException) 
                {
                    value = null;
                    return false;
                }
            }

            if(!SupportedTypes.Contains(type))
            {
                value = null;
                return false;
            }

            var parameters = new object[] { str, null };
            var parseMethod = type.GetMethod("TryParse", new [] { typeof(string), type.MakeByRefType() });
            var result = (bool)parseMethod.Invoke(null, parameters);
            value = parameters[1];
            return result;
        }

        private static readonly Type[] SupportedTypes =
        {
            typeof(int),
            typeof(bool),
            typeof(float)
        };
    }
}

