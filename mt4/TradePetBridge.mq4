#property strict
#property description "TradePet account, order history and chart bridge. No trading or DLL access."

input int InpServerUtcOffsetMinutes = 10000; // Automatic; set broker offset only if no live quote is available.
input bool InpShowLossZones = true;
input color InpLossZoneColor = C'115,35,45';
#define LOSS_ZONE_PREFIX "TradePet::LossZone::"

string instance_id;
long sequence = 0;
datetime last_server_time = 0;
int server_offset = 0;
bool have_offset = false;
int last_login = 0;
string last_server = "";
uint last_history_scan = 0;
string last_zone_command = "";
string offset_key = "";
string last_live_signature = "";
string quote_ring[1024];
int quote_head = 0, quote_count = 0;
long quote_sequence = 0;
string quote_symbols[], quote_values[];

string Quote(string value)
{
   StringReplace(value, "\\", "\\\\");
   StringReplace(value, "\"", "\\\"");
   StringReplace(value, "\r", "\\r");
   StringReplace(value, "\n", "\\n");
   StringReplace(value, "\t", "\\t");
   return "\"" + value + "\"";
}

string Number(double value) { return DoubleToString(value, 8); }
string Utc(datetime value)
{
   string date = TimeToString(value, TIME_DATE);
   StringReplace(date, ".", "-");
   return Quote(date + "T" + TimeToString(value, TIME_SECONDS) + "Z");
}

int OnInit()
{
   if(IsTesting()) return INIT_FAILED;
   instance_id = "mt4-ea-" + IntegerToString(ChartID()) + "-" + IntegerToString((long)TimeLocal()) + "-" + IntegerToString(GetTickCount());
   EventSetTimer(1);
   OnTimer();
   return INIT_SUCCEEDED;
}

void OnDeinit(const int reason) { EventKillTimer(); DeleteManagedLossZones(); }

void RecordQuote(string symbol, bool tick_event = false)
{
   if(!have_offset || !IsConnected() || last_login != AccountNumber() || last_server != AccountServer()) return;
   MqlTick tick;
   if(!SymbolInfoTick(symbol, tick) || tick.bid <= 0 || tick.ask < tick.bid || MathAbs((double)(TimeCurrent()-tick.time)) > 120) return;
   string value = IntegerToString((long)tick.time) + ":" + Number(tick.bid) + ":" + Number(tick.ask) + ":" + Number(tick.last) + ":" + IntegerToString((long)tick.volume);
   int index = -1;
   for(int i=0; i<ArraySize(quote_symbols); i++) if(quote_symbols[i] == symbol) { index=i; break; }
   if(index < 0)
   {
      index=ArraySize(quote_symbols);
      if(index >= 512) return;
      ArrayResize(quote_symbols,index+1); ArrayResize(quote_values,index+1); quote_symbols[index]=symbol;
   }
   if(!tick_event && quote_values[index] == value) return;
   quote_values[index] = value;
   quote_ring[quote_head] = "{\"sequence\":" + IntegerToString(++quote_sequence) + ",\"symbol\":" + Quote(symbol)
      + ",\"time\":" + IntegerToString((long)tick.time) + ",\"bid\":" + Number(tick.bid) + ",\"ask\":" + Number(tick.ask)
      + ",\"last\":" + Number(tick.last) + ",\"volume\":" + IntegerToString((long)tick.volume) + "}";
   quote_head=(quote_head+1)%1024; if(quote_count<1024) quote_count++;
}

void OnTick() { RecordQuote(Symbol(),true); }

string QuotesSnapshot()
{
   long chart=ChartFirst();
   while(chart>=0) { RecordQuote(ChartSymbol(chart)); chart=ChartNext(chart); }
   string result="";
   for(int i=0; i<quote_count; i++)
   {
      if(i>0) result+=",";
      result+=quote_ring[(quote_head-quote_count+i+1024)%1024];
   }
   return "["+result+"]";
}

