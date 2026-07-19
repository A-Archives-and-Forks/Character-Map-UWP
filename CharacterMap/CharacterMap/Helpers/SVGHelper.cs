using CharacterMap.Core;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security;
using System.Threading.Tasks;
using Windows.Storage;
using System.Globalization;
namespace CharacterMap.Helpers;

internal class SVGHelper
{
    public static async Task<FontGlyph> TryLoadFontGlyphAsync(StorageFile file, uint nextPUA)
    {
        try
        {
            string svgText = await FileIO.ReadTextAsync(file);
            List<string> pathDatas = ExtractPaths(svgText);

            if (pathDatas.Count == 0)
                return null;

            CanvasDevice device = CanvasDevice.GetSharedDevice();
            var simplified = new CanvasSvgPathBuilder(device, pathDatas).GetGeometry(false);

            SVGPathReciever pathReceiver = new();
            simplified.SendPathTo(pathReceiver);
            string initialPath = pathReceiver.GetPathData().Replace("F 0 ", "").Replace("F 1 ", "");

            Windows.UI.Xaml.Media.Geometry xamlGeom = Windows.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Windows.UI.Xaml.Media.Geometry), initialPath) as Windows.UI.Xaml.Media.Geometry;
            var exactBounds = xamlGeom.Bounds;

            var previewGeometry = simplified;
            if (exactBounds.Width > 0 && exactBounds.Height > 0)
                previewGeometry = simplified.Transform(Matrix3x2.CreateTranslation((float)-exactBounds.X, (float)-exactBounds.Y));

            var bounds = exactBounds;

            SVGPathReciever receiver = new();
            previewGeometry.SendPathTo(receiver);
            string newPathData = receiver.GetPathData();

            string fillRule = newPathData.Contains("F 0") ? "evenodd" : "nonzero";
            newPathData = newPathData.Replace("F 0 ", "").Replace("F 1 ", "");

            string viewBox = bounds.Width > 0 && bounds.Height > 0
                ? $"0 0 {bounds.Width.ToString(CultureInfo.InvariantCulture)} {bounds.Height.ToString(CultureInfo.InvariantCulture)}"
                : "0 0 1024 1024";

            string newSvg = $"<svg viewBox=\"{viewBox}\" xmlns=\"http://www.w3.org/2000/svg\"><path d=\"{newPathData}\" fill=\"black\" fill-rule=\"{fillRule}\" /></svg>";

            StorageFile tempFile = await StorageHelper.CreateTempFileAsync($"SVGP\\{Guid.NewGuid()}.svg").AsTask().ConfigureAwait(false);
            await FileIO.WriteTextAsync(tempFile, newSvg).AsTask().ConfigureAwait(false);

            float viewBoxX = 0f;
            float viewBoxY = 0f;
            float viewBoxWidth = 0f;
            float viewBoxHeight = 0f;

