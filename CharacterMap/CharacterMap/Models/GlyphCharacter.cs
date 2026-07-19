namespace CharacterMap.Models;

public class GlyphCharacter : Character
{
    public ushort GlyphIndex { get; }

    public GlyphCharacter(ushort glyphIndex) : base(0)
    {
        GlyphIndex = glyphIndex;
    }
}
