// Short-Term Order Block Rejections: NinjaTrader 8 port of Tradingview/Short-Term Order Block Rejections.pine (same logic,
// inputs and defaults, bar by bar). Order blocks anchored to the origin swing of the CHoCH leg with an optional displacement
// (FVG) check, configurable zone interaction (touch / midpoint / sweep), overlap tested against active blocks only, mitigated
// blocks kept on the chart faded, CHoCH confirmed on bar close, session filter, stop and targets per signal, alerts and trades
// taken from the signals with their results. High-probability filter (EMA trend slope + leg displacement in ATR) and TP1
// partial / break-even trade management. VWAP anchored to a session or period with standard-deviation / percentage bands.
// NinjaTrader adaptation:
//   - Boxes, lines and labels are drawn in OnRender and capped like the Pine script (500 of each by default; the oldest is
//     deleted). Label tooltips show when the mouse is over the label.
//   - Sessions are evaluated on the bar open time converted from the platform time zone to the session time zone.
//   - Day / Week / Month VWAP anchors follow the trading day of the trading hours template (18:00 NY for CME futures).
//   - VWAP and EMA line colors and widths are set in the Plots section; band fills use each band's plot color.
//   - Calculated on bar close (the Pine logic only acts on confirmed bars).

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
#endregion

public enum StObTouchMode { Touch, Midpoint, Sweep }
public enum StObTradeEntry { ChochPlusBlock, BlockRejection }
public enum StObTargetMode { RMultiple, LegExtremeTp1 }
public enum StObVwapAnchor { NewYork, London, Tokyo, Sydney, CmeGlobex, Day, Week, Month, CustomSession }
public enum StObVwapBands { StandardDeviation, Percentage }
public enum StObSource { Open, High, Low, Close, HL2, HLC3, OHLC4, HLCC4 }
public enum StObTimeZone { NewYork, Chicago, LosAngeles, SaoPaulo, London, Berlin, Tokyo, HongKong, Singapore, Sydney, UTC }

namespace NinjaTrader.NinjaScript.Indicators
{
	public class ShortTermOrderBlockRejections : Indicator
	{
		// --- Drawing objects (Pine box / line / label) ---
		private class StBox { public int Left, Right, Dir; public double Top, Bottom; public Color Fill, Border; public bool Dotted; }   // Fill / Border: only once mitigated (Dotted)
		private class StLine { public int X1, X2; public double Y1, Y2; public Color Col; public float Width = 1f; public int Style; }   // 0 solid, 1 dashed, 2 dotted
		// Style: 0 label_up (box below the point), 1 label_down (box above), 2 label_left (box to the right). YLoc: 0 price, 1 below bar, 2 above bar
		private class StLabel { public int X, Style, YLoc; public double Y; public string Text, Tip; public Color Bg, Fg; public bool Small; }

		private class OrderBlock
		{
			public double Top, Bottom, Extreme, Disp;   // Extreme: best price reached by the break leg since the CHoCH; Disp: break leg in ATR
			public int BornBar;
			public bool Used;
			public StBox Bx;
		}

		private class Trade
		{
			public int Dir, EntryBar;   // 1 = long, -1 = short
			public double Entry, Stop, Target, Risk, Tp1 = double.NaN, Bank, Rem = 1.0;   // Risk: initial risk in points; Bank: R booked by TP1
			public bool Tp1Hit;
			public StLine LnEntry, LnStop, LnTarget, LnTp1;
		}

		private static readonly Color BullColor = Color.FromRgb(0x08, 0x99, 0x81);
		private static readonly Color BearColor = Color.FromRgb(0xF2, 0x36, 0x45);
		private static readonly Color GrayColor = Color.FromRgb(0x78, 0x7B, 0x86);

		private readonly List<StBox> boxes = new List<StBox>();
		private readonly List<StLine> lines = new List<StLine>();
		private readonly List<StLabel> labels = new List<StLabel>();
		private readonly List<StLine> riskLines = new List<StLine>();
		private readonly List<StLabel> riskLabels = new List<StLabel>();

		private readonly List<OrderBlock> bullBlocks = new List<OrderBlock>();
		private readonly List<OrderBlock> bearBlocks = new List<OrderBlock>();

		// Confirmed swing structure
		private double lastSwingHigh = double.NaN, previousSwingHigh = double.NaN, lastSwingLow = double.NaN, previousSwingLow = double.NaN;
		private int lastSwingHighBar = -1, lastSwingLowBar = -1, structureTrend;

		private ATR hpAtrInd, rangeAtrInd;
		private EMA emaFast, emaSlow;
		private TimeZoneInfo nyTz, vwapTzInfo;
		private int sessStart, sessEnd, vwapStart, vwapEnd;
		private bool vwapPeriodAnchor;

		// Trades and statistics
		private Trade trd;
		private int tradesN, tradesWon, buyCount, sellCount, exitsTp, exitsBe, exitsSl;
		private double tradesR;

		// VWAP
		private SessionIterator sessIt;
		private DateTime tradingDay = DateTime.MinValue, prevTradingDay = DateTime.MinValue;
		private double sumV, sumPV, sumP2V;
		private bool prevVwapInSession;
		private Series<bool> vwapResetS;

		// Outputs for strategies
		private Series<int> rejectionSignal, tradeSignal;

		// Tooltips
		private readonly List<KeyValuePair<SharpDX.RectangleF, string>> tipRects = new List<KeyValuePair<SharpDX.RectangleF, string>>();
		private float mouseX = -1, mouseY = -1;
		private string hoverTip;
		private bool mouseHooked;
		private readonly Dictionary<uint, SharpDX.Direct2D1.SolidColorBrush> dxCache = new Dictionary<uint, SharpDX.Direct2D1.SolidColorBrush>();

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description              = "Port of the TradingView indicator Short-Term Order Block Rejections: CHoCH order blocks, rejection signals, trades with TP1 / break-even management, statistics and anchored VWAP with bands.";
				Name                     = "ST OB Rejections";
				Calculate                = Calculate.OnBarClose;
				IsOverlay                = true;
				DisplayInDataBox         = true;
				DrawOnPricePanel         = true;
				PaintPriceMarkers        = true;
				ScaleJustification       = ScaleJustification.Right;
				IsSuspendedWhileInactive = true;
				MaximumBarsLookBack      = MaximumBarsLookBack.Infinite;

				PivotLength = 3;
				ObSearchBars = 8;
				MaxLegBars = 100;
				RequireDisplacement = true;
				MaxReactionBars = 8;
				TouchMode = StObTouchMode.Touch;
				MinBodyPercent = 0.0;
				MaxBlocksPerSide = 100;

				ShowChoch = true;
				FilterSignalsByTrend = true;
				RequireSwingRange = true;
				AvoidFreshExtremeSignals = true;
				FilterChopSignals = false;
				RangeAtrLength = 14;
				MinPivotRangeAtr = 2.0;

				UseSession = true;
				SessionText = "0930-1600";

				RemoveOverlapping = true;
				OverlapThreshold = 25.0;
				MaxDrawings = 500;

				ShowRisk = false;
				StopBufferTicks = 2;
				Rr = 2.0;
				RiskBars = 20;

				ShowTrades = true;
				TradeEntry = StObTradeEntry.ChochPlusBlock;
				TargetMode = StObTargetMode.RMultiple;
				MaxTradeBars = 0;
				Tp1R = 0.5;
				Tp1Pct = 50;
				BeAfterTp1 = true;
				ExitOnChoch = false;
				TradeSession = false;
				ShowStats = true;

				HpTrend = true;
				HpFastLen = 50;
				HpFastSlope = 10;
				HpSlowLen = 200;
				HpSlowSlope = 20;
				HpDisp = true;
				HpMinDisp = 2.5;
				HpShowEma = false;

				BullBlockColor = Frozen(BullColor);
				BearBlockColor = Frozen(BearColor);
				BullBorderColor = Frozen(BullColor);
				BearBorderColor = Frozen(BearColor);
				BlockFillOpacity = 16;

				ShowVwap = true;
				VwapSource = StObSource.HLC3;
				VwapAnchor = StObVwapAnchor.NewYork;
				VwapCustomSession = "0930-1600";
				VwapCustomTz = StObTimeZone.NewYork;
				VwapSessionOnly = false;
				VwapBandMode = StObVwapBands.StandardDeviation;
				VwapBand1 = true;
				VwapMult1 = 1.0;
				VwapBand2 = true;
				VwapMult2 = 2.0;
				VwapBand3 = false;
				VwapMult3 = 3.0;
				VwapFill = true;
				VwapFillTransp = 92;
				EnableAlerts = false;

