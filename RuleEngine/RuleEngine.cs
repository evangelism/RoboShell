using RoboLogic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace RuleEngineNet
{
    public class RuleEngine
    {
        public State State { get; private set; }
        public State InitialState { get; private set; }
        public List<Rule> KnowlegeBase { get; private set; }
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

        public static RuleEngine LoadXml(XDocument xdoc)
        {
            var KB =
                (from x in xdoc.Descendants("Rules").First().Elements()
                 select Rule.LoadXml(x)).ToList();
            var S = new State();
            var t = from x in xdoc.Descendants("State").First().Elements()
                    select x;
            foreach(var v in t)
            {
                S.Add(v.Attribute("Name").Value, v.Attribute("Value").Value);
            }
            return new RuleEngine(KB,S);
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
            var cs = GetConflictSet(State);
            if (cs.Count() > 0)
            {
                var rule = cs.First();
                rule.Then.Execute(State);
                rule.Active = false;
                return true;
            }
            else return false;
        }

        public void Run()
        {
            while (Step()) ;
        }

    }
}
