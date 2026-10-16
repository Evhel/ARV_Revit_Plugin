using System.Globalization;
using System.Windows;

namespace Arv.OriginRelocator
{
    public partial class RelocationWindow : Window
    {
        public RelocationWindow(double shiftXmm, double shiftYmm, int models, int roots, int views, int annotations)
        {
            InitializeComponent();
            ARV.Common.UiSupport.Initialize(this);
            Loaded += (sender, args) => CancelButton.Focus();
            ShiftXText.Text = shiftXmm.ToString("N1", CultureInfo.CurrentCulture);
            ShiftYText.Text = shiftYmm.ToString("N1", CultureInfo.CurrentCulture);
            ModelCountText.Text = "Элементов модели: " + models;
            RootCountText.Text = "Самостоятельных объектов: " + roots;
            ViewCountText.Text = "Видов модели: " + views;
            AnnotationCountText.Text = "Элементов оформления в видах на листах: " + annotations;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e) { DialogResult = true; }
        private void CancelButton_Click(object sender, RoutedEventArgs e) { DialogResult = false; }
    }
}