				AddPlot(new Stroke(Frozen(Color.FromArgb(204, 0xFF, 0x98, 0x00)), 1), PlotStyle.Line, "Trend EMA fast");
				AddPlot(new Stroke(Frozen(Color.FromArgb(204, 0x9C, 0x27, 0xB0)), 2), PlotStyle.Line, "Trend EMA slow");
				AddPlot(new Stroke(Frozen(Color.FromRgb(0x29, 0x62, 0xFF)), 2), PlotStyle.Line, "VWAP");
				AddPlot(new Stroke(Frozen(Color.FromRgb(0x08, 0x99, 0x81)), 1), PlotStyle.Line, "VWAP upper 1");
				AddPlot(new Stroke(Frozen(Color.FromRgb(0x08, 0x99, 0x81)), 1), PlotStyle.Line, "VWAP lower 1");
				AddPlot(new Stroke(Frozen(Color.FromRgb(0x80, 0x80, 0x00)), 1), PlotStyle.Line, "VWAP upper 2");
				AddPlot(new Stroke(Frozen(Color.FromRgb(0x80, 0x80, 0x00)), 1), PlotStyle.Line, "VWAP lower 2");
				AddPlot(new Stroke(Frozen(Color.FromRgb(0x00, 0x89, 0x7B)), 1), PlotStyle.Line, "VWAP upper 3");
				AddPlot(new Stroke(Frozen(Color.FromRgb(0x00, 0x89, 0x7B)), 1), PlotStyle.Line, "VWAP lower 3");
			}
			else if (State == State.DataLoaded)
			{
				hpAtrInd = ATR(14);
				rangeAtrInd = ATR(RangeAtrLength);
				emaFast = EMA(HpFastLen);
				emaSlow = EMA(HpSlowLen);
				vwapResetS = new Series<bool>(this, MaximumBarsLookBack.Infinite);
				rejectionSignal = new Series<int>(this, MaximumBarsLookBack.Infinite);
				tradeSignal = new Series<int>(this, MaximumBarsLookBack.Infinite);
				sessIt = new SessionIterator(Bars);

				nyTz = FindTz("Eastern Standard Time");
				ParseSession(SessionText, out sessStart, out sessEnd);
				vwapPeriodAnchor = VwapAnchor == StObVwapAnchor.Day || VwapAnchor == StObVwapAnchor.Week || VwapAnchor == StObVwapAnchor.Month;
				switch (VwapAnchor)
				{
					case StObVwapAnchor.NewYork:   vwapStart = 570;  vwapEnd = 960;  vwapTzInfo = nyTz; break;
					case StObVwapAnchor.London:    vwapStart = 480;  vwapEnd = 990;  vwapTzInfo = FindTz("GMT Standard Time"); break;
					case StObVwapAnchor.Tokyo:     vwapStart = 540;  vwapEnd = 930;  vwapTzInfo = FindTz("Tokyo Standard Time"); break;
					case StObVwapAnchor.Sydney:    vwapStart = 600;  vwapEnd = 960;  vwapTzInfo = FindTz("AUS Eastern Standard Time"); break;
					case StObVwapAnchor.CmeGlobex: vwapStart = 1080; vwapEnd = 1020; vwapTzInfo = nyTz; break;
					default: ParseSession(VwapCustomSession, out vwapStart, out vwapEnd); vwapTzInfo = FindTz(TzId(VwapCustomTz)); break;
				}
			}
			else if (State == State.Historical)
			{
				if (ChartControl != null && !mouseHooked)
				{
					mouseHooked = true;
					ChartControl.Dispatcher.InvokeAsync(() => { if (ChartPanel != null) ChartPanel.MouseMove += OnPanelMouseMove; });
				}
			}
			else if (State == State.Terminated)
			{
				if (ChartControl != null && mouseHooked)
				{
					mouseHooked = false;
					ChartControl.Dispatcher.InvokeAsync(() => { if (ChartPanel != null) ChartPanel.MouseMove -= OnPanelMouseMove; });
				}
				DisposeDx();
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0)
				return;

			bool intraday = IsIntraday();
			double tick = TickSize;
			rejectionSignal[0] = 0;
			tradeSignal[0] = 0;

			// --- Confirmed swing structure ---
			int pl = PivotLength;
			double confirmedPivotHigh = PivotHigh(pl), confirmedPivotLow = PivotLow(pl);
			if (!double.IsNaN(confirmedPivotHigh))
			{
				previousSwingHigh = lastSwingHigh;
				lastSwingHigh = confirmedPivotHigh;
				lastSwingHighBar = CurrentBar - pl;
			}
			if (!double.IsNaN(confirmedPivotLow))
			{
				previousSwingLow = lastSwingLow;
				lastSwingLow = confirmedPivotLow;
				lastSwingLowBar = CurrentBar - pl;
			}

			bool swingsKnown = !double.IsNaN(previousSwingHigh) && !double.IsNaN(previousSwingLow);
			bool higherHighHigherLow = swingsKnown && lastSwingHigh > previousSwingHigh && lastSwingLow > previousSwingLow;
			bool lowerHighLowerLow = swingsKnown && lastSwingHigh < previousSwingHigh && lastSwingLow < previousSwingLow;
			if (structureTrend == 0)
			{
				if (higherHighHigherLow)
					structureTrend = 1;
				else if (lowerHighLowerLow)
					structureTrend = -1;
			}

			// A CHoCH is a confirmed close through the protected swing against the established trend
			bool bullishChoch = CurrentBar > 0 && structureTrend == -1 && !double.IsNaN(lastSwingHigh) && lastSwingHighBar >= 0 && Close[0] > lastSwingHigh && Close[1] <= lastSwingHigh;
			bool bearishChoch = CurrentBar > 0 && structureTrend == 1 && !double.IsNaN(lastSwingLow) && lastSwingLowBar >= 0 && Close[0] < lastSwingLow && Close[1] >= lastSwingLow;
			if (bullishChoch)
				structureTrend = 1;
			else if (bearishChoch)
				structureTrend = -1;

			bool bullishStructureAllowed = !FilterSignalsByTrend || structureTrend == 1;
			bool bearishStructureAllowed = !FilterSignalsByTrend || structureTrend == -1;

			// --- CHoCH visualization ---
			if (ShowChoch && bullishChoch)
			{
				AddLine(new StLine { X1 = lastSwingHighBar, Y1 = lastSwingHigh, X2 = CurrentBar, Y2 = lastSwingHigh, Col = BullColor, Style = 1 });
				AddLabel(new StLabel { X = lastSwingHighBar + (CurrentBar - lastSwingHighBar) / 2, Y = lastSwingHigh, Text = "CHoCH", Style = 1, Bg = Colors.Transparent, Fg = BullColor });
			}
			if (ShowChoch && bearishChoch)
			{
				AddLine(new StLine { X1 = lastSwingLowBar, Y1 = lastSwingLow, X2 = CurrentBar, Y2 = lastSwingLow, Col = BearColor, Style = 1 });
				AddLabel(new StLabel { X = lastSwingLowBar + (CurrentBar - lastSwingLowBar) / 2, Y = lastSwingLow, Text = "CHoCH", Style = 0, Bg = Colors.Transparent, Fg = BearColor });
			}

			// --- High-probability filter: EMA trend slope and leg displacement ---
			double hpAtr = CurrentBar >= 13 ? hpAtrInd[0] : double.NaN;
			double hpEmaFast = emaFast[0], hpEmaSlow = emaSlow[0];
			bool slopesKnown = CurrentBar >= HpFastSlope && CurrentBar >= HpSlowSlope;
			bool hpTrendLong = !HpTrend || (slopesKnown && hpEmaFast > emaFast[HpFastSlope] && hpEmaSlow > emaSlow[HpSlowSlope]);
			bool hpTrendShort = !HpTrend || (slopesKnown && hpEmaFast < emaFast[HpFastSlope] && hpEmaSlow < emaSlow[HpSlowSlope]);
			if (HpShowEma)
			{
				Values[0][0] = hpEmaFast;
				Values[1][0] = hpEmaSlow;
			}

			// --- CHoCH-only order block creation ---
			double stopBuffer = StopBufferTicks * tick;
			bool chochBuy = false, chochSell = false;
			double chochBuyStop = double.NaN, chochSellStop = double.NaN, chochBuyDisp = double.NaN, chochSellDisp = double.NaN;

			if (bullishChoch)
			{
				double top, bottom, originPx;
				int offset;
				bool displacement;
				bool found = FindBlock(1, lastSwingHighBar, out top, out bottom, out offset, out displacement, out originPx);
				double legDisp = (Close[0] - originPx) / hpAtr;
				bool withinSwing = !RequireSwingRange || top < lastSwingHigh;
				bool impulsive = !RequireDisplacement || displacement;
				if (found && withinSwing && impulsive)
				{
					bool duplicate = RemoveOverlapping && (OverlapsActive(top, bottom, bullBlocks) || OverlapsActive(top, bottom, bearBlocks));
					if (!duplicate)
					{
						StBox bx = AddBox(new StBox { Left = CurrentBar - offset, Top = top, Right = CurrentBar + 1, Bottom = bottom, Dir = 1 });
						bullBlocks.Add(new OrderBlock { Top = top, Bottom = bottom, BornBar = CurrentBar, Extreme = High[0], Bx = bx, Disp = legDisp });
						if (bullBlocks.Count > MaxBlocksPerSide)
							bullBlocks.RemoveAt(0);
						chochBuy = true;
						chochBuyStop = bottom - stopBuffer;
						chochBuyDisp = legDisp;
					}
				}
			}

			if (bearishChoch)
			{
				double top, bottom, originPx;
				int offset;
				bool displacement;
				bool found = FindBlock(-1, lastSwingLowBar, out top, out bottom, out offset, out displacement, out originPx);
				double legDisp = (originPx - Close[0]) / hpAtr;
				bool withinSwing = !RequireSwingRange || bottom > lastSwingLow;
				bool impulsive = !RequireDisplacement || displacement;
				if (found && withinSwing && impulsive)
				{
					bool duplicate = RemoveOverlapping && (OverlapsActive(top, bottom, bearBlocks) || OverlapsActive(top, bottom, bullBlocks));
					if (!duplicate)
					{
						StBox bx = AddBox(new StBox { Left = CurrentBar - offset, Top = top, Right = CurrentBar + 1, Bottom = bottom, Dir = -1 });
						bearBlocks.Add(new OrderBlock { Top = top, Bottom = bottom, BornBar = CurrentBar, Extreme = Low[0], Bx = bx, Disp = legDisp });
						if (bearBlocks.Count > MaxBlocksPerSide)
							bearBlocks.RemoveAt(0);
						chochSell = true;
						chochSellStop = top + stopBuffer;
						chochSellDisp = legDisp;
					}
				}
			}

			// --- Rejection signals, box extension and mitigation ---
			double candleRange = Math.Max(High[0] - Low[0], tick);
			double bodyPercent = Math.Abs(Close[0] - Open[0]) / candleRange * 100.0;
			double rangeAtrValue = CurrentBar >= RangeAtrLength - 1 ? rangeAtrInd[0] : double.NaN;
			int swingExtremeLookback = Math.Max(pl * 2, 2);
			bool lookbackKnown = CurrentBar >= swingExtremeLookback;
			bool bearishAtFreshLow = lookbackKnown && Low[0] < MinLow(1, swingExtremeLookback);
			bool bullishAtFreshHigh = lookbackKnown && High[0] > MaxHigh(1, swingExtremeLookback);
			DateTime barOpen = BarOpenTime();
			bool sessionTime = !intraday || InSession(ToTz(barOpen, nyTz), sessStart, sessEnd);
			bool inSession = !UseSession || sessionTime;

			bool bullishSignal = false, bearishSignal = false;
			double bullStop = double.NaN, bullTp1 = double.NaN, bullDisp = double.NaN, bearStop = double.NaN, bearTp1 = double.NaN, bearDisp = double.NaN;

