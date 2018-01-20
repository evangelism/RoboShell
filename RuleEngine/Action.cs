using RoboLogic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using RuleEngineNet;

namespace RuleEngineNet
{
    public abstract class Action
    {
        public abstract void Execute(State S);
        public virtual bool LongRunning { get; } = false;
        public static Action LoadXml(XElement X)
        {
            switch(X.Name.LocalName)
            {
                case "Assign":
                    return Assign.Parse(X);
                case "Clear":
                    return Clear.Parse(X);
                case "Say":
                    return Say.Parse(X);
                case "Extension":
                    return Extension.Parse(X);
                case "OneOf":
                    return new OneOf(from z in X.Elements() select Action.LoadXml(z));
                default:
                    throw new RuleEngineException("Unsupported action type");
            }
        }

        public static Action ParseActionSequence(string actionsSequence) {
            Action action = null;

            try {
                action = ParseAtomicAction(actionsSequence);
                return action;
            }
            catch {
                int firstOpeningBracePosition = actionsSequence.IndexOf("(");
                int[] closingBracesPositions =
                    BracketedConfigProcessor.AllIndexesOf(actionsSequence, ")");
                if (firstOpeningBracePosition != -1 && closingBracesPositions.Length == 0) {
                    throw new ActionParseException();
                }

                if (closingBracesPositions.Length > 0) {
                    for (int tryingClosingBraceIndex = 0;
                        tryingClosingBraceIndex != closingBracesPositions.Length;
                        tryingClosingBraceIndex++) {
                        try {
                            int possibleExpression1Start = firstOpeningBracePosition + 1;
                            int possibleAction1End =
                                closingBracesPositions[tryingClosingBraceIndex];
                            int possibleExpression1Length =
                                possibleAction1End - possibleExpression1Start;

                            string possibleAction1Substring =
                                actionsSequence.Substring(possibleExpression1Start,
                                    possibleExpression1Length);
                            Action action1 = ParseActionSequence(possibleAction1Substring);

                            string restOfString = actionsSequence.Substring(possibleAction1End);

                            if (!Regex.IsMatch(restOfString, @"^\s*\)\s*$")) {
                                if (Regex.IsMatch(restOfString, @"^\s*\)\s*(and|AND).+$")) {
                                    string possibleAction2Substring =
                                        restOfString.Substring(
                                            restOfString.IndexOf("and") + "and".Length);
                                    Action action2 = ParseActionSequence(possibleAction2Substring);
                                    if (action1 == null || action2 == null)
                                        throw new ActionParseException();
                                    action = new CombinedAction(
                                        new List<Action> { action1, action2 });
                                }
                                else if (Regex.IsMatch(restOfString, @"^\s*\)\s*(or|OR).+$")) {
                                    string possibleAction2Substring =
                                        restOfString.Substring(
                                            restOfString.IndexOf("or") + "or".Length);
                                    Action action2 = ParseActionSequence(possibleAction2Substring);
                                    if (action1 == null || action2 == null)
                                        throw new ActionParseException();
                                    action = new OneOf(new List<Action> { action1, action2 });
                                }
                                else {
                                    throw new ActionParseException();
                                }

                                break;
                            }
                            else {
                                action = action1;
                            }

                            break;
                        }
                        catch {
                        }
                    }

                    if (action == null) {
                        throw new ActionParseException();
                    }
                }

                return action;
            }
        }

