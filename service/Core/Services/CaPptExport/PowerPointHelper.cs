using Syncfusion.Presentation;

namespace Intune.Commander.Core.Services.CaPptExport;

/// <summary>
/// Helper to manipulate named shapes on a PowerPoint slide.
/// Wraps Syncfusion's ISlide to find shapes by name and set text, visibility, or links.
/// </summary>
public class PowerPointHelper
{
    private readonly ISlide _slide;
    private readonly Dictionary<string, IShape> _shapes = new();

    public PowerPointHelper(ISlide slide)
    {
        _slide = slide;
        InitializeShapes();
    }

    private void InitializeShapes()
    {
        foreach (IShape shape in _slide.Shapes)
        {
            if (!string.IsNullOrEmpty(shape.ShapeName) && !_shapes.ContainsKey(shape.ShapeName))
                _shapes[shape.ShapeName] = shape;
        }
    }

    public bool HasShape(Shape shape) => _shapes.ContainsKey(shape.ToString());

    public void SetText(Shape shape, string? text)
    {
        if (_shapes.TryGetValue(shape.ToString(), out var s))
            s.TextBody.Text = text ?? string.Empty;
    }

    public void SetTextFormatted(Shape shape, string? text, bool applyBold = true)
    {
        if (!_shapes.TryGetValue(shape.ToString(), out var s))
            return;

        var textBody = s.TextBody;
        // Remove existing paragraphs
        while (textBody.Paragraphs.Count > 0)
            textBody.Paragraphs.RemoveAt(0);

        if (string.IsNullOrEmpty(text))
            return;

        var lines = text.Split(Environment.NewLine);
        foreach (var line in lines)
        {
            var para = textBody.AddParagraph(line);
            para.Font.FontSize = 11;
            if (!line.TrimStart().StartsWith("-") && applyBold)
            {
                para.Font.Bold = true;
            }
        }
    }

    /// <summary>
    /// If isShow is false, removes the shapes from the slide (hides them).
    /// </summary>
    public void Show(bool isShow, params Shape[] shapes)
    {
        if (!isShow)
        {
            foreach (var s in shapes)
                Remove(s);
        }
    }

    public void Remove(Shape shape)
    {
        if (_shapes.TryGetValue(shape.ToString(), out var s))
        {
            _slide.Shapes.Remove(s);
            _shapes.Remove(shape.ToString());
        }
    }

    public void SetLink(Shape shape, string url)
    {
        if (_shapes.TryGetValue(shape.ToString(), out var s))
            s.SetHyperlink(url);
    }
}
