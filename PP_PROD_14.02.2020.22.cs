using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading;
using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;
using cAlgo.Indicators;
using System.Text;


namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.FullAccess)]
    public class TenorisMTF : Robot
    {

        [Parameter("Signal TimeFrame 1", DefaultValue = 6)]
        public TimeFrame tfSignal_1 { get; set; }

        [Parameter("Signal TimeFrame 2", DefaultValue = 7)]
        public TimeFrame tfSignal_2 { get; set; }

        [Parameter("Signal TimeFrame 3", DefaultValue = 8)]
        public TimeFrame tfSignal_3 { get; set; }

        [Parameter("Signal TimeFrame 4", DefaultValue = 9)]
        public TimeFrame tfSignal_4 { get; set; }

        // Risk Factors
        [Parameter("% Max Risk", DefaultValue = 0.1, Step = 0.05, Group = "Risk Mgmt")]
        public double Risk_Max { get; set; }

        [Parameter("% Min Risk", DefaultValue = 0.1, Step = 0.05, Group = "Risk Mgmt")]
        public double Risk_Min { get; set; }

        [Parameter("Max Loss", DefaultValue = 200, Step = 5, Group = "Risk Mgmt")]
        public double maxLoss { get; set; }

        [Parameter("ATR Periods", DefaultValue = 20, Group = "Risk Mgmt")]
        public int ATR_periods { get; set; }

        [Parameter("ATR Multiple", DefaultValue = 2.5, Group = "Risk Mgmt")]
        public double ATR_multiple { get; set; }

        [Parameter("Use Trailing Stop", DefaultValue = true, Group = "Risk Mgmt")]
        public bool UseTrailingStop { get; set; }
        // Files

        // [Parameter("Signal Export File", Group = "Admin", DefaultValue = "C:\\Users\\Hp\\Documents\\PP\\Signals\\")]
        [Parameter("Signal Export File", Group = "Admin", DefaultValue = "C:\\Users\\Hp\\Documents\\PP\\TestStage\\")]
        public string Location_SignalHistory { get; set; }

        // [Parameter("Signal Import File", Group = "Admin", DefaultValue = "C:\\Users\\hp\\AppData\\Roaming\\MetaQuotes\\Terminal\\2010C2441A263399B34F537D91A53AC9\\MQL4\\Files\\")]
        [Parameter("Signal Import File", Group = "Admin", DefaultValue = "C:\\Users\\hp\\AppData\\Roaming\\MetaQuotes\\Terminal\\2010C2441A263399B34F537D91A53AC9\\MQL4\\Files\\")]
        public string Location_SignalImport { get; set; }

        [Parameter("Signal File Prefix", Group = "Admin", DefaultValue = "")]
        public string FilePrefix { get; set; }



        //Outputs from SignalData File
        public double SL1, SL2, SL3;
        public double Entry, Entry2, Entry3;
        public double PipsSize, Volume, DD_Final;
        public string MA, MACD, MACD2, STOCH, STOCH2, Direction;
        public string FileHeader = "Date, Time, Symbol, TimeFrame, Direction, Entry, SL, SL2, SL3, TP, TP1, MA, MACD, MACD2, STOCH, STOCH2" + Environment.NewLine;

        public TimeFrame[] tfSignals;
        public string[] sTFs, CSVLines, CSVLines_prev;

        public string ColSep = ",", sDate, sTime;

        public string[] file_History;
        public string file_tfSignal;

        public string TradeSymbol;

        public TradeType TradeDirection;

        public bool NewData = true;
        private bool HasClosedHalf = false;

        Bars[] ms_tfSignals;
        AverageTrueRange[] _atrs;
        string[] Labels;
        MacdCrossOver[] macd;
        // Init variables and obj in OnStart() Function
        protected override void OnStart()
        {
            Positions.Closed += OnPositionsClosed;
            //Positions.Opened += OnPositionsOpened;

            // Init Array Variables 
            tfSignals = new TimeFrame[4];
            sTFs = new string[4];
            ms_tfSignals = new Bars[4];
            _atrs = new AverageTrueRange[4];
            Labels = new string[4];
            file_History = new string[4];
            macd = new MacdCrossOver[4];

            CSVLines = new string[4];
            CSVLines_prev = new string[4];

            // Convert input parameters to Array Variable
            tfSignals[0] = tfSignal_1;
            tfSignals[1] = tfSignal_2;
            tfSignals[2] = tfSignal_3;
            tfSignals[3] = tfSignal_4;

            // Map TimeFrames            
            for (int i = 0; i < 4; i++)
            {
                CSVLines[i] = "";
                CSVLines_prev[i] = "";
                Labels[i] = "";
                file_History[i] = "";
                sTFs[i] = mapTF(tfSignals[i]);

                // Print("->" + sTFs[i]);
                string Env = FilePrefix == "" ? "" : "_" + FilePrefix;

                file_History[i] = "History_" + Symbol.Name + "_" + sTFs[i] + Env + ".csv";
                file_tfSignal = "Object_List_" + Symbol.Name + "_" + sTFs[i] + ".csv";

                if (!File.Exists(Location_SignalHistory + file_History[i]))
                {
                    Print("Creating new history file!" + file_History[i]);
                    File.AppendAllText(Location_SignalHistory + file_History[i], FileHeader);
                    Print("History File: " + Location_SignalHistory + file_History[i]);
                }
            }
            Print(" ***** TimeFrames Mapped ***** ");

            // Market Series Loading ...
            for (int i = 0; i < 4; i++)
            {
                ms_tfSignals[i] = MarketData.GetBars(tfSignals[i]);
                Print(ms_tfSignals[i]);
            }
            Print(" ***** Market Series Loaded ***** ");


            // ATRs Loading ...
            for (int i = 0; i < 4; i++)
            {
                _atrs[i] = Indicators.AverageTrueRange(ms_tfSignals[i], ATR_periods, MovingAverageType.Exponential);
                macd[i] = Indicators.MacdCrossOver(ms_tfSignals[i].ClosePrices, 26, 12, 9);
            }
            Print(" ***** ATRs and MACDs Loaded ******");

            // Finding Labels...
            for (int i = 0; i < 4; i++)
            {
                GetLabels(i);
                Print("Labels: " + Labels[i]);
            }
            Print(" ***** Labels Found ******");

        }

        void OnPositionsOpened(PositionOpenedEventArgs obj)
        {
            // Change SL to keep contant equity at risk
            Print("Position open:" + obj.Position.Label);
            string LabeltoChange = obj.Position.Label;
            double Volume_Total = 0;
            if (FilterPositions(LabeltoChange).Count() > 0)
            {
                foreach (var pos in FilterPositions(LabeltoChange))
                {
                    Volume_Total += pos.VolumeInUnits;
                }
                double SL_new = (Account.Balance * Risk_Max / 100) / (Volume_Total * Symbol.PipValue);

                foreach (var pos in FilterPositions(LabeltoChange))
                {
                    pos.ModifyStopLossPips(SL_new);
                }
            }
        }

        void OnPositionsClosed(PositionClosedEventArgs obj)
        {
            Print("Position closed: " + obj.Reason + "-" + obj.Position.Label);
            string LabeltoClose = obj.Position.Label;
            if (obj.Reason.ToString() == "StopLoss")
            {
                CloseAllPositions(LabeltoClose);
                CancelAllOrders(LabeltoClose);
                AddHistoryData();
            }
        }

        protected override void OnBar()
        {
            Print(" ***** New One Min Bar ***** ");

            // Get current Hours and minutes            
            int currentHours = Server.Time.Hour;
            int currentMins = Server.Time.Minute;
            Print("Current Time : " + currentHours + " " + currentMins);

            for (int i = 0; i < 4; i++)
            {
                char[] charsToTrim = 
                {
                    'M',
                    'H',
                    'D'
                };
                string T_count = sTFs[i].TrimStart(charsToTrim);
                string T_string = sTFs[i].Substring(0, 1);
                Print(" ***** Check TimeFrame -> " + T_string, " ", T_count);


                int min_mod = currentMins % Convert.ToInt32(T_count);
                int hour_mod = currentHours % Convert.ToInt32(T_count);

                ExitCheck(i);
                CSVLines_prev[i] = CSVLines[i];
                LoadNewData(i);

                if (NewData)
                {
                    Print(" ***** Loading New Data ***** ");
                    CloseAllPositions(Labels[i]);
                    CancelAllOrders(Labels[i]);
                    CreateOrders(i);
                    SaveSignalDataHistory(Location_SignalHistory + file_History[i], i);
                }
                //GetMarketStats(i);
            }
        }

        protected override void OnStop()
        {
            Print("Closing up shop");
        }

        void GetLabels(int index)
        {
            string Label_to_Check_Buy = Symbol.Name + "_" + sTFs[index] + "_" + TradeType.Buy.ToString().ToUpper() + "_";
            string Label_to_Check_Sell = Symbol.Name + "_" + sTFs[index] + "_" + TradeType.Sell.ToString().ToUpper() + "_";

            foreach (Position pos in Positions)
            {
                if (pos.Label.Substring(0, Label_to_Check_Buy.Length) == Label_to_Check_Buy)
                {
                    Labels[index] = pos.Label;
                    Print("Buy Label Found: " + Labels[index]);
                    break;
                }

                if (pos.Label.Substring(0, Label_to_Check_Sell.Length) == Label_to_Check_Sell)
                {
                    Labels[index] = pos.Label;
                    Print("Sell Label Found: " + Labels[index]);
                    break;
                }
            }
        }
        void AddHistoryData()
        {
            Print("Add History Data");
            string win_loss = "";

            int history_count = History.Count;
            Print(history_count);

            HistoricalTrade trade = History[history_count - 1];
            if (trade.SymbolName == Symbol.Name)
            {
                win_loss = trade.NetProfit > 0 ? "Win" : "Loss";

                trade.Label.Split('_');

                string file_Name = "History_" + Symbol.Name + "_" + trade.Label.Split('_')[1] + "_" + "Win_Loss" + ".csv";
                string file_Content = "";
                string file_Header = "Symbol,  Label,  EntryTime,  ClosingTime,  Profit,  Result" + Environment.NewLine;

                file_Content = trade.SymbolName + " " + trade.Label + " " + trade.EntryTime + " " + trade.ClosingTime + " " + trade.NetProfit + " " + win_loss + Environment.NewLine;

                if (!File.Exists(Location_SignalHistory + file_Name))
                {
                    Print("Creating new history Trade File!" + file_Name);
                    File.AppendAllText(Location_SignalHistory + file_Name, file_Header);
                    File.AppendAllText(Location_SignalHistory + file_Name, file_Content);
                }
                else
                {
                    File.AppendAllText(Location_SignalHistory + file_Name, file_Content);
                }
            }
        }

        void LoadNewData(int index)
        {
            LoadSignalData(Symbol.Name, sTFs[index], index);

            string[] OldSignal = CSVLines_prev[index].Split(',');
            string[] NewSignal = CSVLines[index].Split(',');

            if (CSVLines_prev[index] != "")
            {
                string OldEntry = (OldSignal[4] + "  " + OldSignal[5]);
                string NewEntry = (NewSignal[4] + "  " + NewSignal[5]);

                if (OldEntry.Equals(NewEntry))
                {
                    NewData = false;
                    Print("No New Data!");
                }
                else if (!OldEntry.Equals(NewEntry))
                {
                    NewData = true;
                    Print("New Data!");
                }
            }
            else
            {
                NewData = false;
            }
        }

        // Get Market State Function 
        void GetMarketStats(int index)
        {
            Print("ATR: {0}", _atrs[index].Result.LastValue);
            Print("PnL: {0}", Symbol.UnrealizedNetProfit);
        }

        // Create Orders Function    
        void CreateOrders(int index)
        {
            string Zeroline = macd[index].MACD.LastValue > 0 ? "++" : "--";
            var Values = CSVLines[index].Split(',');
            Labels[index] = Values[2] + "_" + sTFs[index] + "_" + Direction + "_" + Server.Time.ToString("MMddHHmm");
            string Comment = Zeroline + MACD + MACD2 + STOCH + STOCH2;
            double StopLoss = ATR_periods * ATR_multiple;

            double OrderVolume_Total = ((Account.Balance * Risk_Max / 100) / TrimSL(Entry, SL1) / Symbol.PipValue);

            double OrderVolume1 = Symbol.NormalizeVolumeInUnits(OrderVolume_Total / 2);
            double OrderVolume2 = Symbol.NormalizeVolumeInUnits(OrderVolume_Total / 3);
            double OrderVolume3 = Symbol.NormalizeVolumeInUnits(OrderVolume_Total / 4);

            double OrderVolume11 = Symbol.NormalizeVolumeInUnits(OrderVolume_Total / 10);
            double OrderVolume12 = Symbol.NormalizeVolumeInUnits(OrderVolume_Total / 5);
            double OrderVolume13 = Symbol.NormalizeVolumeInUnits(OrderVolume_Total / 5);

            TradeDirection = Values[4] == "BUY" ? TradeDirection = TradeType.Buy : TradeType.Sell;

            ExecuteMarketOrder(TradeDirection, SymbolName, OrderVolume11, Labels[index], TrimSL(SL1, Entry), null, Comment + 11, UseTrailingStop);

            PlaceStopOrder(TradeDirection, SymbolName, OrderVolume12, Entry + 1 * Symbol.PipSize * TrimSL(SL1, Entry) / 3, Labels[index], TrimSL(SL1, Entry), null, null, Comment + 12, UseTrailingStop);

            PlaceStopOrder(TradeDirection, SymbolName, OrderVolume13, Entry + 2 * Symbol.PipSize * TrimSL(SL1, Entry) / 3, Labels[index], TrimSL(SL1, Entry), null, null, Comment + 13, UseTrailingStop);

            PlaceStopOrder(TradeDirection, SymbolName, OrderVolume2, Entry2, Labels[index], TrimSL(SL1, Entry), null, null, Comment + 2, UseTrailingStop);
            PlaceStopOrder(TradeDirection, SymbolName, OrderVolume3, Entry3, Labels[index], TrimSL(SL1, Entry), null, null, Comment + 3, UseTrailingStop);

            PlaceStopOrder(TradeDirection, SymbolName, OrderVolume3, Entry3 + TrimSL(SL1, Entry), Labels[index], TrimSL(SL1, Entry), null, null, Comment + 4, UseTrailingStop);
            PlaceStopOrder(TradeDirection, SymbolName, OrderVolume3, Entry3 + TrimSL(SL1, Entry) + TrimSL(SL1, Entry), Labels[index], TrimSL(SL1, Entry), null, null, Comment + 5, UseTrailingStop);

        }

        // Exit Check Function
        void ExitCheck(int index)
        {
            if (Symbol.UnrealizedNetProfit < -1 * maxLoss)
            {
                Print(Labels[index] + " has PnL Loss of more than MaxLoss.");
                CloseAllPositions(Labels[index]);
                CancelAllOrders(Labels[index]);
                return;
            }
            //Look at CLosing Half Positions
            if (FilterPositions(Labels[index]).Count() > 0)
            {
                string[] temp_Label = Labels[index].Split('_');
                Print("Exit Check Dir: " + temp_Label[2]);
                if (temp_Label[2].ToUpper() == "BUY" && ms_tfSignals[index].LowPrices.Last(1) < ms_tfSignals[index].LowPrices.Last(2))
                {
                    if (ms_tfSignals[index].LowPrices.Last(2) < ms_tfSignals[index].LowPrices.Last(3))
                    {
                        Print(Labels[index] + " Long positions have breached 2 consecutive lower lows");
                        CloseHalfPositions(index);
                        HasClosedHalf = true;
                    }
                }
                if (temp_Label[2].ToUpper() == "SELL" && ms_tfSignals[index].HighPrices.Last(1) < ms_tfSignals[index].HighPrices.Last(2))
                {
                    if (ms_tfSignals[index].HighPrices.Last(2) < ms_tfSignals[index].HighPrices.Last(3))
                    {
                        Print(Labels[index] + " Short positions have breached 2 consecutive higher highs");
                        CloseHalfPositions(index);
                        HasClosedHalf = true;
                    }
                }
            }


            //CLosing Remaining Positions
            if (FilterPositions(Labels[index]).Count() > 0 && HasClosedHalf)
            {
                string[] temp_Label = Labels[index].Split('_');
                Print("Exit Check Dir: " + temp_Label[2]);
                if (temp_Label[2].ToUpper() == "BUY" && ms_tfSignals[index].LowPrices.Last(1) < ms_tfSignals[index].LowPrices.Last(2))
                {
                    if (ms_tfSignals[index].LowPrices.Last(2) < ms_tfSignals[index].LowPrices.Last(3) && ms_tfSignals[index].LowPrices.Last(3) < ms_tfSignals[index].LowPrices.Last(4))
                    {
                        Print(Labels[index] + " Long positions have breached 3 consecutive lower lows");
                        CloseAllPositions(Labels[index]);
                        CancelAllOrders(Labels[index]);
                    }
                }
                if (temp_Label[2].ToUpper() == "SELL" && ms_tfSignals[index].HighPrices.Last(1) < ms_tfSignals[index].HighPrices.Last(2))
                {
                    if (ms_tfSignals[index].HighPrices.Last(2) < ms_tfSignals[index].HighPrices.Last(3) && ms_tfSignals[index].HighPrices.Last(3) < ms_tfSignals[index].HighPrices.Last(4))
                    {
                        Print(Labels[index] + " Short positions have breached 3 consecutive higher highs");
                        CloseAllPositions(Labels[index]);
                        CancelAllOrders(Labels[index]);
                    }
                }
            }
        }

        // Drawdown oscillator to calculate how much to risk on overall trade
        public void Drawdown_Calc()
        {
            double AccountLow = 1000;
            double AccountPeak = 1000;
            double DD = (Account.Balance - AccountLow) / (AccountPeak - AccountLow);
            DD_Final = Math.Min(Risk_Min, DD * Risk_Max) / 100;
        }


        private void CloseHalfPositions(int index)
        {
            double NumPositions = FilterPositions(Labels[index]).Count();
            double PosToClose = (Math.Round(NumPositions / 2, 0, MidpointRounding.ToEven));

            for (int i = 0; i < PosToClose; i++)
            {
                ClosePosition(Positions.Where(p => p.Label == Labels[index]).OrderByDescending(p => p.EntryTime).ElementAt(i));
            }
        }

        // Function is to Load in data from Signal file produced in MT4 and amend new data to History file
        private void LoadSignalData(string File_SymbolName, string File_TradeTF, int index)
        {
            sDate = DateTime.Now.ToString("dd/MM/yyyy");
            sTime = DateTime.Now.ToString("HH:mm:ss");
            var file = string.Format(Location_SignalImport + "PP_{0}_{1}.csv", File_SymbolName, File_TradeTF);
            var filename = new Uri(file).LocalPath;
            using (var fs = File.OpenRead(filename))
                using (var reader = new StreamReader(fs))
                {
                    Print("LOADING NEW PP FILE");
                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        try
                        {
                            sDate = DateTime.Now.ToString("dd/MM/yyyy");
                            sTime = DateTime.Now.ToString("HH:mm:ss");
                            ConvertSignalData(line);
                        } catch
                        {
                            Print("CSV Load Error: {0}", line);
                        }
                    }
                }
            CreateCSVLine(index);
            //Print("CSV: {0}", CSVLines[index]);
            //Print("CSV Prev: {0}", CSVLines_prev[index]);
        }


        void ConvertSignalData(string temp)
        {
            var values = temp.Split(',');
            //int LineNo = values[0];
            string Description = values[1].ToString();
            string Info = values[2];
            switch (Description)
            {
                case "Add1":
                    Info = Info.Replace("TP or Add at ", "");
                    Entry2 = double.Parse(Info);
                    break;
                case "Add2":
                    Info = Info.Replace("Add2 at ", "");
                    Entry3 = double.Parse(Info.ToString());
                    break;
                case "Bars Text1":
                    var temp2 = Info.Split(':');
                    Entry = double.Parse(temp2[1].ToString());
                    Direction = temp2[0].ToString();
                    break;
                case "MACD2Arrow":
                    MACD2 = Info;
                    break;
                case "MACDArrow":
                    MACD = Info;
                    break;
                case "Risk Text1":
                    //SL = 1;
                    break;
                case "Risk Text2":
                    Info.Split(' ');
                    //Volume = Info[0];
                    break;
                case "Risk Text3":
                    Info.Split(' ');
                    PipsSize = double.Parse(Info[2].ToString());
                    TradeSymbol = Info[4].ToString();
                    break;
                case "SL Text1":
                    var temp_double = Info.Split(':');
                    SL1 = double.Parse(temp_double[1]);
                    break;
                case "SL1":
                    Info = Info.Replace("SL at ", "");
                    SL2 = double.Parse(Info);
                    break;
                case "SL2":
                    Info = Info.Replace("Stop Loss 2 at ", "");
                    SL3 = double.Parse(Info);
                    break;
                case "maArrow":
                    MA = Info;
                    break;
                case "stoch2Arrow":
                    STOCH2 = Info;
                    break;
                case "stochArrow":
                    STOCH = Info;
                    break;
                default:
                    break;
            }
        }

        private IEnumerable<Position> FilterPositions(string Label)
        {
            return Positions.Where(p => p.Label == Label && p.SymbolName == Symbol.Name);
        }

        private IEnumerable<PendingOrder> FilteredOrders(string Label)
        {
            return PendingOrders.Where(o => o.Label == Label && o.SymbolName == Symbol.Name);
        }

        private void CancelAllOrders(string Label)
        {
            foreach (var order in FilteredOrders(Label))
            {
                CancelPendingOrder(order);
            }
        }

        private void CloseAllPositions(string Label)
        {
            foreach (var position in FilterPositions(Label))
            {
                ClosePosition(position);
            }
        }

        public double TrimSL(double First, double Second)
        {
            return Math.Round(1 * Math.Abs(First - Second) / Symbol.PipSize, 1);
        }
        public double TrimTP(double First, double Second)
        {
            return Math.Round(1 * Math.Abs(First - Second) / Symbol.PipSize, 1);
        }


        private void SaveSignalDataHistory(string SaveFile, int index)
        {
            CreateCSVLine(index);
            File.AppendAllText(SaveFile, CSVLines[index]);
        }

        private void CreateCSVLine(int index)
        {
            CSVLines[index] = sDate + ColSep + sTime + ColSep + Symbol.Name + ColSep + sTFs[index] + ColSep + Direction + ColSep + Entry + ColSep + SL1 + ColSep + SL2 + ColSep + SL3 + ColSep;
            CSVLines[index] = CSVLines[index] + Entry2 + ColSep + Entry3 + ColSep + MA + ColSep + MACD + ColSep + MACD2 + ColSep + STOCH + ColSep + STOCH2;
            CSVLines[index] = CSVLines[index] + Environment.NewLine;
        }

        //Mappings
        public string mapTF(TimeFrame _TF)
        {
            string sTF;
            string _TF_temp = _TF.ToString();
            Print("MapTF - " + _TF_temp);
            if (_TF_temp == "Minute")
            {
                sTF = "M1";
            }
            else if (_TF_temp == "Hour")
            {
                sTF = "H1";
            }
            else if (_TF_temp == "Daily")
            {
                sTF = "D1";
            }
            else
            {
                _TF_temp = _TF_temp.Replace("our", "");
                _TF_temp = _TF_temp.Replace("inute", "");
                _TF_temp = _TF_temp.Replace("aily", "");
                _TF_temp = _TF_temp.Replace("eekly", "");
                sTF = _TF_temp;
            }
            return sTF;
        }
    }
}
