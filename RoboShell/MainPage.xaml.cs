using Microsoft.ProjectOxford.Emotion;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Media.Core;
using Windows.Media.FaceAnalysis;
using Windows.Media.MediaProperties;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using RuleEngineNet;
using System.Xml.Linq;
using RoboLogic;
using Microsoft.ProjectOxford.Face;

// Это приложение получает ваше изображение с веб-камеры и
// распознаёт эмоции на нём, обращаясь к Cognitive Services
// Предварительно с помощью Windows UWP API анализируется, есть
// ли на фотографии лицо.

// Эмоции затем сериализуются в формат JSON. Они становятся доступны
// в строке, помеченной TODO:

namespace RoboShell
{
    public sealed partial class MainPage : Page
    {

        MediaCapture MC;

        DispatcherTimer FaceWaitTimer = new DispatcherTimer() { Interval = TimeSpan.FromSeconds(3) };
        DispatcherTimer DropoutTimer = new DispatcherTimer() { Interval = TimeSpan.FromSeconds(1) };

        EmotionServiceClient EmoAPI = new EmotionServiceClient(Config.EmotionAPIKey,Config.EmotionAPIEndpoint);
        FaceServiceClient FaceAPI = new FaceServiceClient(Config.FaceAPIKey,Config.FaceAPIEndpoint);


        FaceDetectionEffect FaceDetector;
        VideoEncodingProperties VideoProps;

        bool IsFacePresent = false;
        bool InDialog = false; 

        RuleEngine RE;

        public MainPage()
        {
            this.InitializeComponent();
        }

        /// <summary>
        /// Первоначальная инициализация страницы
        /// </summary>
        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            var spk = new UWPLocalSpeaker(media);
            var xdoc = XDocument.Load("Robot.kb.xml");
            RE = RuleEngine.LoadXml(xdoc);
            RE.SetSpeaker(spk);
            FaceWaitTimer.Tick += StartDialog;
            DropoutTimer.Tick += FaceDropout;
            await Init();
        }

        /// <summary>
        /// Инициализирует работу с камерой и с локальным распознавателем лиц
        /// </summary>
        private async Task Init()
        {
            MC = new MediaCapture();
            var cameras = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            var camera = cameras.First();
            var settings = new MediaCaptureInitializationSettings() { VideoDeviceId = camera.Id };
            await MC.InitializeAsync(settings);
            ViewFinder.Source = MC;

            // Create face detection
            var def = new FaceDetectionEffectDefinition();
            def.SynchronousDetectionEnabled = false;
            def.DetectionMode = FaceDetectionMode.HighPerformance;
            FaceDetector = (FaceDetectionEffect)(await MC.AddVideoEffectAsync(def, MediaStreamType.VideoPreview));
            FaceDetector.FaceDetected += FaceDetectedEvent;
            FaceDetector.DesiredDetectionInterval = TimeSpan.FromMilliseconds(100);
            FaceDetector.Enabled = true;

            await MC.StartPreviewAsync();
            var props = MC.VideoDeviceController.GetMediaStreamProperties(MediaStreamType.VideoPreview);
            VideoProps = props as VideoEncodingProperties;
        }

        /// <summary>
        /// Срабатывает при локальном обнаружении лица на фотографии.
        /// Рисует рамку и устанавливает переменную IsFacePresent=true
        /// </summary>
        private async void FaceDetectedEvent(FaceDetectionEffect sender, FaceDetectedEventArgs args)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => HighlightDetectedFace(args.ResultFrame.DetectedFaces.FirstOrDefault()));
        }

        /// <summary>
        /// Отвечает за рисование прямоугольника вокруг лица
        /// </summary>
        /// <param name="face">Обнаруженное лицо</param>
        /// <returns></returns>
        private async Task HighlightDetectedFace(DetectedFace face)
        {
            var cx = ViewFinder.ActualWidth / VideoProps.Width;
            var cy = ViewFinder.ActualHeight / VideoProps.Height;

            if (face == null)
            {
                FaceRect.Visibility = Visibility.Collapsed;
                FaceWaitTimer.Stop();
                IsFacePresent = false;
                if (InDialog)
                {
                    DropoutTimer.Start();
                }
            }
            else
            {
                FaceRect.Margin = new Thickness(cx * face.FaceBox.X, cy * face.FaceBox.Y, 0, 0);
                FaceRect.Width = cx * face.FaceBox.Width;
                FaceRect.Height = cy * face.FaceBox.Height;
                FaceRect.Visibility = Visibility.Visible;
                IsFacePresent = true;
                FaceWaitTimer.Start(); // wait for 3 seconds to make sure face stable
            }
        }

        void FaceDropout(object sender, object e)
        {
            RE.Reset();
            RE.SetVar("Event", "FaceOut");
            RE.Run();
        }


        async void StartDialog(object sender, object e)
        {
            if (!IsFacePresent) return;
            var res = await RecognizeFace();
            if (res)
            {
                RE.SetVar("Event", "FaceIn");
                RE.Run();
            }
        }

        async Task<bool> RecognizeFace()
        { 
            if (!IsFacePresent) return false;
            FaceWaitTimer.Stop(); 
            var ms = new MemoryStream();
            await MC.CapturePhotoToStreamAsync(ImageEncodingProperties.CreateJpeg(), ms.AsRandomAccessStream());

            ms.Position = 0L;
            var Fce = await FaceAPI.DetectAsync(ms,false,false,new FaceAttributeType[] { FaceAttributeType.Age, FaceAttributeType.Gender });

            // ms.Position = 0L;
            // var Emo = await EmoAPI.RecognizeAsync(ms);

            if (Fce != null && Fce.Length > 0)
            {
                int males = 0, females = 0, count = 0;
                double sumage = 0;
                foreach(var f in Fce)
                {
                    if (f.FaceAttributes.Gender == "male") males++; else females++;
                    count++;
                    sumage += f.FaceAttributes.Age++;
                }
                RE.SetVar("FaceCount", count.ToString());
                if (males == 0 && females > 0) RE.SetVar("Gender", "F");
                if (males > 0 && females == 0) RE.SetVar("Gender", "M");
                if (males > 0 && females > 0) RE.SetVar("Gender", males > females ? "MF" : "FM");
                RE.SetVar("Age", ((int)(sumage / count)).ToString());
                RE.Run();
            }
            else
            {
                FaceWaitTimer.Start();
                return false;
            }
        }
    }
}

