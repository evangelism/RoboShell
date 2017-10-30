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

    public class LED
    {
        public int nx { get; private set; }
        public int ny { get; private set; }
        public string Name { get; private set; }
        public StackPanel Panel { get; private set; }
        public Rectangle[,] Matrix { get; set; }

        public Brush Off { get; set; } = new SolidColorBrush(Colors.Black);
        public Brush On { get; set; } = new SolidColorBrush(Colors.Green);

        public LED(string name, int nx, int ny, Canvas canvas)
        {
            this.nx = nx; this.ny = ny;
            this.Name = name;
            Matrix = new Rectangle[nx, ny];
            Panel = CreateMatrix(Matrix);
            Panel.Name = name;
        }

        public int Offset { get; set; } = -1;
        public LEDImage Image { get; set; }

        public StackPanel CreateMatrix(Rectangle[,] M)
        {
            var sp = new StackPanel();
            for (int i = 0; i < M.GetLength(1); i++)
            {
                var s = new StackPanel();
                s.Orientation = Orientation.Horizontal;
                for (int j = 0; j < M.GetLength(0); j++)
                {
                    var R = new Rectangle();
                    R.Fill = Off;
                    R.Width = R.Height = 10;
                    M[j, i] = R;
                    s.Children.Add(R);
                }
                sp.Children.Add(s);
            }
            return sp;
        }

        public void LoadStatic(LEDImage Frame, int offset = 0)
        {
            for (int i=0;i<nx;i++)
                for (int j=0;j<ny;j++)
                {
                    Matrix[i, j].Fill = Frame.Matrix[i, j+offset] ? On : Off;
                }
        }

        protected DispatcherTimer ReloadTimer = new DispatcherTimer();

        public void Load(LEDImage Frame)
        {
            ReloadTimer.Stop();
            this.Image = Frame;
            Offset = 0;
            if (Image.Offsets != null)
            {
                ReloadTimer.Tick += (s, a) => ReloadImage();
                ReloadImage();
            }
            else
            {
                LoadStatic(Frame);
            }
        }

        protected void ReloadImage()
        {
            ReloadTimer.Stop();
            LoadStatic(Image, Image.Offsets[Offset].Item1);
            ReloadTimer.Interval = TimeSpan.FromSeconds(Image.Offsets[Offset].Item2);
            Offset++;
            if (Offset >= Image.Offsets.Count) Offset = 0;
            ReloadTimer.Start();
        }
    }
}