string RiskFields(string symbol, int side, double entry, double stop)
{
   string profit=SymbolInfoString(symbol,SYMBOL_CURRENCY_PROFIT);
   string deposit=AccountCurrency();
   double conversion=0;
   bool loss=(stop-entry)*(side==OP_BUY ? 1 : -1)<0;
   if(profit==deposit) conversion=1;
   else
   {
      for(int i=0; i<SymbolsTotal(true); i++)
      {
         string fx=SymbolName(i,true);
         string base=SymbolInfoString(fx,SYMBOL_CURRENCY_BASE), quote=SymbolInfoString(fx,SYMBOL_CURRENCY_PROFIT);
         if(!((base==profit && quote==deposit)||(base==deposit && quote==profit))) continue;
         MqlTick tick;
         if(!SymbolInfoTick(fx,tick) || tick.bid<=0 || tick.ask<tick.bid || MathAbs((double)(TimeCurrent()-tick.time))>120) continue;
         conversion=base==profit ? (loss ? tick.ask : tick.bid) : 1.0/(loss ? tick.bid : tick.ask);
         break;
      }
   }
   return ",\"calculationMode\":"+IntegerToString((int)MarketInfo(symbol,MODE_PROFITCALCMODE))
      +",\"contractSize\":"+Number(MarketInfo(symbol,MODE_LOTSIZE))
      +",\"baseCurrency\":"+Quote(SymbolInfoString(symbol,SYMBOL_CURRENCY_BASE))
      +",\"profitCurrency\":"+Quote(profit)+",\"conversionRate\":"+Number(conversion);
}

