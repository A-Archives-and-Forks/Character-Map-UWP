using Microsoft.Graphics.Canvas.Text;
using Windows.UI.Xaml.Controls;

namespace CharacterMap.Provider
{
    public class XamlDevProvider : DevProviderBase
    {
        public XamlDevProvider(CharacterRenderingOptions o, Character c) : base(o, c)
        {
            DisplayName = "XAML (UWP)";
        }

        protected override DevProviderType GetDevProviderType() => DevProviderType.XAML;
        protected override IReadOnlyList<DevOption> OnGetContextOptions() => Inflate();
        protected override IReadOnlyList<DevOption> OnGetOptions() => Inflate();

        IReadOnlyList<DevOption> Inflate()
        {
            var v = Options.Variant;
            var c = Character;

            bool hasSymbol = FontFinder.IsSystemSymbolFamily(v) && Enum.IsDefined(typeof(Symbol), (int)c.UnicodeIndex);
            var hex = c.UnicodeIndex.ToString("x4").ToUpper();

            string pathIconData = GetOutlineGeometry(c, Options);

            string typographyAttributes = GetTypographyAttributes(Options);

            var ops = new List<DevOption>()
            {
                new ("TxtXamlCode/Header", $"&#x{hex};"),
                new ("TxtFontIcon/Header", $@"<FontIcon FontFamily=""{GetFontSource(v?.XamlFontSource)}"" Glyph=""&#x{hex};""{typographyAttributes} />", supportsTypography: true),
            };

            if (!string.IsNullOrWhiteSpace(pathIconData))
                ops.Add(new DevOption("TxtPathIcon/Text", $"<PathIcon Data=\"{pathIconData}\" VerticalAlignment=\"Center\" HorizontalAlignment=\"Center\" />", supportsTypography: true));

            if (hasSymbol)
                ops.Add(new DevOption("TxtSymbolIcon/Header", $@"<SymbolIcon Symbol=""{(Symbol)c.UnicodeIndex}"" />"));

            return ops;
        }

        private string GetFontSource(string fontSource)
        {
            if (fontSource == "Segoe MDL2 Assets")
                return "{ThemeResource SymbolThemeFontFamily}";
            return fontSource;
        }

