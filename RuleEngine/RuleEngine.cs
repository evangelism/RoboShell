using RoboLogic;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.Storage;
using Windows.UI.ViewManagement;
using RuleEngineNet;


namespace RuleEngineNet
{
    public class RuleEngine
    {
        public State State { get; private set; }
        public State InitialState { get; private set; }
        public List<Rule> KnowlegeBase { get; private set; }

        public bool LastActionLongRunning { get; set; } = false;

        public RuleEngine()
        {
            State = new State();
            KnowlegeBase = new List<Rule>();
        }

        public RuleEngine(List<Rule> KB, State S)
        {
            this.KnowlegeBase = KB;
            this.State = S;
            this.InitialState = S;
        }

        public void SetSpeaker(ISpeaker spk)
        {
            Say.Speaker = spk;
        }

        public void SetExecutor(Action<string,string> Executor)
        {
            Extension.Executor = Executor;
        }

        public void Reset()
        {
            State = new State(InitialState);
            foreach(var x in KnowlegeBase)
            {
                x.Active = true;
            }
        }

        public void SetVar(string Var, string Val)
        {
            State.Assign(Var, Val);
        }

        public IEnumerable<Rule> GetConflictSet(State S)
        {
            return from x in KnowlegeBase
                   where x.Active && x.If.Eval(S).AsBool()
                   orderby x.Priority
                   select x;
        }

        public bool Step()
        {
            LastActionLongRunning = false;
            var cs = GetConflictSet(State);
            if (cs.Count() > 0)
            {
                var rule = ResolveConflict(cs);
                LastActionLongRunning = rule.Then.LongRunning;
                rule.Then.Execute(State);
                if (rule.RuleSet==null) rule.Active = false;
                else
                {
                    var t = from x in cs
                            where (x.RuleSet != null && x.RuleSet == rule.RuleSet)
                            select x;
                    foreach (var x in t) x.Active = false;
                }
                return true;
            }
            else return false;
        }

        public bool StepUntilLongRunning()
        {
            bool res;
            do
            {
                res = Step();
            }
            while (!LastActionLongRunning && res);
            return res;
        }

        private Rule ResolveConflict(IEnumerable<Rule> cs)
        {
            var r = cs.First();
            if (r.RuleSet == null) return r;
            var rs = (from x in cs
                     where (x.RuleSet != null && x.RuleSet == r.RuleSet)
                     select x).ToList();

            Rule res;
            if (rs.Any())
            {
                var rules = (from x in rs where x.ExecutedAlready == false select x).ToList();
                var lastExecuted = (from x in rs where x.ExecutedAlready select x).ToList();
                if (rs.Count() == lastExecuted.Count()) {
                    foreach (var executedRule in lastExecuted){
                        executedRule.ExecutedAlready = false;
                    }
                }

                res = rules.Any() ? rules.OneOf() : rs.OneOf();
            }
            else {
                res = r;
            }

            res.ExecutedAlready = true;
            return res;
        }

        public void Run()
        {
            while (Step()) ;
        }
    }

    public class XMLRuleEngine : RuleEngine
    {
        public static RuleEngine LoadXml(XDocument xdoc) {
            var KB =
            (from x in xdoc.Descendants("Rules").First().Elements()
                select Rule.LoadXml(x)).ToList();
            var S = new State();
            var t = from x in xdoc.Descendants("State").First().Elements()
                select x;
            foreach (var v in t) {
                S.Add(v.Attribute("Name").Value, v.Attribute("Value").Value);
            }
            return new RuleEngine(KB, S);
        }
    }


    public class BracketedRuleEngine : RuleEngine
    {
        public static RuleEngine LoadBracketedKb(string filename) {
            Tuple<State, List<Rule>> kbContent = null;

            Task t = Task.Run(async () => {
                kbContent = await fileLineByLine(filename);
            });
            Task.WaitAll(t);

            State initialState = kbContent.Item1;

            List<Rule> rules = kbContent.Item2;

            return new RuleEngine(rules, initialState);
        }


        private static async Task<Tuple<State, List<Rule>>> fileLineByLine(String filename) {
            //List<string> stateConfigLines = new List<string>();
            List<string> rulesConfigLines = new List<string>();

            State S = new State();
            List<Rule> R = new List<Rule>();

            string line;

            

            StorageFolder appInstalledFolder = Windows.ApplicationModel.Package.Current.InstalledLocation;
            


            var file = await appInstalledFolder.GetFileAsync(filename);//.GetFileFromApplicationUriAsync(new Uri($"ms-appx:///{filename}"));


            using (var inputStream = await file.OpenReadAsync())
            using (var classicStream = inputStream.AsStreamForRead())
            using (var streamReader = new StreamReader(classicStream)) {
                // parse states
                while ((line = streamReader.ReadLine()) != null)
                {
                    // skip if only spaces containing line
                    try
                    {
                        Tuple<string, string> assignement = ParseVarAssignementLine(line);
                        //Console.WriteLine($"{assignement.Item1} = {assignement.Item2}");// TODO: logger.info
                        S.Add(assignement.Item1, assignement.Item2);
                    }
                    catch (StateLineParseException e)
                    {
                        break;
                    }
                    catch (RuleEngineException e)
                    {
                        //                    Console.WriteLine(e.Message); // TODO: logger.error
                    }
                }


                string RulesConfigString = line;
                // parse rules
                while ((line = streamReader.ReadLine()) != null)
                {
                    // skip if only spaces containing line
                    RulesConfigString += " " + line;
                }


                R = ParseRulesConfigString(RulesConfigString);
            }

            return new Tuple<State, List<Rule>>(S, R);
        }


        private static List<Rule> ParseRulesConfigString(string rulesConfigString) {
            BracketedConfigProcessor bracketedConfigProcessor = new BracketedConfigProcessor();
            return bracketedConfigProcessor.ProcessConfig(rulesConfigString);
        }


        // TODO: multiline
        private static Tuple<string, string> ParseVarAssignementLine(string configLine) {
            string pattern = $@"^(?<var>{BracketedConfigProcessor.VARNAME_REGEX_PATTERN})\s*=\s*(?<value>.*)$"; // TODO TEST
            Tuple<string, string> t;
            Match m = Regex.Match(configLine, pattern);

            if (m.Length == 0) throw new StateLineParseException();
            t = new Tuple<string, string>(m.Groups["var"].Value, m.Groups["value"].Value);

            return t;
        }
    }

    internal class StateLineParseException : Exception
    {
    }
}
