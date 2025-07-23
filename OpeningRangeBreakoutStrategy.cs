#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class OpeningRangeBreakoutStrategy : Strategy
    {
        private double rangeHigh;
        private double rangeLow;
        private bool rangeEstablished;
        private bool ordersPlaced;
        private DateTime rangeStartTime;
        private DateTime rangeEndTime;
        private DateTime orderPlacementTime;
        
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Opening Range Breakout Strategy - Places buy order above range and sell order below range with 1:1 risk/reward";
                Name = "OpeningRangeBreakoutStrategy";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsFillLimitOnTouch = false;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution = OrderFillResolution.Standard;
                Slippage = 0;
                StartBehavior = StartBehavior.WaitUntilFlat;
                TimeInForce = TimeInForce.Gtc;
                TraceOrders = false;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 1;
                IsInstantiatedOnEachOptimizationIteration = true;
                
                // Default parameters
                RangeStartHour = 9;
                RangeStartMinute = 30;
                RangeEndHour = 9;
                RangeEndMinute = 35;
                OrderQuantity = 1;
            }
            else if (State == State.Configure)
            {
                // Reset variables
                rangeHigh = 0;
                rangeLow = 0;
                rangeEstablished = false;
                ordersPlaced = false;
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0 || CurrentBars[0] < 1)
                return;

            DateTime currentTime = Time[0];
            
            // Set up range times for current day
            rangeStartTime = new DateTime(currentTime.Year, currentTime.Month, currentTime.Day, 
                                        RangeStartHour, RangeStartMinute, 0);
            rangeEndTime = new DateTime(currentTime.Year, currentTime.Month, currentTime.Day, 
                                      RangeEndHour, RangeEndMinute, 0);
            orderPlacementTime = rangeEndTime.AddMinutes(1);

            // Reset daily variables at start of new trading day
            if (Bars.IsFirstBarOfSession)
            {
                rangeHigh = 0;
                rangeLow = 0;
                rangeEstablished = false;
                ordersPlaced = false;
            }

            // Establish the opening range
            if (currentTime >= rangeStartTime && currentTime <= rangeEndTime && !rangeEstablished)
            {
                if (rangeHigh == 0 || High[0] > rangeHigh)
                    rangeHigh = High[0];
                    
                if (rangeLow == 0 || Low[0] < rangeLow)
                    rangeLow = Low[0];
            }
            
            // Mark range as established when range period ends
            if (currentTime > rangeEndTime && !rangeEstablished && rangeHigh > 0 && rangeLow > 0)
            {
                rangeEstablished = true;
                
                // Draw the range on chart
                Draw.Rectangle(this, "ORB_Range_" + currentTime.ToString("yyyyMMdd"), 
                             false, rangeStartTime, rangeLow, rangeEndTime, rangeHigh, 
                             Brushes.Yellow, Brushes.Transparent, 2);
                             
                Print(string.Format("Range established: High={0}, Low={1}, Width={2}", 
                                  rangeHigh, rangeLow, rangeHigh - rangeLow));
            }

            // Place orders on the next 1-minute candle after range establishment
            if (rangeEstablished && !ordersPlaced && currentTime >= orderPlacementTime)
            {
                double rangeWidth = rangeHigh - rangeLow;
                
                // Buy order: 1 tick above range high
                double buyPrice = rangeHigh + TickSize;
                double buyStopLoss = buyPrice - rangeWidth;
                double buyProfitTarget = buyPrice + rangeWidth;
                
                // Sell order: 1 tick below range low  
                double sellPrice = rangeLow - TickSize;
                double sellStopLoss = sellPrice + rangeWidth;
                double sellProfitTarget = sellPrice - rangeWidth;
                
                // Place buy stop order
                EnterLongStopMarket(OrderQuantity, buyPrice, "BuyBreakout");
                
                // Place sell stop order
                EnterShortStopMarket(OrderQuantity, sellPrice, "SellBreakout");
                
                ordersPlaced = true;
                
                Print(string.Format("Orders placed - Buy: {0} (SL: {1}, PT: {2}), Sell: {3} (SL: {4}, PT: {5})", 
                                  buyPrice, buyStopLoss, buyProfitTarget, sellPrice, sellStopLoss, sellProfitTarget));
            }
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled, double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string comment)
        {
            // Handle order updates if needed
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution.Order != null && execution.Order.OrderState == OrderState.Filled)
            {
                double rangeWidth = rangeHigh - rangeLow;
                
                if (execution.Order.Name == "BuyBreakout")
                {
                    // Set stop loss and profit target for long position
                    ExitLongStopMarket(quantity, price - rangeWidth, "BuyStopLoss", "BuyBreakout");
                    ExitLongLimit(quantity, price + rangeWidth, "BuyProfitTarget", "BuyBreakout");
                    
                    Print(string.Format("Long position filled at {0}, SL: {1}, PT: {2}", 
                                      price, price - rangeWidth, price + rangeWidth));
                }
                else if (execution.Order.Name == "SellBreakout")
                {
                    // Set stop loss and profit target for short position
                    ExitShortStopMarket(quantity, price + rangeWidth, "SellStopLoss", "SellBreakout");
                    ExitShortLimit(quantity, price - rangeWidth, "SellProfitTarget", "SellBreakout");
                    
                    Print(string.Format("Short position filled at {0}, SL: {1}, PT: {2}", 
                                      price, price + rangeWidth, price - rangeWidth));
                }
            }
        }

        #region Properties
        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "Range Start Hour", Description = "Hour when range tracking begins (24-hour format)", Order = 1, GroupName = "Range Parameters")]
        public int RangeStartHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name = "Range Start Minute", Description = "Minute when range tracking begins", Order = 2, GroupName = "Range Parameters")]
        public int RangeStartMinute { get; set; }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "Range End Hour", Description = "Hour when range tracking ends (24-hour format)", Order = 3, GroupName = "Range Parameters")]
        public int RangeEndHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name = "Range End Minute", Description = "Minute when range tracking ends", Order = 4, GroupName = "Range Parameters")]
        public int RangeEndMinute { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Order Quantity", Description = "Number of contracts/shares per order", Order = 5, GroupName = "Order Parameters")]
        public int OrderQuantity { get; set; }
        #endregion
    }
}