void OnTimer()
{
   datetime utc = TimeGMT();
   if(last_login != AccountNumber() || last_server != AccountServer())
   {
      last_login = AccountNumber();
      last_server = AccountServer();
      last_server_time = 0;
      have_offset = false;
      last_history_scan = 0;
      last_zone_command = "";
      last_live_signature = "";
      quote_count=0; quote_head=0;
      ArrayResize(quote_symbols,0); ArrayResize(quote_values,0);
      DeleteManagedLossZones();
      offset_key = StringSubstr("TradePet.Offset." + IntegerToString(last_login) + "." + last_server, 0, 63);
      if(GlobalVariableCheck(offset_key) && utc - GlobalVariableTime(offset_key) < 7 * 86400)
      {
         server_offset = (int)GlobalVariableGet(offset_key);
         have_offset = MathAbs(server_offset) <= 14 * 3600;
      }
   }
   if(MathAbs(InpServerUtcOffsetMinutes) <= 840)
   {
      server_offset = InpServerUtcOffsetMinutes * 60;
      have_offset = true;
   }
   // Round away quote latency; MT4 exposes server timestamps rather than UTC order timestamps.
   datetime server_time = TimeCurrent();
   // Wait for an observed server tick; on quiet weekends TimeCurrent may be days old.
   if(MathAbs(InpServerUtcOffsetMinutes) > 840 && last_server_time != 0 && server_time != last_server_time)
   {
      int candidate = (int)MathRound((double)(server_time - utc) / 900.0) * 900;
      if(MathAbs(candidate) <= 14 * 3600 && MathAbs((double)(server_time - utc - candidate)) < 120)
      {
         server_offset = candidate;
         have_offset = true;
         GlobalVariableSet(offset_key, server_offset);
      }
   }
   last_server_time = server_time;
   int offset = server_offset;
   string positions = "", orders = "", specs = "", live_signature = "";
   int total = OrdersTotal();
   for(int index = 0; index < total; index++)
   {
      if(!OrderSelect(index, SELECT_BY_POS, MODE_TRADES)) return;
      string symbol = OrderSymbol();
      RecordQuote(symbol);
      int kind = OrderType();
      string ticket = IntegerToString(OrderTicket());
      live_signature += ticket + ":" + Number(OrderLots()) + ";";
      if(kind == OP_BUY || kind == OP_SELL)
      {
         if(StringLen(positions) > 0) positions += ",";
         positions += "{\"ticket\":" + ticket + ",\"positionId\":" + ticket
            + ",\"symbol\":" + Quote(symbol) + ",\"side\":" + Quote(kind == OP_BUY ? "buy" : "sell")
            + ",\"volume\":" + Number(OrderLots()) + ",\"entryPrice\":" + Number(OrderOpenPrice())
            + ",\"currentPrice\":" + Number(MarketInfo(symbol, kind == OP_BUY ? MODE_BID : MODE_ASK))
            + ",\"profit\":" + Number(OrderProfit()) + ",\"swap\":" + Number(OrderSwap())
            + ",\"stopLoss\":" + Number(OrderStopLoss()) + ",\"takeProfit\":" + Number(OrderTakeProfit())
            + ",\"tickSize\":" + Number(SymbolInfoDouble(symbol, SYMBOL_TRADE_TICK_SIZE))
            + ",\"tickValue\":" + Number(MarketInfo(symbol, MODE_TICKVALUE))
            + RiskFields(symbol,kind,OrderOpenPrice(),OrderStopLoss())
            + ",\"openedAtUtc\":" + Utc(OrderOpenTime() - offset) + "}";
      }
      else if(kind >= OP_BUYLIMIT && kind <= OP_SELLSTOP)
      {
         if(StringLen(orders) > 0) orders += ",";
         string types[4] = {"buy_limit", "sell_limit", "buy_stop", "sell_stop"};
         orders += "{\"ticket\":" + ticket + ",\"symbol\":" + Quote(symbol)
            + ",\"type\":" + Quote(types[kind - OP_BUYLIMIT]) + ",\"volume\":" + Number(OrderLots())
            + ",\"price\":" + Number(OrderOpenPrice()) + ",\"stopLoss\":" + Number(OrderStopLoss())
            + ",\"takeProfit\":" + Number(OrderTakeProfit()) + ",\"createdAtUtc\":" + Utc(OrderOpenTime() - offset) + "}";
      }
      if(StringLen(specs) > 0) specs += ",";
      double point = MarketInfo(symbol, MODE_POINT);
      specs += "{\"symbol\":" + Quote(symbol) + ",\"point\":" + Number(point)
         + ",\"tickSize\":" + Number(SymbolInfoDouble(symbol, SYMBOL_TRADE_TICK_SIZE))
         + ",\"digits\":" + IntegerToString((int)MarketInfo(symbol, MODE_DIGITS)) + "}";
   }
   if(OrdersTotal() != total) return;
   if(IsConnected() && have_offset)
   {
      uint scan_now=GetTickCount();
      if(last_history_scan==0 || scan_now-last_history_scan>=5000 || last_live_signature!=live_signature)
      {
         if(ExportHistory(live_signature)) { last_history_scan=scan_now; last_live_signature=live_signature; }
      }
   }
   string json = "{\"version\":2,\"platform\":\"mt4\",\"sourceInstanceId\":" + Quote(instance_id)
      + ",\"sequence\":" + IntegerToString(++sequence) + ",\"terminalPath\":" + Quote(TerminalInfoString(TERMINAL_PATH))
      + ",\"connected\":" + (IsConnected() ? "true" : "false") + ",\"capturedAtUtc\":" + Utc(utc)
      + ",\"account\":{\"server\":" + Quote(AccountServer()) + ",\"login\":" + IntegerToString(AccountNumber())
      + ",\"currency\":" + Quote(AccountCurrency()) + "},\"balance\":" + Number(AccountBalance())
      + ",\"equity\":" + Number(AccountEquity()) + ",\"floatingPnl\":" + Number(AccountProfit())
      + (have_offset ? ",\"serverUtcOffsetSeconds\":" + IntegerToString(offset) : "")
      + ",\"positions\":[" + positions + "],\"orders\":[" + orders + "],\"symbolSpecifications\":[" + specs + "]"
      + ",\"liveOrderSignature\":" + Quote(live_signature)
      + (have_offset ? ",\"charts\":" + ChartSnapshot() + ",\"quotes\":" + QuotesSnapshot() : "") + "}";
   int file = FileOpen("TradePet\\snapshot.tmp", FILE_WRITE | FILE_TXT | FILE_ANSI, 0, CP_UTF8);
   if(file == INVALID_HANDLE) return;
   FileWriteString(file, json);
   FileFlush(file);
   FileClose(file);
   FileMove("TradePet\\snapshot.tmp", 0, "TradePet\\snapshot.json", FILE_REWRITE);
   if(IsConnected() && have_offset)
   {
      PollLossZones();
      PollMarketHistory();
   }
}