            int svgIndex = svgText.IndexOf("<svg", StringComparison.OrdinalIgnoreCase);
            if (svgIndex != -1)
            {
                int svgEnd = svgText.IndexOf('>', svgIndex);
                if (svgEnd != -1)
                {
                    string svgTag = svgText.Substring(svgIndex, svgEnd - svgIndex);
                    
                    int viewBoxIdx = FindAttributeIndex(svgTag, "viewBox");
                    if (viewBoxIdx != -1)
                    {
                        string viewBoxAttr = ExtractAttributeValue(svgTag, viewBoxIdx);
                        if (!string.IsNullOrWhiteSpace(viewBoxAttr))
                        {
                            var parts = viewBoxAttr.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length == 4)
                            {
                                float.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out viewBoxX);
                                float.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out viewBoxY);
                                float.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out viewBoxWidth);
                                float.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out viewBoxHeight);
                            }
                        }
                    }
                    else
                    {
                        int widthIdx = FindAttributeIndex(svgTag, "width");
                        int heightIdx = FindAttributeIndex(svgTag, "height");
                        if (widthIdx != -1)
                        {
                            string widthAttr = ExtractAttributeValue(svgTag, widthIdx);
                            if (widthAttr != null)
                                float.TryParse(widthAttr.Replace("px", "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out viewBoxWidth);
                        }
                        if (heightIdx != -1)
                        {
                            string heightAttr = ExtractAttributeValue(svgTag, heightIdx);
                            if (heightAttr != null)
                                float.TryParse(heightAttr.Replace("px", "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out viewBoxHeight);
                        }
                    }
                }
            }

            if (viewBoxWidth <= 0) viewBoxWidth = (float)exactBounds.Width;
            if (viewBoxHeight <= 0) viewBoxHeight = (float)exactBounds.Height;

            var fontGeometry = simplified;
            if (exactBounds.Width > 0 && exactBounds.Height > 0)
                fontGeometry = simplified.Transform(Matrix3x2.CreateTranslation(-viewBoxX, -(viewBoxY + viewBoxHeight)));

            float glyphScale = 1024f / viewBoxHeight;
            float advanceWidth = viewBoxWidth; // Keep in original coordinate space!

            Character svgChar = new(nextPUA);
            FontGlyph glyph = new(null, svgChar, Scale: glyphScale, CustomGeometry: fontGeometry, CustomImagePath: tempFile.GetAppPath(), CustomAdvanceWidth: advanceWidth);
            return glyph;
        }
        catch (Exception ex)
        {
            Utils.AppendDiagnostics("SVGHelper TryLoadFontGlyph", ex);
        }

        return null;
    }




    #region Path Extraction

    private static List<string> ExtractPaths(string svgText)
    {
        List<string> pathDatas = new();
        int pos = 0;
        bool inDefs = false;
        bool inClipPath = false;

        while (pos < svgText.Length)
        {
            while (pos < svgText.Length && char.IsWhiteSpace(svgText[pos]))
                pos++;

            if (pos >= svgText.Length)
                break;

            if (svgText[pos] == '<')
            {
                if (pos + 1 < svgText.Length && svgText[pos + 1] == '/')
                {
                    if (StartsWithIgnoreCase(svgText, pos + 2, "defs"))
                    {
                        inDefs = false;
                        pos = svgText.IndexOf('>', pos) + 1;
                        continue;
                    }
                    if (StartsWithIgnoreCase(svgText, pos + 2, "clipPath"))
                    {
                        inClipPath = false;
                        pos = svgText.IndexOf('>', pos) + 1;
                        continue;
                    }
                }

                if (StartsWithIgnoreCase(svgText, pos + 1, "defs"))
                {
                    int tagEnd = svgText.IndexOf('>', pos);
                    if (tagEnd != -1)
                    {
                        if (tagEnd > 0 && svgText[tagEnd - 1] != '/')
                            inDefs = true;
                        pos = tagEnd + 1;
                        continue;
                    }
                }
                if (StartsWithIgnoreCase(svgText, pos + 1, "clipPath"))
                {
                    int tagEnd = svgText.IndexOf('>', pos);
                    if (tagEnd != -1)
                    {
                        if (tagEnd > 0 && svgText[tagEnd - 1] != '/')
                            inClipPath = true;
                        pos = tagEnd + 1;
                        continue;
                    }
                }

                if (StartsWithIgnoreCase(svgText, pos + 1, "path"))
                {
                    int nextCharIndex = pos + 5;
                    if (nextCharIndex < svgText.Length)
                    {
                        char nextChar = svgText[nextCharIndex];
                        if (char.IsWhiteSpace(nextChar) || nextChar == '/' || nextChar == '>')
                        {
                            int tagEnd = svgText.IndexOf('>', pos);
                            if (tagEnd != -1)
                            {
                                if (!inDefs && !inClipPath)
                                {
                                    string pathTag = svgText.Substring(pos, tagEnd - pos);
                                    if (!IsInvisible(pathTag))
                                    {
                                        int dIndex = FindAttributeIndex(pathTag, "d");
                                        if (dIndex != -1)
                                        {
                                            string dValue = ExtractAttributeValue(pathTag, dIndex);
                                            if (!string.IsNullOrWhiteSpace(dValue))
                                                pathDatas.Add(dValue);
                                        }
                                    }
                                }
                                pos = tagEnd + 1;
                                continue;
                            }
                        }
                    }
                }
            }

            pos++;
        }

        return pathDatas;
    }

    private static bool StartsWithIgnoreCase(string s, int start, string sub)
    {
        if (start + sub.Length > s.Length)
            return false;

        for (int i = 0; i < sub.Length; i++)
        {
            if (char.ToLowerInvariant(s[start + i]) != char.ToLowerInvariant(sub[i]))
                return false;
        }

        int next = start + sub.Length;
        if (next < s.Length)
        {
            char c = s[next];
            return char.IsWhiteSpace(c) || c == '/' || c == '>';
        }

        return true;
    }

    private static bool IsInvisible(string pathTag)
    {
        int fillIdx = FindAttributeIndex(pathTag, "fill");
        if (fillIdx != -1)
        {
            string fillVal = ExtractAttributeValue(pathTag, fillIdx);
            if (fillVal != null && fillVal.Equals("none", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        int displayIdx = FindAttributeIndex(pathTag, "display");
        if (displayIdx != -1)
        {
            string displayVal = ExtractAttributeValue(pathTag, displayIdx);
            if (displayVal != null && displayVal.Equals("none", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        int styleIdx = FindAttributeIndex(pathTag, "style");
        if (styleIdx != -1)
        {
            string styleVal = ExtractAttributeValue(pathTag, styleIdx);
            if (styleVal != null)
            {
                string cleanStyle = styleVal.Replace(" ", "").Replace("\t", "").Replace("\r", "").Replace("\n", "").ToLowerInvariant();
                if (cleanStyle.Contains("fill:none") || cleanStyle.Contains("display:none"))
                    return true;
            }
        }

        return false;
    }

    private static int FindAttributeIndex(string tag, string attrName)
    {
        int index = 0;
        while (true)
        {
            index = tag.IndexOf(attrName, index, StringComparison.Ordinal);
            if (index == -1)
                return -1;

            if (index > 0 && char.IsWhiteSpace(tag[index - 1]))
            {
                int equalsIndex = index + attrName.Length;
                while (equalsIndex < tag.Length && char.IsWhiteSpace(tag[equalsIndex]))
                    equalsIndex++;

                if (equalsIndex < tag.Length && tag[equalsIndex] == '=')
                    return index;
            }

            index += attrName.Length;
        }
    }

    private static string ExtractAttributeValue(string tag, int attrIndex)
    {
        int pos = tag.IndexOf('=', attrIndex);
        if (pos == -1)
            return null;

        pos++;
        while (pos < tag.Length && char.IsWhiteSpace(tag[pos]))
            pos++;

        if (pos >= tag.Length)
            return null;

        char quote = tag[pos];
        if (quote != '"' && quote != '\'')
        {
            int start = pos;
            while (pos < tag.Length && !char.IsWhiteSpace(tag[pos]) && tag[pos] != '/' && tag[pos] != '>')
                pos++;
            return tag.Substring(start, pos - start);
        }

        pos++;
        int valStart = pos;
        int valEnd = tag.IndexOf(quote, pos);
        if (valEnd == -1)
            return null;

        return tag.Substring(valStart, valEnd - valStart);
    }

    #endregion
}

public class CanvasSvgPathBuilder
{
    private CanvasDevice _device;
    private IEnumerable<string> _pathDatas;

    public CanvasSvgPathBuilder(CanvasDevice device, IEnumerable<string> pathDatas)
    {
        _device = device;
        _pathDatas = pathDatas;
    }

    public CanvasPathBuilder Build()
    {
        var builder = new CanvasPathBuilder(_device);

        foreach (var d in _pathDatas)
        {
            try
            {
                SvgPathParser.Parse(d, builder);
            }
            catch { }
        }

        return builder;
    }

    public CanvasGeometry GetGeometry(bool simplify = false)
    {
        CanvasPathBuilder builder = Build();
        try
        {
            var geom = CanvasGeometry.CreatePath(builder);
            if (simplify)
                geom = geom.Simplify(CanvasGeometrySimplification.Lines);

            return geom;
        }
        finally
        {
            builder?.Dispose();
        }
    }
}