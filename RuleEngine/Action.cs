using RoboLogic;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.Devices.Gpio;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Xaml.Media;
using RuleEngineNet;

// ReSharper disable StringLastIndexOfIsCultureSpecific.1

// ReSharper disable StringIndexOfIsCultureSpecific.1

namespace RuleEngineNet {
    public abstract class Action {
        public abstract void Execute(State S);
        public bool ActiveAfterExecution { get; set; } = false;
        public virtual bool LongRunning { get; } = false;
        public static Action LoadXml(XElement X) {
            switch (X.Name.LocalName) {
                case "Assign":
                    return Assign.Parse(X);
                case "Clear":
                    return Clear.Parse(X);
                case "Say":
                    return Say.Parse(X);
                case "Play":
                    return Play.Parse(X, 100);
                case "ShutUp":
                    return ShutUp.Parse(X);
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
                                        if (action2 != null) {
                                            action = new CombinedAction(new List<Action> {action1});
                                            if (action2 is CombinedAction action2AsCombinedAction) {
                                                ((CombinedAction) action).Actions.AddRange(action2AsCombinedAction.Actions);
                                            }
                                            else {
                                                ((CombinedAction)action).Actions.Add(action2);
                                            }
                                        }
                                    }
                                    else if (Regex.IsMatch(restOfString, @"^\s*\)\s*(or|OR).+$")) {
                                        string possibleAction2Substring = restOfString.Substring(restOfString.IndexOf("or") + "or".Length);
                                        Action action2 = ParseActionSequence(possibleAction2Substring);
                                        if (action2 != null) {
                                            action = new OneOf(new List<Action> {action1});
                                            if (action2 is OneOf action2AsOneOf) {
                                                ((OneOf)action).Actions.AddRange(action2AsOneOf.Actions);
                                            }
                                            else {
                                                ((OneOf) action).Actions.Add(action2);
                                            }
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
            string ASSIGNEMENT_STRING_REGEX = $"^(?<var>{BracketedConfigProcessor.VARNAME_REGEX_PATTERN})\\s*=\\s*\\\"(?<value>\\S*)\\\"$";
            string ASSIGNEMENT_REGEX = $"^(?<var>{BracketedConfigProcessor.VARNAME_REGEX_PATTERN})\\s*=\\s*(?<value>\\S+)$";
            string CLEAR_REGEX = $"^clear\\s+\\$(?<var>{BracketedConfigProcessor.VARNAME_REGEX_PATTERN})$";
            string SAY_REGEX = $"^say\\s+((?<probability>\\d*)\\s+)?\".*\"$";
            string SHUT_UP_REGEX = $"^shutUp$";
            string GPIO_REGEX = $"^GPIO\\s+((?<probability>\\d*)\\s+)?(?<signal>([10],)*[10])\\s+(?<time>\\d+)$";
            string EXTERNAL_ACTION_NAME_REGEX_PATTERN = BracketedConfigProcessor.VARNAME_REGEX_PATTERN;
            string EXTERNAL_REGEX = $"^ext:(?<method>{EXTERNAL_ACTION_NAME_REGEX_PATTERN})\\s+\".*\"$";
            string PLAY_REGEX = $"^play\\s+((?<probability>\\d*)\\s+)?\".*\"$";
            string PLAY_DELAY_REGEX = $"^play\\s+((?<probability>\\d*)\\s+)?\".*\"(\\s+(?<time>\\d+))?$";
            string STAY_ACTIVE_REGEX = $"^stayActive$";
            string COMPARE_ANSWERS_REGEX = $"^compareAnswers\\s+(?<goodAnswer>{BracketedConfigProcessor.VARNAME_REGEX_PATTERN})\\s+(?<realAnswer>{BracketedConfigProcessor.VARNAME_REGEX_PATTERN})$";
            Action action = null;

            string prettyActionSequence = actionSequence.Trim();
            int probability;
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
                    Match m = Regex.Match(prettyActionSequence, SAY_REGEX);
                    if (m.Length != 0 && m.Groups["probability"].Value.Length != 0)
                    {
                        probability = Int32.Parse(m.Groups["probability"].Value);
                    }
                    else
                    {
                        probability = 100;
                    }
                    if (BracketedConfigProcessor.AssertValidString(possibleString)) {
                        action = new Say(possibleString, probability);
                    }
                }
                else if (Regex.IsMatch(prettyActionSequence, SHUT_UP_REGEX)) {
                    action = new ShutUp();
                }
                else if (Regex.IsMatch(prettyActionSequence, STAY_ACTIVE_REGEX))
                {
                    action = new StayActive();
                }
                else if (Regex.IsMatch(prettyActionSequence, COMPARE_ANSWERS_REGEX))
                {
                    Match m = Regex.Match(prettyActionSequence, COMPARE_ANSWERS_REGEX);
                    if (m.Length != 0)
                    {
                        action = new CompareAnswers(m.Groups["goodAnswer"].Value, m.Groups["realAnswer"].Value);
                    }
                }
                else if (Regex.IsMatch(prettyActionSequence, PLAY_DELAY_REGEX))
                {
                    int firstQuotePosition = prettyActionSequence.IndexOf("\"");
                    int lastQuotePosition = prettyActionSequence.LastIndexOf("\"");
                    int start = firstQuotePosition + 1;
                    int len = lastQuotePosition - start;
                    string possibleString = prettyActionSequence.Substring(start, len);
                    Match m = Regex.Match(prettyActionSequence, PLAY_DELAY_REGEX);
                    if (m.Length != 0 && m.Groups["probability"].Value.Length != 0)
                    {
                        probability = Int32.Parse(m.Groups["probability"].Value);
                    }
                    else
                    {
                        probability = 100;
                    }
                    Match m2 = Regex.Match(prettyActionSequence, PLAY_DELAY_REGEX);
                    if (m2.Length != 0 && m2.Groups["time"].Value.Length != 0)
                    {
                        var time = Int32.Parse(m.Groups["time"].Value);
                        if (BracketedConfigProcessor.AssertValidString(possibleString))
                        {
                            action = new Play(possibleString, probability, time);
                        }
                    }
                    else {
                        if (BracketedConfigProcessor.AssertValidString(possibleString))
                        {
                            action = new Play(possibleString, probability);
                        }
                    }
                    
                }
                else if (Regex.IsMatch(prettyActionSequence, GPIO_REGEX))
                {
                    Match m = Regex.Match(prettyActionSequence, GPIO_REGEX);
                    if (m.Length != 0 && m.Groups["probability"].Value.Length != 0)
                    {
                        probability = Int32.Parse(m.Groups["probability"].Value);
                    }
                    else
                    {
                        probability = 100;
                    }
                    if (m.Length != 0) {
                        action = new GPIO(m.Groups["signal"].Value.Split(',', ' ')
                            .Select(Int32.Parse).ToList(), Int32.Parse(m.Groups["time"].Value),
                            probability);
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
            {
                x.Execute(S);
                if (x.ActiveAfterExecution) {
                    ActiveAfterExecution = true;
                    x.ActiveAfterExecution = false;
                }

            }
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
            var x = Actions.OneOf();
            x.Execute(S);
            if (x.ActiveAfterExecution){
                ActiveAfterExecution = true;
                x.ActiveAfterExecution = false;
            }
        }
    }

    public class Say : Action {
        public static UWPLocalSpeaker Speaker { get; set; }
        public int Probability { get; set; }
        public string Text { get; set; }

        public static bool isPlaying = false;

        public Say(string Text, int Probability) {
            this.Text = Text;
            this.Probability = Probability;
        }

        public override bool LongRunning => true;

        public override void Execute(State S)
        {
            var rand = new Random();
            if (rand.Next(1, 101) > Probability)
            {
                return;
            }
            

            Debug.WriteLine("say: " + S.EvalString(Text));
            SayHelper(S.EvalString(Text), S);
        }

        public static Say Parse(XElement X) {
            return new Say(X.Attribute("Text").Value, 100);
        }
        public async void SayHelper(String Text, State S)
        {
            Debug.WriteLine("1");
            while (isPlaying)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300));
            }
            Debug.WriteLine("2");
            isPlaying = true;
            S.Assign("isPlaying", "True");
            Debug.WriteLine("3");
            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunTaskAsync(() => Speaker.Speak(Text));
            Debug.WriteLine("4");
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            Debug.WriteLine("5");
            while (Speaker.Media.CurrentState != MediaElementState.Closed && Speaker.Media.CurrentState != MediaElementState.Stopped && Speaker.Media.CurrentState != MediaElementState.Paused) {
                await Task.Delay(TimeSpan.FromMilliseconds(500));
            }
            Debug.WriteLine("6");
            isPlaying = false;
            S.Assign("isPlaying", "False");
        }

    }

