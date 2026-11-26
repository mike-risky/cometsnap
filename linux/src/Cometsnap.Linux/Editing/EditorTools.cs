using Avalonia.Input;

namespace Cometsnap.Linux.Editing;

public enum EditorTool { Select, Arrow, Line, Rectangle, Ellipse, Text, Callout, Highlighter, Pen, Step, Blur, Spotlight, Crop }

public sealed record ToolInfo(EditorTool Tool, string Label, Key Key, string Tip, string? Icon, string? Glyph = null);

/// <summary>The 13 editor tools, their single-key shortcuts and icons (same as the Windows app).</summary>
public static class EditorTools
{
    public static readonly ToolInfo[] All =
    {
        new(EditorTool.Select, "Select", Key.V, "Select (V): click a shape to move, recolor or delete it",
            "M3.5,2 V13 L6.3,10.4 L8.6,14.6 L10.6,13.6 L8.3,9.4 H12 Z"),
        new(EditorTool.Arrow, "Arrow", Key.A, "Arrow (A) · Shift = 45° steps", "M3,13 L13,3 M7.5,3 L13,3 L13,8.5"),
        new(EditorTool.Line, "Line", Key.L, "Line (L) · Shift = 45° steps", "M3,13 L13,3"),
        new(EditorTool.Rectangle, "Box", Key.R, "Rectangle (R) · Shift = square", "M2.5,3.5 H13.5 V12.5 H2.5 Z"),
        new(EditorTool.Ellipse, "Circle", Key.E, "Circle (E) · Shift = perfect circle", "M8,2.5 A5.5,5.5 0 1 1 7.99,2.5 Z"),
        new(EditorTool.Text, "Text", Key.T, "Text (T) · Enter to finish, Shift+Enter new line", null, "T"),
        new(EditorTool.Callout, "Bubble", Key.K, "Speech bubble (K): drag from what you point at to where the bubble goes",
            "M2.5,3 H13.5 V10 H7 L4,13.5 V10 H2.5 Z"),
        new(EditorTool.Highlighter, "Marker", Key.H, "Highlighter (H)", "M9.5,2.5 L13.5,6.5 L7.5,12.5 H3.5 V8.5 Z M2,14.5 H14"),
        new(EditorTool.Pen, "Pen", Key.P, "Freehand pen (P)", "M10.5,2.5 L13.5,5.5 L5.5,13.5 H2.5 V10.5 Z"),
        new(EditorTool.Step, "Steps", Key.N, "Numbered steps (N): each click adds the next number", null, "1"),
        new(EditorTool.Blur, "Blur", Key.B, "Blur / pixelate (B): hide passwords, e-mails, faces",
            "M2.5,2.5 H6 V6 H2.5 Z M10,2.5 H13.5 V6 H10 Z M6.25,6.25 H9.75 V9.75 H6.25 Z M2.5,10 H6 V13.5 H2.5 Z M10,10 H13.5 V13.5 H10 Z"),
        new(EditorTool.Spotlight, "Spotlight", Key.S, "Spotlight (S): greys out everything except the area you drag",
            "M8,1.5 V3 M2,7.5 H3.5 M12.5,7.5 H14 M3.8,3.3 L4.8,4.3 M12.2,3.3 L11.2,4.3 M6,11.5 A3.6,3.6 0 1 1 10,11.5 V13.5 H6 Z"),
        new(EditorTool.Crop, "Crop", Key.C, "Crop (C): keep only the area you drag", "M4.5,1.5 V11.5 H14.5 M1.5,4.5 H11.5 V14.5"),
    };

    public static readonly string[] Palette = { "#E5484D", "#F09625", "#FFD60A", "#2ED573", "#3D6BFF", "#A55EEA", "#FFFFFF", "#111111" };
    public static readonly string[] PaletteNames = { "Red", "Orange", "Yellow", "Green", "Blue", "Purple", "White", "Black" };
}