bool ExportHistory(string expected_signature)
{
   int live_count = OrdersTotal(), history_count = OrdersHistoryTotal();
   if(live_count + history_count > 100000) { Print("TradePet: history export exceeds 100000 orders."); return false; }
   string orders = "", signature = "";
   for(int pool_index = 0; pool_index < 2; pool_index++)
   {
      int count = pool_index == 0 ? live_count : history_count;
      for(int index = 0; index < count; index++)
      {
         if(!OrderSelect(index, SELECT_BY_POS, pool_index == 0 ? MODE_TRADES : MODE_HISTORY)) return false;
         if(pool_index==0) signature+=IntegerToString(OrderTicket())+":"+Number(OrderLots())+";";
         int kind = OrderType();
         if(kind != OP_BUY && kind != OP_SELL && kind != 6 && kind != 7) continue;
         if(StringLen(orders) > 0) orders += ",";
         orders += "{\"ticket\":" + IntegerToString(OrderTicket()) + ",\"type\":" + IntegerToString(kind)
            + ",\"symbol\":" + Quote(OrderSymbol()) + ",\"volume\":" + Number(OrderLots())
            + ",\"openTime\":" + IntegerToString((long)OrderOpenTime())
            + ",\"closeTime\":" + IntegerToString((long)OrderCloseTime())
            + ",\"openPrice\":" + Number(OrderOpenPrice()) + ",\"closePrice\":" + Number(OrderClosePrice())
            + ",\"profit\":" + Number(OrderProfit()) + ",\"commission\":" + Number(OrderCommission())
            + ",\"swap\":" + Number(OrderSwap())
            + ",\"comment\":" + Quote(OrderComment()) + ",\"magic\":" + IntegerToString(OrderMagicNumber())
            + ",\"point\":" + Number(MarketInfo(OrderSymbol(), MODE_POINT))
            + ",\"tickSize\":" + Number(SymbolInfoDouble(OrderSymbol(), SYMBOL_TRADE_TICK_SIZE))
            + ",\"digits\":" + IntegerToString((int)MarketInfo(OrderSymbol(), MODE_DIGITS)) + "}";
      }
   }
   if(live_count != OrdersTotal() || history_count != OrdersHistoryTotal() ||
      last_login != AccountNumber() || last_server != AccountServer() || signature != expected_signature) return false;
   string json = "{\"version\":2,\"platform\":\"mt4\",\"sourceInstanceId\":" + Quote(instance_id)
      + ",\"terminalPath\":" + Quote(TerminalInfoString(TERMINAL_PATH))
      + ",\"accountKey\":" + Quote("MT4:" + AccountServer() + "|" + IntegerToString(AccountNumber()))
      + ",\"capturedAtUtc\":" + Utc(TimeGMT()) + ",\"serverUtcOffsetSeconds\":" + IntegerToString(server_offset)
      + ",\"liveOrderSignature\":" + Quote(signature) + ",\"scanComplete\":true,\"orders\":[" + orders + "]}";
   int file = FileOpen("TradePet\\history.tmp", FILE_WRITE | FILE_TXT | FILE_ANSI, 0, CP_UTF8);
   if(file == INVALID_HANDLE) return false;
   FileWriteString(file, json); FileFlush(file); FileClose(file);
   return FileMove("TradePet\\history.tmp", 0, "TradePet\\history.json", FILE_REWRITE);
}

