using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RuleEngineNet
{
    public class State : Dictionary<string,string>
    {
        public State(State S) : base()
        {
            foreach (var x in S.Keys) this.Add(x, S[x]);
        }

        public string Eval(string x)
        {
            return this[x];
        }

        public void Assign(string Var, string Value)
        {
            if (this.ContainsKey(Var)) this[Var] = Value;
            else this.Add(Var, Value);
        }
    }
}
