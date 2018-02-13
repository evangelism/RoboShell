using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RuleEngineNet;

namespace RuleEngineNet {
    class BracketedConfigProcessor {
        public List<Rule> ProcessConfig(string config) {
            List<Rule> rules = new List<Rule>();

            int[] openingBracesPositions = AllIndexesOf(config, "(");
            int[] closingBracesPositions = AllIndexesOf(config, ")");

            int curPos = 0;
            int openingBracePositionIndex = 0;

            while (openingBracePositionIndex != openingBracesPositions.Length) {
                int currentOpeningBracePosition = openingBracesPositions[openingBracePositionIndex];

                //TODO: check substring
                if (!Regex.IsMatch(config.Substring(curPos, currentOpeningBracePosition - curPos),
                    @"\s*")) {
                    throw new ConfigParseException();
                }

                int tryingClosingBracePosition = currentOpeningBracePosition;
                while (true) {
                    int i;
                    for (i = 0; i < closingBracesPositions.Length; i++) {
                        if (closingBracesPositions[i] <= tryingClosingBracePosition) continue;
                        tryingClosingBracePosition = closingBracesPositions[i];
                        break;
                    }

                    if (i == closingBracesPositions.Length) throw new ConfigParseException();

                    try {
                        int possibleRuleSubstringLength =
                            tryingClosingBracePosition - currentOpeningBracePosition + 1;
                        Rule rule = Rule.ParseRule(config.Substring(currentOpeningBracePosition,
                            possibleRuleSubstringLength));
                        rules.Add(rule);
                        curPos = tryingClosingBracePosition;

                        while (openingBracePositionIndex < openingBracesPositions.Length &&
                               openingBracesPositions[openingBracePositionIndex] <= curPos) {
                            openingBracePositionIndex++;
                        }

                        break;
                    }
                    catch {
                        continue;
                    }
                }
            }

            if (!Regex.IsMatch(config.Substring(curPos + 1), @"^\s*$")) {
                throw new ConfigParseException();
            }

//            Console.WriteLine("config parsed!");
            return rules;
        }


        public static bool AssertValidString(string possibleString) {
            if (possibleString == null) return false;
            if (possibleString.Replace("\\\"", "").Contains("\"")) {
                return false;
            }

            return true;
        }


        public static int[] AllIndexesOf(string str, string searchstring) {
            List<int> indexes = new List<int>();

            int minIndex = str.IndexOf(searchstring);
            while (minIndex != -1) {
                indexes.Add(minIndex);
                minIndex = str.IndexOf(searchstring, minIndex + searchstring.Length);
            }

            return indexes.ToArray();
        }

        public static string VARNAME_REGEX_PATTERN = @"[a-zA-Z][a-zA-Z0-9_]*";
    }

    internal class ConfigParseException : Exception
    {
    }
}
