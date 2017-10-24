using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace RuleEngineNet
{

    public abstract class Expression
    {
        public abstract string Eval(State S);

        public static Expression LoadXml(XElement X)
        {
            switch (X.Name.LocalName)
            {
                case "And":
                    return new ExpressionAnd(from z in X.Descendants() select Expression.LoadXml(z));
                case "Compare":
                    return new Comparison(X.Attribute("Var").Value, X.Attribute("Value").Value, X.Attribute("Type").Value);
                default:
                    throw new RuleEngineException("Invalid expression");
            }
        }

    }

    public class Comparison : Expression
    {
        public string Var { get; set; }
        public string Value { get; set; }
        public string ComparisonType { get; set; }

        public Comparison(string Var, string Value, string ComparisonType)
        {
            this.ComparisonType = ComparisonType;
            this.Var = Var;
            this.Value = Value;
        }

        public override string Eval(State s)
        {
            var x = s.Eval(Var);
            switch (ComparisonType)
            {
                case "eq":
                    return (x == Value).AsString();
                case "ne":
                    return (x != Value).AsString();
                case "lt":
                    return (x.AsFloat() < Value.AsFloat()).AsString();
                case "gt":
                    return (x.AsFloat() > Value.AsFloat()).AsString();
                // TODO: Add other comparison ops
                default:
                    throw new RuleEngineException($"Invalid conversion op: {ComparisonType}");
            }
        }
    }

    public abstract class ExpressionSeq : Expression
    {
        public ExpressionSeq(IEnumerable<Expression> Expressions)
        {
            Operands = new List<Expression>(Expressions);
        }

        public List<Expression> Operands { get; private set; } = new List<Expression>();
    }

    public class ExpressionAnd : ExpressionSeq
    {
        public ExpressionAnd(IEnumerable<Expression> Expressions) : base(Expressions) { }
        public override string Eval(State S)
        {
            foreach (var e in Operands)
            {
                if (!e.Eval(S).AsBool()) return false.AsString();
            }
            return true.AsString();
        }

    }

}