string ChartSnapshot()
{
   string objects = "";
   long chart = ChartFirst();
   while(chart >= 0)
   {
      int total = ObjectsTotal(chart, -1, -1);
      for(int i = 0; i < total; i++)
      {
         string name = ObjectName(chart, i, -1, -1);
         if(name == "" || StringFind(name, LOSS_ZONE_PREFIX) == 0 || ObjectGetInteger(chart, name, OBJPROP_HIDDEN)) continue;
         ENUM_OBJECT type = (ENUM_OBJECT)ObjectGetInteger(chart, name, OBJPROP_TYPE);
         if(type != OBJ_HLINE && type != OBJ_RECTANGLE && type != OBJ_TREND && type != OBJ_TEXT && type != OBJ_LABEL) continue;
         string kind = type == OBJ_HLINE ? "horizontalLine" : type == OBJ_RECTANGLE ? "rectangle" :
            type == OBJ_TREND ? "trendLine" : type == OBJ_TEXT ? "text" : "label";
         int points = type == OBJ_RECTANGLE || type == OBJ_TREND ? 2 : type == OBJ_LABEL ? 0 : 1;
         string anchors = "";
         for(int p = 0; p < points; p++)
         {
            if(p > 0) anchors += ",";
            long time = ObjectGetInteger(chart, name, OBJPROP_TIME, p);
            anchors += "{\"timeEpoch\":" + IntegerToString(time > 0 ? time - server_offset : 0)
               + ",\"price\":" + Number(ObjectGetDouble(chart, name, OBJPROP_PRICE, p)) + "}";
         }
         if(StringLen(objects) > 0) objects += ",";
         objects += "{\"terminalId\":" + Quote(TerminalInfoString(TERMINAL_PATH)) + ",\"chartId\":" + IntegerToString(chart)
            + ",\"objectName\":" + Quote(name) + ",\"symbol\":" + Quote(ChartSymbol(chart))
            + ",\"timeframe\":" + Quote(EnumToString((ENUM_TIMEFRAMES)ChartPeriod(chart))) + ",\"kind\":" + Quote(kind)
            + ",\"anchors\":[" + anchors + "],\"text\":" + Quote(ObjectGetString(chart, name, OBJPROP_TEXT))
            + ",\"colorArgb\":" + IntegerToString(ObjectGetInteger(chart, name, OBJPROP_COLOR)) + "}";
      }
      chart = ChartNext(chart);
   }
   return "{\"terminalPath\":" + Quote(TerminalInfoString(TERMINAL_PATH)) + ",\"hostChartId\":" + IntegerToString(ChartID())
      + ",\"objects\":[" + objects + "]}";
}

void PollLossZones()
{
   if(!InpShowLossZones) { DeleteManagedLossZones(); last_zone_command = ""; return; }
   int file = FileOpen("TradePet\\loss-zones.json", FILE_READ | FILE_TXT | FILE_ANSI | FILE_SHARE_READ | FILE_SHARE_WRITE, 0, CP_UTF8);
   if(file == INVALID_HANDLE) return;
   string command = FileReadString(file); FileClose(file);
   if(command == last_zone_command) return;
   ApplyLossZoneSnapshot(command);
}

