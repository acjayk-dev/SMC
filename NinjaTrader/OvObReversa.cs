// OV OB Reversa: entra apenas ST OB Rejections forma un Order Block, con los ajustes del indicador guardados en la plantilla de gráfico
// (Default).
// Reglas elegidas por el usuario (2026-10-29):
//   - Gatillo, al cierre de la vela: el indicador forma y dibuja un OB nuevo (vela del CHoCH). OB alcista → compra, bajista → venta,
//     a mercado inmediata. Sin filtro de riesgo.
//   - Stop inicial StopTicks más allá del OB (bajo su borde inferior en compra, sobre el superior en venta).
//   - TP1: ContratosTp1 contratos a RatioTp1 x el riesgo desde el precio de entrada real; TP2: el resto a RatioTp2 x el riesgo.
//     Con TP1 completo el stop del resto pasa a breakeven (precio de entrada).
//   - Antes de TP1, un OB contrario cierra todo y entra en su sentido (giro).
//   - Después de TP1, un CHoCH contrario cierra el resto; si ese CHoCH forma un OB, entra en su sentido (giro); sin OB queda plano.
//   - Un OB nuevo del mismo sentido se ignora. Una operación a la vez.
//   - 24 h: sin entradas de HoraCierre a 18:00 NY y todo plano a HoraCierre.
//   - Oliver Velez Patrones no interviene en las reglas: solo se dibuja (MostrarIndicadores).
// Órdenes no gestionadas: en modo gestionado NinjaTrader ignora la segunda entrada de un giro con dos tramos.
// Registro de operaciones: OvObReversa_<cuenta>.csv en la carpeta de NinjaTrader.

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Media;
using System.Xml;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.AddOns;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
	public class OvObReversa : Strategy
	{
		// Una operación con sus órdenes; durante un giro conviven la que se cierra y la nueva
		private class Operacion
		{
			public int Id, Dir, Qty, Abiertos, Salidos;
			public double Stop, StopActual, Tp1, Tp2, Precio, SumaEntrada, SumaSalida, PnlPts, ObAlto, ObBajo;
			public DateTime TSenal;
			public bool Llena, Tp1Hecho, Saliendo;
			public string Motivo;
			public Order Entrada, StopOrd, Tp1Ord, Tp2Ord;
		}

		private OliverVelezPatrones ovp;
		private ShortTermOrderBlockRejections stob;
		private readonly Dictionary<int, Operacion> ops = new Dictionary<int, Operacion>();
		private Operacion actual;   // la última entrada: pendiente, abierta o saliendo
		private int ultimoId;
		private readonly StringBuilder registro = new StringBuilder();
		private bool procesado;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "Entra apenas ST OB Rejections forma un Order Block con el stop más allá del OB; TP1 a 1R, TP2 a 2R y stop a breakeven tras TP1. Gira con el OB contrario; después de TP1 también sale en el CHoCH contrario.";
				Name        = "OV OB Reversa";
				Calculate   = Calculate.OnBarClose;
				IsUnmanaged = true;
				IsExitOnSessionCloseStrategy = false;
				BarsRequiredToTrade = 20;
				IsInstantiatedOnEachOptimizationIteration = true;
				Plantilla          = "Default";
				MostrarIndicadores = true;
				Contratos          = 2;
				ContratosTp1       = 1;
				RatioTp1           = 1;
				RatioTp2           = 2;
				StopTicks          = 2;
				HoraCierre         = 1655;
			}
			else if (State == State.Configure)
			{
				// También aquí: las instancias creadas con la versión gestionada (gráfico, Analyzer) conservan IsUnmanaged = false
				IsUnmanaged = true;
				AddDataSeries(BarsPeriodType.Minute, 1);
				// Las mismas series que añade Oliver Velez Patrones para su confirmación de 3 temporalidades
				AddDataSeries(BarsPeriodType.Minute, 15);
				AddDataSeries(BarsPeriodType.Minute, 60);
			}
			else if (State == State.DataLoaded)
			{
				stob = ShortTermOrderBlockRejections();
				Aplicar(stob, LeerPlantilla("ShortTermOrderBlockRejections"));
				if (MostrarIndicadores)
				{
					Dictionary<string, string> a = LeerPlantilla("OliverVelezPatrones");
					ovp = OliverVelezPatrones(Enumerado(a, "Ma1Tipo"), Entero(a, "Ma1Periodo", 8), Enumerado(a, "Ma2Tipo"), Entero(a, "Ma2Periodo", 20), Enumerado(a, "Ma3Tipo"), Entero(a, "Ma3Periodo", 200));
					Aplicar(ovp, a);
					AddChartIndicator(ovp);
					AddChartIndicator(stob);
				}
			}
			else if (State == State.Realtime)
			{
				// Las órdenes históricas que siguen vivas pasan a tiempo real
				foreach (Operacion o in ops.Values)
				{
					if (o.Entrada != null) o.Entrada = GetRealtimeOrder(o.Entrada);
					if (o.StopOrd != null) o.StopOrd = GetRealtimeOrder(o.StopOrd);
					if (o.Tp1Ord != null) o.Tp1Ord = GetRealtimeOrder(o.Tp1Ord);
					if (o.Tp2Ord != null) o.Tp2Ord = GetRealtimeOrder(o.Tp2Ord);
				}
			}
			else if (State == State.Terminated && procesado)
				Escribir();
		}

		// ---------- Plantilla de gráfico: ajustes de cada indicador ----------
		private Dictionary<string, string> LeerPlantilla(string indicador)
		{
			Dictionary<string, string> a = new Dictionary<string, string>();
			string ruta = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "templates", "Chart", Plantilla + ".xml");
			try
			{
				XmlDocument doc = new XmlDocument();
				doc.Load(ruta);
				XmlNodeList nodos = doc.GetElementsByTagName(indicador);
				if (nodos.Count == 0)
					Print("OV OB Reversa: " + indicador + " no está en la plantilla " + ruta + "; se usan sus valores por defecto");
				else
					foreach (XmlNode x in nodos[0].ChildNodes)
						if (x.NodeType == XmlNodeType.Element && (!x.HasChildNodes || (x.ChildNodes.Count == 1 && x.FirstChild.NodeType == XmlNodeType.Text)))
							a[x.Name] = x.InnerText;
			}
			catch (Exception ex) { Print("OV OB Reversa: plantilla " + ruta + ": " + ex.Message); }
			return a;
		}

		// Copia en el indicador sus ajustes de la plantilla (propiedades propias que se guardan en ella)
		private static void Aplicar(object ind, Dictionary<string, string> a)
		{
			PropertyInfo[] props = ind.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
			foreach (PropertyInfo p in props)
			{
				string v;
				if (!p.CanWrite || p.GetCustomAttributes(typeof(XmlIgnoreAttribute), true).Length > 0 || !a.TryGetValue(p.Name, out v))
					continue;
				object valor = Convertir(v, p.PropertyType);
				if (valor != null)
					try { p.SetValue(ind, valor, null); } catch { }
			}
			// Los colores leídos se congelan: los indicadores los usan en OnRender, en el hilo del gráfico
			foreach (PropertyInfo p in props)
			{
				if (p.PropertyType != typeof(Brush) || !p.CanRead || !p.CanWrite)
					continue;
				Brush b = p.GetValue(ind, null) as Brush;
				if (b == null || b.IsFrozen || !b.CanFreeze)
					continue;
				Brush c = b.Clone();
				c.Freeze();
				try { p.SetValue(ind, c, null); } catch { }
			}
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
				if (ovp != null)
					ovp.Update();
				stob.Update();
				if (CurrentBars[0] < BarsRequiredToTrade || CurrentBars[1] < 1)
					return;
				procesado = true;
				Vela2();
			}
			// Cada vela de 1 min: plano a la hora de cierre
			else if (BarsInProgress == 1 && actual != null && actual.Llena && SinEntradas(Times[1][0]))
				Cerrar(actual, "Cierre " + HoraCierre);
		}

		// Cierre de la vela del gráfico: OB y CHoCH formados en esta vela
		private void Vela2()
		{
			double alto, bajo;
			int d = ObNuevo(out alto, out bajo), ch = stob.ChochThisBar();
			Operacion o = actual;
			if (o != null)
			{
				if (!o.Llena || o.Saliendo)
					return;
				// Un OB contrario cierra todo y gira; uno del mismo sentido se ignora. Después de TP1 también cierra el CHoCH
				// contrario sin OB, y queda plano
				bool giro = d == -o.Dir;
				if (!giro && !(o.Tp1Hecho && ch == -o.Dir))
					return;
				Cerrar(o, giro ? "Giro por OB contrario" : "CHoCH contrario");
				if (!giro)
					return;
			}
			if (d == 0 || SinEntradas(Time[0]))
				return;

			double s = d == 1 ? bajo - StopTicks * TickSize : alto + StopTicks * TickSize;
			if ((Close[0] - s) * d <= 0)
				return;
			Operacion n = new Operacion { Id = ++ultimoId, Dir = d, Stop = s, StopActual = s, ObAlto = alto, ObBajo = bajo, TSenal = Time[0] };
			ops[n.Id] = n;
			actual = n;
			n.Entrada = SubmitOrderUnmanaged(1, d == 1 ? OrderAction.Buy : OrderAction.SellShort, OrderType.Market, Contratos, 0, 0, "", Nombre(n, "Entrada"));
		}

		// Sentido del OB que el indicador formó y dibujó en esta vela (el último de su lista); 0 si no hay o si se formaron los dos
		private int ObNuevo(out double alto, out double bajo)
		{
			alto = bajo = 0;
			int d = 0;
			for (int s = 1; s >= -1; s -= 2)
			{
				int n = stob.ActiveBlockCount(s);
				if (n == 0)
					continue;
				double top, bottom;
				int born;
				stob.ActiveBlock(s, n - 1, out top, out bottom, out born);
				if (born != CurrentBar || !stob.ActiveBlockDrawn(s, n - 1))
					continue;
				if (d != 0)
					return 0;
				d = s;
				alto = top;
				bajo = bottom;
			}
			return d;
		}

		// ---------- Órdenes ----------
		// Nombre "OBR L12 Stop": sentido e identificador de la operación y tipo de orden (Entrada, Stop, TP1, TP2, Salida)
		private static string Nombre(Operacion o, string tipo)
		{
			return "OBR " + (o.Dir == 1 ? "L" : "C") + o.Id.ToString(CultureInfo.InvariantCulture) + " " + tipo;
		}

		private Operacion DeOrden(Order ord, out string tipo)
		{
			tipo = null;
			string[] p = ord.Name.Split(' ');
			int id;
			Operacion o;
			if (p.Length != 3 || p[0] != "OBR" || p[1].Length < 2 || !int.TryParse(p[1].Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out id) || !ops.TryGetValue(id, out o))
				return null;
			tipo = p[2];
			return o;
		}

		private void Cancelar(Order ord)
		{
			if (ord != null && !Order.IsTerminalState(ord.OrderState))
				CancelOrder(ord);
		}

		// Contratos que a una orden viva le faltan por ejecutar
		private static int Pendiente(Order ord)
		{
			return ord != null && !Order.IsTerminalState(ord.OrderState) ? ord.Quantity - ord.Filled : 0;
		}

		// Entrada completa: stop por todo, TP1 por ContratosTp1 y TP2 por el resto; con hueco más allá del stop sale a mercado
		private void Proteger(Operacion o)
		{
			o.Llena = true;
			double riesgo = (o.Precio - o.Stop) * o.Dir;
			if (riesgo <= 0)
			{
				Cerrar(o, "Hueco tras el stop");
				return;
			}
			o.Tp1 = Instrument.MasterInstrument.RoundToTickSize(o.Precio + o.Dir * RatioTp1 * riesgo);
			o.Tp2 = Instrument.MasterInstrument.RoundToTickSize(o.Precio + o.Dir * RatioTp2 * riesgo);
			OrderAction salida = o.Dir == 1 ? OrderAction.Sell : OrderAction.BuyToCover;
			o.StopOrd = SubmitOrderUnmanaged(1, salida, OrderType.StopMarket, o.Abiertos, 0, o.Stop, "", Nombre(o, "Stop"));
			int q1 = Math.Min(ContratosTp1, o.Abiertos), q2 = o.Abiertos - q1;
			o.Tp1Ord = SubmitOrderUnmanaged(1, salida, OrderType.Limit, q1, o.Tp1, 0, "", Nombre(o, "TP1"));
			if (q2 > 0)
				o.Tp2Ord = SubmitOrderUnmanaged(1, salida, OrderType.Limit, q2, o.Tp2, 0, "", Nombre(o, "TP2"));
		}

		// Sale a mercado con todo lo abierto y cancela su stop y sus objetivos
		private void Cerrar(Operacion o, string motivo)
		{
			if (o.Saliendo || o.Abiertos <= 0)
				return;
			o.Saliendo = true;
			o.Motivo = motivo;
			Cancelar(o.StopOrd);
			Cancelar(o.Tp1Ord);
			Cancelar(o.Tp2Ord);
			SubmitOrderUnmanaged(1, o.Dir == 1 ? OrderAction.Sell : OrderAction.BuyToCover, OrderType.Market, o.Abiertos, 0, 0, "", Nombre(o, "Salida"));
		}

		private void Quitar(Operacion o)
		{
			ops.Remove(o.Id);
			if (actual == o)
				actual = null;
		}

		protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled, double averageFillPrice,
			OrderState orderState, DateTime time, ErrorCode error, string comment)
		{
			string tipo;
			Operacion o = DeOrden(order, out tipo);
			if (o == null)
				return;
			// Las referencias se toman aquí, como recomienda NinjaTrader para las órdenes no gestionadas
			if (tipo == "Entrada") o.Entrada = order;
			else if (tipo == "Stop") o.StopOrd = order;
			else if (tipo == "TP1") o.Tp1Ord = order;
			else if (tipo == "TP2") o.Tp2Ord = order;
			// Entrada cancelada o rechazada: sin ejecución no hay operación; con ejecución parcial se protege lo que entró
			if (tipo == "Entrada" && (orderState == OrderState.Cancelled || orderState == OrderState.Rejected))
			{
				if (o.Qty == 0)
					Quitar(o);
				else if (!o.Llena)
					Proteger(o);
			}
		}

		protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
		{
			Order ord = execution.Order;
			string tipo = null;
			Operacion o = ord != null ? DeOrden(ord, out tipo) : null;
			if (o == null)
				return;
			if (tipo == "Entrada")
			{
				o.Qty += quantity;
				o.Abiertos += quantity;
				o.SumaEntrada += price * quantity;
				o.Precio = o.SumaEntrada / o.Qty;
				if (ord.Filled >= ord.Quantity && !o.Llena)
					Proteger(o);
				return;
			}

			// Salida: stop, TP1, TP2 o a mercado
			o.Abiertos -= quantity;
			o.Salidos += quantity;
			o.SumaSalida += price * quantity;
			o.PnlPts += (price - o.Precio) * o.Dir * quantity;
			if (o.Abiertos <= 0)
			{
				if (o.Abiertos < 0)
					Print("OV OB Reversa: " + ord.Name + " ejecutó más contratos de los abiertos");
				Cancelar(o.StopOrd);
				Cancelar(o.Tp1Ord);
				Cancelar(o.Tp2Ord);
				Registrar(o, tipo == "Salida" ? o.Motivo : tipo == "Stop" ? (o.Tp1Hecho ? "Breakeven" : "Stop") : tipo, time);
				Quitar(o);
				return;
			}
			if (o.Saliendo)
				return;
			if (tipo == "TP1" || tipo == "TP2")
			{
				// Con el primer objetivo completo el stop del resto pasa a breakeven; el stop cubre solo lo que queda
				if (ord.Filled >= ord.Quantity && !o.Tp1Hecho)
				{
					o.Tp1Hecho = true;
					double be = Instrument.MasterInstrument.RoundToTickSize(o.Precio);
					if ((be - o.StopActual) * o.Dir > 0)
						o.StopActual = be;
				}
				if ((price - o.StopActual) * o.Dir <= 0)
					Cerrar(o, "Breakeven");
				else if (o.StopOrd != null && !Order.IsTerminalState(o.StopOrd.OrderState))
					ChangeOrder(o.StopOrd, o.Abiertos, 0, o.StopActual);
			}
			// Stop parcial: los objetivos no pueden sumar más que lo abierto
			else if (tipo == "Stop")
			{
				int p1 = Pendiente(o.Tp1Ord);
				if (p1 + Pendiente(o.Tp2Ord) > o.Abiertos)
				{
					Cancelar(o.Tp2Ord);
					if (p1 > o.Abiertos)
						Cancelar(o.Tp1Ord);
				}
			}
		}

		private void Registrar(Operacion o, string motivo, DateTime t)
		{
			if (registro.Length == 0)
				registro.Append("fechaSenal,horaSenal,dir,contratos,entrada,stop,tp1,tp2,tp1Hecho,stopFinal,riesgoPts,obAlto,obBajo,fechaSalida,horaSalida,salidaMedia,motivo,resultadoR,pnlUsd\n");
			DateTime es = VwapTiempo.AEt(o.TSenal), ex = VwapTiempo.AEt(t);
			double riesgo = Math.Abs(o.Precio - o.Stop);
			registro.Append(string.Join(",", new string[] {
				es.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), es.ToString("HHmm", CultureInfo.InvariantCulture), o.Dir == 1 ? "larga" : "corta",
				o.Qty.ToString(CultureInfo.InvariantCulture), F(o.Precio), F(o.Stop), F(o.Tp1), F(o.Tp2), o.Tp1Hecho ? "si" : "no", F(o.StopActual), F(riesgo),
				F(o.ObAlto), F(o.ObBajo), ex.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ex.ToString("HHmm", CultureInfo.InvariantCulture),
				F(o.Salidos > 0 ? o.SumaSalida / o.Salidos : double.NaN), motivo, F(riesgo > 0 && o.Qty > 0 ? o.PnlPts / (riesgo * o.Qty) : 0),
				F(o.PnlPts * Instrument.MasterInstrument.PointValue) })).Append('\n');
		}

		private void Escribir()
		{
			if (registro.Length == 0)
				return;
			try
			{
				string archivo = "OvObReversa_" + (Account != null ? Account.Name : "Backtest") + ".csv";
				File.WriteAllText(Path.Combine(NinjaTrader.Core.Globals.UserDataDir, archivo), registro.ToString());
			}
			catch (Exception ex) { Print("OV OB Reversa: " + ex.Message); }
		}

		private static string F(double v)
		{
			return double.IsNaN(v) || double.IsInfinity(v) ? "" : v.ToString("0.##", CultureInfo.InvariantCulture);
		}

		#region Properties
		[NinjaScriptProperty]
		[Display(Name = "Plantilla de gráfico", Description = "Plantilla de la que se leen los ajustes de ST OB Rejections y de Oliver Velez Patrones.", Order = 1, GroupName = "1. Indicadores")]
		public string Plantilla { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Mostrar los indicadores", Description = "Dibuja en el gráfico ST OB Rejections tal como lo usa la estrategia y Oliver Velez Patrones (solo visual).", Order = 2, GroupName = "1. Indicadores")]
		public bool MostrarIndicadores { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Contratos", Order = 1, GroupName = "2. Gestión")]
		public int Contratos { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Contratos en TP1", Description = "Los que salen en el primer objetivo; el resto sale en TP2.", Order = 2, GroupName = "2. Gestión")]
		public int ContratosTp1 { get; set; }

		[NinjaScriptProperty]
		[Range(0.25, 20)]
		[Display(Name = "TP1 (R)", Description = "Múltiplo del riesgo desde el precio de entrada real hasta el stop inicial.", Order = 3, GroupName = "2. Gestión")]
		public double RatioTp1 { get; set; }

		[NinjaScriptProperty]
		[Range(0.25, 20)]
		[Display(Name = "TP2 (R)", Description = "Múltiplo del riesgo para los contratos que no salen en TP1.", Order = 4, GroupName = "2. Gestión")]
		public double RatioTp2 { get; set; }

		[NinjaScriptProperty]
		[Range(0, 1000)]
		[Display(Name = "Stop más allá del OB (ticks)", Description = "Stop inicial: bajo el borde inferior del OB en compra, sobre el superior en venta. Tras TP1 pasa a breakeven.", Order = 5, GroupName = "2. Gestión")]
		public int StopTicks { get; set; }

		[NinjaScriptProperty]
		[Range(0, 2359)]
		[Display(Name = "Plano a las (HHMM NY)", Description = "Sin entradas desde esta hora hasta las 18:00 NY; la operación abierta sale a mercado.", Order = 6, GroupName = "2. Gestión")]
		public int HoraCierre { get; set; }
		#endregion
	}
}
