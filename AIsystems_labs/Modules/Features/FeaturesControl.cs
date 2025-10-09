using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Features2D;
using Emgu.CV.Structure;
using Emgu.CV.Util;

namespace AIsystems_labs.Features
{
    public class FeaturesControl : UserControl, IModule
    {
        private readonly Panel _top = new Panel { Dock = DockStyle.Top, Height = 72, Padding = new Padding(6) };
        private readonly Button _btnLoadScene = new Button { Text = "Загрузить сцену", AutoSize = true };
        private readonly Button _btnLoadTemplate = new Button { Text = "Загрузить образ", AutoSize = true };
        private readonly Button _btnMatch = new Button { Text = "Сопоставить", AutoSize = true, Enabled = false };
        private readonly Button _btnClear = new Button { Text = "Очистить", AutoSize = true };

        private readonly Label _lFeat = new Label { Text = "Features:", AutoSize = true };
        private readonly NumericUpDown _numFeat = new NumericUpDown { Minimum = 500, Maximum = 8000, Increment = 500, Value = 3000, Width = 70 };
        private readonly Label _lRatio = new Label { Text = "Ratio:", AutoSize = true };
        private readonly NumericUpDown _numRatio = new NumericUpDown { Minimum = 50, Maximum = 90, Increment = 5, Value = 70, Width = 60 };
        private readonly Label _lEps = new Label { Text = "RANSAC ε:", AutoSize = true };
        private readonly NumericUpDown _numEps = new NumericUpDown { Minimum = 1, Maximum = 10, Value = 5, Width = 60 };

        private readonly Label _lblStats = new Label { Text = "kpT/kpS: –/–  matches/good/safe: –/–/–  inliers: –  mode: –", AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right };

        private readonly PictureBox _view = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };

        private Mat? _sceneMat;
        private Mat? _tmplMat;

        public string Title => "ЛР2: Устойчивые признаки";

        public FeaturesControl()
        {
            // Размещение
            int x = 6, y = 8, gap = 10;
            _top.Controls.Add(_btnLoadScene); _btnLoadScene.Left = x; _btnLoadScene.Top = y; x += _btnLoadScene.Width + gap;
            _top.Controls.Add(_btnLoadTemplate); _btnLoadTemplate.Left = x; _btnLoadTemplate.Top = y; x += _btnLoadTemplate.Width + gap;
            _top.Controls.Add(_btnMatch); _btnMatch.Left = x; _btnMatch.Top = y; x += _btnMatch.Width + gap;
            _top.Controls.Add(_btnClear); _btnClear.Left = x; _btnClear.Top = y; x += _btnClear.Width + 22;

            _top.Controls.Add(_lFeat); _lFeat.Left = x; _lFeat.Top = y + 3; x += _lFeat.Width + 4;
            _top.Controls.Add(_numFeat); _numFeat.Left = x; _numFeat.Top = y; x += _numFeat.Width + 12;
            _top.Controls.Add(_lRatio); _lRatio.Left = x; _lRatio.Top = y + 3; x += _lRatio.Width + 4;
            _top.Controls.Add(_numRatio); _numRatio.Left = x; _numRatio.Top = y; x += _numRatio.Width + 12;
            _top.Controls.Add(_lEps); _lEps.Left = x; _lEps.Top = y + 3; x += _lEps.Width + 4;
            _top.Controls.Add(_numEps); _numEps.Left = x; _numEps.Top = y; x += _numEps.Width + 24;

            _top.Controls.Add(_lblStats);
            _lblStats.Top = y + 3; _lblStats.Left = _top.Width - _lblStats.Width - 8;
            _lblStats.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            Controls.Add(_view);
            Controls.Add(_top);

            _btnLoadScene.Click += (s, e) => LoadImage(true);
            _btnLoadTemplate.Click += (s, e) => LoadImage(false);
            _btnMatch.Click += (s, e) => MatchAndDraw();
            _btnClear.Click += (s, e) => { _view.Image = null; _lblStats.Text = "kpT/kpS: –/–  matches/good/safe: –/–/–  inliers: –  mode: –"; };

            Disposed += (s, e) => { _sceneMat?.Dispose(); _tmplMat?.Dispose(); };
        }