void PollMarketHistory()
{
   string filename;
   long search = FileFindFirst("TradePet\\market-request-*.json", filename);
   if(search == INVALID_HANDLE) return;
   FileFindClose(search);
   string path = "TradePet\\" + filename;
   int file = FileOpen(path, FILE_READ | FILE_TXT | FILE_ANSI | FILE_SHARE_READ, 0, CP_UTF8);
   if(file == INVALID_HANDLE) return;
   string command = FileReadString(file); FileClose(file);
   string id = JsonStringValue(command, "requestId");
   string account = "MT4:" + AccountServer() + "|" + IntegerToString(AccountNumber());
   if(StringLen(id) != 32 || filename != "market-request-" + id + ".json") { FileDelete(path); return; }
   if(JsonStringValue(command, "accountKey") != account || JsonStringValue(command, "timeframe") != "M5" ||
      StringCompare(JsonStringValue(command, "terminalPath"), TerminalInfoString(TERMINAL_PATH), false) != 0 ||
      JsonLongValue(command, "serverUtcOffsetSeconds", 100000) != server_offset ||
      MathAbs((double)(TimeGMT() - JsonLongValue(command, "createdAtEpochUtc", 0))) > 30)
   { FileDelete(path); return; }
   string symbol = JsonStringValue(command, "symbol");
   datetime from = (datetime)(JsonLongValue(command, "fromEpochUtc", 0) + server_offset);
   datetime to = (datetime)(JsonLongValue(command, "toEpochUtc", 0) + server_offset);
   if(to <= from || to - from > 366 * 86400) { FileDelete(path); return; }
   MqlRates rates[];
   string error = "";
   bool selected = SymbolSelect(symbol,true);
   int count = selected ? CopyRates(symbol, PERIOD_M5, from, to, rates) : 0;
   if(!selected) error = "该 MT4 账户没有此品种，无法读取历史行情。请核对品种后缀。";
   if(count < 0) return; // MT4 may be downloading history; retry while the request is fresh.
   int maximum = (int)MathMin(5000, MathMax(1, JsonLongValue(command, "maximumBars", 5000)));
   string bars = "";
   int emitted = 0;
   for(int i = 0; i < count && emitted < maximum; i++)
   {
      if(rates[i].time < from || rates[i].time > to) continue;
      if(emitted++ > 0) bars += ",";
      bars += "{\"time\":" + IntegerToString((long)rates[i].time) + ",\"open\":" + Number(rates[i].open)
         + ",\"high\":" + Number(rates[i].high) + ",\"low\":" + Number(rates[i].low)
         + ",\"close\":" + Number(rates[i].close) + ",\"tickVolume\":" + IntegerToString(rates[i].tick_volume) + "}";
   }
   string response = "{\"requestId\":" + Quote(id) + ",\"accountKey\":" + Quote(account)
      + ",\"terminalPath\":" + Quote(TerminalInfoString(TERMINAL_PATH)) + ",\"symbol\":" + Quote(symbol)
      + ",\"timeframe\":\"M5\",\"serverUtcOffsetSeconds\":" + IntegerToString(server_offset) + ",\"error\":" + Quote(error) + ",\"bars\":[" + bars + "]}";
   string result = "TradePet\\market-response-" + id;
   file = FileOpen(result + ".tmp", FILE_WRITE | FILE_TXT | FILE_ANSI, 0, CP_UTF8);
   if(file == INVALID_HANDLE) return;
   FileWriteString(file, response); FileFlush(file); FileClose(file);
   if(FileMove(result + ".tmp", 0, result + ".json", FILE_REWRITE)) FileDelete(path);
}

