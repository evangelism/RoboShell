using RoboLogic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.Devices.Gpio;
using RuleEngineNet;

// ReSharper disable StringLastIndexOfIsCultureSpecific.1

// ReSharper disable StringIndexOfIsCultureSpecific.1

namespace RuleEngineNet {
    public abstract class Action {
        public abstract void Execute(State S);
        public virtual bool LongRunning { get; } = false;

        public static Action LoadXml(XElement X) {
            switch (X.Name.LocalName) {
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
                case "GPIO":
                    return GPIO.Parse(X);
                default:
                    throw new RuleEngineException("Unsupported action type");
            }
        }

        public static Action ParseActionSequence(string actionsSequence) {
            // ReSharper disable once RedundantAssignment
            Action action = null;

            action = ParseAtomicAction(actionsSequence);
            if (action == null) {
                int firstOpeningBracePosition = actionsSequence.IndexOf("(");
                int[] closingBracesPositions = BracketedConfigProcessor.AllIndexesOf(actionsSequence, ")");
                if (firstOpeningBracePosition == -1 || closingBracesPositions.Length != 0) {
                    if (closingBracesPositions.Length > 0) {
                        for (int tryingClosingBraceIndex = 0; tryingClosingBraceIndex != closingBracesPositions.Length; tryingClosingBraceIndex++) {
                            int possibleExpression1Start = firstOpeningBracePosition + 1;
                            int possibleAction1End = closingBracesPositions[tryingClosingBraceIndex];
                            int possibleExpression1Length = possibleAction1End - possibleExpression1Start;

                            Action action1 = null;
                            if (possibleExpression1Length > 0) {
                                string possibleAction1Substring = actionsSequence.Substring(possibleExpression1Start, possibleExpression1Length);
                                action1 = ParseActionSequence(possibleAction1Substring);
                            }
                            if (action1 != null) {
                                string restOfString = actionsSequence.Substring(possibleAction1End);

                                if (!Regex.IsMatch(restOfString, @"^\s*\)\s*$")) {
                                    if (Regex.IsMatch(restOfString, @"^\s*\)\s*(and|AND).+$")) {
                                        string possibleAction2Substring =
                                            restOfString.Substring(restOfString.IndexOf("and") + "and".Length);
                                        Action action2 = ParseActionSequence(possibleAction2Substring);
                                        if (action1 != null && action2 != null) {
                                            action = new CombinedAction(new List<Action> {action1, action2});
                                        }
                                    }
                                    else if (Regex.IsMatch(restOfString, @"^\s*\)\s*(or|OR).+$")) {
                                        string possibleAction2Substring = restOfString.Substring(restOfString.IndexOf("or") + "or".Length);
                                        Action action2 = ParseActionSequence(possibleAction2Substring);
                                        if (action1 != null && action2 != null) {
                                            action = new OneOf(new List<Action> {action1, action2});
                                        }
                                    }
                                }
                                else {
                                    action = action1;
                                }

                                if (action != null) {
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            return action;
        }

        private static Action ParseAtomicAction(string actionSequence) {
            string ASSIGNEMENT_STRING_REGEX =
                $"^(?<var>{BracketedConfigProcessor.VARNAME_REGEX_PATTERN})\\s*=\\s*\\\"(?<value>\\S+)\\\"$";
            string ASSIGNEMENT_REGEX =
                $"^(?<var>{BracketedConfigProcessor.VARNAME_REGEX_PATTERN})\\s*=\\s*(?<value>\\S+)$";
            string CLEAR_REGEX =
                $"^clear\\s+\\$(?<var>{BracketedConfigProcessor.VARNAME_REGEX_PATTERN})$";
            string SAY_REGEX = $"^say\\s+\".*\"$";
            string GPIO_REGEX = $"^GPIO\\s+(?<signal>([10],)*[10])\\s+(?<time>\\d+)$";
            string EXTERNAL_ACTION_NAME_REGEX_PATTERN =
                BracketedConfigProcessor.VARNAME_REGEX_PATTERN;
            string EXTERNAL_REGEX =
                $"^ext:(?<method>{EXTERNAL_ACTION_NAME_REGEX_PATTERN})\\s+\".*\"$";
            Action action = null;

            string prettyActionSequence = actionSequence.Trim();
            try {
                if (Regex.IsMatch(prettyActionSequence, ASSIGNEMENT_STRING_REGEX)) {
                    Match m = Regex.Match(prettyActionSequence, ASSIGNEMENT_STRING_REGEX);
                    if (m.Length != 0) {
                        action = new Assign(m.Groups["var"].Value, m.Groups["value"].Value);
                    }
                }
                else if (Regex.IsMatch(prettyActionSequence, ASSIGNEMENT_REGEX)) {
                    Match m = Regex.Match(prettyActionSequence, ASSIGNEMENT_REGEX);
                    if (m.Length != 0) {
                        action = new Assign(m.Groups["var"].Value, m.Groups["value"].Value);
                    }
                }
                else if (Regex.IsMatch(prettyActionSequence, CLEAR_REGEX)) {
                    Match m = Regex.Match(prettyActionSequence, CLEAR_REGEX);
                    if (m.Length != 0) {
                        action = new Clear(m.Groups["var"].Value);
                    }
                }
                else if (Regex.IsMatch(prettyActionSequence, SAY_REGEX)) {
                    int firstQuotePosition = prettyActionSequence.IndexOf("\"");
                    int lastQuotePosition = prettyActionSequence.LastIndexOf("\"");
                    int start = firstQuotePosition + 1;
                    int len = lastQuotePosition - start;
                    string possibleString = prettyActionSequence.Substring(start, len);
                    if (BracketedConfigProcessor.AssertValidString(possibleString)) {
                        action = new Say(possibleString);
                    }
                }
                else if (Regex.IsMatch(prettyActionSequence, GPIO_REGEX)) {
                    Match m = Regex.Match(prettyActionSequence, GPIO_REGEX);
                    if (m.Length != 0) {
                        action = new GPIO(m.Groups["signal"].Value.Split(',', ' ')
                            .Select(Int32.Parse).ToList(), Int32.Parse(m.Groups["time"].Value));
                    }
                }
                else if (Regex.IsMatch(prettyActionSequence, EXTERNAL_REGEX)) {
                    int firstQuotePosition = prettyActionSequence.IndexOf("\"");
                    int lastQuotePosition = prettyActionSequence.LastIndexOf("\"");
                    int start = firstQuotePosition + 1;
                    int len = lastQuotePosition - start;
                    string possibleString = prettyActionSequence.Substring(start, len);
                    if (BracketedConfigProcessor.AssertValidString(possibleString)) {
                        Match m = Regex.Match(prettyActionSequence, EXTERNAL_REGEX);
                        if (m.Length != 0) {
                            action = new Extension(m.Groups["method"].Value, possibleString);
                        }
                    }
                }
            }
            catch {
                action = null;
            }

            return action;
        }
    }

//    internal class ActionParseException : Exception
//    {
//    }

    public class Assign : Action {
        public string Var { get; set; }
        public Expression Expr { get; set; }
        public string Value { get; set; }

        public Assign(string Var, string Value) {
            this.Value = Value;
            this.Var = Var;
        }

        public override void Execute(State S) {
            if (Expr != null) S.Assign(Var, Expr.Eval(S));
            if (Value != null) S.Assign(Var, S.EvalString(Value));
        }

        public static Assign Parse(XElement X) {
            return new Assign(X.Attribute("Var").Value, X.Attribute("Value").Value);
        }
    }

    public class Clear : Action {
        public string Var { get; set; }

        public Clear(string Var) {
            this.Var = Var;
        }

        public override void Execute(State S) {
            S.Remove(Var);
        }

        public static Clear Parse(XElement X) {
            return new Clear(X.Attribute("Var").Value);
        }
    }

    public class CombinedAction : Action {
        public List<Action> Actions { get; set; }

        public override void Execute(State S) {
            foreach (var x in Actions)
                x.Execute(S);
        }

        protected bool? long_running;

        public override bool LongRunning {
            get {
                if (long_running.HasValue) return long_running.Value;
                long_running = false;
                foreach (var a in Actions) {
                    if (a.LongRunning) {
                        long_running = true;
                        break;
                    }
                }

                return long_running.Value;
            }
        }

        public CombinedAction(IEnumerable<Action> Actions) {
            this.Actions = new List<Action>(Actions);
        }
    }

    public class OneOf : CombinedAction {
        public OneOf(IEnumerable<Action> Actions) : base(Actions) { }

        public override void Execute(State S) {
            Actions.OneOf().Execute(S);
        }
    }

    public class Say : Action {
        public static ISpeaker Speaker { get; set; }

        public string Text { get; set; }

        public Say(string Text) {
            this.Text = Text;
        }

        public override bool LongRunning => true;

        public override void Execute(State S) {
            Speaker.Speak(S.EvalString(Text));
            System.Diagnostics.Debug.WriteLine(S.EvalString(Text));
        }

        public static Say Parse(XElement X) {
            return new Say(X.Attribute("Text").Value);
        }
    }

    public class Extension : Action {
        public static Action<string, string> Executor { get; set; }

        public string Command { get; set; }
        public string Param { get; set; }

        public Extension(string Cmd, string Param = null) {
            this.Command = Cmd;
            this.Param = Param;
        }

        public override void Execute(State S) {
            Executor(Command, Param);
        }

        public static Extension Parse(XElement X) {
            if (X.Attribute("Param") == null) return new Extension(X.Attribute("Command").Value);
            else return new Extension(X.Attribute("Command").Value, X.Attribute("Param").Value);
        }
    }

    public class GPIO : Action {
        public List<int> Signal { get; set; }
        public int Time;
        private List<int> pinsNums = new List<int>() {17, 27, 22, 23, 24, 25};

        public GPIO(IEnumerable<int> signal, int time) {
            this.Signal = new List<int>(signal);
            this.Time = time;
        }

        public override void Execute(State S) {
            var gpio = GpioController.GetDefault();
            if (gpio == null) {
                return;
            }

            List<GpioPin> pins = new List<GpioPin>();
            //GpioPin pin;
            foreach (var num in pinsNums) {
                var pin = gpio.OpenPin(num);
                pin.Write(GpioPinValue.High);
                pin.SetDriveMode(GpioPinDriveMode.Output);
                pins.Add(pin);
            }

            long startTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            for (int i = 0; i < 6; ++i) {
                if (Signal[i] == 1)
                    pins[i].Write(GpioPinValue.Low);
            }

            while (DateTimeOffset.Now.ToUnixTimeMilliseconds() - startTime < Time) { }

            foreach (var pin in pins) {
                pin.Write(GpioPinValue.High);
                pin.Dispose();
            }

            return;
        }

        public static GPIO Parse(XElement X) {
            try {
                return new GPIO(
                    X.Attribute("Signal").Value.Split(',', ' ').Select(Int32.Parse).ToList(),
                    Int32.Parse(X.Attribute("Time").Value));
            }
            catch {
                throw new RuleEngineException("Error converting string to number");
            }
        }
    }
}