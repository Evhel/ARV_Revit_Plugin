using System;
using System.Linq;
using System.Windows;
using ARV.Common;

namespace ViewNumberingPlugin
{
    public partial class NumberingDialog : Window
    {
        public int StartIndex { get; private set; }
        public NumberingType SelectedType { get; private set; }

        public NumberingDialog()
        {
            InitializeComponent();
            UiSupport.Initialize(this);
            _cmbType.SelectedIndex = 0;
            _cmbType.SelectionChanged += (sender, args) => UpdateInfo();
            UpdateInfo();
            Loaded += (sender, args) => { _txtStart.Focus(); _txtStart.SelectAll(); };
        }

        private void OnApply(object sender, RoutedEventArgs e)
        {
            if (ValidateInput()) DialogResult = true;
        }

        private void OnCancel(object sender, RoutedEventArgs e) { DialogResult = false; }

        private void UpdateInfo()
        {
            SelectedType = (NumberingType)_cmbType.SelectedIndex;
            _txtStart.Text = SelectedType == NumberingType.Digits ? "1" : SelectedType == NumberingType.Upper ? "А" : "а";
        }

        private bool ValidateInput()
        {
            SelectedType = (NumberingType)_cmbType.SelectedIndex;
            var text = _txtStart.Text.Trim();

            if (SelectedType == NumberingType.Digits)
            {
                if (int.TryParse(text, out int num) && num > 0) { StartIndex = num - 1; return true; }
                UiSupport.Error(this, _txtStart, "Введите целое положительное число.");
                return false;
            }

            if (SelectedType == NumberingType.Upper)
            {
                if (text.Length == 1 && Command.UpperCyrillic.Contains(char.ToUpper(text[0])))
                { StartIndex = Array.IndexOf(Command.UpperCyrillic, char.ToUpper(text[0])); return true; }
                UiSupport.Error(this, _txtStart, "Введите одну заглавную букву (А-Я).");
                return false;
            }

            if (SelectedType == NumberingType.Lower)
            {
                if (text.Length == 1 && Command.LowerCyrillic.Contains(char.ToLower(text[0])))
                { StartIndex = Array.IndexOf(Command.LowerCyrillic, char.ToLower(text[0])); return true; }
                UiSupport.Error(this, _txtStart, "Введите одну строчную букву (а-я).");
                return false;
            }
            return false;
        }
    }
}
