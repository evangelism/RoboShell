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
    public class LEDManager
    {
        protected Canvas canvas { get; set; }
        public Dictionary<string, LED> LEDS { get; private set; } = new Dictionary<string, LED>();

        public LEDManager(Canvas canvas)
        {
            this.canvas = canvas;
        }

        public void AddLED(string name, int nx, int ny, double px, double py)
        {
            var L = new LED(name, nx, ny, canvas);
            var xoff = (canvas.ActualWidth - L.nx*10) * px;
            var yoff = (canvas.ActualHeight - L.ny*10) * py;
            canvas.Children.Add(L.Panel);
            Canvas.SetTop(L.Panel, yoff);
            Canvas.SetLeft(L.Panel, xoff);
            LEDS.Add(name, L);
        }
    }
}
