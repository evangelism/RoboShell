using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using RuleEngineNet;

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
        public bool ExecutedAlready { get; set; } = false;

        public string RuleSet { get; set; }

        public static Rule LoadXml(XElement X)
        {
            List<Expression> t;
            var IfNode = X.Descendants("If").First();
            if (IfNode.Elements()!=null && IfNode.Elements().Count()>0)
            {
                t = (from x in X.Descendants("If").First().Elements()
                     select Expression.LoadXml(x)).ToList();
            }
            else t = new List<Expression>();
            // var t = (t1 == null || t1.Count() == 0) ? new List<Expression>() : t1.ToList(); 
            if (IfNode.Attribute("Text")!=null)
            {
                t.Insert(0, Expression.ParseString(IfNode.Attribute("Text").Value));
            }
            Expression _if;
            if (t.Count == 1) _if = t[0];
            else _if = new ExpressionAnd(t);
            var s = (from x in X.Descendants("Then").First().Elements()
                     select Action.LoadXml(x)).ToList();
            Action _then;
            if (s.Count == 1) _then = s[0];
            else _then = new CombinedAction(s);
            var R = new Rule(_if, _then);
            if (X.Attribute("Priority")!=null)
            {
                R.Priority = int.Parse(X.Attribute("Priority").Value);
            }
            if (X.Attribute("RuleSet") != null)
            {
                R.RuleSet = X.Attribute("RuleSet").Value;
            }
            return R;
        }

        public static Rule ParseRule(string ruleContainingString) {
            Rule rule;
            string oldRuleContainingString = ruleContainingString;
            string FULL_METAINFO_REGEX =
                $"priority\\s*=\\s*(?<priority1>\\d+)\\s*rule_set\\s*=\\s*\\\"(?<rule_set1>{BracketedConfigProcessor.VARNAME_REGEX_PATTERN})\\\"" +
                "|" +
                $"rule_set\\s*=\\s*\\\"(?<rule_set2>{BracketedConfigProcessor.VARNAME_REGEX_PATTERN})\\\"\\s*priority\\s*=\\s*(?<priority2>\\d+)";
            string NONFULL_METAINFO_REGEX =
                "priority\\s*=\\s*(?<priority>\\d+)" + "|" +
                $"rule_set\\s*=\\s*\\\"(?<rule_set>{BracketedConfigProcessor.VARNAME_REGEX_PATTERN})\\\"";

            string RULE_WITH_FULL_METAINFO_REGEX =
                $"^\\(\\s*\\(\\s*({FULL_METAINFO_REGEX})\\s*\\)\\s*\\((?<rule>.*)\\)\\s*\\)$";
            string RULE_WITH_NONFULL_METAINFO_REGEX =
                $"^\\(\\s*\\(\\s*({NONFULL_METAINFO_REGEX})\\s*\\)\\s*\\((?<rule>.*)\\)\\s*\\)$";


            string priority = null;
            string rule_set = null;
            Match metaInfo = Regex.Match(ruleContainingString, RULE_WITH_FULL_METAINFO_REGEX);
            if (metaInfo.Length > 0) {
                if (metaInfo.Groups["priority1"].Value != null) {
                    priority = metaInfo.Groups["priority1"].Value;
                    rule_set = metaInfo.Groups["rule_set1"].Value;
                }
                else {
                    priority = metaInfo.Groups["priority2"].Value;
                    rule_set = metaInfo.Groups["rule_set2"].Value;
                }

                ruleContainingString = metaInfo.Groups["rule"].Value;
            }
            else {
                metaInfo = Regex.Match(ruleContainingString, RULE_WITH_NONFULL_METAINFO_REGEX);
                if (metaInfo.Length > 0) {
                    priority = metaInfo.Groups["priority"].Value;
                    rule_set = metaInfo.Groups["rule_set"].Value;

                    ruleContainingString = metaInfo.Groups["rule"].Value;
                }
            }

            if (rule_set == "") rule_set = null;
            if (priority == "") priority = null;


            int[] arrowsPositions =
                BracketedConfigProcessor.AllIndexesOf(ruleContainingString, "=>");
            int tryingArrowPositionIndex = 0;
            // TODO check arrow-containing expr or act
            while (true) {
                int tryingArrowPosition = arrowsPositions[tryingArrowPositionIndex];
                int firstSymbolAfterArrowPosition = tryingArrowPosition + 2;
                int possibleActionStringLength = ruleContainingString.Length - firstSymbolAfterArrowPosition;
                Expression expr = Expression.ParseExpressionsSequence(ruleContainingString.Substring(0, tryingArrowPosition));
                Action act = Action.ParseActionSequence(ruleContainingString.Substring(firstSymbolAfterArrowPosition, possibleActionStringLength));
                if (expr != null && act != null) {
                    rule = new Rule(expr, act);
                    if (rule_set != null) {
                        rule.RuleSet = rule_set;
                    }
                    if (priority != null) {
                        rule.Priority = Int32.Parse(priority);
                    }

                    return rule;
                }
                else {
                    tryingArrowPositionIndex++;
                }               
            }

        }
    }
}
