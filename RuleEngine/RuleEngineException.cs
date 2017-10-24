using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RuleEngineNet
{
    public class RuleEngineException : Exception
    {
        public RuleEngineException(string s) : base(s) { }
    }
}
