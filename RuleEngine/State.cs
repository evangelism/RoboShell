using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RuleEngineNet
{
    public class State : ConcurrentDictionary<string,string>, IEvaluator
    {
        protected StringProcessor _string_proc;
        protected StringProcessor StringProc
        {
            get
            {
                if (_string_proc == null) _string_proc = new StringProcessor(this);
                return _string_proc;
            }
        }

        public State(State S) : base()
        {
            foreach (var x in S.Keys) this.TryAdd(x, S[x]);
        }

        public State() : base() { }

        public string Eval(string x)
        {
            if (!this.ContainsKey(x))
            {
//                System.Diagnostics.LogLib.Log.Trace($"Var {x} not found");
                return null;
            }
            return this[x];
        }

        public string EvalString(string s) => StringProc.Eval(s);

        public void Assign(string Var, string Value)
        {
            if (this.ContainsKey(Var)) this[Var] = Value;
            else this.AddOrUpdate(Var, Value, (key, oldValue) => (Value));
        }

        
        
        
    }
    public static class ConcurrentDictionaryEx
    {
        public static bool Remove<TKey, TValue>(
            this ConcurrentDictionary<TKey, TValue> self, TKey key)
        {
            return ((IDictionary<TKey, TValue>)self).Remove(key);
        }

    }
}
