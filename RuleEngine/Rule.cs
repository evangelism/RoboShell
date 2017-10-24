using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace RuleEngineNet
{
    public class Rule
    {
        public Rule() { }

        public Rule(Expression If, Action Then)
        {
            this.If = If;
            this.Then = Then;
        }
        public Expression If { get; set; }
        public Action Then { get; set; }
        public bool ExecutionFlag { get; set; } = false;
        public int Priority { get; set; } = 100;
        public bool Active { get; set; } = true;

        public static Rule LoadXml(XElement X)
        {
            var t = (from x in X.Descendants("If").First().Descendants()
                     select Expression.LoadXml(x)).ToList();
            Expression _if;
            if (t.Count == 1) _if = t[0];
            else _if = new ExpressionAnd(t);
            var s = (from x in X.Descendants("Then").First().Descendants()
                     select Action.LoadXml(x)).ToList();
            Action _then;
            if (s.Count == 1) _then = s[0];
            else _then = new CombinedAction(s);
            return new Rule(_if, _then);
        }

    }
}
