using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RuleEngineNet
{
    public interface IEvaluator
    {
        string Eval(string s);
    }

    public class StringProcessor : IEvaluator
    {
        public IEvaluator Evaluator { get; private set; }
        public StringProcessor(IEvaluator Eval)
        {
            Evaluator = Eval;
        }

        public static string ApplyRegex(string RegEx, Func<string,string> f, string inp)
        {
            var rx = new Regex(RegEx);
            return rx.Replace(inp, (Match m) => f(m.ToString()) );
        }

        private string Proc1(string x)
        {
            x = x.Trim('{', '}');
            if (x.Contains(':')) // {val:A=a|B=b|C}
            {
                var t = x.Split(':');
                var s = t[1].Split('|');
                foreach(var z in s)
                {
                    if (!z.Contains("=")) return z;
                    var t1 = z.Split('=');
                    if (t1[0] == t[0]) return t1[1];
                }
                return "";
            }
            else // {A|B}
            {
                return x.Split('|').OneOf();
            }
        }

        public string Eval(string s)
        {
            if (!s.Contains('{')) return s;
            // 1. Handle ${var} expressions
            s = StringProcessor.ApplyRegex(@"\${(.*?)}", x => Evaluator.Eval(x.Trim('$','{', '}')),s);
            // 2. Handle {A|B} and {val:A=a|B=b}
            s = StringProcessor.ApplyRegex(@"{(.*?)}", Proc1, s);
            return s;
        }

    }
}
