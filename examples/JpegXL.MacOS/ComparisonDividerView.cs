using AppKit;
using CoreGraphics;
using Foundation;

namespace JpegXL.MacOS;

/// <summary>
/// A transparent overlay view that draws a vertical divider line with a draggable handle.
/// Used in HDR vs SDR comparison mode to split the view between HDR (left) and SDR (right).
/// </summary>
public class ComparisonDividerView : NSView
{
    private const float HandleWidth = 36f;
    private const float HandleHeight = 24f;
    private const float LineWidth = 2f;
    private const float MinPosition = 0.05f;
    private const float MaxPosition = 0.95f;
    private const float HitZone = 20f;

    private bool _isDragging;
    private nfloat _dragStartMouseX;
    private nfloat _dragStartPosition;
    private NSTrackingArea? _trackingArea;

    /// <summary>
    /// The divider position as a fraction of the parent width (0.0 to 1.0).
    /// </summary>
    public nfloat DividerPosition { get; set; } = 0.5f;

    /// <summary>
    /// Called when divider position changes during drag. Parameter is the new fraction (0.0 - 1.0).
    /// </summary>
    public Action<nfloat>? OnDividerMoved { get; set; }

    public ComparisonDividerView(CGRect frame) : base(frame)
    {
    }

    public override bool AcceptsFirstMouse(NSEvent? theEvent) => true;

    public override void ViewDidMoveToWindow()
    {
        base.ViewDidMoveToWindow();
        if (Window != null)
        {
            UpdateTrackingAreas();
        }
    }

    public override void DrawRect(CGRect dirtyRect)
    {
        var lineX = Bounds.Width * DividerPosition;

        // Draw a single divider line
        var linePath = new NSBezierPath();
        linePath.MoveTo(new CGPoint(lineX, 0));
        linePath.LineTo(new CGPoint(lineX, Bounds.Height));
        NSColor.LightGray.SetStroke();
        linePath.LineWidth = LineWidth;
        linePath.Stroke();

        // Draw the handle at vertical center
        var handleX = lineX - HandleWidth / 2;
        var handleY = Bounds.Height / 2 - HandleHeight / 2;
        var handleRect = new CGRect(handleX, handleY, HandleWidth, HandleHeight);

        // Handle background
        var handlePath = NSBezierPath.FromRoundedRect(handleRect, HandleHeight / 2, HandleHeight / 2);
        NSColor.Black.ColorWithAlphaComponent(0.6f).SetFill();
        handlePath.Fill();
        NSColor.White.ColorWithAlphaComponent(0.8f).SetStroke();
        handlePath.LineWidth = 1;
        handlePath.Stroke();

        // Draw arrow text
        var attrs = new NSStringAttributes
        {
            ForegroundColor = NSColor.White,
            Font = NSFont.SystemFontOfSize(14)!
        };
        var text = new NSAttributedString("\u2194", attrs); // ↔
        var textSize = text.Size;
        var textX = lineX - textSize.Width / 2;
        var textY = Bounds.Height / 2 - textSize.Height / 2;
        text.DrawAtPoint(new CGPoint(textX, textY));
    }

    public override void UpdateTrackingAreas()
    {
        base.UpdateTrackingAreas();

        // Remove previous tracking area
        if (_trackingArea != null)
        {
            RemoveTrackingArea(_trackingArea);
            _trackingArea.Dispose();
        }

        // Create tracking area covering the narrow band around the divider
        var lineX = Bounds.Width * DividerPosition;
        var trackRect = new CGRect(
            Math.Max(0, (double)(lineX - HitZone)), 0,
            Math.Min((double)(HitZone * 2), (double)Bounds.Width), (double)Bounds.Height);

        _trackingArea = new NSTrackingArea(
            trackRect,
            NSTrackingAreaOptions.MouseEnteredAndExited | NSTrackingAreaOptions.ActiveAlways | NSTrackingAreaOptions.CursorUpdate,
            this,
            null);
        AddTrackingArea(_trackingArea);
    }

    public override void CursorUpdate(NSEvent theEvent)
    {
        NSCursor.ResizeLeftRightCursor.Set();
    }

    public override void MouseEntered(NSEvent theEvent)
    {
        NSCursor.ResizeLeftRightCursor.Set();
    }

    public override void MouseExited(NSEvent theEvent)
    {
        NSCursor.ArrowCursor.Set();
    }

    public override void MouseDown(NSEvent theEvent)
    {
        _isDragging = true;
        _dragStartMouseX = ConvertPointFromView(theEvent.LocationInWindow, null).X;
        _dragStartPosition = DividerPosition;
        NSCursor.ResizeLeftRightCursor.Set();
    }

    public override void MouseDragged(NSEvent theEvent)
    {
        if (!_isDragging) return;

        var currentX = ConvertPointFromView(theEvent.LocationInWindow, null).X;
        var deltaX = currentX - _dragStartMouseX;
        var deltaFraction = deltaX / Bounds.Width;

        DividerPosition = (nfloat)Math.Clamp(
            (double)(_dragStartPosition + deltaFraction),
            MinPosition, MaxPosition);

        NeedsDisplay = true;
        UpdateTrackingAreas();
        OnDividerMoved?.Invoke(DividerPosition);
    }

    public override void MouseUp(NSEvent theEvent)
    {
        _isDragging = false;
    }

    public override NSView? HitTest(CGPoint point)
    {
        // point is in superview's coordinate system, and so is our Frame
        if (!Frame.Contains(point)) return null;

        // Convert to local X by subtracting frame origin
        var localX = point.X - Frame.X;
        var lineX = Bounds.Width * DividerPosition;

        // Only capture events within the hit zone of the divider line
        if (Math.Abs((double)(localX - lineX)) <= HitZone)
        {
            return this;
        }

        // Let events pass through to views below
        return null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_trackingArea != null)
            {
                RemoveTrackingArea(_trackingArea);
                _trackingArea.Dispose();
                _trackingArea = null;
            }
        }
        base.Dispose(disposing);
    }
}
