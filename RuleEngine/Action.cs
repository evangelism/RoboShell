using RoboLogic;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.Devices.Gpio;
using Windows.Media.SpeechSynthesis;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Xaml.Media;
using CsvHelper;
using RuleEngineNet;
using RuleEngineUtils;
using LogLib;
// ReSharper disable StringLastIndexOfIsCultureSpecific.1

// ReSharper disable StringIndexOfIsCultureSpecific.1

namespace RuleEngineUtils {
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

namespace RuleEngineNet {
    public abstract class Action {
        public abstract void Execute(State S);
        public bool ActiveAfterExecution { get; set; } = false;
        public virtual bool LongRunning { get; } = false;
        public static Action LoadXml(XElement X) { // TODO add new actions
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

        public abstract void Initialize();
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
                                            if (action2 is OneOf) {
                                                ((CombinedAction)action).Actions.Add(action2);
                                            }
                                            else if (action2 is CombinedAction)
                                            {
                                                ((CombinedAction)action).Actions.AddRange(((CombinedAction)action2).Actions);
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
            string ASSIGNEMENT_STRING_REGEX = $"^(?<var>{BracketedConfigProcessor.VARNAME_REGEX_PATTERN})\\s*=\\s*\".*\"$";
            string ASSIGNEMENT_REGEX = $"^(?<var>{BracketedConfigProcessor.VARNAME_REGEX_PATTERN})\\s*=\\s*(?<value>\\S+)$";
            string CLEAR_REGEX = $"^clear\\s+\\$(?<var>{BracketedConfigProcessor.VARNAME_REGEX_PATTERN})$";
            string SAY_REGEX = $"^say\\s+((?<probability>\\d*)\\s+)?\".*\"$";
            string SHUT_UP_REGEX = $"^shutUp$";
            string GPIO_REGEX = $"^GPIO\\s+((?<probability>\\d*)\\s+)?(?<signal>([10],)*[10])\\s+(?<time>\\d+)$";
            string EXTERNAL_ACTION_NAME_REGEX_PATTERN = BracketedConfigProcessor.VARNAME_REGEX_PATTERN;
            string EXTERNAL_REGEX = $"^ext:(?<method>{EXTERNAL_ACTION_NAME_REGEX_PATTERN})\\s+\".*\"$";
            string PLAY_DELAY_REGEX = $"^play\\s+((?<probability>\\d*)\\s+)?\".*\"(\\s+(?<time>\\d+))?$";
            string STAY_ACTIVE_REGEX = $"^stayActive$";
            string COMPARE_ANSWERS_REGEX = $"^compareAnswers\\s+(?<goodAnswer>{BracketedConfigProcessor.VARNAME_REGEX_PATTERN})\\s+(?<realAnswer>{BracketedConfigProcessor.VARNAME_REGEX_PATTERN})$";
            string QUIZ_REGEX = $"^\\s*quiz\\s+(\\\"(?<filename>.*)\\\"\\s*)((?<randomOrder>randomOrder)\\s*)?((?<length>\\d+\\:\\d+))?\\s*$";
            Action action = null;

            string prettyActionSequence = actionSequence.Trim();
            int probability;
            try {
                if (Regex.IsMatch(prettyActionSequence, ASSIGNEMENT_STRING_REGEX)) {
                    int firstQuotePosition = prettyActionSequence.IndexOf("\"");
                    int lastQuotePosition = prettyActionSequence.LastIndexOf("\"");
                    int start = firstQuotePosition + 1;
                    int len = lastQuotePosition - start;
                    string possibleString = prettyActionSequence.Substring(start, len);
                    Match m = Regex.Match(prettyActionSequence, ASSIGNEMENT_STRING_REGEX);
                    if (BracketedConfigProcessor.AssertValidString(possibleString))
                    {
                        if (m.Length != 0)
                        {
                            action = new Assign(m.Groups["var"].Value, possibleString);
                        }
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
                else if (Regex.IsMatch(prettyActionSequence, QUIZ_REGEX)) {
                    int firstQuotePosition = prettyActionSequence.IndexOf("\"");
                    int lastQuotePosition = prettyActionSequence.LastIndexOf("\"");
                    int start = firstQuotePosition + 1;
                    int len = lastQuotePosition - start;

                    string possibleString = prettyActionSequence.Substring(start, len);
                    if (BracketedConfigProcessor.AssertValidString(possibleString))
                    {
                        action = new Quiz(possibleString);

                        Match m = Regex.Match(prettyActionSequence, QUIZ_REGEX);

                        ((Quiz)action).randomOrdered = m.Length != 0 && m.Groups["randomOrder"].Value.Length != 0;

                        //                    Match m = Regex.Match(prettyActionSequence, QUIZ_REGEX);
                        if (m.Length != 0 && m.Groups["length"].Value.Length != 0)
                        {
                            var lengths = m.Groups["length"].Value.Split(':');
                            ((Quiz)action).lengthLowerBound = int.Parse(lengths[0]);
                            ((Quiz)action).lengthUpperBound = int.Parse(lengths[1]);
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
            LogLib.Log.Trace($"BEFORE {GetType().Name}.Execute()");
            if (Expr != null) S.Assign(Var, Expr.Eval(S));
            if (Value != null) S.Assign(Var, S.EvalString(Value));
            LogLib.Log.Trace($"AFTER {GetType().Name}.Execute()");
        }

        public override void Initialize() {
            return;
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
        public override void Initialize()
        {
            return;
        }

        public override void Execute(State S) {
            LogLib.Log.Trace($"BEFORE {GetType().Name}.Execute()");
            S.Remove(Var);
            LogLib.Log.Trace($"AFTER {GetType().Name}.Execute()");
        }

        public static Clear Parse(XElement X) {
            return new Clear(X.Attribute("Var").Value);
        }
    }

    public class CombinedAction : Action {
        public List<Action> Actions { get; set; }

        public override void Execute(State S) {
            LogLib.Log.Trace($"BEFORE {GetType().Name}.Execute()");
            foreach (var x in Actions)
            {
                x.Execute(S);
                if (x.ActiveAfterExecution) {
                    ActiveAfterExecution = true;
                    x.ActiveAfterExecution = false;
                }
            }
            LogLib.Log.Trace($"AFTER {GetType().Name}.Execute()");
        }

        protected bool? long_running;

        public override void Initialize()
        {
            foreach (var x in Actions)
            {
                x.Initialize();
            }
        }

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
            LogLib.Log.Trace($"BEFORE {GetType().Name}.Execute()");
            var x = Actions.OneOf();
            x.Execute(S);
            if (x.ActiveAfterExecution){
                ActiveAfterExecution = true;
                x.ActiveAfterExecution = false;
            }
            LogLib.Log.Trace($"AFTER {GetType().Name}.Execute()");
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

        public override void Initialize() {}
        public override bool LongRunning => true;

        public override void Execute(State S)
        {
            LogLib.Log.Trace($"BEFORE {GetType().Name}.Execute()");
            var rand = new Random();
            if (rand.Next(1, 101) > Probability)
            {
                return;
            }
            

            Debug.WriteLine("say: " + S.EvalString(Text));
            SayHelper(S.EvalString(Text), S);
            LogLib.Log.Trace($"AFTER {GetType().Name}.Execute()");
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

        public override void Initialize() { }


        public override void Execute(State S)
        {
            LogLib.Log.Trace($"BEFORE {GetType().Name}.Execute()");
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
            LogLib.Log.Trace($"AFTER {GetType().Name}.Execute()");
        }


    }

    public class ShutUp : Action {
        public static UWPLocalSpeaker Speaker { get; set; }
        public override void Execute(State S) {
            LogLib.Log.Trace($"BEFORE {GetType().Name}.Execute()");
            Speaker.ShutUp();
            LogLib.Log.Trace($"AFTER {GetType().Name}.Execute()");
        }

        public override void Initialize() { }


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

        public override void Initialize() { }

        public Play(string filename, int prob, int duration) {
            FileName = new Uri(WAV_PATH_PREFIX + filename);
            Probability = prob;
            _duration = duration;
        }

        public override bool LongRunning => true;


        public override void Execute(State S)
        {
            LogLib.Log.Trace($"BEFORE {GetType().Name}.Execute()");
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
            LogLib.Log.Trace($"AFTER {GetType().Name}.Execute()");
        }

        public static Play Parse(XElement X, int _prob) {
            return new Play(X.Attribute("FileName").Value, _prob);
        }

    }

    public class StayActive : Action
    {
        public override void Execute(State S) {
            LogLib.Log.Trace($"BEFORE {GetType().Name}.Execute()");
            ActiveAfterExecution = true;
            LogLib.Log.Trace($"AFTER {GetType().Name}.Execute()");
        }
        public override void Initialize() { }

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
            LogLib.Log.Trace($"BEFORE {GetType().Name}.Execute()");
            Executor(Command, Param);
            LogLib.Log.Trace($"AFTER {GetType().Name}.Execute()");
        }

        public override void Initialize() { }


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
        public override bool LongRunning => true;


        public override void Initialize() { }


        public GPIO(IEnumerable<int> signal, int time, int probability) {
            this.Signal = new List<int>(signal);
            this.Time = time;
            this.Probability = probability;
            tokenSource2 = new CancellationTokenSource();
            ct =  tokenSource2.Token;
        }

        public override void Execute(State S)
        {
            LogLib.Log.Trace($"BEFORE {GetType().Name}.Execute()");
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
            LogLib.Log.Trace($"AFTER {GetType().Name}.Execute()");
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

    public class Quiz : Action {

        private Uri quizFileName = null;
        public bool randomOrdered = false;
        public int lengthLowerBound;
        public int lengthUpperBound;
        public static UWPLocalSpeaker Speaker;
        private IList<Tuple<string, bool?, string>> _quizText = new List<Tuple<string, bool?, string>>();
        private IList<Tuple<SpeechSynthesisStream, bool?, SpeechSynthesisStream>> _quiz = new List<Tuple<SpeechSynthesisStream, bool?, SpeechSynthesisStream>>();
        private static IEnumerable<int> QUESTION_SIGNAL = new []{1, 0, 1, 1};
        private static IEnumerable<int> DEFAULT_SIGNAL = new[] { 0, 0, 0, 0 };

        private static int QUIZ_QUESTION_TIME_MILLIS = 2000000;
        private GPIO _questionner = new GPIO(QUESTION_SIGNAL, QUIZ_QUESTION_TIME_MILLIS, 100);
        private GPIO _defaultArduinoState = new GPIO(DEFAULT_SIGNAL, 5000, 100);
        public override bool LongRunning => false;


        public Quiz(string quizFileName)
        {
            this.quizFileName = new Uri("ms-appx:///Quizs/" + quizFileName);

            var quest1 = "ответь нет";
            var expl1 = "правильный ответ нет";
            bool? answ1 = false;

            var quest2 = "ответь хоть что-то";
            var expl2 = "правильный ответ любой";
            bool? answ2 = null;

            var quest3 = "ответь да";
            var expl3 = "правильный ответ да";
            bool? answ3 = true;


            Task.Run(async () => {
                StorageFile f = await StorageFile.GetFileFromApplicationUriAsync(this.quizFileName);
                using (var inputStream = await f.OpenReadAsync())
                using (var classicStream = inputStream.AsStreamForRead())
                using (TextReader fileReader = new StreamReader(classicStream))
                {
                    var csv = new CsvReader(fileReader);
                    csv.Configuration.HasHeaderRecord = false;
                    csv.Configuration.Delimiter = ";";
                    csv.Configuration.IgnoreQuotes = false;
                    while (csv.Read())
                    {
                        Debug.WriteLine(csv.GetField(0));

                        bool? answ;
                        if (csv.GetField(1) == "да") answ = true;
                        else if (csv.GetField(1) == "нет") answ = false;
                        else answ = null;

                        _quizText.Add(new Tuple<string, bool?, string>(csv.GetField(0), answ, csv.GetField(2)));
                        

                    }
                }
            }).Wait();

            
            lengthLowerBound = _quizText.Count();
            lengthUpperBound = _quizText.Count();




        }
        private static string ARDUIO_NO = "0010";
        private static string ARDUINO_YES = "0100";
        private static string ARDUINO_NONE = "0001";
        private Random random = new Random();


        public override void Initialize() {
            Task.Run(async () => {
                for (int i = 0; i < _quizText.Count; i++) {
                    _quiz.Add(new Tuple<SpeechSynthesisStream, bool?, SpeechSynthesisStream>(
                        await Speaker.Synthesizer.SynthesizeTextToStreamAsync(_quizText.ElementAt(i).Item1),
                        _quizText.ElementAt(i).Item2,
                        await Speaker.Synthesizer.SynthesizeTextToStreamAsync(_quizText.ElementAt(i).Item3)));
                }
            }).Wait();

            

            

        }


        public override void Execute(State S) {
            LogLib.Log.Trace($"BEFORE {GetType().Name}.Execute()");
            Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunTaskAsync(() => (QuizHelper(S)));
            LogLib.Log.Trace($"AFTER {GetType().Name}.Execute()");
        }

        private async Task QuizHelper(State S)
        {
            if (S.ContainsKey("inQuiz")) {
                if (S["inQuiz"] == "True") {
                    return;
                }
            }
            S.Assign("inQuiz", "True");
            if (!S.ContainsKey("ArduinoInput")) {
                S["ArduinoInput"] = "";
            }
            if (!S.ContainsKey("KeyboardIn"))
            {
                S["KeyboardIn"] = "";
            }

            var userAnswers = new List<bool?>();
            var correctAnswers = new List<bool?>();

            var length = random.Next(lengthLowerBound, lengthUpperBound);
            var quests = Enumerable.Range(0, _quiz.Count).ToList();
            List<int> questionsToAsk = (randomOrdered ? quests.OrderBy(item => random.Next()).ToList() : quests).GetRange(0, Math.Min(length, _quiz.Count));

            foreach (var i in questionsToAsk) {
                while (Say.isPlaying) {
                    await Task.Delay(TimeSpan.FromMilliseconds(300));
                }

                correctAnswers.Add(_quiz.ElementAt(i).Item2);
                await SpeakingFunction(S, _quiz.ElementAt(i).Item1);
                S.Assign("isPlaying", "False");

                //await Task.Delay(TimeSpan.FromMilliseconds(500));

                try
                {
                    _questionner.Execute(S);
                    LogLib.Log.Trace("Started fixation");
                    while (S["ArduinoInput"] != ARDUINO_YES && 
                           S["ArduinoInput"] != ARDUIO_NO && 
                           S["ArduinoInput"] != ARDUINO_NONE && 
                           S["KeyboardIn"] != "yes" &&
                           S["KeyboardIn"] != "no" &&
                           S["KeyboardIn"] != "none")
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(200));
                    }

                    if (S["KeyboardIn"] == "yes" || S["ArduinoInput"] == ARDUINO_YES) userAnswers.Add(true);
                    else if (S["KeyboardIn"] == "no" || S["ArduinoInput"] == ARDUIO_NO) userAnswers.Add(false);
                    else if (S["KeyboardIn"] == "none" || S["ArduinoInput"] == ARDUINO_NONE) userAnswers.Add(null);
                    S["KeyboardIn"] = "";
                    LogLib.Log.Trace("Finished fixation");
                    _defaultArduinoState.Execute(S);
                }
                catch (KeyNotFoundException e) {
                    S.Assign("inQuiz", "False");
                    return;
                }
                
            }
            var result = compareLists(correctAnswers, userAnswers);
            foreach (var i in result.Item1) {
                await SpeakingFunction(S, _quiz.ElementAt(questionsToAsk[i]).Item3);
            }

            if (result.Item1.Count == 0) {
                await SpeakingFunction(S, await Speaker.Synthesizer.SynthesizeTextToStreamAsync("всё правильно"));

            }
            S.Assign("inQuiz", "False");

            async Task SpeakingFunction(State state, SpeechSynthesisStream s) {
                Say.isPlaying = true;
                state.Assign("isPlaying", "True");
                await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunTaskAsync(() =>
                    Say.Speaker.Speak(s));
                await Task.Delay(TimeSpan.FromMilliseconds(500));
                while (Say.Speaker.Media.CurrentState != MediaElementState.Closed &&
                       Say.Speaker.Media.CurrentState != MediaElementState.Stopped &&
                       Say.Speaker.Media.CurrentState != MediaElementState.Paused) {
                    await Task.Delay(TimeSpan.FromMilliseconds(100));
                }

                Say.isPlaying = false;
            }
        }

        private Tuple<List<int>, int> compareLists(List<bool?> orig, List<bool?> copy) {
            List<int> errors = new List<int>();
            int numberOfCorrectAnswers = 0;
            int numberOfQuestions = orig.Count;
            for (int i = 0; i < numberOfQuestions; i++) {
                if (orig[i] == null) {
                    if (copy[i] != null) {
                        numberOfCorrectAnswers += 1;
                    }
                    else {
                        errors.Add(i);
                    }
                }
                else if (orig[i] == copy[i]) {
                    numberOfCorrectAnswers += 1;
                }
                else {
                    errors.Add(i);
                }
            }


            return new Tuple<List<int>, int>(errors, ((int)((float)numberOfCorrectAnswers / numberOfQuestions * 100)));
        }
    }
}