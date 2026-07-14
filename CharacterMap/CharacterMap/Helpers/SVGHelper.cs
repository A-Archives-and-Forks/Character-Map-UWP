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
            Windows.Data.Xml.Dom.XmlDocument doc = new();
            doc.LoadXml(svgText);
            var paths = doc.SelectNodes("//*[local-name()='path']");
            List<string> pathDatas = new();

            bool IsInvisibleOrDef(Windows.Data.Xml.Dom.IXmlNode node)
            {
                if (node == null) return false;
                if (node.LocalName == "clipPath" || node.LocalName == "defs") return true;
                if (node.Attributes != null)
                {
                    var fillAttr = node.Attributes.GetNamedItem("fill");
                    if (fillAttr != null && fillAttr.NodeValue?.ToString() == "none") return true;

                    var styleAttr = node.Attributes.GetNamedItem("style");
                    if (styleAttr != null && styleAttr.NodeValue?.ToString().Replace(" ", "").Contains("fill:none") == true) return true;
                }
                return IsInvisibleOrDef(node.ParentNode);
            }

            foreach (var path in paths)
            {
                if (IsInvisibleOrDef(path))
                    continue;

                if (path.Attributes.GetNamedItem("d") is { } dAttr && !string.IsNullOrWhiteSpace(dAttr.NodeValue?.ToString()))
                    pathDatas.Add(dAttr.NodeValue.ToString());
            }

            if (pathDatas.Count == 0)
                return null;

            CanvasDevice device = CanvasDevice.GetSharedDevice();
            var simplified = new CanvasSvgPathBuilder(device, pathDatas).GetGeometry(false);

            SVGPathReciever pathReceiver = new();
            simplified.SendPathTo(pathReceiver);
            string initialPath = pathReceiver.GetPathData();

            Windows.UI.Xaml.Media.Geometry xamlGeom = Windows.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Windows.UI.Xaml.Media.Geometry), initialPath) as Windows.UI.Xaml.Media.Geometry;
            var exactBounds = xamlGeom.Bounds;

            var previewGeometry = simplified;
            if (exactBounds.Width > 0 && exactBounds.Height > 0)
            {
                previewGeometry = simplified.Transform(Matrix3x2.CreateTranslation((float)-exactBounds.X, (float)-exactBounds.Y));
            }

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

            var svgElement = doc.DocumentElement;
            if (svgElement != null && svgElement.Attributes != null)
            {
                var viewBoxAttr = svgElement.Attributes.GetNamedItem("viewBox");
                if (viewBoxAttr != null && !string.IsNullOrWhiteSpace(viewBoxAttr.NodeValue?.ToString()))
                {
                    var parts = viewBoxAttr.NodeValue.ToString().Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 4)
                    {
                        float.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out viewBoxX);
                        float.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out viewBoxY);
                        float.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out viewBoxWidth);
                        float.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out viewBoxHeight);
                    }
                }
                else
                {
                    var widthAttr = svgElement.Attributes.GetNamedItem("width");
                    var heightAttr = svgElement.Attributes.GetNamedItem("height");
                    if (widthAttr != null)
                        float.TryParse(widthAttr.NodeValue?.ToString().Replace("px", "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out viewBoxWidth);
                    if (heightAttr != null)
                        float.TryParse(heightAttr.NodeValue?.ToString().Replace("px", "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out viewBoxHeight);
                }
            }

            if (viewBoxWidth <= 0) viewBoxWidth = (float)exactBounds.Width;
            if (viewBoxHeight <= 0) viewBoxHeight = (float)exactBounds.Height;

            var fontGeometry = simplified;
            if (exactBounds.Width > 0 && exactBounds.Height > 0)
            {
                // Map the SVG's viewBox directly to the TTF coordinate space
                // Translating Y by -(viewBoxY + viewBoxHeight) ensures the bottom of the viewBox sits at Y=0 (the TTF baseline)
                fontGeometry = simplified.Transform(Matrix3x2.CreateTranslation(-viewBoxX, -(viewBoxY + viewBoxHeight)));
            }

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