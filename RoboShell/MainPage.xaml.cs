using Microsoft.ProjectOxford.Emotion;
using Newtonsoft.Json;
using RoboShell.Logic;
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
using Windows.UI.Xaml.Media;
using Windows.System;
using Microsoft.ProjectOxford.Emotion.Contract;
using RoboShell.LED;
using System.Net.Http;
using System.Text;
using System.Net;
using Windows.Devices.Gpio;
using System.Collections.Generic;

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

        DispatcherTimer FaceWaitTimer = new DispatcherTimer() { Interval = TimeSpan.FromSeconds(2) };
        DispatcherTimer DropoutTimer = new DispatcherTimer() { Interval = TimeSpan.FromSeconds(3) };
        DispatcherTimer InferenceTimer = new DispatcherTimer() { Interval = TimeSpan.FromSeconds(1) };
        DispatcherTimer GpioTimer = new DispatcherTimer() { Interval = TimeSpan.FromMilliseconds(500) };
        DispatcherTimer ArduinoInputTimer = new DispatcherTimer() { Interval = TimeSpan.FromMilliseconds(500) };

        //EmotionServiceClient EmoAPI = new EmotionServiceClient(Config.EmotionAPIKey,Config.EmotionAPIEndpoint);
        //FaceServiceClient FaceAPI = new FaceServiceClient(Config.FaceAPIKey,Config.FaceAPIEndpoint);

        private static HttpClient httpClient = new HttpClient();

        FaceDetectionEffect FaceDetector;
        VideoEncodingProperties VideoProps;

        LEDManager LEDMgr;

        private GpioPin[] ArduinoPins;
        private readonly int[] ArduinoPinsNumbers = Config.InputPinsNumbers; //must change

        GpioController gpio;

        private void InitGpio()
        {
            gpio = GpioController.GetDefault();
            ArduinoPins = new GpioPin[ArduinoPinsNumbers.Length];
            if (gpio == null)
            {
                return;
            }

            for(int i = 0; i < ArduinoPinsNumbers.Length; i++)
            {
                ArduinoPins[i] = gpio.OpenPin(ArduinoPinsNumbers[i]);
                ArduinoPins[i].SetDriveMode(GpioPinDriveMode.Input);
            }

            Trace($"Gpio initialized correctly.");

        }

        bool IsFacePresent = false; // Shows the short-term state of the face in camera
        bool InDialog = false; // represents long-term state - are we in dialog, or waiting for user
        bool CaptureAfterEnd = false; // do face capture after speech ends

        RuleEngine RE;

        int BoringCounter = 60;

        public MainPage()
        {
            this.InitializeComponent();
        }

        

        public void Trace(string s)
        {
            System.Diagnostics.Debug.WriteLine(s);
            if (!Config.Headless) log.Text += s + "\r\n";
        }

        /// <summary>
        /// Первоначальная инициализация страницы
        /// </summary>
        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            var spk = new UWPLocalSpeaker(media,Windows.Media.SpeechSynthesis.VoiceGender.Female);
            Trace("Loading knowlegdebase");
            //var xdoc = XDocument.Load("Robot.kb.xml");
            //RE = XMLRuleEngine.LoadXml(xdoc);
            var filename = "GpioTest.kb.brc";
            RE = BracketedRuleEngine.LoadBracketedKb(filename);
            RE.SetSpeaker(spk);
            RE.SetExecutor(ExExecutor);
            FaceWaitTimer.Tick += StartDialog;
            DropoutTimer.Tick += FaceDropout;
            InferenceTimer.Tick += InferenceStep;
            InitGpio();
            if (gpio != null)
            {
                ArduinoInputTimer.Tick += ArduinoInput;
                ArduinoInputTimer.Start();
            }
            media.MediaEnded += EndSpeech;
            CoreWindow.GetForCurrentThread().KeyDown += KeyPressed;
            await Init();
            LEDMgr = new LEDManager(canvas);
            if (!Config.Headless)
            {
                LEDMgr.AddLED("LE", 8, 8, 0.3, 0.2);
                LEDMgr.AddLED("RE", 8, 8, 0.7, 0.2);
                LEDMgr.AddLED("M", 10, 5, 0.5, 0.8);
                LEDMgr.LEDS["LE"].Load(new LEDImage("eye_blink"));
                LEDMgr.LEDS["RE"].Load(new LEDImage("eye_blink"));
                LEDMgr.LEDS["M"].Load(new LEDImage("mouth_neutral"));
            }
        }

        private async void EndSpeech(object sender, RoutedEventArgs e)
        {
            if (CaptureAfterEnd)
            {
                CaptureAfterEnd = false;
                await RecognizeFace();
            }
        }

        // Function used to execute extensions commands of rule engine
        private async void ExExecutor(string Cmd, string Param)
        {
            switch (Cmd)
            {
                case "Recapture":
                    if (Param == "EndSpeech") CaptureAfterEnd = true;
                    else if (Param.StartsWith("After:"))
                    {
                        var t = Param.Split(':');
                        var v = double.Parse(t[1]);
                        var dt = new DispatcherTimer() { Interval = TimeSpan.FromSeconds(v) };
                        dt.Tick +=
                            async (s, ea) => { dt.Stop(); await RecognizeFace(); };
                        dt.Start();
                    }
                    else await RecognizeFace();
                    break;
                case "LED":
                    var t1 = Param.Split(':');
                    LEDMgr.LEDS[t1[0]].Load(new LEDImage(t1[1]));
                    break;
            }
        }

        private void ArduinoInput(object sender, object e)
        {
            string input = "";
            for (int i = 0; i < ArduinoPinsNumbers.Length; ++i)
            {
                if (ArduinoPins[i].Read() == GpioPinValue.High)
                {
                    input += "1";
                } else
                {
                    input += "0";
                }
            }
            if (input != "0000") {
                Trace($"Received: {input}");
            }
            RE.SetVar("ArduinoInput", input);
        }

        private void KeyPressed(CoreWindow sender, KeyEventArgs args)
        {
            if (args.VirtualKey >= VirtualKey.Number0 &&
                args.VirtualKey <= VirtualKey.Number9)
            {
                var st = $"Key_{args.VirtualKey - VirtualKey.Number0}";
                Trace($"Initiating event {st}");
                RE.SetVar("Event", st);
                RE.Step();
            }
            // S = print state
            if (args.VirtualKey == VirtualKey.S)
            {
                foreach(var x in RE.State)
                {
                    Trace($" > {x.Key} -> {x.Value}");
                }
            }
        }

        Random Rnd = new Random();

        private void InferenceStep(object sender, object e)
        {
            if (media.CurrentState == MediaElementState.Playing) return;
            if (!InDialog) BoringCounter--;
            if (BoringCounter==0)
            {
                RE.SetVar("Event", "Ping");
                Trace("Ping event intiated");
                BoringCounter = Rnd.Next(Config.MinBoringSeconds, Config.MaxBoringSeconds);
            }
            var s = RE.StepUntilLongRunning();
        }

        /// <summary>
        /// Инициализирует работу с камерой и с локальным распознавателем лиц
        /// </summary>
        private async Task Init()
        {
            Trace("Initializing media...");
            MC = new MediaCapture();
            var cameras = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            var camera = cameras.Last();
            var settings = new MediaCaptureInitializationSettings() { VideoDeviceId = camera.Id };
            await MC.InitializeAsync(settings);

            if (!Config.Headless)
            {
                ViewFinder.Source = MC;
            }

            // Create face detection
            var def = new FaceDetectionEffectDefinition();
            def.SynchronousDetectionEnabled = false;
            def.DetectionMode = FaceDetectionMode.HighPerformance;
            FaceDetector = (FaceDetectionEffect)(await MC.AddVideoEffectAsync(def, MediaStreamType.VideoPreview));
            FaceDetector.FaceDetected += FaceDetectedEvent;
            FaceDetector.DesiredDetectionInterval = TimeSpan.FromMilliseconds(100);
            FaceDetector.Enabled = true;
            Trace("Ready to start face recognition");
            await MC.StartPreviewAsync();
            Trace("Face Recognition Started");
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
            double cx=0, cy=0;
            if (!Config.Headless)
            {
                cx = ViewFinder.ActualWidth / VideoProps.Width;
                cy = ViewFinder.ActualHeight / VideoProps.Height;
            }

            if (face == null)
            {
                if (!Config.Headless) FaceRect.Visibility = Visibility.Collapsed;
                FaceWaitTimer.Stop();
                if (IsFacePresent)
                {
                    IsFacePresent = false;
                    DropoutTimer.Start();
                }
            }
            else
            {
                DropoutTimer.Stop();
                if (!Config.Headless)
                {
                    FaceRect.Margin = new Thickness(cx * face.FaceBox.X, cy * face.FaceBox.Y, 0, 0);
                    FaceRect.Width = cx * face.FaceBox.Width;
                    FaceRect.Height = cy * face.FaceBox.Height;
                    FaceRect.Visibility = Visibility.Visible;
                }
                if (!IsFacePresent)
                {
                    IsFacePresent = true;
                    if (!InDialog) FaceWaitTimer.Start(); // wait for 3 seconds to make sure face stable
                }
            }
        }

        void FaceDropout(object sender, object e)
        {
            DropoutTimer.Stop();
            InDialog = false;
            BoringCounter = Rnd.Next(Config.MinBoringSeconds, Config.MaxBoringSeconds);
            Trace("Face dropout initiated");
            InferenceTimer.Stop();
            RE.Reset();
            RE.SetVar("Event", "FaceOut");
            RE.Run();
            InferenceTimer.Start();
        }


        async void StartDialog(object sender, object e)
        {
            if (!IsFacePresent) return;
            InDialog = true;
            Trace("Calling face recognition");
            var res = await RecognizeFace();
            if (res)
            {
                Trace("Initiating FaceIn Event");
                RE.SetVar("Event", "FaceIn");
                RE.Step();
                InferenceTimer.Start();
            }
        }

        async Task<bool> RecognizeFace() {
            if (!IsFacePresent) {
                return false;
            }
            Trace("RecognizeFace() started");
            FaceWaitTimer.Stop();

            var photoAsStream = new MemoryStream();
            await MC.CapturePhotoToStreamAsync(ImageEncodingProperties.CreateJpeg(), photoAsStream.AsRandomAccessStream());

            byte[] photoAsByteArray = photoAsStream.ToArray();

            Trace("BEFORE ProcessPhotoAsync()");
            PhotoInfoDTO photoInfo = await ProcessPhotoAsync(photoAsByteArray, Config.RecognizeEmotions);
            Trace("AFTER ProcessPhotoAsync()");

            if (photoInfo.FoundAndProcessedFaces) {
                RE.SetVar("FaceCount", photoInfo.FaceCountAsString);
                RE.SetVar("Gender", photoInfo.Gender);
                RE.SetVar("Age", photoInfo.Age);
                if (Config.RecognizeEmotions) {
                    RE.SetVar("Emotion", photoInfo.Emotion);
                }

                Trace($"Face data: #faces={RE.State.Eval("FaceCount")}, age={RE.State.Eval("Age")}, gender={RE.State.Eval("Gender")}, emo={RE.State.Eval("Emotion")}");
                Trace("RecognizeFace() finished");
                return true;
            }
            else {
                FaceWaitTimer.Start();
                Trace("RecognizeFace() finished");
                return false;
            }
        }


        async Task<PhotoInfoDTO> ProcessPhotoAsync(byte[] photoAsByteArray, bool recognizeEmotions) {
            PhotoToProcessDTO photoToProcessDTO = new PhotoToProcessDTO {
                PhotoAsByteArray = photoAsByteArray,
                RecognizeEmotions = recognizeEmotions
            };
            var json = JsonConvert.SerializeObject(photoToProcessDTO);

            PhotoInfoDTO photoInfoDTO;

            using (StringContent content = new StringContent(json.ToString(), Encoding.UTF8, "application/json")){
                try {
                    HttpResponseMessage response = await httpClient.PostAsync(Config.CognitiveEndpoint, content);
                    if (response.StatusCode.Equals(HttpStatusCode.OK)) {
                        photoInfoDTO = JsonConvert.DeserializeObject<PhotoInfoDTO>(await response.Content.ReadAsStringAsync());
                    }
                    else {
                        Trace("No faces found and analyzed");
                        photoInfoDTO = new PhotoInfoDTO {
                            FoundAndProcessedFaces = false
                        };
                    }
                } catch (Exception e) {
                    Trace("Error! Exception message: " + e.Message);
                    photoInfoDTO = new PhotoInfoDTO {
                        FoundAndProcessedFaces = false
                    };
                }
                
            }
           
            return photoInfoDTO;
        }
    }

    class PhotoToProcessDTO {
        public byte[] PhotoAsByteArray { get; set; }
        public bool RecognizeEmotions { get; set; }
    }

    class PhotoInfoDTO {
        public string FaceCountAsString { get; set; }
        public string Gender { get; set; }
        public string Age { get; set; }
        public string Emotion { get; set; }
        public bool FoundAndProcessedFaces { get; set; }
    }
}