        private static string GetTypographyAttributes(CharacterRenderingOptions options)
        {
            var typo = options.DefaultTypography;
            if (typo is null)
                return string.Empty;

            var f = typo.Feature;

            return f switch
            {
                // Stylistic Sets
                CanvasTypographyFeatureName.StylisticSet1 => " Typography.StylisticSet1=\"True\"",
                CanvasTypographyFeatureName.StylisticSet2 => " Typography.StylisticSet2=\"True\"",
                CanvasTypographyFeatureName.StylisticSet3 => " Typography.StylisticSet3=\"True\"",
                CanvasTypographyFeatureName.StylisticSet4 => " Typography.StylisticSet4=\"True\"",
                CanvasTypographyFeatureName.StylisticSet5 => " Typography.StylisticSet5=\"True\"",
                CanvasTypographyFeatureName.StylisticSet6 => " Typography.StylisticSet6=\"True\"",
                CanvasTypographyFeatureName.StylisticSet7 => " Typography.StylisticSet7=\"True\"",
                CanvasTypographyFeatureName.StylisticSet8 => " Typography.StylisticSet8=\"True\"",
                CanvasTypographyFeatureName.StylisticSet9 => " Typography.StylisticSet9=\"True\"",
                CanvasTypographyFeatureName.StylisticSet10 => " Typography.StylisticSet10=\"True\"",
                CanvasTypographyFeatureName.StylisticSet11 => " Typography.StylisticSet11=\"True\"",
                CanvasTypographyFeatureName.StylisticSet12 => " Typography.StylisticSet12=\"True\"",
                CanvasTypographyFeatureName.StylisticSet13 => " Typography.StylisticSet13=\"True\"",
                CanvasTypographyFeatureName.StylisticSet14 => " Typography.StylisticSet14=\"True\"",
                CanvasTypographyFeatureName.StylisticSet15 => " Typography.StylisticSet15=\"True\"",
                CanvasTypographyFeatureName.StylisticSet16 => " Typography.StylisticSet16=\"True\"",
                CanvasTypographyFeatureName.StylisticSet17 => " Typography.StylisticSet17=\"True\"",
                CanvasTypographyFeatureName.StylisticSet18 => " Typography.StylisticSet18=\"True\"",
                CanvasTypographyFeatureName.StylisticSet19 => " Typography.StylisticSet19=\"True\"",
                CanvasTypographyFeatureName.StylisticSet20 => " Typography.StylisticSet20=\"True\"",

                // Capitals
                CanvasTypographyFeatureName.SmallCapitalsFromCapitals => " Typography.Capitals=\"AllSmallCaps\"",
                CanvasTypographyFeatureName.SmallCapitals => " Typography.Capitals=\"SmallCaps\"",
                CanvasTypographyFeatureName.PetiteCapitalsFromCapitals => " Typography.Capitals=\"AllPetiteCaps\"",
                CanvasTypographyFeatureName.PetiteCapitals => " Typography.Capitals=\"PetiteCaps\"",
                CanvasTypographyFeatureName.Unicase => " Typography.Capitals=\"Unicase\"",
                CanvasTypographyFeatureName.Titling => " Typography.Capitals=\"Titling\"",

                // Numeral Style
                CanvasTypographyFeatureName.LiningFigures => " Typography.NumeralStyle=\"Lining\"",
                CanvasTypographyFeatureName.OldStyleFigures => " Typography.NumeralStyle=\"OldStyle\"",

                // Numeral Alignment
                CanvasTypographyFeatureName.ProportionalFigures => " Typography.NumeralAlignment=\"Proportional\"",
                CanvasTypographyFeatureName.TabularFigures => " Typography.NumeralAlignment=\"Tabular\"",

                // Variants
                CanvasTypographyFeatureName.Superscript => " Typography.Variants=\"Superscript\"",
                CanvasTypographyFeatureName.Subscript => " Typography.Variants=\"Subscript\"",
                CanvasTypographyFeatureName.Ordinals => " Typography.Variants=\"Ordinal\"",
                CanvasTypographyFeatureName.ScientificInferiors => " Typography.Variants=\"Inferior\"",
                CanvasTypographyFeatureName.RubyNotationForms => " Typography.Variants=\"Ruby\"",

                // Fraction
                CanvasTypographyFeatureName.Fractions => " Typography.Fraction=\"Slashed\"",

                // Swashes
                CanvasTypographyFeatureName.Swash => " Typography.StandardSwashes=\"1\"",
                CanvasTypographyFeatureName.ContextualSwash => " Typography.ContextualSwashes=\"1\"",

                // Alternates
                CanvasTypographyFeatureName.StylisticAlternates => " Typography.StylisticAlternates=\"1\"",
                CanvasTypographyFeatureName.ContextualAlternates => " Typography.ContextualAlternates=\"True\"",

                // Forms
                CanvasTypographyFeatureName.AlternateAnnotationForms => " Typography.AnnotationAlternates=\"1\"",
                CanvasTypographyFeatureName.CaseSensitiveForms => " Typography.CaseSensitiveForms=\"True\"",
                CanvasTypographyFeatureName.HistoricalForms => " Typography.HistoricalForms=\"True\"",
                CanvasTypographyFeatureName.ExpertForms => " Typography.EastAsianExpertForms=\"True\"",

                // East Asian Forms
                CanvasTypographyFeatureName.HojoKanjiForms => " Typography.EastAsianLanguage=\"HojoKanji\"",
                CanvasTypographyFeatureName.Jis04Forms => " Typography.EastAsianLanguage=\"Jis04\"",
                CanvasTypographyFeatureName.Jis78Forms => " Typography.EastAsianLanguage=\"Jis78\"",
                CanvasTypographyFeatureName.Jis83Forms => " Typography.EastAsianLanguage=\"Jis83\"",
                CanvasTypographyFeatureName.Jis90Forms => " Typography.EastAsianLanguage=\"Jis90\"",
                CanvasTypographyFeatureName.NlcKanjiForms => " Typography.EastAsianLanguage=\"NlcKanji\"",
                CanvasTypographyFeatureName.SimplifiedForms => " Typography.EastAsianLanguage=\"Simplified\"",
                CanvasTypographyFeatureName.TraditionalForms => " Typography.EastAsianLanguage=\"Traditional\"",
                CanvasTypographyFeatureName.TraditionalNameForms => " Typography.EastAsianLanguage=\"TraditionalNames\"",

                // East Asian Widths
                CanvasTypographyFeatureName.FullWidth => " Typography.EastAsianWidths=\"Full\"",
                CanvasTypographyFeatureName.HalfWidth => " Typography.EastAsianWidths=\"Half\"",
                CanvasTypographyFeatureName.ProportionalWidths => " Typography.EastAsianWidths=\"Proportional\"",
                CanvasTypographyFeatureName.QuarterWidths => " Typography.EastAsianWidths=\"Quarter\"",
                CanvasTypographyFeatureName.ThirdWidths => " Typography.EastAsianWidths=\"Third\"",

                // Ligatures
                CanvasTypographyFeatureName.StandardLigatures => " Typography.StandardLigatures=\"True\"",
                CanvasTypographyFeatureName.ContextualLigatures => " Typography.ContextualLigatures=\"True\"",
                CanvasTypographyFeatureName.DiscretionaryLigatures => " Typography.DiscretionaryLigatures=\"True\"",
                CanvasTypographyFeatureName.HistoricalLigatures => " Typography.HistoricalLigatures=\"True\"",

                // Other
                CanvasTypographyFeatureName.CapitalSpacing => " Typography.CapitalSpacing=\"True\"",
                CanvasTypographyFeatureName.Kerning => " Typography.Kerning=\"True\"",
                CanvasTypographyFeatureName.MathematicalGreek => " Typography.MathematicalGreek=\"True\"",
                CanvasTypographyFeatureName.SlashedZero => " Typography.SlashedZero=\"True\"",

                _ => string.Empty
            };
        }
    }
}