        public void Start() { }
        public void Stop() { }

        private void LoadImage(bool isScene)
        {
            using var ofd = new OpenFileDialog { Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp" };
            if (ofd.ShowDialog() != DialogResult.OK) return;

            var img = CvInvoke.Imread(ofd.FileName);
            if (img.IsEmpty) { MessageBox.Show("Не удалось загрузить изображение."); img.Dispose(); return; }

            if (isScene) { _sceneMat?.Dispose(); _sceneMat = img; }
            else { _tmplMat?.Dispose(); _tmplMat = img; }

            _view.Image?.Dispose();
            _view.Image = img.ToBitmap();
            _btnMatch.Enabled = _sceneMat != null && _tmplMat != null;
        }

        private void MatchAndDraw()
        {
            if (_sceneMat == null || _tmplMat == null) return;

            // Серый + выравнивание
            using var sceneGray = new Mat(); using var tmplGray = new Mat();
            CvInvoke.CvtColor(_sceneMat, sceneGray, ColorConversion.Bgr2Gray);
            CvInvoke.CvtColor(_tmplMat, tmplGray, ColorConversion.Bgr2Gray);
            CvInvoke.EqualizeHist(sceneGray, sceneGray);
            CvInvoke.EqualizeHist(tmplGray, tmplGray);

            // ORB детектор
            int features = (int)_numFeat.Value;
            double ratio = (int)_numRatio.Value / 100.0;
            double eps = (double)_numEps.Value;

            using var orb = new ORB(features);
            using var kpScene = new VectorOfKeyPoint();
            using var kpTmpl = new VectorOfKeyPoint();
            using var descScene = new Mat();
            using var descTmpl = new Mat();

            orb.DetectAndCompute(sceneGray, null, kpScene, descScene, false);
            orb.DetectAndCompute(tmplGray, null, kpTmpl, descTmpl, false);

            // KNN + ratio
            using var matcher = new BFMatcher(DistanceType.Hamming, crossCheck: false);
            using var knnMatches = new VectorOfVectorOfDMatch();
            matcher.KnnMatch(descTmpl, descScene, knnMatches, 2, null);

            var good = new VectorOfDMatch();
            for (int i = 0; i < knnMatches.Size; i++)
            {
                var m = knnMatches[i];
                if (m.Size < 2) continue;
                if (m[0].Distance < ratio * m[1].Distance)
                    good.Push(new[] { m[0] });
            }

            // sanitize индексов
            var safe = new VectorOfDMatch();
            int nT = kpTmpl.Size, nS = kpScene.Size;
            foreach (var m in good.ToArray())
                if (m.QueryIdx >= 0 && m.QueryIdx < nT && m.TrainIdx >= 0 && m.TrainIdx < nS)
                    safe.Push(new[] { m });

            int inliers = 0;
            Mat? H = null;

            if (safe.Size >= 4)
            {
                using var ptsT = new VectorOfPointF(safe.ToArray().Select(m => kpTmpl[m.QueryIdx].Point).ToArray());
                using var ptsS = new VectorOfPointF(safe.ToArray().Select(m => kpScene[m.TrainIdx].Point).ToArray());
                using var mask = new Mat();
                H = CvInvoke.FindHomography(ptsT, ptsS, RobustEstimationAlgorithm.Ransac, eps, mask);
                if (!mask.IsEmpty) inliers = CvInvoke.CountNonZero(mask);
            }

            // Ключевыми точками
            bool okByFeatures = H != null && !H.IsEmpty && inliers >= 10;

            // Подготовим холст (tmpl | scene)
            int h = Math.Max(_tmplMat.Height, _sceneMat.Height);
            int w = _tmplMat.Width + _sceneMat.Width;
            var result = new Mat(h, w, DepthType.Cv8U, 3);
            result.SetTo(new MCvScalar(0, 0, 0));

            var left = new Mat(result, new Rectangle(0, 0, _tmplMat.Width, _tmplMat.Height));
            var right = new Mat(result, new Rectangle(_tmplMat.Width, 0, _sceneMat.Width, _sceneMat.Height));
            _tmplMat.CopyTo(left); _sceneMat.CopyTo(right);

            // рисуем соответствия (без падений)
            foreach (var m in safe.ToArray())
            {
                var p1 = kpTmpl[m.QueryIdx].Point;
                var p2 = kpScene[m.TrainIdx].Point; p2.X += _tmplMat.Width;
                CvInvoke.Circle(result, Point.Round(p1), 3, new MCvScalar(0, 255, 0), -1);
                CvInvoke.Circle(result, Point.Round(p2), 3, new MCvScalar(0, 255, 0), -1);
                CvInvoke.Line(result, Point.Round(p1), Point.Round(p2), new MCvScalar(255, 0, 0), 1);
            }

            if (okByFeatures)
            {
                // Полигон в координатах сцены (учёт сдвига)
                int wT = _tmplMat.Width, hT = _tmplMat.Height;
                var srcQuad = new[] { new PointF(0, 0), new PointF(wT, 0), new PointF(wT, hT), new PointF(0, hT) };
                var dstQuad = CvInvoke.PerspectiveTransform(srcQuad, H);
                var poly = dstQuad.Select(p => new Point((int)(p.X + _tmplMat.Width), (int)p.Y)).ToArray();
                CvInvoke.Polylines(result, poly, true, new MCvScalar(0, 0, 255), 3);

                _lblStats.Text = $"kpT/kpS: {nT}/{nS}  matches/good/safe: {knnMatches.Size}/{good.Size}/{safe.Size}  inliers: {inliers}  mode: features";
            }
            else
            {
                // Template Matching по нескольким масштабам
                double[] scales = new double[] { 0.5, 0.65, 0.8, 1.0, 1.25, 1.5 };
                double bestScore = double.NegativeInfinity;
                Rectangle bestRect = Rectangle.Empty;
                double bestScale = 1.0;

                foreach (var s in scales)
                {
                    using var tResized = new Mat();
                    if (Math.Abs(s - 1.0) < 1e-6) tmplGray.CopyTo(tResized);
                    else CvInvoke.Resize(tmplGray, tResized, new Size((int)(tmplGray.Width * s), (int)(tmplGray.Height * s)), 0, 0, Inter.Area);

                    if (tResized.Width < 10 || tResized.Height < 10) continue;
                    if (tResized.Width >= sceneGray.Width || tResized.Height >= sceneGray.Height) continue;

                    int rw = sceneGray.Width - tResized.Width + 1;
                    int rh = sceneGray.Height - tResized.Height + 1;
                    using var resp = new Mat(rh, rw, DepthType.Cv32F, 1);

                    CvInvoke.MatchTemplate(sceneGray, tResized, resp, TemplateMatchingType.CcoeffNormed);
                    double minVal = 0, maxVal = 0;
                    Point minLoc = Point.Empty, maxLoc = Point.Empty;
                    CvInvoke.MinMaxLoc(resp, ref minVal, ref maxVal, ref minLoc, ref maxLoc);

                    if (maxVal > bestScore)
                    {
                        bestScore = maxVal;
                        bestScale = s;
                        bestRect = new Rectangle(maxLoc, tResized.Size);
                    }
                }

                // рисуем прямоугольник на правой части (сдвиг на ширину шаблона слева)
                if (!bestRect.IsEmpty)
                {
                    var shifted = new Rectangle(bestRect.X + _tmplMat.Width, bestRect.Y, bestRect.Width, bestRect.Height);
                    CvInvoke.Rectangle(result, shifted, new MCvScalar(0, 0, 255), 3);
                }

                _lblStats.Text = $"kpT/kpS: {nT}/{nS}  matches/good/safe: {knnMatches.Size}/{good.Size}/{safe.Size}  inliers: {inliers}  mode: template (score={bestScore:0.00}, scale={bestScale:0.00})";
            }

            _view.Image?.Dispose();
            _view.Image = result.ToBitmap();

            H?.Dispose();
            result.Dispose();
            good.Dispose();
            safe.Dispose();
        }
    }
}
