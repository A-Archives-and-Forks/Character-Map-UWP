using Windows.UI.Xaml;

namespace CharacterMap.Models;

[DependencyProperty<TypographyFeatureInfo>("Typography")]
[DependencyProperty<ExportStyle>("Style", ExportStyle.Black)]
public partial class ExportParameters : DependencyObject
{
   public Character Character { get; set; }

    public ExportParameters With(Character c)
    {
        return new ExportParameters()
        {
            Character = c,
            Typography = this.Typography,
            Style = this.Style
        };
    }
}