void ApplyLossZoneSnapshot(const string command)
  {
   if(JsonStringValue(command, "kind") != "loss_zone_snapshot")
      return;

   string expected_account = JsonStringValue(command, "accountKey");
   string expected_date = JsonStringValue(command, "serverDate");
   string expected_terminal = JsonStringValue(command, "terminalPath");
   string current_date = TimeToString((TimeGMT() + server_offset), TIME_DATE);
   StringReplace(current_date, ".", "-");
   string current_account = "MT4:" + AccountServer() + "|" +
      IntegerToString(AccountNumber());
   if(expected_account != current_account || expected_date != current_date ||
      StringCompare(expected_terminal, TerminalInfoString(TERMINAL_PATH), false) != 0)
      return;

   long revision = JsonLongValue(command, "revision", -1);
   if(revision < 0)
      return;

   DeleteManagedLossZones();
   string marker = "\"zones\":[";
   int array_start = StringFind(command, marker);
   int rendered_zones = 0;
   int rendered_charts = 0;
   if(array_start >= 0)
     {
      int position = array_start + StringLen(marker);
      int array_end = StringFind(command, "]", position);
      while(array_end >= 0)
        {
         int object_start = StringFind(command, "{", position);
         if(object_start < 0 || object_start >= array_end)
            break;
         int object_end = StringFind(command, "}", object_start);
         if(object_end < 0 || object_end > array_end)
            break;

         string zone = StringSubstr(command, object_start, object_end - object_start + 1);
         string id = JsonStringValue(zone, "id");
         string symbol = JsonStringValue(zone, "symbol");
         double lower_bound = JsonDoubleValue(zone, "lowerBound", 0.0);
         double center_price = JsonDoubleValue(zone, "centerPrice", 0.0);
         double upper_bound = JsonDoubleValue(zone, "upperBound", 0.0);
         int attempt_count = (int)JsonLongValue(zone, "attemptCount", 0);
         int loss_count = (int)JsonLongValue(zone, "lossCount", 0);
         double cumulative_loss = JsonDoubleValue(zone, "cumulativeLoss", 0.0);
         if(id != "" && symbol != "" && lower_bound < upper_bound)
           {
            rendered_zones++;
            rendered_charts += DrawLossZone(id, symbol, lower_bound, center_price, upper_bound,
               attempt_count, loss_count, cumulative_loss);
           }
         position = object_end + 1;
        }
     }

   last_zone_command = command;
   PrintFormat("TradePet loss zones rendered: revision=%I64d zones=%d chart_matches=%d",
      revision, rendered_zones, rendered_charts);
  }

int DrawLossZone(
   const string id,
   const string symbol,
   const double lower_bound,
   const double center_price,
   const double upper_bound,
   const int attempt_count,
   const int loss_count,
   const double cumulative_loss)
  {
   int chart_matches = 0;
   long chart_id = ChartFirst();
   while(chart_id >= 0)
     {
      if(ChartSymbol(chart_id) == symbol)
        {
         string base_name = LOSS_ZONE_PREFIX + id;
         string band_name = base_name + "::Band";
         string center_name = base_name + "::Center";
         datetime left_time = D'2000.01.01 00:00';
         datetime right_time = D'2099.12.31 23:59';
         string tooltip = "TradePet 亏损价格带\n" +
            DoubleToString(lower_bound, (int)SymbolInfoInteger(symbol, SYMBOL_DIGITS)) + " — " +
            DoubleToString(upper_bound, (int)SymbolInfoInteger(symbol, SYMBOL_DIGITS)) +
            "\n尝试 " + IntegerToString(attempt_count) + " 次，亏损 " + IntegerToString(loss_count) +
            " 次，累计 " + DoubleToString(cumulative_loss, 2);

         ObjectCreate(chart_id, band_name, OBJ_RECTANGLE, 0,
            left_time, upper_bound, right_time, lower_bound);
         ObjectMove(chart_id, band_name, 0, left_time, upper_bound);
         ObjectMove(chart_id, band_name, 1, right_time, lower_bound);
         ObjectSetInteger(chart_id, band_name, OBJPROP_COLOR, InpLossZoneColor);
         ObjectSetInteger(chart_id, band_name, OBJPROP_STYLE, STYLE_SOLID);
         ObjectSetInteger(chart_id, band_name, OBJPROP_WIDTH, 1);
         ObjectSetInteger(chart_id, band_name, OBJPROP_FILL, true);
         ObjectSetInteger(chart_id, band_name, OBJPROP_BACK, true);
         ObjectSetInteger(chart_id, band_name, OBJPROP_SELECTABLE, false);
         ObjectSetInteger(chart_id, band_name, OBJPROP_SELECTED, false);
         ObjectSetInteger(chart_id, band_name, OBJPROP_HIDDEN, true);
         ObjectSetString(chart_id, band_name, OBJPROP_TOOLTIP, tooltip);

         ObjectCreate(chart_id, center_name, OBJ_HLINE, 0, 0, center_price);
         ObjectSetDouble(chart_id, center_name, OBJPROP_PRICE, center_price);
         ObjectSetInteger(chart_id, center_name, OBJPROP_COLOR, InpLossZoneColor);
         ObjectSetInteger(chart_id, center_name, OBJPROP_STYLE, STYLE_DOT);
         ObjectSetInteger(chart_id, center_name, OBJPROP_WIDTH, 1);
         ObjectSetInteger(chart_id, center_name, OBJPROP_BACK, true);
         ObjectSetInteger(chart_id, center_name, OBJPROP_SELECTABLE, false);
         ObjectSetInteger(chart_id, center_name, OBJPROP_SELECTED, false);
         ObjectSetInteger(chart_id, center_name, OBJPROP_HIDDEN, true);
         ObjectSetString(chart_id, center_name, OBJPROP_TOOLTIP, tooltip + "\n中心价");
         ChartRedraw(chart_id);
         chart_matches++;
        }
      chart_id = ChartNext(chart_id);
     }
   return(chart_matches);
  }