    public class CompareAnswers : Action {
        public string correctAnswersVarName;
        public string realAnswersVarName;

        public CompareAnswers(string correctAnswersVarName, string realAnswersVarName) {
            this.correctAnswersVarName = correctAnswersVarName;
            this.realAnswersVarName = realAnswersVarName;
        }

        public override void Execute(State S)
        {
            if (S.ContainsKey(correctAnswersVarName) && S.ContainsKey(realAnswersVarName)) {
                var tmp1 = S[correctAnswersVarName];
                var tmp2 = S[realAnswersVarName];
                if (tmp1.Length != tmp2.Length) {
                    S["comparisonRes"] = "error";
                    S["comparisonErrors"] = "error";
                    return ;
                }

                int numOfQuestions = tmp1.Length;
                int numOfGoodQuestions = 0;
                List<int> badQuestions = new List<int>();
                for (int i = 0; i < numOfQuestions; i++)
                {
                    if (tmp1[i] == tmp2[i])
                    {
                        numOfGoodQuestions += 1;
                    }
                    else {
                        badQuestions.Add(i+1);
                    }
                }

                string res = ((int) ((float) numOfGoodQuestions / numOfQuestions * 100)).ToString();
                string errors = string.Join(", ", badQuestions.Select(x => x.ToString()).ToArray());
                S["comparisonRes"] = res;
                S["comparisonErrors"] = errors;
            }
        }


    }

