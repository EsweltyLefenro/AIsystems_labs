using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace AIsystems_labs
{
    public class MainForm : Form
    {
        private readonly TabControl _tabs = new TabControl { Dock = DockStyle.Fill };
        private IModule? _current;

        public MainForm()
        {
            Text = "AIsystems_labs – Лабораторные работы по \"Системам искуственного интеллекта\"";
            MinimumSize = new Size(1000, 600);
            Controls.Add(_tabs);

            _tabs.TabPages.Add(MakeTab(new FacesEyes.FaceEyeControl()));
            _tabs.TabPages.Add(MakeTab(new Features.FeaturesControl()));
            _tabs.TabPages.Add(MakeTab(new Contours.ContoursControl()));

            _tabs.SelectedIndexChanged += (s, e) => SwitchModule(); // при смене вкладки
            FormClosing += (s, e) => _current?.Stop(); // при закрытии окна
            SwitchModule();
        }

        private TabPage MakeTab(Control c)
        {
            var tab = new TabPage("…") { Padding = new Padding(6) };
            c.Dock = DockStyle.Fill;
            tab.Controls.Add(c);
            if (c is IModule m) tab.Text = m.Title;
            return tab;
        }

        private void SwitchModule()
        {
            _current?.Stop();
            var page = _tabs.SelectedTab;
            _current = page?.Controls.OfType<IModule>().FirstOrDefault();
        }
    }
}
