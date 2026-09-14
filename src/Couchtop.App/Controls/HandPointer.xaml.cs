using System.Windows;
using System.Windows.Controls;

namespace Couchtop.App.Controls;

public partial class HandPointer : UserControl
{
    /// <summary>Fingertip position inside the 64x80 control.</summary>
    public static readonly Point Hotspot = new(25.6, 5.8);

    public HandPointer()
    {
        InitializeComponent();
    }
}
