using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;

using WinTimer = System.Windows.Forms.Timer;

namespace AIsystems_labs.FacesEyes
{
    public class FaceEyeControl : UserControl, IModule
    {
        private readonly Button _btnStart = new Button { Text = "Запустить камеру", AutoSize = true };
        private readonly Button _btnStop = new Button { Text = "Остановить", AutoSize = true, Enabled = false };
        private readonly PictureBox _view = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
        private readonly Panel _top = new Panel { Dock = DockStyle.Top, Height = 48 };

        private VideoCapture? _cap;
        private WinTimer? _timer;
        private CascadeClassifier? _faceCascade;
        private CascadeClassifier? _eyeCascade;

        public string Title => "ЛР1: Лица/Глаза (камера)";

        public FaceEyeControl()
        {
            _top.Padding = new Padding(6);
            _top.Controls.Add(_btnStart); _btnStart.Left = 6; _btnStart.Top = 10;
            _top.Controls.Add(_btnStop); _btnStop.Left = _btnStart.Right + 12; _btnStop.Top = 10;
            Controls.Add(_view);
            Controls.Add(_top);

            _btnStart.Click += (s, e) => Start();
            _btnStop.Click += (s, e) => Stop();
        }

        public void Start()
        {
            if (_cap != null) return;

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string cascadesDir = Path.Combine(baseDir, "Modules", "FacesEyes", "cascades");
            string faceXml = Path.Combine(cascadesDir, "haarcascade_frontalface_default.xml");
            string eyeXml = Path.Combine(cascadesDir, "haarcascade_eye.xml");

            if (!File.Exists(faceXml) || !File.Exists(eyeXml))
            {
                MessageBox.Show("Не найдены XML каскады. Пометь Copy to Output Directory.", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _faceCascade = new CascadeClassifier(faceXml);
            _eyeCascade = new CascadeClassifier(eyeXml);

            try
            {
                _cap = new VideoCapture(0, VideoCapture.API.Any);
                _cap.Set(CapProp.FrameWidth, 1280);
                _cap.Set(CapProp.FrameHeight, 720);
                _cap.Set(CapProp.Fps, 30);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось открыть камеру: " + ex.Message, "Камера",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                Cleanup();
                return;
            }

            _btnStart.Enabled = false;
            _btnStop.Enabled = true;

            _timer = new WinTimer { Interval = 33 };
            _timer.Tick += OnTick;
            _timer.Start();
        }

        public void Stop()
        {
            _btnStart.Enabled = true;
            _btnStop.Enabled = false;

            if (_timer != null)
            {
                _timer.Stop();
                _timer.Tick -= OnTick;
                _timer.Dispose();
                _timer = null;
            }

            Cleanup();
            _view.Image = null;
        }

        private void Cleanup()
        {
            _cap?.Dispose(); _cap = null;
            _faceCascade?.Dispose(); _faceCascade = null;
            _eyeCascade?.Dispose(); _eyeCascade = null;
        }

        private void OnTick(object? sender, EventArgs e)
        {
            if (_cap == null) return;

            using var frame = _cap.QueryFrame();
            if (frame == null || frame.IsEmpty) return;

            using var imgBgr = frame.ToImage<Bgr, byte>();
            using var imgGray = imgBgr.Convert<Gray, byte>();
            CvInvoke.EqualizeHist(imgGray, imgGray);

            var faces = _faceCascade!.DetectMultiScale(imgGray, 1.1, 5, new Size(40, 40), Size.Empty);

            foreach (var rect in faces)
            {
                imgBgr.Draw(rect, new Bgr(Color.Red), 2);

                var faceROI = new Rectangle(rect.X, rect.Y, rect.Width, rect.Height);
                using var faceGray = imgGray.GetSubRect(faceROI);
                var eyes = _eyeCascade!.DetectMultiScale(faceGray, 1.1, 5, new Size(20, 20), Size.Empty);

                foreach (var er in eyes)
                {
                    var eyeRect = new Rectangle(er.X + rect.X, er.Y + rect.Y, er.Width, er.Height);
                    imgBgr.Draw(eyeRect, new Bgr(Color.Lime), 2);
                }
            }

            _view.Image?.Dispose();
            _view.Image = imgBgr.ToBitmap();
        }
    }
}