void DeleteManagedLossZones()
  {
   long chart_id = ChartFirst();
   while(chart_id >= 0)
     {
      bool changed = false;
      for(int index = ObjectsTotal(chart_id, -1, -1) - 1; index >= 0; index--)
        {
         string object_name = ObjectName(chart_id, index, -1, -1);
         if(StringFind(object_name, LOSS_ZONE_PREFIX) == 0)
           {
            ObjectDelete(chart_id, object_name);
            changed = true;
           }
        }
      if(changed)
         ChartRedraw(chart_id);
      chart_id = ChartNext(chart_id);
     }
  }

string JsonStringValue(const string json,const string key)
  {
   string marker = "\"" + key + "\":";
   int position = StringFind(json, marker);
   if(position < 0)
      return("");
   position += StringLen(marker);
   while(position < StringLen(json) && StringGetCharacter(json, position) <= 32)
      position++;
   if(position >= StringLen(json) || StringGetCharacter(json, position) != '"')
      return("");
   position++;
   string value = "";
   while(position < StringLen(json))
     {
      ushort character = StringGetCharacter(json, position++);
      if(character == '"') return(value);
      if(character == '\\')
        {
         if(position >= StringLen(json)) return("");
         character = StringGetCharacter(json, position++);
         if(character == 'u')
           {
            int code = 0;
            for(int digit = 0; digit < 4; digit++)
              {
               if(position >= StringLen(json)) return("");
               ushort hex = StringGetCharacter(json, position++);
               int part = hex >= '0' && hex <= '9' ? hex - '0'
                  : hex >= 'a' && hex <= 'f' ? hex - 'a' + 10
                  : hex >= 'A' && hex <= 'F' ? hex - 'A' + 10 : -1;
               if(part < 0) return("");
               code = code * 16 + part;
              }
            character = (ushort)code;
           }
         else if(character == 'n') character = 10;
         else if(character == 'r') character = 13;
         else if(character == 't') character = 9;
         else if(character == 'b') character = 8;
         else if(character == 'f') character = 12;
         else if(character != '"' && character != '\\' && character != '/') return("");
        }
      value += ShortToString(character);
     }
   return("");
  }

long JsonLongValue(const string json,const string key,const long fallback)
  {
   string value = JsonNumberValue(json, key);
   return(value == "" ? fallback : StringToInteger(value));
  }

double JsonDoubleValue(const string json,const string key,const double fallback)
  {
   string value = JsonNumberValue(json, key);
   return(value == "" ? fallback : StringToDouble(value));
  }

string JsonNumberValue(const string json,const string key)
  {
   string marker = "\"" + key + "\":";
   int position = StringFind(json, marker);
   if(position < 0)
      return("");
   position += StringLen(marker);
   while(position < StringLen(json) && StringGetCharacter(json, position) <= 32)
      position++;
   int end = position;
   while(end < StringLen(json))
     {
      ushort character = (ushort)StringGetCharacter(json, end);
      if(character == ',' || character == '}' || character == ']')
         break;
      end++;
     }
   return(StringSubstr(json, position, end - position));
  }
