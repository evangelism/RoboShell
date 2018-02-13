using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using RuleEngineNet;
// ReSharper disable InvertIf
// ReSharper disable StringIndexOfIsCultureSpecific.1

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


        public static Expression ParseExpressionsSequence(string expressionsSequence) {
            Expression expression = null;
            expression = ParseAtomicExpression(expressionsSequence);
                
            if (expression == null) {
                int firstOpeningBracePosition = expressionsSequence.IndexOf("(");
                int[] closingBracesPositions = BracketedConfigProcessor.AllIndexesOf(expressionsSequence, ")");
                if (firstOpeningBracePosition == -1 || closingBracesPositions.Length != 0) {
                    if (closingBracesPositions.Length > 0) {
                        for (int tryingClosingBraceIndex = 0;
                            tryingClosingBraceIndex != closingBracesPositions.Length;
                            tryingClosingBraceIndex++) {

                            int possibleExpression1Start = firstOpeningBracePosition + 1;
                            int possibleExpression1End = closingBracesPositions[tryingClosingBraceIndex];
                            int possibleExpression1Length = possibleExpression1End - possibleExpression1Start;

                            Expression expression1 = null;
                            if (possibleExpression1Length >= 0) {
                                string possibleExpression1Substring =
                                    expressionsSequence.Substring(possibleExpression1Start, possibleExpression1Length);
                                expression1 = ParseExpressionsSequence(possibleExpression1Substring);
                            }

                            if (expression1 != null) {
                                string restOfString = expressionsSequence.Substring(possibleExpression1End);

                                if (!Regex.IsMatch(restOfString, @"^\s*\)\s*$")) {
                                    if (Regex.IsMatch(restOfString, @"^\s*\)\s*(and|AND).+$")) {
                                        string possibleExpression2Substring =
                                            restOfString.Substring(restOfString.IndexOf("and") + "and".Length);
                                        Expression expression2 = ParseExpressionsSequence(possibleExpression2Substring);
                                        if (expression1 != null && expression2 != null) {
                                            expression = new ExpressionAnd(new List<Expression> {expression1, expression2});
                                        }
                                    }
                                    else if (Regex.IsMatch(restOfString, @"^\s*\)\s*(or|OR).+$")) {
                                        string possibleExpression2Substring =
                                            restOfString.Substring(restOfString.IndexOf("or") + "or".Length);
                                        Expression expression2 = ParseExpressionsSequence(possibleExpression2Substring);
                                        if (expression1 != null && expression2 != null) {
                                            expression = new ExpressionOr(new List<Expression> {expression1, expression2});
                                        }
                                    }
                                }
                                else {
                                    expression = expression1;
                                }

                                if (expression != null) {
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            return expression;
        }

        private static Expression ParseAtomicExpression(string expressionsSequence) {
            string prettyExpressionsSequence = expressionsSequence.Trim();
            string COMPARISON_MARK_REGEX = $@"([<>]|!=|==)";
            string FLOAT_REGEX = @"-?[0-9]*(?:\.[0-9]*)?";
            string STRING_COMPARISON_REGEX =
                $"^\\$(?<var>{BracketedConfigProcessor.VARNAME_REGEX_PATTERN})\\s*(?<cmp>{COMPARISON_MARK_REGEX})\\s*\".*\"$";
            string FLOAT_COMPARISON_REGEX =
                $"^\\$(?<var>{BracketedConfigProcessor.VARNAME_REGEX_PATTERN})\\s*(?<cmp>{COMPARISON_MARK_REGEX})\\s*(?<arg>{FLOAT_REGEX})\\s*$";

            string INTERVAL_REGEX =
                $"^\\$(?<var>{BracketedConfigProcessor.VARNAME_REGEX_PATTERN})\\s*in\\s*\\[(?<min>{FLOAT_REGEX})\\s*,(?<max>{FLOAT_REGEX})\\s*\\]$";

            Expression expression = null;

            
            if (Regex.IsMatch(prettyExpressionsSequence, STRING_COMPARISON_REGEX)) {
                int firstQuotePosition = prettyExpressionsSequence.IndexOf("\"");
                int lastQuotePosition = prettyExpressionsSequence.LastIndexOf("\"");
                int start = firstQuotePosition + 1;
                int len = lastQuotePosition - start;
                string possibleString = prettyExpressionsSequence.Substring(start, len);
                if (BracketedConfigProcessor.AssertValidString(possibleString)) {

                    Match m = Regex.Match(prettyExpressionsSequence, STRING_COMPARISON_REGEX);
                    if (m.Length != 0) {
                        string cmp = m.Groups["cmp"].Value;
                        string comparisonType = null;

                        switch (cmp) {
                            case "<":
                                comparisonType = "lt";
                                break;
                            case ">":
                                comparisonType = "gt";
                                break;
                            case "!=":
                                comparisonType = "ne";
                                break;
                            case "==":
                                comparisonType = "eq";
                                break;
                        }

                        if (comparisonType != null) {
                            expression = new Comparison(m.Groups["var"].Value, possibleString, comparisonType);
                        }
                    }
                }
            }
            else if (Regex.IsMatch(prettyExpressionsSequence, FLOAT_COMPARISON_REGEX)) {
                Match m = Regex.Match(prettyExpressionsSequence, FLOAT_COMPARISON_REGEX);
                if (m.Length != 0) {
                    string cmp = m.Groups["cmp"].Value;
                    string comparison_type = null;

                    switch (cmp) {
                        case "<":
                            comparison_type = "lt";
                            break;
                        case ">":
                            comparison_type = "gt";
                            break;
                        case "!=":
                            comparison_type = "ne";
                            break;
                        case "==":
                            comparison_type = "eq";
                            break;
                    }

                    if (comparison_type != null) {
                        expression = new Comparison(m.Groups["var"].Value, m.Groups["arg"].Value, comparison_type);
                    }
                }
            }
            else if (Regex.IsMatch(prettyExpressionsSequence, INTERVAL_REGEX)) {
                Match m = Regex.Match(prettyExpressionsSequence, INTERVAL_REGEX);
                if (m.Length != 0) {
                    expression = new Interval(m.Groups["var"].Value, m.Groups["min"].Value, m.Groups["max"].Value);
                }
            }
                

            return expression;
            
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
