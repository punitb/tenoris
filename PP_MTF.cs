using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;
using cAlgo.Indicators;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.FullAccess)]
    public class PP_TF : Robot
    {
        [Parameter(DefaultValue = false)]
        public bool DebugMode { get; set; }

        public string CCYPairs = "AUDCAD,AUDJPY,AUDNZD,AUDUSD,CADCHF,CADJPY,EURAUD,EURCAD,EURGBP,EURJPY,EURNZD,EURUSD,GBPAUD,GBPCAD,GBPJPY,GBPNZD,GBPUSD,NZDCAD,NZDJPY,NZDUSD,USDCAD,USDJPY,USDCHF,USDSGD,XAUUSD";
        public int TradeLookBack = 3;

        public double BadVolumeSize = 200000;

        public double Risk_Max = 0.2;
        public double mATR = 2;

        string InputDir = "C:\\Users\\Hp\\AppData\\Roaming\\MetaQuotes\\Terminal\\2010C2441A263399B34F537D91A53AC9\\MQL4\\Files\\";
        string LogFile = "C:\\Users\\Hp\\Documents\\Trading\\PP\\Logs\\\\" + "PP_Log.txt";
        string tempFolder = "C:\\Users\\Hp\\Documents\\Trading\\PP\\temp\\";
        string HistoryFolder = "C:\\Users\\Hp\\Documents\\Trading\\PP\\History\\";

        public string[] Assets;
        public TimeFrame[] TF;
        public Bars[,] MktData;
        public string[,] Label;
        public AverageTrueRange[,] _atr;
        public PPTrade[,] NewTrades;
        public PPTrade[,] PrevTrades;

        public int total_assets, total_tf;

        private string NewData;
        private bool NewStart;

        public int WinRateThreshold = 60;

        protected override void OnStop()
        {
            Notifications.SendEmail("punit.trade.ideas@gmail.com", "punit.trade.ideas@gmail.com", "PP Bot Stopped", "PP Bot Stopped - please restart");
        }
        protected override void OnStart()
        {
            NewStart = true;

            //Positions.Closed += OnPositionsClosed;
            Positions.Opened += OnPositionsOpened;
            //Positions.Modified += OnPositionModified;

            string[] temp = CCYPairs.Split(',');
            int CCYPair_Count = CCYPairs.Split(',').Length;
            Assets = new string[CCYPair_Count];
            for (int i = 0; i < CCYPair_Count; i++)
                Assets[i] = temp[i];


            TF = new TimeFrame[5];

            //TF[0] = TimeFrame.Minute5;
            TF[0] = TimeFrame.Minute15;
            TF[1] = TimeFrame.Minute30;
            TF[2] = TimeFrame.Hour;
            TF[3] = TimeFrame.Hour4;
            TF[4] = TimeFrame.Daily;
            // TF[6] = TimeFrame.Monthly;

            total_assets = Assets.GetUpperBound(0);
            total_tf = TF.GetUpperBound(0);
            MktData = new Bars[total_assets, total_tf];
            _atr = new AverageTrueRange[total_assets, total_tf];
            Label = new string[total_assets, total_tf];
            NewTrades = new PPTrade[total_assets, total_tf];
            PrevTrades = new PPTrade[total_assets, total_tf];
            Print("TF: " + total_tf);
            Print("Assets: " + total_assets);

            DeleteFiles(tempFolder);
            for (int j = Assets.GetLowerBound(0); j < Assets.GetUpperBound(0); j++)
                for (int k = TF.GetLowerBound(0); k < TF.GetUpperBound(0); k++)
                {
                    Label[j, k] = string.Format("{0}_{1}_{2}", "PP", Assets[j], MapTF(TF[k]));
                    MktData[j, k] = MarketData.GetBars(TF[k], Assets[j]);
                    _atr[j, k] = Indicators.AverageTrueRange(MktData[j, k], 20, MovingAverageType.Simple);
                    //Logger(Label[j, k]);
                }
            Logger("Everything Loaded");

        }

        private void OnPositionsOpened(PositionOpenedEventArgs obj)
        {
            Position pos = obj.Position;
            string Body = "";
            if (pos.Label.Substring(0, 2) == "PP" || pos.Label != null)
            {
                string Subject = "New Trade: " + obj.Position.Label;
                string[] sComment = pos.Comment.Split('_');
                Body = string.Format("\n{5} {0}-{1}\nEntry: {2}\nVolume: {3}\nStopLoss: {4}", pos.Label, pos.Comment, pos.EntryPrice, pos.Quantity, double.Parse(sComment.Last()) * 100, pos.TradeType);
                Notifications.SendEmail("punit.trade.ideas@gmail.com", "punit.trade.ideas@gmail.com", Subject, Body);
            }
            Logger("Email sent with content:\n" + Body);
        }


        protected override void OnBar()
        {
            Logger("New Bar");
            DeleteFiles(tempFolder);
            // CopyFiles(InputDir, tempFolder);
            for (int j = Assets.GetLowerBound(0); j < Assets.GetUpperBound(0); j++)
                for (int k = TF.GetLowerBound(0) + 1; k < TF.GetUpperBound(0); k++)
                {
                    GetSymbolFiles(InputDir, Assets[j], TF[k], j, k);
                    if (NewTrades[j, k] != null)
                        Logger(string.Format("[{0},{1}] New Data: {2}", j, k, NewTrades[j, k].AllData));

                    SaveHistory("History", NewTrades[j, k]);
                }

            Logger("Symbols Refreshed");
            //Delete files from sources once files are loaded in
            //DeleteFiles(InputDir);
            //check files after second cycle of run
            if (!NewStart)
            {
                CheckRisks();
                Logger("Checked Open Risk");
                Print("Hour " + Server.Time.Hour + "  " + Server.TimeInUtc.Hour);
                if (Server.Time.Hour >= 6 && Server.Time.Hour < 23)
                {
                    CreateTrades();
                    Logger("New Trades Created");
                }
            }


            for (int j = Assets.GetLowerBound(0); j < Assets.GetUpperBound(0); j++)
                for (int k = TF.GetLowerBound(0) + 1; k < TF.GetUpperBound(0); k++)
                    if (NewTrades[j, k] != null)
                        PrevTrades[j, k] = new PPTrade(NewTrades[j, k].OriginalData);

            NewStart = false;
            CloseOldTrades();

        }

        void CloseOldTrades()
        {
            foreach (PendingOrder po in PendingOrders)
                if (Positions.FindAll(po.Label).Count() == 0)
                    po.Cancel();
        }

        void SaveHistory(string FilePrefix, PPTrade Trade)
        {

            string filename = string.Format("{0}_{1}_{2}", FilePrefix, Trade.Asset, Trade.TF);
            string line = string.Join(",", DateTime.Now, Trade.Asset, Trade.TF, Trade.TradeDir, Trade.Entry, Trade.StopLoss, Trade.TakeProfit, Trade.Desc);
            string filepath = HistoryFolder + filename + ".csv";

            var fs = new FileStream(filepath, FileMode.Append);
            using (var reader = new StreamWriter(fs))
            {
                if (File.Exists(filepath))
                {
                    reader.WriteLine(line);
                }
                else
                {
                    reader.WriteLine("DateTime,Asset,TF,DIrection,Entry,StopLoss,TakeProfit,Desc");
                }
            }
        }


        void GetSymbolFiles(string folder, string FXPair, TimeFrame TF, int asset_n, int tf_n)
        {
            string FilePattern = string.Format("{0}{1}{2}{3}{4}", "*", FXPair, "_", MapTF(TF), "*");
            foreach (string file in Directory.GetFiles(folder, FilePattern))
            {
                File.Copy(file, file.Replace(folder, tempFolder));
                string tempfile = file.Replace(folder, tempFolder);
                var fs = new FileStream(tempfile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using (var reader = new StreamReader(fs))
                {
                    List<string> item = new List<string>();
                    while (!reader.EndOfStream)
                    {
                        var count = 0;
                        var line = reader.ReadLine();
                        if (line.Length > 7)
                        {
                            var values = line.Split(',');
                            item.Add(values[2]);
                            count++;
                        }
                    }

                    string removeitems = "20,19,17,16,15,13,12,11,10,7,6,5,2";
                    foreach (var num in removeitems.Split(','))
                        item.RemoveAt(int.Parse(num));

                    NewData = string.Format("{0},{1},{2}", FXPair, MapTF(TF), string.Join(",", item));
                    NewTrades[asset_n, tf_n] = new PPTrade(NewData.Split(','));

                }
            }
        }


        void CreateTrades()
        {
            for (int j = Assets.GetLowerBound(0); j < Assets.GetUpperBound(0); j++)
            {
                for (int k = TF.GetLowerBound(0) + 1; k < TF.GetUpperBound(0) - 1; k++)
                {
                    Symbol sym = Symbols.GetSymbol(Assets[j]);
                    if (NewTrades[j, k] != null && PrevTrades[j, k] != null)
                    {
                        bool Entry = NewTrades[j, k].Entry == PrevTrades[j, k].Entry;
                        bool Direction = NewTrades[j, k].TradeDir == PrevTrades[j, k].TradeDir;
                        string PrevEntry = NewTrades[j, k].TradeDir + NewTrades[j, k].Entry.ToString();
                        string CurrentEntry = PrevTrades[j, k].TradeDir + PrevTrades[j, k].Entry.ToString();
                        if (PrevEntry != CurrentEntry && NewTrades[j, k] != null && PrevTrades[j, k] != null)
                        {
                            Logger(string.Format("{6}\t{0} {1} {2}={3} {4}={5}", Entry, Direction, NewTrades[j, k].Entry, PrevTrades[j, k].Entry, NewTrades[j, k].TradeDir, PrevTrades[j, k].TradeDir, NewTrades[j, k].AssetID));

                            double Close = MarketData.GetBars(TF[k], Assets[j]).ClosePrices.Last(0);
                            double atr = _atr[j, k].Result.LastValue / sym.PipSize;
                            Print("Trade Direction: {0}", NewTrades[j, k].TradeDir);
                            if (NewTrades[j, k].TradeDir == TradeType.Buy)
                            {
                                double StopLoss = Math.Round(Math.Abs(NewTrades[j, k].Entry - NewTrades[j, k].StopLoss) / sym.PipSize, 0);
                                double OrderVolume = GetOrderVolume(StopLoss, Risk_Max, 1, sym);
                                //if (LastWinRate(Assets[j], TF[k], NewTrades[j, k].TradeDir, TradeLookBack) < WinRateThreshold)
                                //OrderVolume = sym.VolumeInUnitsMin;

                                string Comment = string.Format("{0}_{1}_{2}_{3}_{4}", 1, NewTrades[j, k].Desc, LastWinRate(Assets[j], TF[k], NewTrades[j, k].TradeDir, TradeLookBack), OrderVolume, StopLoss);
                                ExecuteMarketOrder(NewTrades[j, k].TradeDir, Assets[j], OrderVolume, Label[j, k], StopLoss, null, Comment);
                                Print("Mkt Order {0}", Label[j, k]);
                                for (int i = 2; i < 4; i++)
                                {
                                    OrderVolume = GetOrderVolume(StopLoss, Risk_Max, i, sym);
                                    //if (LastWinRate(Assets[j], TF[k], NewTrades[j, k].TradeDir, TradeLookBack) < WinRateThreshold)
                                    //OrderVolume = sym.VolumeInUnitsMin;

                                    Comment = string.Format("{0}_{1}_{2}_{3}_{4}", i, NewTrades[j, k].Desc, LastWinRate(Assets[j], TF[k], NewTrades[j, k].TradeDir, TradeLookBack), OrderVolume, StopLoss);
                                    PlaceStopOrder(NewTrades[j, k].TradeDir, Assets[j], OrderVolume, NewTrades[j, k].Entry + (i - 1) * StopLoss * sym.PipSize, Label[j, k], StopLoss, null, null, Comment);
                                }

                            }
                            else if (NewTrades[j, k].TradeDir == TradeType.Sell)
                            {
                                double StopLoss = Math.Round(Math.Abs(NewTrades[j, k].Entry - NewTrades[j, k].StopLoss) / sym.PipSize, 0);
                                double OrderVolume = GetOrderVolume(StopLoss, Risk_Max, 1, sym);
                                //if (LastWinRate(Assets[j], TF[k], NewTrades[j, k].TradeDir, TradeLookBack) < WinRateThreshold)
                                //  OrderVolume = sym.VolumeInUnitsMin;

                                string Comment = string.Format("{0}_{1}_{2}_{3}_{4}", 1, NewTrades[j, k].Desc, LastWinRate(Assets[j], TF[k], NewTrades[j, k].TradeDir, TradeLookBack), OrderVolume, StopLoss);
                                ExecuteMarketOrder(NewTrades[j, k].TradeDir, Assets[j], OrderVolume, Label[j, k], StopLoss, null, Comment);

                                for (int i = 2; i < 4; i++)
                                {
                                    OrderVolume = GetOrderVolume(StopLoss, Risk_Max, i, sym);
                                    // if (LastWinRate(Assets[j], TF[k], NewTrades[j, k].TradeDir, TradeLookBack) < WinRateThreshold)
                                    //   OrderVolume = sym.VolumeInUnitsMin;

                                    Comment = string.Format("{0}_{1}_{2}_{3}_{4}", i, NewTrades[j, k].Desc, LastWinRate(Assets[j], TF[k], NewTrades[j, k].TradeDir, TradeLookBack), OrderVolume, StopLoss);
                                    PlaceStopOrder(NewTrades[j, k].TradeDir, Assets[j], OrderVolume, NewTrades[j, k].Entry - (i - 1) * StopLoss * sym.PipSize, Label[j, k], StopLoss, null, null, Comment);
                                }

                            }
                        }
                    }
                }
            }
        }

        void CheckRisks()
        {

            for (int j = Assets.GetLowerBound(0); j < Assets.GetUpperBound(0); j++)
                for (int k = TF.GetLowerBound(0) + 1; k < TF.GetUpperBound(0); k++)
                {
                    Symbol sym = Symbols.GetSymbol(Assets[j]);
                    if (Positions.FindAll(Label[j, k]).Count() > 0)
                    {
                        Print(Positions.FindAll(Label[j, k]).Count());
                        foreach (Position pos in Positions.FindAll(Label[j, k]))
                        {
                            Symbol symbol = Symbols.GetSymbol(Assets[j]);
                            double MaxLoss = Account.Balance * Risk_Max / 100;
                            if (pos.StopLoss.HasValue)
                                MaxLoss = (double)(pos.EntryPrice - pos.StopLoss) * sym.PipValue * pos.VolumeInUnits / sym.PipSize;

                            Print("{0} MaxLoss: {1}", Label[j, k], Math.Round(MaxLoss, 0));

                            if (pos.NetProfit > Math.Abs(MaxLoss) && !pos.HasTrailingStop)
                            {
                                pos.ModifyStopLossPrice(pos.EntryPrice);
                                pos.ModifyTrailingStop(true);
                                if (pos.VolumeInUnits > sym.VolumeInUnitsMin)
                                    pos.ModifyVolume(sym.NormalizeVolumeInUnits(0.5 * pos.VolumeInUnits));
                            }

                            //if 2 lower lows, then close trades
                            if (MktData[j, k].OpenTimes.GetIndexByTime(pos.EntryTime) + 4 < MktData[j, k].OpenTimes.Count)
                            {
                                if (MktData[j, k].LowPrices.Last(2) < MktData[j, k].LowPrices.Last(3) && pos.TradeType == TradeType.Buy)
                                    if (MktData[j, k].LowPrices.Last(1) < MktData[j, k].LowPrices.Last(2))
                                    {
                                        Print("{0} has had 2 Lower Lows. Close Half", pos.Label);
                                        if (pos.VolumeInUnits == sym.VolumeInUnitsMin)
                                        {
                                            ClosePosition(pos);
                                            CancelAllOrders(pos.Label, TradeType.Buy);
                                        }
                                        else
                                        {
                                            pos.ModifyVolume(sym.NormalizeVolumeInUnits(0.5 * pos.VolumeInUnits));
                                        }
                                    }

                                if (MktData[j, k].LowPrices.Last(3) < MktData[j, k].LowPrices.Last(4) && pos.TradeType == TradeType.Buy && !pos.HasTrailingStop)
                                    if (MktData[j, k].LowPrices.Last(1) < MktData[j, k].LowPrices.Last(2) && MktData[j, k].LowPrices.Last(2) < MktData[j, k].LowPrices.Last(3))
                                    {
                                        Print("{0} has had 3 Lower Lows. Close All", pos.Label);
                                        CloseAllPositions(Label[j, k], TradeType.Buy);
                                        CancelAllOrders(Label[j, k], TradeType.Buy);
                                    }

                                if (MktData[j, k].HighPrices.Last(2) > MktData[j, k].HighPrices.Last(3) && pos.TradeType == TradeType.Sell)
                                    if (MktData[j, k].HighPrices.Last(1) > MktData[j, k].HighPrices.Last(2))
                                    {
                                        Print("{0} has had 2 Higher Highs. Close Half", pos.Label);
                                        if (pos.VolumeInUnits == sym.VolumeInUnitsMin)
                                        {
                                            ClosePosition(pos);
                                            CancelAllOrders(pos.Label, TradeType.Sell);
                                        }
                                        else
                                        {
                                            pos.ModifyVolume(sym.NormalizeVolumeInUnits(0.5 * pos.VolumeInUnits));
                                        }
                                    }

                                if (MktData[j, k].HighPrices.Last(3) > MktData[j, k].HighPrices.Last(4) && pos.TradeType == TradeType.Sell && !pos.HasTrailingStop)
                                    if (MktData[j, k].HighPrices.Last(1) > MktData[j, k].HighPrices.Last(2) && MktData[j, k].HighPrices.Last(2) > MktData[j, k].HighPrices.Last(3))
                                    {
                                        Print("{0} has had 3 Higher Highs. Close All", pos.Label);
                                        CloseAllPositions(Label[j, k], TradeType.Sell);
                                        CancelAllOrders(Label[j, k], TradeType.Sell);
                                    }
                            }
                        }
                    }
                }
        }


        void Logger(string Text)
        {
            string Filename = LogFile;
            FileStream stream = null;
            stream = new FileStream(Filename, FileMode.Append);
            using (StreamWriter sw = new StreamWriter(stream))
            {
                string FinalText = DateTime.Now + " = " + Text;
                sw.WriteLine(FinalText);
            }
            if (DebugMode)
                Print(Text);
        }

        double LastWinRate(string asset, TimeFrame tf, TradeType TradeDir, int TradeLookBack)
        {
            double Wins = 0;
            int count = 0;
            string temp_label = string.Format("{0}_{1}_{2}", "PP", asset, MapTF(tf));
            foreach (HistoricalTrade trade in History.FindAll(temp_label))
            {
                if (trade.NetProfit > 0 && int.Parse(trade.Comment.Substring(0, 1)) == 1)
                {
                    Wins++;
                }
                count++;
                if (count > TradeLookBack)
                    break;
            }
            double FinalWinRate = 100 * Math.Round(Wins / TradeLookBack, 2);
            Logger("WinRate:" + FinalWinRate + " with Lookback: " + TradeLookBack);
            return FinalWinRate;
        }


        //Helper Functions
        void CopyFiles(string fromFolder, string toFolder)
        {

            string[] filePaths = Directory.GetFiles(fromFolder);
            foreach (string filePath in filePaths)
            {
                var toFile = filePath.Replace(fromFolder, toFolder);
                File.Copy(filePath, toFile);
            }
        }
        void DeleteFiles(string Folder)
        {

            string[] filePaths = Directory.GetFiles(Folder);
            foreach (string filePath in filePaths)
                File.Delete(filePath);
        }
        public string MapTF(TimeFrame _TF)
        {
            string sTF;
            string _TF_temp = _TF.ToString();
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

        private double EquityAtRisk(double AccountSize, double Px, double SL, double OrderVolume, Symbol sym)
        {
            double Equity = (Px - SL) * sym.PipValue * OrderVolume;
            return Math.Round(Equity * 100 / AccountSize, 3);
        }

        private double GetOrderVolume(double StopLoss, double Risk, int NoOfTrades, Symbol sym)
        {
            double risk_tranche = Risk / NoOfTrades;
            double _OrderVolume = ((Account.Balance * risk_tranche / 100) / StopLoss / sym.PipValue);
            double FinalVolume = Math.Max(sym.VolumeInUnitsMin, Math.Min(BadVolumeSize, _OrderVolume));
            return sym.NormalizeVolumeInUnits(FinalVolume, RoundingMode.ToNearest);
        }
        private IEnumerable<Position> FilterPositions(string Label, TradeType TradeDirection)
        {
            return Positions.Where(p => p.Label == Label && p.TradeType == TradeDirection);
        }

        private IEnumerable<PendingOrder> FilteredOrders(string Label, TradeType TradeDirection)
        {
            return PendingOrders.Where(o => o.Label == Label && o.TradeType == TradeDirection);
        }

        private void CancelAllOrders(string Label, TradeType TradeDirection)
        {
            foreach (var order in FilteredOrders(Label, TradeDirection))
                CancelPendingOrder(order);
        }

        private void CloseAllPositions(string Label, TradeType TradeDirection)
        {
            foreach (var position in FilterPositions(Label, TradeDirection))
                ClosePosition(position);
        }
        public class PPTrade
        {
            public string Asset { get; set; }
            public string TF { get; set; }
            public string AssetID { get; set; }
            public string Desc { get; set; }
            public TradeType TradeDir { get; set; }
            public double Entry { get; set; }
            public double StopLoss { get; set; }
            public double TakeProfit { get; set; }
            public string AllData { get; set; }
            public string[] OriginalData { get; set; }

            public PPTrade(string[] item)
            {
                //var item = line.Split(',');
                Asset = item[0];
                TF = item[1];
                AssetID = item[0] + item[1];
                Desc = string.Format("{0}{1}{2}{3}{4}", item[9], item[7], item[6], item[11], item[10]);
                TradeDir = item[5].Split(':').First() == "BUY" ? TradeType.Buy : TradeType.Sell;
                Entry = double.Parse(item[5].Split(':').Last());
                StopLoss = double.Parse(item[8].Split(':').Last());
                TakeProfit = double.Parse(item[2].Split(' ').Last());
                AllData = string.Concat(AssetID, TradeDir, Entry, Desc);
                OriginalData = item;
            }
        }
    }
}
