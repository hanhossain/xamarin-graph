﻿using System;
using System.Collections.Generic;
using System.Linq;
using CoreGraphics;
using Foundation;
using Graphon.Core;
using Graphon.iOS.Extensions;
using UIKit;

namespace Graphon.iOS.Views
{
	public class LineChartView<Tx, Ty> : UIView
        where Tx : struct
		where Ty : struct
	{
		private const int EdgeOffset = 20;
		private const int TickSize = 10;

		private readonly double _pointSize;
		private readonly IChartDataSource _chartDataSource;
		private readonly IChartAxisSource<Tx, Ty> _chartAxisSource;

		private IEnumerable<LineData> _lines;
		private ChartContext _chartContext;
		private IEnumerable<IEnumerable<(ChartEntry Entry, DataPointView View)>> _entries;
		private bool _completedInitialLoad;

		private static readonly UIStringAttributes _axisStringAttributes = new UIStringAttributes()
		{
			ForegroundColor = UIColor.SystemGrayColor,
			Font = UIFont.PreferredCaption2
		};

		public LineChartView(IChartDataSource chartDataSource, IChartAxisSource<Tx, Ty> chartAxisSource)
        {
			_chartDataSource = chartDataSource ?? throw new ArgumentNullException(nameof(chartDataSource));
			_chartAxisSource = chartAxisSource ?? throw new ArgumentNullException(nameof(chartAxisSource));
			_pointSize = 10;
        }

        public void ReloadData()
        {
			// remove existing views
			var views = _entries.SelectMany(x => x).Select(x => x.View).ToList();
            foreach (var view in views)
            {
				view.RemoveFromSuperview();
            }

			LoadData();
        }

        public override void LayoutSubviews()
        {
            base.LayoutSubviews();

            if (!_completedInitialLoad)
            {
				LoadData();
				_completedInitialLoad = true;
            }

            // redraw when the rotation changes
            SetNeedsDisplay();
        }

        public override void Draw(CGRect rect)
		{
			var chartSize = new CGSize(rect.Width - EdgeOffset * 2, rect.Height - EdgeOffset * 2);

			nfloat xCoefficient = chartSize.Width / _chartContext.Domain;
			nfloat yCoefficient = -chartSize.Height / _chartContext.Range;
			nfloat xDelta = Math.Abs(_chartContext.XMin) / (nfloat)_chartContext.Domain * chartSize.Width + EdgeOffset;
			nfloat yDelta = chartSize.Height - Math.Abs(_chartContext.YMin) / (nfloat)_chartContext.Range * chartSize.Height + EdgeOffset;

			var transform = new CGAffineTransform(xCoefficient, 0, 0, yCoefficient, xDelta, yDelta);

			(int xCount, int yCount) = _chartAxisSource.GetAxisTickCount();

			using var context = UIGraphics.GetCurrentContext();

			// draw x-axis
			context.AddLines(new[] { transform.TransformPoint(new CGPoint(_chartContext.XMin, 0)), transform.TransformPoint(new CGPoint(_chartContext.XMax, 0)) });

			for (int i = 0; i < xCount; i++)
			{
				Tx x = _chartAxisSource.GetXValue(i);
				DrawXTick(x, transform, context);
				DrawXLabel(x, transform);
			}

			// draw y-axis
			context.AddLines(new[] { transform.TransformPoint(new CGPoint(0, _chartContext.YMin)), transform.TransformPoint(new CGPoint(0, _chartContext.YMax)) });

			for (int i = 0; i < yCount; i++)
			{
				Ty y = _chartAxisSource.GetYValue(i);
				DrawYTick(y, transform, context);
				DrawYLabel(y, transform);
			}

			UIColor.SystemGrayColor.SetStroke();

			context.SetLineWidth(1);
			context.StrokePath();

			UpdateDataPoints(transform);

			DrawLines(transform);
		}

		private void LoadData()
		{
			_lines = _chartDataSource.GetChartData() ?? Enumerable.Empty<LineData>();
			_entries = _lines
				.Select(line => line.Entries
					.Select(entry => (entry, new DataPointView()
					{
						Size = _pointSize,
						Color = line.Color
					}))
					.ToList())
				.ToList();

			var chartEntries = _entries.SelectMany(x => x).Select(x => x.Entry).ToList();
			_chartContext = ChartContext.Create(chartEntries);

			var views = _entries
                .SelectMany(x => x)
                .Select(x => x.View)
				.Reverse()
				.ToArray();

			AddSubviews(views);
		}

		private void UpdateDataPoints(CGAffineTransform transform)
		{
			foreach (var (entry, view) in _entries.SelectMany(x => x))
			{
				double shift = -_pointSize / 2.0;
				var calculatedPoint = transform.TransformPoint(entry.AsPoint()).Translate(shift, shift);

				if (!view.Point.IsEqualTo(calculatedPoint))
				{
					view.Point = calculatedPoint;
				}
			}
		}

        private void DrawXTick(Tx value, CGAffineTransform transform, CGContext context)
        {
			if (!_chartAxisSource.ShouldDrawXTick(value))
			{
				return;
			}

			// draw ticks
			var tickTopTransform = CGAffineTransform.MakeTranslation(0, -TickSize / 2);
			var tickBottomTransform = CGAffineTransform.MakeTranslation(0, TickSize / 2);

			var point = new CGPoint(_chartAxisSource.MapToXCoordinate(value), 0);
			var transformedPoint = transform.TransformPoint(point);

			var top = tickTopTransform.TransformPoint(transformedPoint);
			var bottom = tickBottomTransform.TransformPoint(transformedPoint);

			context.AddLines(new[] { top, bottom });
		}

		private void DrawYTick(Ty value, CGAffineTransform transform, CGContext context)
		{
			if (!_chartAxisSource.ShouldDrawYTick(value))
			{
				return;
			}

			// draw ticks
			var leadingTransform = CGAffineTransform.MakeTranslation(-TickSize / 2, 0);
			var trailingTransform = CGAffineTransform.MakeTranslation(TickSize / 2, 0);
			
			var point = new CGPoint(0, _chartAxisSource.MapToYCoordinate(value));
			var transformedPoint = transform.TransformPoint(point);

			var leading = leadingTransform.TransformPoint(transformedPoint);
			var trailing = trailingTransform.TransformPoint(transformedPoint);
			context.AddLines(new[] { leading, trailing });
		}

		private void DrawXLabel(Tx value, CGAffineTransform transform)
		{
			if (!_chartAxisSource.ShouldDrawXLabel(value))
            {
				return;
            }

			var label = (NSString)_chartAxisSource.GetXLabel(value);
			var labelSize = label.GetSizeUsingAttributes(_axisStringAttributes);

			double xCoordinate = _chartAxisSource.MapToXCoordinate(value);

			var point = new CGPoint(xCoordinate, 0);
			var transformedPoint = transform.TransformPoint(point);
			var xLabelTransform = CGAffineTransform.MakeTranslation(-labelSize.Width / 2, TickSize / 2 + 3);
			var labelPoint = xLabelTransform.TransformPoint(transformedPoint);

			label.DrawString(labelPoint, _axisStringAttributes);
		}

		private void DrawYLabel(Ty value, CGAffineTransform transform)
		{
			if (!_chartAxisSource.ShouldDrawYLabel(value))
			{
				return;
			}

			var label = (NSString)_chartAxisSource.GetYLabel(value);
			var labelSize = label.GetSizeUsingAttributes(_axisStringAttributes);

			double yCoordinate = _chartAxisSource.MapToYCoordinate(value);

			var point = new CGPoint(0, yCoordinate);
			var transformedPoint = transform.TransformPoint(point);
			var labelTransform = CGAffineTransform.MakeTranslation(-TickSize / 2 - labelSize.Width - 3, -labelSize.Height / 2);
			var labelPoint = labelTransform.TransformPoint(transformedPoint);

			label.DrawString(labelPoint, _axisStringAttributes);
		}

		private void DrawLines(CGAffineTransform transform)
		{
			using var context = UIGraphics.GetCurrentContext();

			foreach (var line in _lines.Reverse())
			{
				var points = line.Entries.Select(x => transform.TransformPoint(x.AsPoint())).ToArray();
				context.AddLines(points);
				line.Color.SetStroke();
				context.SetLineWidth(1);
				context.StrokePath();
			}
		}
    }
}