    public class ShutUp : Action {
        public static UWPLocalSpeaker Speaker { get; set; }
        public override void Execute(State S) {
            Speaker.ShutUp();
        }

        public static ShutUp Parse(XElement X) {
            return new ShutUp();
        }
    }

    public class Play : Action
    {
        private const string WAV_PATH_PREFIX = "ms-appx:///Sounds/";
        public static UWPLocalSpeaker Speaker { get; set; }
        public int Probability { get; set; }
        public Uri FileName { get; set; }//TODO type
        private readonly int _duration = -1;
        public Play(string filename, int prob)
        {
            this.FileName = new Uri(WAV_PATH_PREFIX + filename);
            Probability = prob;
        }

        public Play(string filename, int prob, int duration) {
            FileName = new Uri(WAV_PATH_PREFIX + filename);
            Probability = prob;
            _duration = duration;
        }

        public override bool LongRunning => true;


        public override void Execute(State S)
        {
            var rand = new Random();
            if (rand.Next(1, 101) > Probability)
            {
                return;
            }

            if (_duration == -1) {
                Speaker.Play(FileName);
            }
            else {
                Speaker.Play(FileName, _duration);
            }

        }

        public static Play Parse(XElement X, int _prob) {
            return new Play(X.Attribute("FileName").Value, _prob);
        }

    }

    public class StayActive : Action
    {
        public override void Execute(State S) {
            ActiveAfterExecution = true;
        }
    }

    public class Extension : Action
    {
        public static Action<string,string> Executor { get; set; }

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
        public int Probability { get; set; }
        private int[] pinsNums = Config.OutputPinsNumbers;

        CancellationTokenSource tokenSource2;
        CancellationToken ct;
        private Task task = Task.CompletedTask;
        private static Boolean stopExecution = false;
        private static Boolean executing = false;

        public GPIO(IEnumerable<int> signal, int time, int probability) {
            this.Signal = new List<int>(signal);
            this.Time = time;
            this.Probability = probability;
            tokenSource2 = new CancellationTokenSource();
            ct =  tokenSource2.Token;
        }

        public override void Execute(State S)
        {
            System.Diagnostics.Debug.WriteLine($"GPIO_TASK {task.Status}");
            var rand = new Random();
            int tmp = rand.Next(1, 101);
            if (tmp > Probability)
            {
                return;
            }
            var gpio = GpioController.GetDefault();
            if (gpio == null) {
                return;
            }

            if (executing) {
                stopExecution = true;
                while (executing) {}
            }

            stopExecution = false;
            executing = true;

            List<GpioPin> pins = new List<GpioPin>();
            //GpioPin pin;
            foreach (var num in pinsNums) {
                var pin = gpio.OpenPin(num);
                pin.SetDriveMode(GpioPinDriveMode.Output);
                pin.Write(GpioPinValue.Low);
                pins.Add(pin);
            }
            
            long startTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            string debug = "";
            for (int i = 0; i < 4; ++i) {
                if (Signal[i] == 1)
                    pins[i].Write(GpioPinValue.High);
                debug += pins[i].Read().ToString();
            }
            System.Diagnostics.Debug.WriteLine($"Sended {debug}");

            Task.Run(() => {
                while (DateTimeOffset.Now.ToUnixTimeMilliseconds() - startTime < Time) {
                    if (stopExecution) break;
                }

                foreach (var pin in pins) {
                    pin.Write(GpioPinValue.Low);
                    pin.Dispose();
                }
                System.Diagnostics.Debug.WriteLine("Disposed");
                executing = false;
            });
            System.Diagnostics.Debug.WriteLine("Exited GPIO");
            return;
        }

        public static GPIO Parse(XElement X) {
            try {
                return new GPIO(
                    X.Attribute("Signal").Value.Split(',', ' ').Select(Int32.Parse).ToList(),
                    Int32.Parse(X.Attribute("Time").Value), 100);
            }
            catch {
                throw new RuleEngineException("Error converting string to number");
            }
        }
    }

    public static class DispatcherTaskExtensions
    {
        public static async Task<T> RunTaskAsync<T>(this CoreDispatcher dispatcher,
            Func<Task<T>> func, CoreDispatcherPriority priority = CoreDispatcherPriority.Normal)
        {
            var taskCompletionSource = new TaskCompletionSource<T>();
            await dispatcher.RunAsync(priority, async () =>
            {
                try
                {
                    taskCompletionSource.SetResult(await func());
                }
                catch (Exception ex)
                {
                    taskCompletionSource.SetException(ex);
                }
            });
            return await taskCompletionSource.Task;
        }

        // There is no TaskCompletionSource<void> so we use a bool that we throw away.
        public static async Task RunTaskAsync(this CoreDispatcher dispatcher,
            Func<Task> func, CoreDispatcherPriority priority = CoreDispatcherPriority.Normal) =>
            await RunTaskAsync(dispatcher, async () => { await func(); return false; }, priority);
    }
}