			for (int bullIndex = bullBlocks.Count - 1; bullIndex >= 0; bullIndex--)
			{
				OrderBlock ob = bullBlocks[bullIndex];
				int zoneAge = CurrentBar - ob.BornBar;
				double zoneMid = (ob.Top + ob.Bottom) / 2;
				bool zoneMitigated = zoneAge > 0 && Close[0] < ob.Bottom;
				bool fullZoneReclaim = Low[0] < ob.Bottom && Close[0] > ob.Top;
				bool touched = TouchMode == StObTouchMode.Touch ? Low[0] <= ob.Top : TouchMode == StObTouchMode.Midpoint ? Low[0] <= zoneMid : Low[0] < ob.Bottom;
				bool rejected = TouchMode == StObTouchMode.Sweep ? Open[0] > ob.Bottom && Close[0] > ob.Bottom : Close[0] > ob.Top;
				bool zoneInReactionWindow = zoneAge > 0 && zoneAge <= MaxReactionBars;
				bool bullishRangePass = !FilterChopSignals || (!double.IsNaN(lastSwingHigh) && !double.IsNaN(rangeAtrValue) && lastSwingHigh - ob.Top >= rangeAtrValue * MinPivotRangeAtr);
				bool zoneSignal = inSession && bullishRangePass && (bullishStructureAllowed || fullZoneReclaim) && (fullZoneReclaim || !AvoidFreshExtremeSignals || !bullishAtFreshHigh)
					&& zoneInReactionWindow && !ob.Used && ((touched && rejected) || fullZoneReclaim) && Close[0] > Open[0] && bodyPercent >= MinBodyPercent;
				if (zoneAge >= 0 && zoneAge <= MaxReactionBars)
					ob.Bx.Right = CurrentBar + 1;
				if (zoneSignal)
				{
					ob.Used = true;
					// The newest block that fires sets the trade levels
					if (!bullishSignal)
					{
						bullStop = Math.Min(Low[0], ob.Bottom) - stopBuffer;
						bullTp1 = ob.Extreme > Close[0] ? ob.Extreme : double.NaN;
						bullDisp = ob.Disp;
					}
					bullishSignal = true;
				}
				ob.Extreme = Math.Max(ob.Extreme, High[0]);
				if (zoneMitigated)
				{
					// Stop tracking, but keep the drawing faded as context for the signals it produced
					ob.Bx.Fill = WithAlpha(BullColor, 5);
					ob.Bx.Border = WithAlpha(BullColor, 30);
					ob.Bx.Dotted = true;
					bullBlocks.RemoveAt(bullIndex);
				}
			}

			for (int bearIndex = bearBlocks.Count - 1; bearIndex >= 0; bearIndex--)
			{
				OrderBlock ob = bearBlocks[bearIndex];
				int zoneAge = CurrentBar - ob.BornBar;
				double zoneMid = (ob.Top + ob.Bottom) / 2;
				bool zoneMitigated = zoneAge > 0 && Close[0] > ob.Top;
				bool fullZoneReclaim = High[0] > ob.Top && Close[0] < ob.Bottom;
				bool touched = TouchMode == StObTouchMode.Touch ? High[0] >= ob.Bottom : TouchMode == StObTouchMode.Midpoint ? High[0] >= zoneMid : High[0] > ob.Top;
				bool rejected = TouchMode == StObTouchMode.Sweep ? Open[0] < ob.Top && Close[0] < ob.Top : Close[0] < ob.Bottom;
				bool zoneInReactionWindow = zoneAge > 0 && zoneAge <= MaxReactionBars;
				bool bearishRangePass = !FilterChopSignals || (!double.IsNaN(lastSwingLow) && !double.IsNaN(rangeAtrValue) && ob.Bottom - lastSwingLow >= rangeAtrValue * MinPivotRangeAtr);
				bool zoneSignal = inSession && bearishRangePass && (bearishStructureAllowed || fullZoneReclaim) && (fullZoneReclaim || !AvoidFreshExtremeSignals || !bearishAtFreshLow)
					&& zoneInReactionWindow && !ob.Used && ((touched && rejected) || fullZoneReclaim) && Close[0] < Open[0] && bodyPercent >= MinBodyPercent;
				if (zoneAge >= 0 && zoneAge <= MaxReactionBars)
					ob.Bx.Right = CurrentBar + 1;
				if (zoneSignal)
				{
					ob.Used = true;
					if (!bearishSignal)
					{
						bearStop = Math.Max(High[0], ob.Top) + stopBuffer;
						bearTp1 = ob.Extreme < Close[0] ? ob.Extreme : double.NaN;
						bearDisp = ob.Disp;
					}
					bearishSignal = true;
				}
				ob.Extreme = Math.Min(ob.Extreme, Low[0]);
				if (zoneMitigated)
				{
					ob.Bx.Fill = WithAlpha(BearColor, 5);
					ob.Bx.Border = WithAlpha(BearColor, 30);
					ob.Bx.Dotted = true;
					bearBlocks.RemoveAt(bearIndex);
				}
			}

			// --- Visual signals, risk projection and alerts ---
			if (bullishSignal)
			{
				rejectionSignal[0] = 1;
				DoAlert("ObBull", Signal(1, bullStop, bullTp1));
			}
			if (bearishSignal)
			{
				rejectionSignal[0] = -1;
				DoAlert("ObBear", Signal(-1, bearStop, bearTp1));
			}

			// --- Trades taken from the signals ---
			if (ShowTrades)
			{
				if (trd != null)
				{
					trd.LnEntry.X2 = CurrentBar;
					trd.LnStop.X2 = CurrentBar;
					trd.LnTarget.X2 = CurrentBar;
					if (trd.LnTp1 != null)
						trd.LnTp1.X2 = CurrentBar;
				}

				bool chochMode = TradeEntry == StObTradeEntry.ChochPlusBlock;
				bool sigLong = chochMode ? chochBuy : bullishSignal;
				bool sigShort = chochMode ? chochSell : bearishSignal;
				double exitR = double.NaN;
				string exitTag = "";

				// Stop / TP1 / target / time exit, from the candle after the entry. The stop is checked first, with the level it had
				// at the candle open, so a candle that reaches both counts as a stop.
				if (trd != null && CurrentBar > trd.EntryBar)
				{
					if (trd.Dir == 1 ? Low[0] <= trd.Stop : High[0] >= trd.Stop)
					{
						exitTag = trd.Tp1Hit ? "BE" : "SL";
						exitR = CloseTrade(trd, trd.Stop, exitTag);
						trd = null;
					}
					else
					{
						if (!double.IsNaN(trd.Tp1) && !trd.Tp1Hit && (trd.Dir == 1 ? High[0] >= trd.Tp1 : Low[0] <= trd.Tp1))
						{
							double part = Tp1Pct / 100.0;
							trd.Tp1Hit = true;
							trd.Bank += part * (trd.Tp1 - trd.Entry) * trd.Dir / trd.Risk;
							trd.Rem -= part;
							if (BeAfterTp1)
							{
								trd.Stop = trd.Entry;
								trd.LnStop.Y1 = trd.Entry;
								trd.LnStop.Y2 = trd.Entry;
							}
							AddLabel(new StLabel { X = CurrentBar, Y = trd.Tp1, Text = "TP1", Style = 2, Bg = Colors.Transparent, Fg = BullColor });
							DoAlert("ObTp1", (trd.Dir == 1 ? "Long" : "Short") + " trade TP1 on " + Ticker() + " " + Period() + " @ " + Px(trd.Tp1)
								+ (part > 0 ? " | " + Tp1Pct + "% closed" : "") + (BeAfterTp1 ? " | stop to break-even " + Px(trd.Entry) : ""));
						}
						bool hitTarget = trd.Dir == 1 ? High[0] >= trd.Target : Low[0] <= trd.Target;
						bool timedOut = MaxTradeBars > 0 && CurrentBar - trd.EntryBar >= MaxTradeBars;
						if (trd.Rem <= 0)
						{
							exitTag = "TP1";
							exitR = CloseTrade(trd, trd.Tp1, exitTag);
							trd = null;
						}
						else if (hitTarget || timedOut)
						{
							exitTag = hitTarget ? "TP" : "Time";
							exitR = CloseTrade(trd, hitTarget ? trd.Target : Close[0], exitTag);
							trd = null;
						}
					}
				}

				// CHoCH mode: optionally a new CHoCH signal closes the open trade at its close
				if (chochMode && ExitOnChoch && trd != null && sigLong != sigShort)
				{
					exitTag = "CHoCH";
					exitR = CloseTrade(trd, Close[0], exitTag);
					trd = null;
				}

				if (!double.IsNaN(exitR))
				{
					tradesN++;
					tradesWon += exitR > 0 ? 1 : 0;
					tradesR += exitR;
					exitsTp += exitTag == "TP" || exitTag == "TP1" ? 1 : 0;
					exitsBe += exitTag == "BE" ? 1 : 0;
					exitsSl += exitTag == "SL" ? 1 : 0;
				}

				// Entry at the signal close while flat, when the signal passes the high-probability filter
				if (trd == null && sigLong != sigShort)
				{
					int dir = sigLong ? 1 : -1;
					double stop = chochMode ? (dir == 1 ? chochBuyStop : chochSellStop) : (dir == 1 ? bullStop : bearStop);
					double tp1 = dir == 1 ? bullTp1 : bearTp1;
					double disp = chochMode ? (dir == 1 ? chochBuyDisp : chochSellDisp) : (dir == 1 ? bullDisp : bearDisp);
					double risk = (Close[0] - stop) * dir;
					bool filterOk = (dir == 1 ? hpTrendLong : hpTrendShort) && (!HpDisp || disp >= HpMinDisp) && (!TradeSession || sessionTime);
					if (risk > 0 && filterOk)
					{
						double target = !chochMode && TargetMode == StObTargetMode.LegExtremeTp1 && !double.IsNaN(tp1) ? tp1 : Close[0] + dir * Rr * risk;
						double tp1Px = Close[0] + dir * Tp1R * risk;
						bool useTp1 = (target - tp1Px) * dir > 0 && (Tp1Pct > 0 || BeAfterTp1);
						Color c = dir == 1 ? BullColor : BearColor;
						trd = new Trade
						{
							Dir = dir, Entry = Close[0], Stop = stop, Target = target, EntryBar = CurrentBar, Risk = risk,
							LnEntry = AddLine(new StLine { X1 = CurrentBar, Y1 = Close[0], X2 = CurrentBar, Y2 = Close[0], Col = GrayColor }),
							LnStop = AddLine(new StLine { X1 = CurrentBar, Y1 = stop, X2 = CurrentBar, Y2 = stop, Col = BearColor }),
							LnTarget = AddLine(new StLine { X1 = CurrentBar, Y1 = target, X2 = CurrentBar, Y2 = target, Col = BullColor }),
							Tp1 = useTp1 ? tp1Px : double.NaN,
							LnTp1 = useTp1 ? AddLine(new StLine { X1 = CurrentBar, Y1 = tp1Px, X2 = CurrentBar, Y2 = tp1Px, Col = WithAlpha(BullColor, 60), Style = 1 }) : null
						};
						tradeSignal[0] = dir;
						buyCount += dir == 1 ? 1 : 0;
						sellCount += dir == -1 ? 1 : 0;
						AddLabel(new StLabel
						{
							X = CurrentBar, Y = dir == 1 ? Low[0] : High[0], Text = dir == 1 ? "BUY" : "SELL", YLoc = dir == 1 ? 1 : 2, Style = dir == 1 ? 0 : 1, Bg = c, Fg = Colors.White, Small = true,
							Tip = "Entry " + Px(Close[0]) + "\nStop " + Px(stop) + (useTp1 ? "\nTP1 " + Px(tp1Px) : "") + "\nTarget " + Px(target) + "\nLeg displacement " + Num(disp, "0.#") + " ATR"
						});
						DoAlert("ObTrade", (dir == 1 ? "BUY " : "SELL ") + Ticker() + " " + Period() + (chochMode ? " (CHoCH + order block)" : " (order block rejection)") + " @ " + Px(Close[0])
							+ " | Stop " + Px(stop) + (useTp1 ? " | TP1 " + Px(tp1Px) : "") + " | Target " + Px(target)
							+ " | Risk " + Num(risk, "0.##") + " pts ($" + Num(risk * Instrument.MasterInstrument.PointValue, "#,##0.##") + " per contract)");
					}
				}
			}

