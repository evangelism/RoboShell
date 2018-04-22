using System.Text.RegularExpressions;

namespace DBCreator
{
    class Program
    {
        private static void ReadIncludes(string source)
        {
            string line;
            var file =
                new System.IO.StreamReader(source);
            var output =
                new System.IO.StreamWriter(@"tmp.brc", true);
            const string includeRegex = "^\\(include\\s+(?<filename>[^)]+)\\)$";
            while ((line = file.ReadLine()) != null)
            {
                if (!line.Contains("include"))
                    output.WriteLine(line);
                else
                {
                    var m = Regex.Match(line, includeRegex);
                    var includingFilename = m.Groups["filename"].Value;
                    output.Close();
                    ReadIncludes(includingFilename);
                    output =
                        new System.IO.StreamWriter(@"tmp.brc", true);
                }
            }

            file.Close();
            output.Close();
        }

        private static void Main(string[] args)
        {
            var output =
                new System.IO.StreamWriter(@"tmp.brc");
            output.Close();
            ReadIncludes("main.brc");
        }
    }
}
