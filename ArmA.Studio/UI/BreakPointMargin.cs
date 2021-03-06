﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace ArmA.Studio.UI
{
    public class BreakPointMargin : AbstractMargin, IBackgroundRenderer
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private SolutionUtil.SolutionFile SolutionFileRef;
        public BreakPointMargin(SolutionUtil.SolutionFile sf)
        {
            this.SolutionFileRef = sf;
            
        }
        
        

        protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
        {
            if (oldTextView != null)
            {
                newTextView.BackgroundRenderers.Remove(this);
                oldTextView.VisualLinesChanged -= TextView_VisualLinesChanged;
            }
            base.OnTextViewChanged(oldTextView, newTextView);
            if (newTextView != null)
            {
                newTextView.BackgroundRenderers.Add(this);
                newTextView.VisualLinesChanged += TextView_VisualLinesChanged;
            }
            this.InvalidateVisual();
        }

        private void TextView_VisualLinesChanged(object sender, EventArgs e)
        {
            this.InvalidateVisual();
        }

        protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters)
        {
            //accept clicks even when clicking on the background
            return new PointHitTestResult(this, hitTestParameters.HitPoint);
        }
        
        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(18, 0);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var view = this.TextView;
            if (view == null || !view.VisualLinesValid)
                return;
            drawingContext.DrawRectangle(Brushes.LightGray, null, new Rect(0, 0, this.ActualWidth, this.ActualHeight));
            var colorActive = new SolidColorBrush(ConfigHost.Coloring.BreakPoint.MainColor);
            colorActive.Freeze();
            var colorInactive = new SolidColorBrush(ConfigHost.Coloring.BreakPoint.MainColorInactive);
            colorInactive.Freeze();
            var pen = new Pen(new SolidColorBrush(ConfigHost.Coloring.BreakPoint.BorderColor), 1);
            pen.Freeze();
            foreach (var line in view.VisualLines)
            {
                var lineNumber = this.GetLineNumber(line);
                var bp = this.SolutionFileRef.GetFirstBreakpoint(lineNumber);
                if (bp != null)
                {
                    var lineTop = line.GetTextLineVisualYPosition(line.TextLines[0], VisualYPosition.TextTop) - view.VerticalOffset;
                    var lineBot = line.GetTextLineVisualYPosition(line.TextLines[0], VisualYPosition.TextBottom) - view.VerticalOffset;
                    //drawingContext.DrawRoundedRectangle(color, pen, new Rect((18 - 12) / 2, lineTop, 12, 12), 5, 5);
                    const double rectSize = 12;
                    drawingContext.DrawRectangle(bp.IsEnabled ? colorActive : colorInactive, pen, new Rect((18 - rectSize) / 2, lineTop + (18 - rectSize) / 4, rectSize, rectSize));
                }
            }

        }
        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            var view = this.TextView;
            if (view == null || !view.VisualLinesValid)
                return;
            var pos = e.MouseDevice.GetPosition(this);
            
            foreach(var line in view.VisualLines)
            {
                var lineNumber = this.GetLineNumber(this.GetLineFromPoint(view, e.GetPosition(this)));
                if (pos.Y >= line.VisualTop && pos.Y <= line.VisualTop + line.Height)
                {
                    var bp = this.SolutionFileRef.GetFirstBreakpoint(lineNumber);
                    if (bp != null)
                    {
                        this.SolutionFileRef.BreakPoints.Remove(bp);
                    }
                    else
                    {
                        this.SolutionFileRef.AddBreakpoint(lineNumber, 0);
                    }
                    this.InvalidateVisual();
                    this.TextView.InvalidateVisual();
                    break;
                }
            }
            e.Handled = true;
        }

        public int GetLineNumber(VisualLine line)
        {
            return line.FirstDocumentLine.LineNumber;
        }
        public VisualLine GetLineFromPoint(TextView view, Point p)
        {
            return view.GetVisualLineFromVisualTop(p.Y + view.ScrollOffset.Y);
        }
        public KnownLayer Layer { get { return KnownLayer.Background; } }

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            textView.EnsureVisualLines();
            var color = new SolidColorBrush(ConfigHost.Coloring.BreakPoint.TextHighlightColor);
            color.Freeze();
            var invalidBps = new List<DataContext.BreakpointsPaneUtil.Breakpoint>();
            foreach (var bp in this.SolutionFileRef.BreakPoints)
            {
                if (bp.Line < 0)
                {
                    Logger.Log(NLog.LogLevel.Warn, $"Removed invalid breakpoint in file '{this.SolutionFileRef.FileName}'.");
                    invalidBps.Add(bp);
                    continue;
                }
                var line = this.Document.GetLineByNumber(bp.Line);
                var segment = new TextSegment { StartOffset = line.Offset, EndOffset = line.EndOffset };
                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                {
                    drawingContext.DrawRectangle(color, null, new Rect(rect.Location, new Size(textView.ActualWidth, rect.Height)));
                }
            }
            foreach (var bp in invalidBps)
            {
                this.SolutionFileRef.BreakPoints.Remove(bp);
            }
        }
        
    }
}
