﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RuleEngineNet
{
    public class State : Dictionary<string,string>, IEvaluator
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
            foreach (var x in S.Keys) this.Add(x, S[x]);
        }

        public State() : base() { }

        public string Eval(string x)
        {
            if (!this.ContainsKey(x))
            {
                System.Diagnostics.Debug.WriteLine($"Var {x} not found");
                return null;
            }
            return this[x];
        }

        public string EvalString(string s) => StringProc.Eval(s);

        public void Assign(string Var, string Value)
        {
            if (this.ContainsKey(Var)) this[Var] = Value;
            else this.Add(Var, Value);
        }
    }
}
