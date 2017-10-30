using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;

namespace RoboShell.LED
{
    public class LEDImage
    {
        public bool[,] Matrix { get; set; }
        public List<Tuple<int, double>> Offsets { get; set; }
        public LEDImage(string name)
        {
            var fn = File.ReadAllLines(@"LEDFrames\" + name + ".txt");
            var k = 0;
            while (fn[k].Contains(':'))
            {
                var t = fn[k++].Split(':');
                if (Offsets == null) Offsets = new List<Tuple<int, double>>();
                Offsets.Add(new Tuple<int, double>(int.Parse(t[0]), double.Parse(t[1])));
            }
            var x = fn[k].Length;
            var y = fn.Length;
            Matrix = new bool[x, y];
            for (var i=0; k < fn.Length; k++,i++)
            {
                var a = fn[k].ToCharArray();
                for (int j = 0; j < x; j++)
                {
                    Matrix[j, i] = !(a[j] == ' ' || a[j] == '_');
                }
            }
        }
    }
}