			UpdateVwap(intraday, barOpen);
		}

		#region Logic helpers
		// ta.pivothigh(high, n, n): the bar n bars ago is higher than the n bars before it and not lower than the n bars after it
		private double PivotHigh(int n)
		{
			if (CurrentBar < 2 * n)
				return double.NaN;
			double p = High[n];
			for (int i = 1; i <= n; i++)
				if (High[n - i] > p || High[n + i] >= p)
					return double.NaN;
			return p;
		}

		private double PivotLow(int n)
		{
			if (CurrentBar < 2 * n)
				return double.NaN;
			double p = Low[n];
			for (int i = 1; i <= n; i++)
				if (Low[n - i] < p || Low[n + i] <= p)
					return double.NaN;
			return p;
		}

		private double MinLow(int from, int count)
		{
			double m = double.MaxValue;
			for (int i = from; i < from + count; i++)
				m = Math.Min(m, Low[i]);
			return m;
		}

		private double MaxHigh(int from, int count)
		{
			double m = double.MinValue;
			for (int i = from; i < from + count; i++)
				m = Math.Max(m, High[i]);
			return m;
		}

		// True when the zone overlaps, by at least the threshold of the smaller zone, a block still inside its reaction window
		private bool OverlapsActive(double newTop, double newBottom, List<OrderBlock> blocks)
		{
			double newHeight = Math.Max(newTop - newBottom, TickSize);
			foreach (OrderBlock ob in blocks)
				if (CurrentBar - ob.BornBar <= MaxReactionBars)
				{
					double existingHeight = Math.Max(ob.Top - ob.Bottom, TickSize);
					double intersection = Math.Max(0.0, Math.Min(newTop, ob.Top) - Math.Max(newBottom, ob.Bottom));
					if (intersection / Math.Min(newHeight, existingHeight) * 100.0 >= OverlapThreshold)
						return true;
				}
			return false;
		}

		// Locates the order block of a CHoCH leg. dir = 1 for a bullish break of the swing high at swingBar, -1 for a bearish one.
		private bool FindBlock(int dir, int swingBar, out double top, out double bottom, out int offset, out bool displacement, out double originPx)
		{
			int legBars = Math.Min(Math.Max(Math.Min(CurrentBar - swingBar, MaxLegBars), 1), CurrentBar);
			// Origin of the leg: the lowest low (highest high) since the broken swing
			int originOffset = 1;
			for (int i = 1; i <= legBars; i++)
				if (dir == 1 ? Low[i] < Low[originOffset] : High[i] > High[originOffset])
					originOffset = i;
			// Block: the lowest bearish (highest bullish) candle from the CHoCH back to a few candles before the origin
			bool found = false;
			top = double.NaN;
			bottom = double.NaN;
			offset = 0;
			int last = Math.Min(originOffset + ObSearchBars, legBars);
			for (int i = 1; i <= last; i++)
			{
				bool opposite = dir == 1 ? Close[i] < Open[i] : Close[i] > Open[i];
				if (opposite && (!found || (dir == 1 ? Low[i] < bottom : High[i] > top)))
				{
					top = High[i];
					bottom = Low[i];
					offset = i;
					found = true;
				}
			}
			// Displacement: a fair value gap in the direction of the break between the origin and the CHoCH bar
			displacement = false;
			for (int j = 0; j <= originOffset - 2; j++)
				if (dir == 1 ? Low[j] > High[j + 2] : High[j] < Low[j + 2])
				{
					displacement = true;
					break;
				}
			originPx = dir == 1 ? Low[originOffset] : High[originOffset];
			return found;
		}

		// Draws the signal tag and replaces the previous stop/target projection; returns the alert message
		private string Signal(int dir, double stop, double tp1)
		{
			bool isLong = dir == 1;
			Color c = isLong ? BullColor : BearColor;
			double risk = Math.Abs(Close[0] - stop);
			double tp2 = Close[0] + dir * risk * Rr;
			string details = "Entry " + Px(Close[0]) + " | Stop " + Px(stop) + " | TP1 (leg extreme) " + Px(tp1)
				+ " | TP2 (" + Num(Rr, "0.##") + "R) " + Px(tp2)
				+ " | Risk " + Num(risk, "0.##") + " pts ($" + Num(risk * Instrument.MasterInstrument.PointValue, "#,##0.##") + " per contract)";
			AddLabel(new StLabel
			{
				X = CurrentBar, Y = isLong ? Low[0] : High[0], Text = "OB", Style = isLong ? 0 : 1, Bg = WithAlpha(c, 95), Fg = Colors.White,
				Tip = (isLong ? "Bullish" : "Bearish") + " order block rejection\n" + details.Replace(" | ", "\n")
			});
			if (ShowRisk)
			{
				foreach (StLine ln in riskLines)
					lines.Remove(ln);
				foreach (StLabel lb in riskLabels)
					labels.Remove(lb);
				riskLines.Clear();
				riskLabels.Clear();
				int x2 = CurrentBar + RiskBars;
				riskLines.Add(AddLine(new StLine { X1 = CurrentBar, Y1 = Close[0], X2 = x2, Y2 = Close[0], Col = WithAlpha(GrayColor, 70), Style = 2 }));
				riskLines.Add(AddLine(new StLine { X1 = CurrentBar, Y1 = stop, X2 = x2, Y2 = stop, Col = BearColor, Width = 2f }));
				riskLabels.Add(AddLabel(new StLabel { X = x2, Y = stop, Text = "Stop " + Px(stop), Style = 2, Bg = WithAlpha(BearColor, 80), Fg = Colors.White }));
				riskLines.Add(AddLine(new StLine { X1 = CurrentBar, Y1 = tp2, X2 = x2, Y2 = tp2, Col = BullColor, Width = 2f }));
				riskLabels.Add(AddLabel(new StLabel { X = x2, Y = tp2, Text = "TP2 " + Px(tp2), Style = 2, Bg = WithAlpha(BullColor, 80), Fg = Colors.White }));
				if (!double.IsNaN(tp1))
				{
					riskLines.Add(AddLine(new StLine { X1 = CurrentBar, Y1 = tp1, X2 = x2, Y2 = tp1, Col = WithAlpha(BullColor, 60), Style = 1 }));
					riskLabels.Add(AddLabel(new StLabel { X = x2, Y = tp1, Text = "TP1 " + Px(tp1), Style = 2, Bg = WithAlpha(BullColor, 50), Fg = Colors.White }));
				}
			}
			return (isLong ? "Bullish" : "Bearish") + " order block rejection on " + Ticker() + " " + Period() + " | " + details;
		}

		// Closes what is left of the open trade at the given price, books the result (TP1 partial included) and labels it
		private double CloseTrade(Trade t, double exitPx, string tag)
		{
			double exitR = t.Bank + t.Rem * (exitPx - t.Entry) * t.Dir / t.Risk;
			string res = tag + " " + Num(exitR, "+0.##;-0.##") + "R";
			AddLabel(new StLabel { X = CurrentBar, Y = exitPx, Text = res, Style = 2, Bg = Colors.Transparent, Fg = exitR > 0 ? BullColor : BearColor, Small = true });
			DoAlert("ObClose", (t.Dir == 1 ? "Long" : "Short") + " trade closed on " + Ticker() + " " + Period() + " | " + res + " @ " + Px(exitPx));
			return exitR;
		}

		// --- VWAP anchored to the selected session or period ---
		private void UpdateVwap(bool intraday, DateTime barOpen)
		{
			// Trading day of the bar, for the Day / Week / Month anchors and for non-intraday charts
			prevTradingDay = tradingDay;
			if (!intraday)
				tradingDay = Time[0].Date;
			else if (Bars.IsFirstBarOfSession || CurrentBar == 0)
			{
				sessIt.GetNextSession(Time[0], true);
				tradingDay = sessIt.ActualTradingDayExchange.Date;
			}
			bool first = CurrentBar == 0;
			bool dayChange = !first && tradingDay != prevTradingDay;

			bool vwapInSession = false, reset;
			if (intraday && !vwapPeriodAnchor)
			{
				vwapInSession = InSession(ToTz(barOpen, vwapTzInfo), vwapStart, vwapEnd);
				// True when the session open falls between the previous bar's close and this bar's close: symbols that trade the whole
				// session window have no out-of-session bar before the open
				bool openCrossed = false;
				if (!first)
				{
					if ((Time[0] - Time[1]).TotalMilliseconds >= 86400000)
						openCrossed = true;
					else
					{
						int a = LocalMinute(ToTz(Time[1], vwapTzInfo)), b = LocalMinute(ToTz(Time[0], vwapTzInfo));
						openCrossed = a <= b ? vwapStart >= a && vwapStart < b : vwapStart >= a || vwapStart < b;
					}
				}
				reset = vwapInSession && (!prevVwapInSession || openCrossed);
			}
			else if (VwapAnchor == StObVwapAnchor.Week)
				reset = !first && WeekStart(tradingDay) != WeekStart(prevTradingDay);
			else if (VwapAnchor == StObVwapAnchor.Month)
				reset = !first && (tradingDay.Month != prevTradingDay.Month || tradingDay.Year != prevTradingDay.Year);
			else
				reset = dayChange;
			prevVwapInSession = vwapInSession;

			double src = Source(), v = Volume[0];
			if (reset || first)
			{
				sumV = 0;
				sumPV = 0;
				sumP2V = 0;
			}
			sumV += v;
			sumPV += src * v;
			sumP2V += src * src * v;
			vwapResetS[0] = reset;

			bool vwapVisible = ShowVwap && (!VwapSessionOnly || !intraday || vwapPeriodAnchor || vwapInSession);
			if (!vwapVisible || sumV <= 0)
				return;
			double vwap = sumPV / sumV;
			double stdev = Math.Sqrt(Math.Max(sumP2V / sumV - vwap * vwap, 0));
			SetLevel(2, true, vwap, reset);
			SetLevel(3, VwapBand1, vwap + Offset(vwap, stdev, VwapMult1), reset);
			SetLevel(4, VwapBand1, vwap - Offset(vwap, stdev, VwapMult1), reset);
			SetLevel(5, VwapBand2, vwap + Offset(vwap, stdev, VwapMult2), reset);
			SetLevel(6, VwapBand2, vwap - Offset(vwap, stdev, VwapMult2), reset);
			SetLevel(7, VwapBand3, vwap + Offset(vwap, stdev, VwapMult3), reset);
			SetLevel(8, VwapBand3, vwap - Offset(vwap, stdev, VwapMult3), reset);
		}

		// Band offset for a multiplier: standard deviations or a percentage of the VWAP
		private double Offset(double vwap, double stdev, double mult)
		{
			return VwapBandMode == StObVwapBands.Percentage ? vwap * mult / 100.0 : stdev * mult;
		}

		// No connecting segment across a reset
		private void SetLevel(int plot, bool show, double value, bool reset)
		{
			if (!show)
				return;
			Values[plot][0] = value;
			if (reset)
				PlotBrushes[plot][0] = Brushes.Transparent;
		}

		private double Source()
		{
			switch (VwapSource)
			{
				case StObSource.Open:  return Open[0];
				case StObSource.High:  return High[0];
				case StObSource.Low:   return Low[0];
				case StObSource.Close: return Close[0];
				case StObSource.HL2:   return (High[0] + Low[0]) / 2;
				case StObSource.OHLC4: return (Open[0] + High[0] + Low[0] + Close[0]) / 4;
				case StObSource.HLCC4: return (High[0] + Low[0] + 2 * Close[0]) / 4;
				default:               return (High[0] + Low[0] + Close[0]) / 3;
			}
		}

		private bool IsIntraday()
		{
			BarsPeriodType t = BarsPeriod.BarsPeriodType;
			return t != BarsPeriodType.Day && t != BarsPeriodType.Week && t != BarsPeriodType.Month && t != BarsPeriodType.Year;
		}

		// Bar open time (NinjaTrader time stamps bars with their close)
		private DateTime BarOpenTime()
		{
			if (BarsPeriod.BarsPeriodType == BarsPeriodType.Minute)
				return Time[0].AddMinutes(-BarsPeriod.Value);
			if (BarsPeriod.BarsPeriodType == BarsPeriodType.Second)
				return Time[0].AddSeconds(-BarsPeriod.Value);
			return CurrentBar > 0 && !Bars.IsFirstBarOfSession ? Time[1] : Time[0];
		}

		private static bool InSession(DateTime local, int start, int end)
		{
			int m = LocalMinute(local);
			if (start < end)
				return m >= start && m < end;
			if (start > end)
				return m >= start || m < end;
			return true;
		}

		private static int LocalMinute(DateTime t)
		{
			return t.Hour * 60 + t.Minute;
		}

		private static DateTime WeekStart(DateTime d)
		{
			return d.Date.AddDays(-(((int)d.DayOfWeek + 6) % 7));
		}

		private static void ParseSession(string s, out int start, out int end)
		{
			start = 570;
			end = 960;
			if (string.IsNullOrEmpty(s))
				return;
			string[] p = s.Trim().Split('-');
			int a, b;
			if (p.Length == 2 && p[0].Length == 4 && p[1].Length == 4 && int.TryParse(p[0], out a) && int.TryParse(p[1], out b))
			{
				start = a / 100 * 60 + a % 100;
				end = b / 100 * 60 + b % 100;
			}
		}

		private DateTime ToTz(DateTime t, TimeZoneInfo tz)
		{
			if (tz == null)
				return t;
			try { return TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(t, DateTimeKind.Unspecified), NinjaTrader.Core.Globals.GeneralOptions.TimeZoneInfo, tz); }
			catch { return t; }
		}

		private static TimeZoneInfo FindTz(string id)
		{
			try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
			catch { return null; }
		}

		private static string TzId(StObTimeZone z)
		{
			switch (z)
			{
				case StObTimeZone.Chicago:    return "Central Standard Time";
				case StObTimeZone.LosAngeles: return "Pacific Standard Time";
				case StObTimeZone.SaoPaulo:   return "E. South America Standard Time";
				case StObTimeZone.London:     return "GMT Standard Time";
				case StObTimeZone.Berlin:     return "W. Europe Standard Time";
				case StObTimeZone.Tokyo:      return "Tokyo Standard Time";
				case StObTimeZone.HongKong:   return "China Standard Time";
				case StObTimeZone.Singapore:  return "Singapore Standard Time";
				case StObTimeZone.Sydney:     return "AUS Eastern Standard Time";
				case StObTimeZone.UTC:        return "UTC";
				default:                      return "Eastern Standard Time";
			}
		}

		private void DoAlert(string id, string message)
		{
			if (EnableAlerts && State == State.Realtime)
				Alert(id + CurrentBar, Priority.Medium, message, NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert1.wav", 0, Brushes.Black, Brushes.Yellow);
		}

		private string Px(double price)
		{
			return double.IsNaN(price) ? "n/a" : Instrument.MasterInstrument.FormatPrice(Instrument.MasterInstrument.RoundToTickSize(price));
		}

		private static string Num(double v, string fmt)
		{
			return v.ToString(fmt, CultureInfo.InvariantCulture);
		}

		private string Ticker()
		{
			return Instrument.MasterInstrument.Name;
		}

		private string Period()
		{
			switch (BarsPeriod.BarsPeriodType)
			{
				case BarsPeriodType.Minute: return BarsPeriod.Value.ToString();
				case BarsPeriodType.Second: return BarsPeriod.Value + "S";
				case BarsPeriodType.Tick:   return BarsPeriod.Value + "T";
				case BarsPeriodType.Day:    return BarsPeriod.Value + "D";
				case BarsPeriodType.Week:   return BarsPeriod.Value + "W";
				case BarsPeriodType.Month:  return BarsPeriod.Value + "M";
				default:                    return BarsPeriod.ToString();
			}
		}

		// Pine transparency t (0 opaque .. 100 invisible) is opacity 100 - t here
		private static Color WithAlpha(Color c, int opacityPct)
		{
			return Color.FromArgb((byte)Math.Round(255.0 * Math.Max(0, Math.Min(100, opacityPct)) / 100.0), c.R, c.G, c.B);
		}

		private static Color BrushColor(Brush b, Color fallback)
		{
			SolidColorBrush s = b as SolidColorBrush;
			return s != null ? s.Color : fallback;
		}

		private static SolidColorBrush Frozen(Color c)
		{
			SolidColorBrush b = new SolidColorBrush(c);
			b.Freeze();
			return b;
		}

		// Pine keeps the newest max_*_count drawings of each kind and deletes the oldest
		private StBox AddBox(StBox b)
		{
			boxes.Add(b);
			if (boxes.Count > MaxDrawings)
				boxes.RemoveAt(0);
			return b;
		}

		private StLine AddLine(StLine l)
		{
			lines.Add(l);
			if (lines.Count > MaxDrawings)
				lines.RemoveAt(0);
			return l;
		}

		private StLabel AddLabel(StLabel l)
		{
			labels.Add(l);
			if (labels.Count > MaxDrawings)
				labels.RemoveAt(0);
			return l;
		}
		#endregion

		#region Rendering
		private void OnPanelMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
		{
			if (ChartControl == null || ChartPanel == null)
				return;
			System.Windows.Point p = e.GetPosition(ChartPanel as System.Windows.IInputElement);
			mouseX = ChartingExtensions.ConvertToHorizontalPixels(p.X, ChartControl.PresentationSource);
			mouseY = ChartingExtensions.ConvertToVerticalPixels(p.Y, ChartControl.PresentationSource);
			string tip = null;
			foreach (KeyValuePair<SharpDX.RectangleF, string> r in tipRects)
				if (r.Key.Contains(mouseX, mouseY))
					tip = r.Value;
			if (tip != hoverTip || tip != null)   // a shown tooltip follows the mouse
			{
				hoverTip = tip;
				ForceRefresh();
			}
		}

		private SharpDX.Direct2D1.SolidColorBrush Dx(Color c)
		{
			uint k = ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
			SharpDX.Direct2D1.SolidColorBrush b;
			if (!dxCache.TryGetValue(k, out b))
			{
				b = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f));
				dxCache[k] = b;
			}
			return b;
		}

		private void DisposeDx()
		{
			foreach (SharpDX.Direct2D1.SolidColorBrush b in dxCache.Values)
				b.Dispose();
			dxCache.Clear();
		}

		public override void OnRenderTargetChanged()
		{
			DisposeDx();
		}

		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			if (ChartBars == null || RenderTarget == null)
				return;
			int from = ChartBars.FromIndex, to = ChartBars.ToIndex;
			SharpDX.Direct2D1.AntialiasMode aa = RenderTarget.AntialiasMode;

			// VWAP band fills behind the lines
			if (VwapFill && ShowVwap)
			{
				RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.PerPrimitive;
				int op = 100 - VwapFillTransp;
				if (VwapBand1)
					FillBand(chartControl, chartScale, from, to, 3, 4, WithAlpha(BrushColor(Plots[3].Brush, Colors.Teal), op));
				if (VwapBand2)
				{
					int inner = VwapBand1 ? 3 : 2, innerL = VwapBand1 ? 4 : 2;
					Color c = WithAlpha(BrushColor(Plots[5].Brush, Colors.Olive), op);
					FillBand(chartControl, chartScale, from, to, 5, inner, c);
					FillBand(chartControl, chartScale, from, to, innerL, 6, c);
				}
				if (VwapBand3)
				{
					int inner = VwapBand2 ? 5 : VwapBand1 ? 3 : 2, innerL = VwapBand2 ? 6 : VwapBand1 ? 4 : 2;
					Color c = WithAlpha(BrushColor(Plots[7].Brush, Colors.Teal), op);
					FillBand(chartControl, chartScale, from, to, 7, inner, c);
					FillBand(chartControl, chartScale, from, to, innerL, 8, c);
				}
				RenderTarget.AntialiasMode = aa;
			}

			base.OnRender(chartControl, chartScale);

			using (SharpDX.Direct2D1.StrokeStyle dash = new SharpDX.Direct2D1.StrokeStyle(NinjaTrader.Core.Globals.D2DFactory, new SharpDX.Direct2D1.StrokeStyleProperties { DashStyle = SharpDX.Direct2D1.DashStyle.Dash }))
			using (SharpDX.Direct2D1.StrokeStyle dot = new SharpDX.Direct2D1.StrokeStyle(NinjaTrader.Core.Globals.D2DFactory, new SharpDX.Direct2D1.StrokeStyleProperties { DashStyle = SharpDX.Direct2D1.DashStyle.Dot }))
			using (SharpDX.DirectWrite.TextFormat tiny = new SharpDX.DirectWrite.TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, "Arial", SharpDX.DirectWrite.FontWeight.Normal, SharpDX.DirectWrite.FontStyle.Normal, 10f))
			using (SharpDX.DirectWrite.TextFormat small = new SharpDX.DirectWrite.TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, "Arial", SharpDX.DirectWrite.FontWeight.Normal, SharpDX.DirectWrite.FontStyle.Normal, 12f))
			{
				// Boxes: style colors are read here, on the UI thread that owns the brushes; mitigated blocks use the fixed faded colors
				Color bullFill = WithAlpha(BrushColor(BullBlockColor, BullColor), BlockFillOpacity), bearFill = WithAlpha(BrushColor(BearBlockColor, BearColor), BlockFillOpacity);
				Color bullBorder = BrushColor(BullBorderColor, BullColor), bearBorder = BrushColor(BearBorderColor, BearColor);
				foreach (StBox b in boxes)
				{
					if (b.Right < from - 1 || b.Left > to + 1)
						continue;
					float x0 = chartControl.GetXByBarIndex(ChartBars, b.Left), x1 = chartControl.GetXByBarIndex(ChartBars, b.Right);
					float y0 = chartScale.GetYByValue(b.Top), y1 = chartScale.GetYByValue(b.Bottom);
					SharpDX.RectangleF r = new SharpDX.RectangleF(x0, y0, Math.Max(x1 - x0, 1f), Math.Max(y1 - y0, 1f));
					Color fill = b.Dotted ? b.Fill : b.Dir == 1 ? bullFill : bearFill, border = b.Dotted ? b.Border : b.Dir == 1 ? bullBorder : bearBorder;
					RenderTarget.FillRectangle(r, Dx(fill));
					if (b.Dotted)
						RenderTarget.DrawRectangle(r, Dx(border), 1f, dot);
					else
						RenderTarget.DrawRectangle(r, Dx(border), 1f);
				}

				// Lines
				RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.PerPrimitive;
				foreach (StLine l in lines)
				{
					if (Math.Max(l.X1, l.X2) < from - 1 || Math.Min(l.X1, l.X2) > to + 1)
						continue;
					SharpDX.Vector2 a = new SharpDX.Vector2(chartControl.GetXByBarIndex(ChartBars, l.X1), chartScale.GetYByValue(l.Y1));
					SharpDX.Vector2 z = new SharpDX.Vector2(chartControl.GetXByBarIndex(ChartBars, l.X2), chartScale.GetYByValue(l.Y2));
					if (l.Style == 1)
						RenderTarget.DrawLine(a, z, Dx(l.Col), l.Width, dash);
					else if (l.Style == 2)
						RenderTarget.DrawLine(a, z, Dx(l.Col), l.Width, dot);
					else
						RenderTarget.DrawLine(a, z, Dx(l.Col), l.Width);
				}

				// Labels
				tipRects.Clear();
				foreach (StLabel lb in labels)
				{
					if (lb.X < from - 1 || lb.X > to + 1)
						continue;
					DrawLabel(chartControl, chartScale, lb, lb.Small ? small : tiny);
				}
				RenderTarget.AntialiasMode = aa;

				if (ShowTrades && ShowStats)
					DrawStats(small);

				if (hoverTip != null)
					DrawTooltip(hoverTip, small);
			}
		}

		// Fills between two plots over contiguous runs of valid values, breaking at VWAP resets
		private void FillBand(ChartControl cc, ChartScale cs, int from, int to, int topPlot, int botPlot, Color c)
		{
			int i = Math.Max(from - 1, 0);
			int last = Math.Min(to + 1, CurrentBars[0]);
			List<SharpDX.Vector2> up = new List<SharpDX.Vector2>(), dn = new List<SharpDX.Vector2>();
			while (i <= last + 1)
			{
				bool ok = i <= last && Values[topPlot].IsValidDataPointAt(i) && Values[botPlot].IsValidDataPointAt(i);
				bool brk = !ok || (up.Count > 0 && vwapResetS.GetValueAt(i));
				if (brk && up.Count > 0)
				{
					FillRun(up, dn, c);
					up.Clear();
					dn.Clear();
				}
				if (ok)
				{
					float x = cc.GetXByBarIndex(ChartBars, i);
					up.Add(new SharpDX.Vector2(x, cs.GetYByValue(Values[topPlot].GetValueAt(i))));
					dn.Add(new SharpDX.Vector2(x, cs.GetYByValue(Values[botPlot].GetValueAt(i))));
				}
				i++;
			}
		}

		private void FillRun(List<SharpDX.Vector2> up, List<SharpDX.Vector2> dn, Color c)
		{
			if (up.Count < 2)
				return;
			using (SharpDX.Direct2D1.PathGeometry g = new SharpDX.Direct2D1.PathGeometry(NinjaTrader.Core.Globals.D2DFactory))
			{
				using (SharpDX.Direct2D1.GeometrySink s = g.Open())
				{
					s.BeginFigure(up[0], SharpDX.Direct2D1.FigureBegin.Filled);
					for (int k = 1; k < up.Count; k++)
						s.AddLine(up[k]);
					for (int k = dn.Count - 1; k >= 0; k--)
						s.AddLine(dn[k]);
					s.EndFigure(SharpDX.Direct2D1.FigureEnd.Closed);
					s.Close();
				}
				RenderTarget.FillGeometry(g, Dx(c));
			}
		}

		private void DrawLabel(ChartControl cc, ChartScale cs, StLabel lb, SharpDX.DirectWrite.TextFormat fmt)
		{
			float x = cc.GetXByBarIndex(ChartBars, lb.X);
			float y;
			if (lb.YLoc == 1)
				y = cs.GetYByValue(ChartBars.Bars.GetLow(Math.Min(lb.X, ChartBars.Bars.Count - 1))) + 3f;
			else if (lb.YLoc == 2)
				y = cs.GetYByValue(ChartBars.Bars.GetHigh(Math.Min(lb.X, ChartBars.Bars.Count - 1))) - 3f;
			else
				y = cs.GetYByValue(lb.Y);

			using (SharpDX.DirectWrite.TextLayout tl = new SharpDX.DirectWrite.TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, lb.Text, fmt, 400f, 100f))
			{
				float w = tl.Metrics.Width + 8f, h = tl.Metrics.Height + 4f, ptr = 5f;
				float bx, by;
				if (lb.Style == 0)
				{
					bx = x - w / 2;
					by = y + ptr;
				}
				else if (lb.Style == 1)
				{
					bx = x - w / 2;
					by = y - ptr - h;
				}
				else
				{
					bx = x + ptr;
					by = y - h / 2;
				}
				SharpDX.RectangleF r = new SharpDX.RectangleF(bx, by, w, h);
				if (lb.Bg.A > 0)
				{
					SharpDX.Direct2D1.SolidColorBrush bg = Dx(lb.Bg);
					RenderTarget.FillRoundedRectangle(new SharpDX.Direct2D1.RoundedRectangle { Rect = r, RadiusX = 2f, RadiusY = 2f }, bg);
					using (SharpDX.Direct2D1.PathGeometry g = new SharpDX.Direct2D1.PathGeometry(NinjaTrader.Core.Globals.D2DFactory))
					{
						using (SharpDX.Direct2D1.GeometrySink s = g.Open())
						{
							if (lb.Style == 0)
							{
								s.BeginFigure(new SharpDX.Vector2(x, y), SharpDX.Direct2D1.FigureBegin.Filled);
								s.AddLine(new SharpDX.Vector2(x + ptr, by + 0.5f));
								s.AddLine(new SharpDX.Vector2(x - ptr, by + 0.5f));
							}
							else if (lb.Style == 1)
							{
								s.BeginFigure(new SharpDX.Vector2(x, y), SharpDX.Direct2D1.FigureBegin.Filled);
								s.AddLine(new SharpDX.Vector2(x + ptr, by + h - 0.5f));
								s.AddLine(new SharpDX.Vector2(x - ptr, by + h - 0.5f));
							}
							else
							{
								s.BeginFigure(new SharpDX.Vector2(x, y), SharpDX.Direct2D1.FigureBegin.Filled);
								s.AddLine(new SharpDX.Vector2(bx + 0.5f, y - ptr));
								s.AddLine(new SharpDX.Vector2(bx + 0.5f, y + ptr));
							}
							s.EndFigure(SharpDX.Direct2D1.FigureEnd.Closed);
							s.Close();
						}
						RenderTarget.FillGeometry(g, bg);
					}
				}
				RenderTarget.DrawTextLayout(new SharpDX.Vector2(bx + 4f, by + 2f), tl, Dx(lb.Fg));
				if (lb.Tip != null)
					tipRects.Add(new KeyValuePair<SharpDX.RectangleF, string>(r, lb.Tip));
			}
		}

		// Dashboard: signal counter and trade results (Pine table at the bottom right)
		private void DrawStats(SharpDX.DirectWrite.TextFormat fmt)
		{
			Color hdr = Color.FromRgb(0x80, 0x80, 0x80), val = Color.FromRgb(0xDB, 0xDB, 0xDB);
			bool hpOn = HpTrend || HpDisp;
			string title = "Signals · " + (TradeEntry == StObTradeEntry.ChochPlusBlock ? "CHoCH + block" : "Block rejection") + (hpOn ? " · HP filter" : "");
			string[,] txt =
			{
				{ "BUY", "SELL", "Total" },
				{ buyCount.ToString(), sellCount.ToString(), (buyCount + sellCount).ToString() },
				{ "Closed", "Win %", "Net R" },
				{ tradesN + (trd == null ? "" : trd.Dir == 1 ? " + LONG" : " + SHORT"), tradesN > 0 ? Num(100.0 * tradesWon / tradesN, "0.#") + "%" : "—", Num(tradesR, "+0.#;-0.#") },
				{ "TP", "BE", "SL" },
				{ exitsTp.ToString(), exitsBe.ToString(), exitsSl.ToString() }
			};
			Color[,] col =
			{
				{ hdr, hdr, hdr },
				{ BullColor, BearColor, val },
				{ hdr, hdr, hdr },
				{ val, val, tradesR >= 0 ? BullColor : BearColor },
				{ hdr, hdr, hdr },
				{ BullColor, val, BearColor }
			};

			float pad = 6f, cellH = fmt.FontSize + 8f;
			float[] colW = new float[3];
			for (int c = 0; c < 3; c++)
				for (int r = 0; r < 6; r++)
					colW[c] = Math.Max(colW[c], Measure(txt[r, c], fmt) + 2 * pad);
			float titleW = Measure(title, fmt) + 2 * pad;
			float totalW = colW[0] + colW[1] + colW[2];
			if (titleW > totalW)
			{
				float extra = (titleW - totalW) / 3;
				for (int c = 0; c < 3; c++)
					colW[c] += extra;
				totalW = titleW;
			}
			float totalH = cellH * 7;
			float x0 = ChartPanel.X + ChartPanel.W - totalW - 10f, y0 = ChartPanel.Y + ChartPanel.H - totalH - 10f;
			SharpDX.RectangleF frame = new SharpDX.RectangleF(x0, y0, totalW, totalH);
			RenderTarget.FillRectangle(frame, Dx(Color.FromRgb(0x16, 0x16, 0x16)));
			RenderTarget.DrawRectangle(frame, Dx(Color.FromRgb(0x2E, 0x2E, 0x2E)), 1f);

			fmt.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
			fmt.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center;
			RenderTarget.DrawText(title, fmt, new SharpDX.RectangleF(x0, y0, totalW, cellH), Dx(val));
			for (int r = 0; r < 6; r++)
			{
				float cx = x0;
				for (int c = 0; c < 3; c++)
				{
					RenderTarget.DrawText(txt[r, c], fmt, new SharpDX.RectangleF(cx, y0 + cellH * (r + 1), colW[c], cellH), Dx(col[r, c]));
					cx += colW[c];
				}
			}
			fmt.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
			fmt.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Near;
		}

		private void DrawTooltip(string tip, SharpDX.DirectWrite.TextFormat fmt)
		{
			using (SharpDX.DirectWrite.TextLayout tl = new SharpDX.DirectWrite.TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, tip, fmt, 600f, 400f))
			{
				float w = tl.Metrics.Width + 12f, h = tl.Metrics.Height + 10f;
				float x = Math.Min(mouseX + 14f, ChartPanel.X + ChartPanel.W - w - 4f), y = Math.Min(mouseY + 14f, ChartPanel.Y + ChartPanel.H - h - 4f);
				SharpDX.RectangleF r = new SharpDX.RectangleF(x, y, w, h);
				RenderTarget.FillRectangle(r, Dx(Color.FromArgb(240, 0x1E, 0x22, 0x2D)));
				RenderTarget.DrawRectangle(r, Dx(Color.FromRgb(0x50, 0x53, 0x5E)), 1f);
				RenderTarget.DrawTextLayout(new SharpDX.Vector2(x + 6f, y + 5f), tl, Dx(Colors.White));
			}
		}

		private static float Measure(string s, SharpDX.DirectWrite.TextFormat fmt)
		{
			using (SharpDX.DirectWrite.TextLayout tl = new SharpDX.DirectWrite.TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, s, fmt, 1000f, 100f))
				return tl.Metrics.Width;
		}
		#endregion

		#region Properties
		[Range(1, 20)]
		[Display(Name = "Structure pivot bars", Description = "Bars on each side required to confirm a swing high or low for market structure.", Order = 1, GroupName = "1. Order Blocks")]
		public int PivotLength { get; set; }

		[Range(1, 30)]
		[Display(Name = "Search before the leg origin", Description = "The order block is searched from the CHoCH back to the origin of the break leg (the lowest low since the broken swing high for bullish blocks, the highest high since the broken swing low for bearish ones) plus this many candles before that origin. The lowest bearish candle is selected for bullish blocks and the highest bullish candle for bearish blocks.", Order = 2, GroupName = "1. Order Blocks")]
		public int ObSearchBars { get; set; }

		[Range(10, 300)]
		[Display(Name = "Maximum CHoCH leg length", Description = "Longest leg, in bars from the broken swing to the CHoCH, scanned for the origin and the order block.", Order = 3, GroupName = "1. Order Blocks")]
		public int MaxLegBars { get; set; }

		[Display(Name = "Require displacement (FVG in the leg)", Description = "Creates a block only when the leg from its origin to the CHoCH left at least one fair value gap in the direction of the break.", Order = 4, GroupName = "1. Order Blocks")]
		public bool RequireDisplacement { get; set; }

		[Range(1, 100)]
		[Display(Name = "Maximum reaction window", Description = "Signals are allowed only this many bars after a CHoCH order block forms. Blocks remain drawn afterward as historical context. On 1-minute charts 15–30 gives retracements time to arrive.", Order = 5, GroupName = "1. Order Blocks")]
		public int MaxReactionBars { get; set; }

		[Display(Name = "Zone interaction", Description = "Touch: the wick enters the block and the candle closes back out of it.\nMidpoint: the wick reaches the block's 50% and the candle closes back out of it.\nSweep (original): the wick runs through the whole block and the candle closes back inside or beyond it.\nA candle that engulfs the whole block from the far side always qualifies.", Order = 6, GroupName = "1. Order Blocks")]
		public StObTouchMode TouchMode { get; set; }

		[Range(0.0, 100.0)]
		[Display(Name = "Minimum signal body (% of candle range)", Description = "The signal candle body must use at least this percentage of its full high-low range. 0 = off.", Order = 7, GroupName = "1. Order Blocks")]
		public double MinBodyPercent { get; set; }

		[Range(1, 200)]
		[Display(Name = "Tracked blocks per direction", Description = "Maximum number of unmitigated bullish and bearish blocks tracked for mitigation. The oldest stops being tracked when this limit is reached; its drawing stays.", Order = 8, GroupName = "1. Order Blocks")]
		public int MaxBlocksPerSide { get; set; }

		[Display(Name = "Show CHoCH levels", Description = "Draws a dashed line from the broken confirmed swing level and marks each change of character.", Order = 1, GroupName = "2. Market Structure")]
		public bool ShowChoch { get; set; }

		[Display(Name = "Only signal with current structure", Description = "Allows bullish rejection signals only after bullish CHoCH and bearish rejection signals only after bearish CHoCH.", Order = 2, GroupName = "2. Market Structure")]
		public bool FilterSignalsByTrend { get; set; }

		[Display(Name = "Require block inside broken swing range", Description = "Rejects bullish source candles fully above the broken swing high and bearish source candles fully below the broken swing low.", Order = 3, GroupName = "2. Market Structure")]
		public bool RequireSwingRange { get; set; }

		[Display(Name = "Avoid signals on fresh swing extremes", Description = "Suppresses short signals making a fresh local low and long signals making a fresh local high, avoiding entries at the end of an extension.", Order = 4, GroupName = "2. Market Structure")]
		public bool AvoidFreshExtremeSignals { get; set; }

		[Display(Name = "Require significant pivot-to-block range", Description = "Allows signals only when the latest confirmed swing pivot is sufficiently far from the order block, filtering narrow sideways structures.", Order = 1, GroupName = "3. Range Filter")]
		public bool FilterChopSignals { get; set; }

		[Range(1, 100)]
		[Display(Name = "Range ATR length", Description = "ATR period used to normalize pivot-to-block distance across symbols and timeframes.", Order = 2, GroupName = "3. Range Filter")]
		public int RangeAtrLength { get; set; }

		[Range(0.1, 10.0)]
		[Display(Name = "Minimum pivot-to-block distance (ATR)", Description = "The latest confirmed pivot must be at least this many ATRs away from the order block before a rejection signal is allowed.", Order = 3, GroupName = "3. Range Filter")]
		public double MinPivotRangeAtr { get; set; }

		[Display(Name = "Signals only in session", Description = "Intraday charts only. Blocks keep forming around the clock; rejection signals print only inside this New York session. CHoCH + block trades are not filtered.", Order = 1, GroupName = "4. Session")]
		public bool UseSession { get; set; }

		[Display(Name = "Session (HHMM-HHMM, New York)", Order = 2, GroupName = "4. Session")]
		public string SessionText { get; set; }

		[Display(Name = "Suppress overlapping blocks", Description = "Avoids adding a block when it substantially overlaps an active block (still inside its reaction window) of either direction.", Order = 1, GroupName = "5. Cleanup")]
		public bool RemoveOverlapping { get; set; }

		[Range(1.0, 100.0)]
		[Display(Name = "Overlap threshold (%)", Description = "A new block is skipped when its overlap with an active block covers at least this percentage of the smaller zone.", Order = 2, GroupName = "5. Cleanup")]
		public double OverlapThreshold { get; set; }

		[Range(50, 100000)]
		[Display(Name = "Max boxes / lines / labels", Description = "Newest drawings kept of each kind; the oldest is deleted beyond this. TradingView limit of the original script: 500.", Order = 3, GroupName = "5. Cleanup")]
		public int MaxDrawings { get; set; }

		[Display(Name = "Project stop and targets of the latest signal", Description = "Fixed-length projection of the latest signal. The trades draw their own levels until they close.", Order = 1, GroupName = "6. Risk")]
		public bool ShowRisk { get; set; }

		[Range(0, 100)]
		[Display(Name = "Stop buffer (ticks)", Description = "Stop goes this many ticks beyond the signal candle's wick or the block's far edge, whichever is further.", Order = 2, GroupName = "6. Risk")]
		public int StopBufferTicks { get; set; }

		[Range(0.5, 20.0)]
		[Display(Name = "Target (R)", Description = "Trade target and second projected target, as a multiple of the risk. First projected target: the extreme the break leg reached after the CHoCH.", Order = 3, GroupName = "6. Risk")]
		public double Rr { get; set; }

		[Range(5, 200)]
		[Display(Name = "Projection length (bars)", Order = 4, GroupName = "6. Risk")]
		public int RiskBars { get; set; }

		[Display(Name = "Trade the signals", Description = "Shows BUY / SELL signals, manages each trade and counts the results. Exits are checked from the candle after the entry; when stop and target are both inside one candle the stop counts.", Order = 1, GroupName = "7. Trades")]
		public bool ShowTrades { get; set; }

		[Display(Name = "Trade entry", Description = "CHoCH + block: every CHoCH that creates an order block opens a trade in the new structure direction at the CHoCH close, stop beyond the block.\nBlock rejection: enters at the close of an OB rejection candle with its stop; one trade at a time, signals while a trade is open are only marked.", Order = 2, GroupName = "7. Trades")]
		public StObTradeEntry TradeEntry { get; set; }

		[Display(Name = "Trade target", Description = "R multiple: the Target (R) above. Leg extreme (block rejection only): the extreme the break leg reached after the CHoCH, falling back to the R multiple when the signal has none.", Order = 3, GroupName = "7. Trades")]
		public StObTargetMode TargetMode { get; set; }

		[Range(0, int.MaxValue)]
		[Display(Name = "Max bars in trade", Description = "Closes the trade at the candle close after this many bars. 0 = no time exit.", Order = 4, GroupName = "7. Trades")]
		public int MaxTradeBars { get; set; }

		[Range(0.1, 10.0)]
		[Display(Name = "TP1 (R)", Description = "First target, as a multiple of the risk. Used only when it is closer than the trade target.", Order = 5, GroupName = "7. Trades")]
		public double Tp1R { get; set; }

		[Range(0, 100)]
		[Display(Name = "TP1 partial (%)", Description = "Share of the position closed at TP1. 100 = the whole trade exits at TP1. 0 = no partial.", Order = 6, GroupName = "7. Trades")]
		public int Tp1Pct { get; set; }

		[Display(Name = "Stop to break-even after TP1", Description = "Once TP1 is reached the stop of the remaining position moves to the entry.", Order = 7, GroupName = "7. Trades")]
		public bool BeAfterTp1 { get; set; }

		[Display(Name = "Exit on a new CHoCH + block", Description = "CHoCH + block mode: a new CHoCH that creates a block closes the open trade at its close, and a new trade opens when it passes the filters. Off: the trade runs to its stop, break-even or target.", Order = 8, GroupName = "7. Trades")]
		public bool ExitOnChoch { get; set; }

		[Display(Name = "Trades only in session", Description = "Opens trades only inside the Session input (New York time). Off: trades around the clock.", Order = 9, GroupName = "7. Trades")]
		public bool TradeSession { get; set; }

		[Display(Name = "Show trade statistics", Order = 10, GroupName = "7. Trades")]
		public bool ShowStats { get; set; }

		[Display(Name = "EMA trend filter", Description = "Trades only in the direction both EMAs are rising (longs) or falling (shorts): the fast EMA against its value N bars ago and the slow EMA against its value M bars ago.", Order = 1, GroupName = "8. High-Probability Filter")]
		public bool HpTrend { get; set; }

		[Range(2, int.MaxValue)]
		[Display(Name = "Fast EMA", Order = 2, GroupName = "8. High-Probability Filter")]
		public int HpFastLen { get; set; }

		[Range(1, int.MaxValue)]
		[Display(Name = "Fast EMA slope bars", Order = 3, GroupName = "8. High-Probability Filter")]
		public int HpFastSlope { get; set; }

		[Range(2, int.MaxValue)]
		[Display(Name = "Slow EMA", Order = 4, GroupName = "8. High-Probability Filter")]
		public int HpSlowLen { get; set; }

		[Range(1, int.MaxValue)]
		[Display(Name = "Slow EMA slope bars", Order = 5, GroupName = "8. High-Probability Filter")]
		public int HpSlowSlope { get; set; }

		[Display(Name = "Minimum leg displacement", Description = "Trades only blocks whose break leg ran at least this many ATR(14) from its origin (lowest low / highest high) to the CHoCH close.", Order = 6, GroupName = "8. High-Probability Filter")]
		public bool HpDisp { get; set; }

		[Range(0.0, 20.0)]
		[Display(Name = "Minimum leg displacement (ATR)", Order = 7, GroupName = "8. High-Probability Filter")]
		public double HpMinDisp { get; set; }

		[Display(Name = "Show trend EMAs", Description = "Colors and widths in the plots \"Trend EMA fast\" and \"Trend EMA slow\".", Order = 8, GroupName = "8. High-Probability Filter")]
		public bool HpShowEma { get; set; }

		[XmlIgnore]
		[Display(Name = "Bullish block fill", Order = 1, GroupName = "9. Style")]
		public Brush BullBlockColor { get; set; }

		[Browsable(false)]
		public string BullBlockColorSerializable
		{
			get { return Serialize.BrushToString(BullBlockColor); }
			set { BullBlockColor = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Bearish block fill", Order = 2, GroupName = "9. Style")]
		public Brush BearBlockColor { get; set; }

		[Browsable(false)]
		public string BearBlockColorSerializable
		{
			get { return Serialize.BrushToString(BearBlockColor); }
			set { BearBlockColor = Serialize.StringToBrush(value); }
		}

		[Range(0, 100)]
		[Display(Name = "Block fill opacity (%)", Description = "TradingView default: 84 % transparency = 16 % opacity.", Order = 3, GroupName = "9. Style")]
		public int BlockFillOpacity { get; set; }

		[XmlIgnore]
		[Display(Name = "Bullish block border", Order = 4, GroupName = "9. Style")]
		public Brush BullBorderColor { get; set; }

		[Browsable(false)]
		public string BullBorderColorSerializable
		{
			get { return Serialize.BrushToString(BullBorderColor); }
			set { BullBorderColor = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Bearish block border", Order = 5, GroupName = "9. Style")]
		public Brush BearBorderColor { get; set; }

		[Browsable(false)]
		public string BearBorderColorSerializable
		{
			get { return Serialize.BrushToString(BearBorderColor); }
			set { BearBorderColor = Serialize.StringToBrush(value); }
		}

		[Display(Name = "Show VWAP", Description = "Colors and widths of the VWAP and its bands in the Plots section.", Order = 1, GroupName = "10. VWAP")]
		public bool ShowVwap { get; set; }

		[Display(Name = "Source", Order = 2, GroupName = "10. VWAP")]
		public StObSource VwapSource { get; set; }

		[Display(Name = "Reset at", Description = "Sessions: the VWAP and its bands restart on the first candle of the session and keep running until the next one opens.\nNew York 09:30-16:00 NY · London 08:00-16:30 London · Tokyo 09:00-15:30 Tokyo · Sydney 10:00-16:00 Sydney · CME Globex 18:00-17:00 NY.\nDay / Week / Month: restart with each new trading day, week or month.\nCustom session: the session and timezone below.", Order = 3, GroupName = "10. VWAP")]
		public StObVwapAnchor VwapAnchor { get; set; }

		[Display(Name = "Custom session (HHMM-HHMM)", Order = 4, GroupName = "10. VWAP")]
		public string VwapCustomSession { get; set; }

		[Display(Name = "Custom session timezone", Order = 5, GroupName = "10. VWAP")]
		public StObTimeZone VwapCustomTz { get; set; }

		[Display(Name = "Show only during the session", Description = "Hides the VWAP outside the selected session. Ignored for Day / Week / Month.", Order = 6, GroupName = "10. VWAP")]
		public bool VwapSessionOnly { get; set; }

		[Display(Name = "Bands", Description = "Standard deviation: VWAP ± multiplier × the volume-weighted standard deviation since the reset. Percentage: VWAP ± multiplier % of the VWAP.", Order = 7, GroupName = "10. VWAP")]
		public StObVwapBands VwapBandMode { get; set; }

		[Display(Name = "Band 1", Order = 8, GroupName = "10. VWAP")]
		public bool VwapBand1 { get; set; }

		[Range(0.0, double.MaxValue)]
		[Display(Name = "Band 1 ×", Order = 9, GroupName = "10. VWAP")]
		public double VwapMult1 { get; set; }

		[Display(Name = "Band 2", Order = 10, GroupName = "10. VWAP")]
		public bool VwapBand2 { get; set; }

		[Range(0.0, double.MaxValue)]
		[Display(Name = "Band 2 ×", Order = 11, GroupName = "10. VWAP")]
		public double VwapMult2 { get; set; }

		[Display(Name = "Band 3", Order = 12, GroupName = "10. VWAP")]
		public bool VwapBand3 { get; set; }

		[Range(0.0, double.MaxValue)]
		[Display(Name = "Band 3 ×", Order = 13, GroupName = "10. VWAP")]
		public double VwapMult3 { get; set; }

		[Display(Name = "Fill bands", Order = 14, GroupName = "10. VWAP")]
		public bool VwapFill { get; set; }

		[Range(0, 100)]
		[Display(Name = "Fill transparency", Order = 15, GroupName = "10. VWAP")]
		public int VwapFillTransp { get; set; }

		[Display(Name = "Alerts", Description = "Real-time alerts (Alerts log + sound) for rejection signals, trade entries, TP1 and trade exits: the alert() messages of the Pine script.", Order = 1, GroupName = "11. Alerts")]
		public bool EnableAlerts { get; set; }

		// Outputs for strategies: 1 bullish / -1 bearish rejection signal; 1 BUY / -1 SELL trade entry
		[Browsable(false)]
		[XmlIgnore]
		public Series<int> RejectionSignal
		{
			get { Update(); return rejectionSignal; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<int> TradeSignal
		{
			get { Update(); return tradeSignal; }
		}
		#endregion
	}
}
