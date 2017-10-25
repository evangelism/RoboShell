using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RuleEngineNet
{
    public static class ExtraOps
    {
        public static Random Rnd = new Random();

        public static T OneOf<T>(this IEnumerable<T> A)
        {
            return A.ElementAt(Rnd.Next(0, A.Count()));
        }

        public static bool AsBool(this string s)
        {
            if ("t".Equals(s.ToLower())) return true;
            if ("f".Equals(s.ToLower())) return false;
            throw new RuleEngineException($"Cannot convert string {s} to bool");
        }

        public static float AsFloat(this string s)
        {
            try
            {
                return float.Parse(s);
            }
            catch
            {
                throw new RuleEngineException("Error converting string to number");
            }
        }

        public static string AsString(this bool x)
        {
            if (x) return "t";
            else return "f";
        }
    }
}
