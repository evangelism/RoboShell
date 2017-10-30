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
                    return new ExpressionAnd(from z in X.Elements() select Expression.LoadXml(z));
                case "Compare":
                    if (X.Attribute("Type") == null) return new Comparison(X.Attribute("Var").Value, X.Attribute("Value").Value);
                    return new Comparison(X.Attribute("Var").Value, X.Attribute("Value").Value, X.Attribute("Type").Value);
                case "Interval":
                    return new Interval(X.Attribute("Var").Value, X.Attribute("Min").Value, X.Attribute("Max").Value);
                default:
                    throw new RuleEngineException("Invalid expression");
            }
        }

        public static Expression ParseString(string s)
        {
            string[] t;
            if (s.Contains('&'))
            {
                t = s.Split('&');
                return new ExpressionAnd(from x in t select Expression.ParseString(x));
            }
            if (s.Contains('|'))
            {
                t = s.Split('|');
                return new ExpressionOr(from x in t select Expression.ParseString(x));
            }
            if (s.Contains("="))
            {
                t = s.Split('=');
                return new Comparison(t[0].Trim(), t[1].Trim());
            }
            if (s.Contains("#"))
            {
                t = s.Split('#');
                return new Comparison(t[0].Trim(), t[1].Trim(), "ne");
            }
            if (s.Contains(">"))
            {
                t = s.Split('>');
                return new Comparison(t[0].Trim(), t[1].Trim(), "gt");
            }
            if (s.Contains("<"))
            {
                t = s.Split('<');
                return new Comparison(t[0].Trim(), t[1].Trim(), "lt");
            }
            throw new RuleEngineException("Invalid text expression");
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

        public Comparison(string Var, string Value) : this(Var, Value, "eq") { }



        public override string Eval(State s)
        {
            var x = s.Eval(Var);
            if (x == null) return false.AsString();
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

    public class Interval: Expression
    {
        public string Var { get; set; }
        public string Min { get; set; }
        public string Max { get; set; }

        public Interval(string Var, string Min, string Max)
        {
            this.Min = Min;
            this.Var = Var;
            this.Max = Max;
        }

        public override string Eval(State s)
        {
            var x = s.Eval(Var);
            if (x == null) return false.AsString();
            return (x.AsFloat() < Max.AsFloat() && x.AsFloat() > Min.AsFloat()).AsString();
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

    public class ExpressionOr : ExpressionSeq
    {
        public ExpressionOr(IEnumerable<Expression> Expressions) : base(Expressions) { }
        public override string Eval(State S)
        {
            foreach (var e in Operands)
            {
                if (e.Eval(S).AsBool()) return true.AsString();
            }
            return false.AsString();
        }

    }


}
