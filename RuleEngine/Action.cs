using RoboLogic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace RuleEngineNet
{
    public abstract class Action
    {
        public abstract void Execute(State S);
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
                case "OneOf":
                    return new OneOf(from z in X.Elements() select Action.LoadXml(z));
                default:
                    throw new RuleEngineException("Unsupported action type");
            }
        }
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

        public override void Execute(State S)
        {
            Speaker.Speak(S.EvalString(Text));
        }

        public static Say Parse(XElement X)
        {
            return new Say(X.Attribute("Text").Value);
        }
    }
}