        private static Action ParseAtomicAction(string actionSequence) {
            string ASSIGNEMENT_STRING_REGEX = $"^(?<var>{BracketedConfigProcessor.VARNAME_REGEX_PATTERN})\\s*=\\s*\\\"(?<value>\\S+)\\\"$";
            string ASSIGNEMENT_REGEX =
                $"^(?<var>{BracketedConfigProcessor.VARNAME_REGEX_PATTERN})\\s*=\\s*(?<value>\\S+)$";
            string CLEAR_REGEX = $"^clear\\s+\\$(?<var>{BracketedConfigProcessor.VARNAME_REGEX_PATTERN})$";
            string SAY_REGEX = $"^say\\s+\".*\"$";
            string EXTERNAL_ACTION_NAME_REGEX_PATTERN = BracketedConfigProcessor.VARNAME_REGEX_PATTERN;
            string EXTERNAL_REGEX =
                $"^ext:(?<method>{EXTERNAL_ACTION_NAME_REGEX_PATTERN})\\s+\".*\"$";
            Action action = null;

            string prettyActionSequence = actionSequence.Trim();
            try {
                if (Regex.IsMatch(prettyActionSequence, ASSIGNEMENT_STRING_REGEX)) {
                    Match m = Regex.Match(prettyActionSequence, ASSIGNEMENT_STRING_REGEX);
                    if (m.Length == 0) throw new ActionParseException();

                    action = new Assign(m.Groups["var"].Value, m.Groups["value"].Value);
                    //Console.WriteLine($"parsed {actionSequence} as assignement {m.Groups["var"].Value}:={m.Groups["value"].Value}");
                }
                else if (Regex.IsMatch(prettyActionSequence, ASSIGNEMENT_REGEX)) {
                    Match m = Regex.Match(prettyActionSequence, ASSIGNEMENT_REGEX);
                    if (m.Length == 0) throw new ActionParseException();

                    action = new Assign(m.Groups["var"].Value, m.Groups["value"].Value);
                    //Console.WriteLine($"parsed {actionSequence} as assignement {m.Groups["var"].Value}:={m.Groups["value"].Value}");
                }
                else if (Regex.IsMatch(prettyActionSequence, CLEAR_REGEX)) {
                    Match m = Regex.Match(prettyActionSequence, CLEAR_REGEX);
                    if (m.Length == 0) throw new ActionParseException();

                    action = new Clear(m.Groups["var"].Value);
                    //Console.WriteLine($"parsed {actionSequence} as clear {m.Groups["var"].Value}");
                }
                else if (Regex.IsMatch(prettyActionSequence, SAY_REGEX)) {
                    int firstQuotePosition = prettyActionSequence.IndexOf("\"");
                    int lastQuotePosition = prettyActionSequence.LastIndexOf("\"");
                    int start = firstQuotePosition + 1;
                    int len = lastQuotePosition - start;
                    string possibleString = prettyActionSequence.Substring(start, len);
                    BracketedConfigProcessor.AssertValidString(possibleString);

                    action = new Say(possibleString);
                    //Console.WriteLine($"parsed {actionSequence} as say {possibleString}");
                }
                else if (Regex.IsMatch(prettyActionSequence, EXTERNAL_REGEX)) {
                    int firstQuotePosition = prettyActionSequence.IndexOf("\"");
                    int lastQuotePosition = prettyActionSequence.LastIndexOf("\"");
                    int start = firstQuotePosition + 1;
                    int len = lastQuotePosition - start;
                    string possibleString = prettyActionSequence.Substring(start, len);
                    BracketedConfigProcessor.AssertValidString(possibleString);

                    Match m = Regex.Match(prettyActionSequence, EXTERNAL_REGEX);
                    if (m.Length == 0) throw new ActionParseException();

                    action = new Extension(m.Groups["method"].Value, possibleString);
                    //Console.WriteLine($"parsed {actionSequence} as external {m.Groups["method"].Value} {possibleString}");
                }
                else {
                    throw new ActionParseException();
                }

                if (action == null) {
                    throw new ActionParseException();
                }

                return action;
            }
            catch {
                throw new ActionParseException();
            }
        }
    }

    internal class ActionParseException : Exception
    {
    }

    public class Assign : Action
    {
        public string Var { get; set; }
        public Expression Expr { get; set; }
        public string Value { get; set; }

        public Assign(string Var, string Value)
        {
            this.Value = Value; this.Var = Var;
        }

        public override void Execute(State S)
        {
            if (Expr != null) S.Assign(Var, Expr.Eval(S));
            if (Value != null) S.Assign(Var, S.EvalString(Value));
        }

        public static Assign Parse(XElement X)
        {
            return new Assign(X.Attribute("Var").Value, X.Attribute("Value").Value);
        }

    }

    public class Clear : Action
    {
        public string Var { get; set; }
        public Clear(string Var)
        { this.Var = Var; }

        public override void Execute(State S)
        {
            S.Remove(Var);
        }

        public static Clear Parse(XElement X)
        {
            return new Clear(X.Attribute("Var").Value);
        }

    }

    public class CombinedAction : Action
    {
        public List<Action> Actions { get; set; }
        public override void Execute(State S)
        {
            foreach (var x in Actions)
                x.Execute(S);
        }

        protected bool? long_running;
        public override bool LongRunning
        {
            get
            {
                if (long_running.HasValue) return long_running.Value;
                long_running = false;
                foreach(var a in Actions)
                {
                    if (a.LongRunning)
                    {
                        long_running = true;
                        break;
                    }
                }
                return long_running.Value;
            }
        }

        public CombinedAction(IEnumerable<Action> Actions)
        {
            this.Actions = new List<Action>(Actions);
        }
    }

    public class OneOf : CombinedAction
    {
        public OneOf(IEnumerable<Action> Actions) : base(Actions) { }

        public override void Execute(State S)
        {
            Actions.OneOf().Execute(S);
        }

    }

    public class Say : Action
    {
        public static ISpeaker Speaker { get; set; }

        public string Text { get; set; }

        public Say(string Text)
        {
            this.Text=Text;
        }

        public override bool LongRunning => true;

        public override void Execute(State S)
        {
            Speaker.Speak(S.EvalString(Text));
        }

        public static Say Parse(XElement X)
        {
            return new Say(X.Attribute("Text").Value);
        }
    }

    public class Extension : Action
    {
        public static Action<string,string> Executor { get; set; }

        public string Command { get; set; }
        public string Param { get; set; }

        public Extension(string Cmd, string Param = null)
        {
            this.Command = Cmd;
            this.Param = Param;
        }

        public override void Execute(State S)
        {
            Executor(Command,Param);
        }

        public static Extension Parse(XElement X)
        {
            if (X.Attribute("Param")==null) return new Extension(X.Attribute("Command").Value);
            else return new Extension(X.Attribute("Command").Value, X.Attribute("Param").Value);
        }
    }

}
