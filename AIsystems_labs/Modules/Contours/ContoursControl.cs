using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Tesseract;
using System.Drawing.Imaging;


namespace AIsystems_labs.Contours
{
    public class ContoursControl : UserControl, IModule
    {
        // UI
        private readonly Panel _top = new Panel { Dock = DockStyle.Top, Height = 72, Padding = new Padding(6) };
        private readonly PictureBox _view = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.Black, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };
        private readonly TextBox _txt = new TextBox { Dock = DockStyle.Bottom, Height = 180, Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 10) };

        private readonly Button _btnLoad = new Button { Text = "Загрузить", AutoSize = true };
        private readonly Button _btnOcr = new Button { Text = "Распознать", AutoSize = true, Enabled = false };
        private readonly Button _btnSave = new Button { Text = "Сохранить текст", AutoSize = true, Enabled = false };

        private readonly Label _lLang = new Label { Text = "Язык:", AutoSize = true };
        private readonly ComboBox _cbLang = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };

        private readonly CheckBox _cbBinarize = new CheckBox { Text = "Бинаризация (Otsu)", Checked = true, AutoSize = true };
        private readonly CheckBox _cbUseRoi = new CheckBox { Text = "Распознавать только выделенную область", AutoSize = true };

        // Данные
        private Mat? _srcMat;            // исходник в Mat
        private Rectangle? _roi;         // прямоугольник выделения
        private bool _dragging;          // для рисования ROI
        private Point _dragStart;        // начальная точка мыши
        private Bitmap? _lastShown;      // чтобы поверх показывать ROI-рамку

        public string Title => "ЛР3: Расшифровка текста (OCR)";

        public ContoursControl()
        {
            // Верхняя панель
            int x = 6, y = 8, gap = 10;
            _top.Controls.Add(_btnLoad); _btnLoad.Left = x; _btnLoad.Top = y; x += _btnLoad.Width + gap;
            _top.Controls.Add(_btnOcr); _btnOcr.Left = x; _btnOcr.Top = y; x += _btnOcr.Width + gap;
            _top.Controls.Add(_btnSave); _btnSave.Left = x; _btnSave.Top = y; x += _btnSave.Width + 20;

            _top.Controls.Add(_lLang); _lLang.Left = x; _lLang.Top = y + 5; x += _lLang.Width + 4;
            _cbLang.Items.AddRange(new object[] { "rus", "eng", "rus+eng" });
            _cbLang.SelectedIndex = 2;
            _top.Controls.Add(_cbLang); _cbLang.Left = x; _cbLang.Top = y; x += _cbLang.Width + 12;

            _top.Controls.Add(_cbBinarize); _cbBinarize.Left = x; _cbBinarize.Top = y + 4; x += _cbBinarize.Width + 12;
            _top.Controls.Add(_cbUseRoi); _cbUseRoi.Left = x; _cbUseRoi.Top = y + 4;

            Controls.Add(_view);
            Controls.Add(_txt);
            Controls.Add(_top);

            // События
            _btnLoad.Click += (s, e) => LoadImage();
            _btnOcr.Click += (s, e) => RunOcr();
            _btnSave.Click += (s, e) => SaveText();

            // ROI мышью
            _view.MouseDown += View_MouseDown;
            _view.MouseMove += View_MouseMove;
            _view.MouseUp += View_MouseUp;
            _view.Paint += View_Paint;
        }

        private static Tesseract.Pix BitmapToPix(Bitmap bmp)
        {
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;
            return Tesseract.Pix.LoadFromMemory(ms.ToArray());
        }

        // IModule
        public void Start() { }
        public void Stop()
        {
            _view.Image = null;
            _lastShown?.Dispose(); _lastShown = null;
            _srcMat?.Dispose(); _srcMat = null;
            _roi = null;
            _txt.Clear();
            _btnOcr.Enabled = false;
            _btnSave.Enabled = false;
        }

        // Загрузка изображения
        private void LoadImage()
        {
            using var ofd = new OpenFileDialog { Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp" };
            if (ofd.ShowDialog() != DialogResult.OK) return;

            _srcMat?.Dispose();
            _srcMat = CvInvoke.Imread(ofd.FileName, Emgu.CV.CvEnum.ImreadModes.AnyColor);
            if (_srcMat.IsEmpty)
            {
                MessageBox.Show("Не удалось загрузить изображение.");
                return;
            }

            _lastShown?.Dispose();
            _lastShown = _srcMat.ToBitmap();

            _view.Image = (Bitmap)_lastShown.Clone();
            _txt.Clear();
            _roi = null;
            _btnOcr.Enabled = true;
            _btnSave.Enabled = false;
        }

        // Запуск OCR
        private void RunOcr()
        {
            if (_srcMat == null || _srcMat.IsEmpty)
            {
                MessageBox.Show("Сначала загрузите изображение.");
                return;
            }

            try
            {
                // 1) Вырезаем ROI, если требуется
                Mat work = _srcMat;
                Mat? toDispose = null;
                if (_cbUseRoi.Checked && _roi.HasValue)
                {
                    var r = MapRoiFromPictureBoxToImage(_roi.Value, _srcMat.Size, _view);
                    r.Intersect(new Rectangle(0, 0, _srcMat.Width, _srcMat.Height));
                    if (r.Width <= 0 || r.Height <= 0)
                    {
                        MessageBox.Show("Пустая или некорректная область выделения.");
                        return;
                    }
                    toDispose = new Mat(work, r);
                    work = toDispose;
                }

                // 2) Предобработка: серый -> (опционально) Otsu -> апскейл для качества OCR
                using var gray = new Mat();
                CvInvoke.CvtColor(work, gray, ColorConversion.Bgr2Gray);

                Mat binOrGray = gray;
                Mat? tmp = null;
                if (_cbBinarize.Checked)
                {
                    tmp = new Mat();
                    CvInvoke.Threshold(gray, tmp, 0, 255, ThresholdType.Binary | ThresholdType.Otsu);
                    binOrGray = tmp;
                }

                // Tesseract по умолчанию принимает 300 DPI. Если текст мелкий, подтянем ширину до 1200 px
                Mat ready = binOrGray;
                Mat? scaled = null;
                if (ready.Width < 1200)
                {
                    double scale = 1200.0 / ready.Width;
                    int nw = 1200;
                    int nh = Math.Max(1, (int)Math.Round(ready.Height * scale));
                    scaled = new Mat();
                    CvInvoke.Resize(ready, scaled, new Size(nw, nh), 0, 0, Inter.Cubic);
                    ready = scaled;
                }

                using var bmp = ready.ToBitmap();

                // 3) OCR через Tesseract
                string lang = _cbLang.SelectedItem?.ToString() ?? "rus+eng";
                string tessPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modules", "Contours", "tessdata");
                if (!Directory.Exists(tessPath))
                {
                    MessageBox.Show("Не найдена папка с языковыми данными Tesseract:\n" +
                                    tessPath +
                                    "\nПоложи сюда rus.traineddata/eng.traineddata и пометь Copy to Output.",
                                    "OCR", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    scaled?.Dispose(); tmp?.Dispose(); toDispose?.Dispose();
                    return;
                }

                using var engine = new TesseractEngine(tessPath, lang, EngineMode.Default);
                using var pix = BitmapToPix(bmp);
                using var page = engine.Process(pix);
                string text = page.GetText();
                float conf = page.GetMeanConfidence();

                _txt.Text = $"[CONF={conf:0.00}]\r\n{text}";
                _btnSave.Enabled = !string.IsNullOrWhiteSpace(text);

                // очистка
                scaled?.Dispose();
                tmp?.Dispose();
                toDispose?.Dispose();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка OCR: " + ex.Message, "OCR", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SaveText()
        {
            using var sfd = new SaveFileDialog
            {
                Filter = "Text|*.txt",
                FileName = $"ocr_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            };
            if (sfd.ShowDialog() != DialogResult.OK) return;
            File.WriteAllText(sfd.FileName, _txt.Text ?? string.Empty);
            MessageBox.Show("Текст сохранён.");
        }

        // ROI мышью: рисуем прямоугольник поверх картинки
        private void View_MouseDown(object? sender, MouseEventArgs e)
        {
            if (!_cbUseRoi.Checked || _view.Image == null) return;
            _dragging = true;
            _dragStart = e.Location;
            _roi = new Rectangle(e.Location, Size.Empty);
            _view.Invalidate();
        }

        private void View_MouseMove(object? sender, MouseEventArgs e)
        {
            if (!_dragging || _view.Image == null) return;
            var p1 = _dragStart;
            var p2 = e.Location;
            _roi = Rectangle.FromLTRB(Math.Min(p1.X, p2.X), Math.Min(p1.Y, p2.Y),
                                      Math.Max(p1.X, p2.X), Math.Max(p1.Y, p2.Y));
            _view.Invalidate();
        }

        private void View_MouseUp(object? sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            _view.Invalidate();
        }

        private void View_Paint(object? sender, PaintEventArgs e)
        {
            if (_roi.HasValue && _view.Image != null && _cbUseRoi.Checked)
            {
                using var pen = new Pen(Color.Lime, 2);
                e.Graphics.DrawRectangle(pen, _roi.Value);
            }
        }

        // Перевод ROI из координат PictureBox в координаты исходного изображения
        private static Rectangle MapRoiFromPictureBoxToImage(Rectangle pbRect, Size imgSize, PictureBox pb)
        {
            if (pb.Image == null) return Rectangle.Empty;

            // Рассчитываем прямоугольник, в котором реально отрисована картинка внутри PictureBox.Zoom
            float imgAspect = imgSize.Width / (float)imgSize.Height;
            float pbAspect = pb.ClientSize.Width / (float)pb.ClientSize.Height;

            Rectangle drawRect;
            if (imgAspect > pbAspect)
            {
                // картинка занимает всю ширину
                int w = pb.ClientSize.Width;
                int h = (int)(w / imgAspect);
                int y = (pb.ClientSize.Height - h) / 2;
                drawRect = new Rectangle(0, y, w, h);
            }
            else
            {
                // картинка занимает всю высоту
                int h = pb.ClientSize.Height;
                int w = (int)(h * imgAspect);
                int x = (pb.ClientSize.Width - w) / 2;
                drawRect = new Rectangle(x, 0, w, h);
            }

            // Пересечение ROI с областью отрисовки
            var inter = Rectangle.Intersect(drawRect, pbRect);
            if (inter.IsEmpty) return Rectangle.Empty;

            // Масштабируем в координаты исходного изображения
            float scaleX = imgSize.Width / (float)drawRect.Width;
            float scaleY = imgSize.Height / (float)drawRect.Height;

            int ix = (int)Math.Round((inter.X - drawRect.X) * scaleX);
            int iy = (int)Math.Round((inter.Y - drawRect.Y) * scaleY);
            int iw = (int)Math.Round(inter.Width * scaleX);
            int ih = (int)Math.Round(inter.Height * scaleY);

            // Нормализация в границах исходника
            ix = Math.Max(0, Math.Min(ix, imgSize.Width - 1));
            iy = Math.Max(0, Math.Min(iy, imgSize.Height - 1));
            if (ix + iw > imgSize.Width) iw = imgSize.Width - ix;
            if (iy + ih > imgSize.Height) ih = imgSize.Height - iy;

            return new Rectangle(ix, iy, Math.Max(1, iw), Math.Max(1, ih));
        }
    }
}
