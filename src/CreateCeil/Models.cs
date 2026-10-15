using Autodesk.Revit.DB;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CeilingPlugin
{
    public class StyleMapping : INotifyPropertyChanged
    {
        public string StyleName { get; set; }

        private CeilingTypeItem _selectedCeilingType;
        public CeilingTypeItem SelectedCeilingType
        {
            get => _selectedCeilingType;
            set { _selectedCeilingType = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class CeilingTypeItem
    {
        public ElementId TypeId { get; set; }
        public string TypeName { get; set; }
        public bool IsNone => TypeName == "нет";
        public override string ToString() => TypeName;
    }
}