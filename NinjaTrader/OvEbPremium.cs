// OV EB Premium: opera las EB confirmadas (EB◆) de Oliver Velez Patrones (Elephant que barre el VAL / VAH RTH del día anterior y cierra de vuelta
// dentro, con el ATR >= 1,2 x su media), con los ajustes del indicador guardados en la plantilla de gráfico (Default).
// Reglas elegidas por el usuario (2026-10-26):
//   - Entrada Velez: orden stop 1 tick más allá del extremo de la EB◆ (máximo en la alcista, mínimo en la bajista), válida
//     VelasEntrada velas; se cancela si antes el precio toca el otro extremo (1 tick más allá) o a la hora de cierre.
//   - Stop 1 tick más allá del otro extremo de la EB◆; objetivo fijo RatioObjetivo x el riesgo desde el precio de entrada real.
//   - Tamaño: contratos = RiesgoUsd / (riesgo en puntos x valor del punto), entre ContratosMin y ContratosMax; si con 1 contrato
//     el riesgo ya supera RiesgoUsd, entra con ContratosMin.
//   - Cada EB◆ es una operación independiente (varias a la vez en la misma dirección); una EB◆ contraria a las operaciones
//     abiertas o pendientes se ignora.
//   - 24 h: sin entradas de HoraCierre a 18:00 NY y todo plano a HoraCierre; cada operación sale a mercado tras MaxVelas velas.
// Backtest del estudio (MNQ 2 min 10/2025-10/2026, 1 contrato, $4,50 por operación): 504 operaciones, 42 % win, PF 1,40,
// +$10.778, DD $1.408; en MES (validación) en equilibrio. La regla se eligió en ese mismo año de MNQ: esperar menos en vivo.
// Registro de operaciones: OvEbPremium_<cuenta>.csv en la carpeta de NinjaTrader.

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.AddOns;
using NinjaTrader.NinjaScript.Indicators;
using Ovp = NinjaTrader.NinjaScript.Indicators.OliverVelezPatrones;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
	public class OvEbPremium : Strategy
	{
		private class Op
		{
			public string Nombre;
			public int Dir, Qty, BarSenal, BarLleno = -1;
			public double Gatillo, Stop, Anula, Objetivo, Precio;
			public DateTime T;
			public Order Entrada;
			public bool Lleno, Cancelando, Saliendo;
		}

		private OliverVelezPatrones ovp;
		private readonly List<Op> ops = new List<Op>();
		private readonly StringBuilder registro = new StringBuilder();
		private bool procesado;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "Opera las EB◆ de Oliver Velez Patrones: entrada stop tras la Elephant, stop en el otro extremo, objetivo fijo en R, riesgo fijo en $ y operaciones independientes.";
				Name        = "OV EB Premium";
				Calculate   = Calculate.OnBarClose;
				EntriesPerDirection = 1;
				EntryHandling = EntryHandling.UniqueEntries;
				IsExitOnSessionCloseStrategy = false;
				StopTargetHandling = StopTargetHandling.PerEntryExecution;
				BarsRequiredToTrade = 20;
				IsInstantiatedOnEachOptimizationIteration = true;
				Plantilla     = "Default";
				RiesgoUsd     = 150;
				ContratosMin  = 1;
				ContratosMax  = 5;
				RatioObjetivo = 2;
				VelasEntrada  = 3;
				MaxVelas      = 60;
				HoraCierre    = 1655;
			}
			else if (State == State.Configure)
			{
				AddDataSeries(BarsPeriodType.Minute, 1);
				// Las mismas series que añade Oliver Velez Patrones para su confirmación de 3 temporalidades
				AddDataSeries(BarsPeriodType.Minute, 15);
				AddDataSeries(BarsPeriodType.Minute, 60);
			}
			else if (State == State.DataLoaded)
			{
				Dictionary<string, string> a = LeerPlantilla();
				ovp = OliverVelezPatrones(Enumerado(a, "Ma1Tipo"), Entero(a, "Ma1Periodo", 8), Enumerado(a, "Ma2Tipo"), Entero(a, "Ma2Periodo", 20), Enumerado(a, "Ma3Tipo"), Entero(a, "Ma3Periodo", 200));
				foreach (PropertyInfo p in typeof(OliverVelezPatrones).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
				{
					string v;
					if (!p.CanWrite || p.GetCustomAttributes(typeof(XmlIgnoreAttribute), true).Length > 0 || !a.TryGetValue(p.Name, out v))
						continue;
					object valor = Convertir(v, p.PropertyType);
					if (valor != null)
						try { p.SetValue(ovp, valor, null); } catch { }
				}
				AddChartIndicator(ovp);
			}
			else if (State == State.Realtime)
			{
				foreach (Op o in ops)
					if (o.Entrada != null)
						o.Entrada = GetRealtimeOrder(o.Entrada);
			}
			else if (State == State.Terminated && procesado)
				Escribir();
		}

		// ---------- Plantilla de gráfico: ajustes de <OliverVelezPatrones> ----------
		private Dictionary<string, string> LeerPlantilla()
		{
			Dictionary<string, string> a = new Dictionary<string, string>();
			string ruta = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "templates", "Chart", Plantilla + ".xml");
			try
			{
				XmlDocument doc = new XmlDocument();
				doc.Load(ruta);
				XmlNodeList nodos = doc.GetElementsByTagName("OliverVelezPatrones");
				if (nodos.Count > 0)
					foreach (XmlNode x in nodos[0].ChildNodes)
						if (x.NodeType == XmlNodeType.Element && (!x.HasChildNodes || (x.ChildNodes.Count == 1 && x.FirstChild.NodeType == XmlNodeType.Text)))
							a[x.Name] = x.InnerText;
			}
			catch (Exception ex) { Print("OV EB Premium: plantilla " + ruta + ": " + ex.Message); }
			return a;
		}

		private static object Convertir(string s, Type t)
		{
			try
			{
				if (t == typeof(string)) return s;
				if (t == typeof(bool))   return bool.Parse(s);
				if (t == typeof(int))    return int.Parse(s, CultureInfo.InvariantCulture);
				if (t == typeof(double)) return double.Parse(s, CultureInfo.InvariantCulture);
				if (t == typeof(float))  return float.Parse(s, CultureInfo.InvariantCulture);
				if (t.IsEnum)            return Enum.Parse(t, s);
			}
			catch { }
			return null;
		}

		private static OvTipoMedia Enumerado(Dictionary<string, string> a, string k)
		{
			string v;
			object r = a.TryGetValue(k, out v) ? Convertir(v, typeof(OvTipoMedia)) : null;
			return r != null ? (OvTipoMedia)r : OvTipoMedia.SMA;
		}

		private static int Entero(Dictionary<string, string> a, string k, int def)
		{
			string v;
			object r = a.TryGetValue(k, out v) ? Convertir(v, typeof(int)) : null;
			return r != null ? (int)r : def;
		}

		private static int Hhmm(DateTime t)
		{
			DateTime et = VwapTiempo.AEt(t);
			return et.Hour * 100 + et.Minute;
		}

		private bool SinEntradas(DateTime t)
		{
			int hh = Hhmm(t);
			return hh >= HoraCierre && hh < 1800;
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress == 0)
			{
				ovp.Update();
				if (CurrentBars[0] < BarsRequiredToTrade || CurrentBars[1] < 1)
					return;
				procesado = true;
				Vela2();
			}
			else if (BarsInProgress == 1 && CurrentBars[0] >= BarsRequiredToTrade)
				Vela1();
		}

		// Cierre de la vela de 2 min: caducidad de las entradas, tope de velas y EB◆ nueva
		private void Vela2()
		{
			foreach (Op o in ops.ToArray())
			{
				if (!o.Lleno && !o.Cancelando && o.Entrada != null && CurrentBar - o.BarSenal >= VelasEntrada)
					Cancelar(o);
				else if (o.Lleno && CurrentBar - o.BarLleno >= MaxVelas)
					Salir(o, "Tope de velas");
			}
			if (SinEntradas(Time[0]))
				return;
			int f = ovp.PatronesVela(0);
			int dir = (f & Ovp.EbAlcPrem) != 0 ? 1 : (f & Ovp.EbBajPrem) != 0 ? -1 : 0;
			if (dir == 0)
				return;
			// Una EB◆ contraria a las operaciones abiertas o pendientes se ignora
			foreach (Op o in ops)
				if (o.Dir != dir)
					return;
			if ((dir == 1 && Position.MarketPosition == MarketPosition.Short) || (dir == -1 && Position.MarketPosition == MarketPosition.Long))
				return;

			Op n = new Op { Dir = dir, BarSenal = CurrentBar, T = Time[0] };
			n.Nombre = (dir == 1 ? "EBP L " : "EBP C ") + CurrentBar;
			n.Gatillo = dir == 1 ? High[0] + TickSize : Low[0] - TickSize;
			n.Stop = dir == 1 ? Low[0] - TickSize : High[0] + TickSize;
			n.Anula = n.Stop;
			double riesgo = Math.Abs(n.Gatillo - n.Stop), pv = Instrument.MasterInstrument.PointValue;
			n.Qty = Math.Max(ContratosMin, Math.Min(ContratosMax, (int)Math.Floor(RiesgoUsd / (riesgo * pv))));
			n.Objetivo = n.Gatillo + dir * RatioObjetivo * riesgo;
			SetStopLoss(n.Nombre, CalculationMode.Price, n.Stop, false);
			SetProfitTarget(n.Nombre, CalculationMode.Price, n.Objetivo);
			ops.Add(n);
			n.Entrada = dir == 1 ? EnterLongStopMarket(1, true, n.Qty, n.Gatillo, n.Nombre) : EnterShortStopMarket(1, true, n.Qty, n.Gatillo, n.Nombre);
		}

		// Cada vela de 1 min: la entrada se anula si el precio toca el otro extremo antes; plano a la hora de cierre
		private void Vela1()
		{
			bool cierre = SinEntradas(Times[1][0]);
			foreach (Op o in ops.ToArray())
			{
				if (!o.Lleno)
				{
					if (o.Cancelando || o.Entrada == null)
						continue;
					if (cierre || (o.Dir == 1 ? Lows[1][0] <= o.Anula : Highs[1][0] >= o.Anula))
						Cancelar(o);
				}
				else if (cierre)
					Salir(o, "Cierre " + HoraCierre);
			}
		}

		private void Cancelar(Op o)
		{
			o.Cancelando = true;
			CancelOrder(o.Entrada);
		}

		private void Salir(Op o, string motivo)
		{
			if (o.Saliendo)
				return;
			o.Saliendo = true;
			if (o.Dir == 1) ExitLong(1, o.Qty, motivo, o.Nombre);
			else ExitShort(1, o.Qty, motivo, o.Nombre);
		}

		private Op Buscar(string nombre)
		{
			foreach (Op o in ops)
				if (o.Nombre == nombre)
					return o;
			return null;
		}

		protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled, double averageFillPrice,
			OrderState orderState, DateTime time, ErrorCode error, string comment)
		{
			Op o = Buscar(order.Name);
			if (o == null)   // las órdenes de stop, objetivo y salida no llevan el nombre de la señal
				return;
			if (order.OrderAction == OrderAction.Buy || order.OrderAction == OrderAction.SellShort)
			{
				o.Entrada = order;
				// Entrada cancelada o rechazada sin ejecución: la operación desaparece
				if ((orderState == OrderState.Cancelled || orderState == OrderState.Rejected) && filled == 0)
					ops.Remove(o);
			}
		}

		protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
		{
			Order ord = execution.Order;
			if (ord == null)
				return;
			if (ord.OrderAction == OrderAction.Buy || ord.OrderAction == OrderAction.SellShort)
			{
				Op o = Buscar(ord.Name);
				if (o == null)
					return;
				if (!o.Lleno)
					o.BarLleno = CurrentBars[0];
				o.Lleno = true;
				o.Precio = ord.AverageFillPrice > 0 ? ord.AverageFillPrice : price;
				o.Qty = Math.Max(1, ord.Filled);
				// El objetivo se mide desde el precio real de entrada (con hueco, la entrada pasa del gatillo)
				o.Objetivo = o.Precio + o.Dir * RatioObjetivo * Math.Abs(o.Precio - o.Stop);
				SetProfitTarget(o.Nombre, CalculationMode.Price, o.Objetivo);
			}
			else
			{
				Op o = Buscar(ord.FromEntrySignal);
				if (o == null || ord.OrderState != OrderState.Filled)
					return;
				double pv = Instrument.MasterInstrument.PointValue;
				double pnl = (ord.AverageFillPrice - o.Precio) * o.Dir * o.Qty * pv;
				Registrar(o, ord.Name, ord.AverageFillPrice, time, pnl);
				ops.Remove(o);
			}
		}

		private void Registrar(Op o, string motivo, double salida, DateTime t, double pnl)
		{
			if (registro.Length == 0)
				registro.Append("fechaSenal,horaSenal,dir,contratos,entrada,stop,objetivo,riesgoPts,fechaSalida,horaSalida,salida,motivo,resultadoR,pnlUsd\n");
			DateTime es = VwapTiempo.AEt(o.T), ex = VwapTiempo.AEt(t);
			double riesgo = Math.Abs(o.Precio - o.Stop);
			registro.Append(string.Join(",", new string[] {
				es.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), es.ToString("HHmm", CultureInfo.InvariantCulture), o.Dir == 1 ? "larga" : "corta",
				o.Qty.ToString(CultureInfo.InvariantCulture), F(o.Precio), F(o.Stop), F(o.Objetivo), F(riesgo),
				ex.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ex.ToString("HHmm", CultureInfo.InvariantCulture), F(salida), motivo,
				F(riesgo > 0 ? (salida - o.Precio) * o.Dir / riesgo : 0), F(pnl) })).Append('\n');
		}

		private void Escribir()
		{
			if (registro.Length == 0)
				return;
			try
			{
				string nombre = "OvEbPremium_" + (Account != null ? Account.Name : "Backtest") + ".csv";
				File.WriteAllText(Path.Combine(NinjaTrader.Core.Globals.UserDataDir, nombre), registro.ToString());
			}
			catch (Exception ex) { Print("OV EB Premium: " + ex.Message); }
		}

		private static string F(double v)
		{
			return double.IsNaN(v) || double.IsInfinity(v) ? "" : v.ToString("0.##", CultureInfo.InvariantCulture);
		}

		#region Properties
		[NinjaScriptProperty]
		[Display(Name = "Plantilla de gráfico", Description = "Plantilla de la que se leen los ajustes de Oliver Velez Patrones.", Order = 1, GroupName = "1. Indicador")]
		public string Plantilla { get; set; }

		[NinjaScriptProperty]
		[Range(1, double.MaxValue)]
		[Display(Name = "Riesgo por operación ($)", Order = 1, GroupName = "2. Tamaño")]
		public double RiesgoUsd { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Contratos mínimos", Description = "También cuando con 1 contrato el riesgo ya supera el riesgo por operación.", Order = 2, GroupName = "2. Tamaño")]
		public int ContratosMin { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Contratos máximos", Order = 3, GroupName = "2. Tamaño")]
		public int ContratosMax { get; set; }

		[NinjaScriptProperty]
		[Range(0.25, 20)]
		[Display(Name = "Objetivo (R)", Description = "Múltiplo del riesgo desde el precio de entrada.", Order = 1, GroupName = "3. Gestión")]
		public double RatioObjetivo { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Velas de validez de la entrada", Order = 2, GroupName = "3. Gestión")]
		public int VelasEntrada { get; set; }

		[NinjaScriptProperty]
		[Range(1, 10000)]
		[Display(Name = "Máximo de velas en la operación", Description = "Salida a mercado al cierre de esa vela (60 en el backtest).", Order = 3, GroupName = "3. Gestión")]
		public int MaxVelas { get; set; }

		[NinjaScriptProperty]
		[Range(0, 2359)]
		[Display(Name = "Plano a las (HHMM NY)", Description = "Sin entradas desde esta hora hasta las 18:00 NY; las operaciones abiertas salen a mercado.", Order = 4, GroupName = "3. Gestión")]
		public int HoraCierre { get; set; }
		#endregion
	}
}
